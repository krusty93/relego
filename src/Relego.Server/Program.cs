using System.Data;
using System.Reflection;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using Quartz;
using Serilog;
using Relego.Core.Contracts;
using Relego.Server.Data;
using Relego.Server.Endpoints;
using Relego.Server.Infrastructure.Database;
using Relego.Server.Infrastructure.Logging;
using Relego.Server.Infrastructure.Smtp;
using Relego.Server.Jobs;
using Relego.Server.Services;

SqlMapper.AddTypeHandler(new DateTimeOffsetTypeHandler());

var dbPath = ".data/relego.db";
var connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString();

var webRootPath = Environment.GetEnvironmentVariable("RELEGO_WEB_ROOT");
var builder = string.IsNullOrWhiteSpace(webRootPath)
    ? WebApplication.CreateBuilder(args)
    : WebApplication.CreateBuilder(new WebApplicationOptions
    {
        Args = args,
        WebRootPath = webRootPath,
    });

SerilogConfiguration.ConfigureLogging(builder);

var smtpEnvironmentOverrides = GetLegacySmtpEnvironmentOverrides();
if (smtpEnvironmentOverrides.Count > 0)
    builder.Configuration.AddInMemoryCollection(smtpEnvironmentOverrides);

builder.Services.Configure<SmtpSettings>(builder.Configuration.GetSection("Smtp"));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc(
       "v1",
        new OpenApiInfo
        {
            Title = "Relego APIs",
            Version = "v1",
            Contact = new OpenApiContact
            {
                Name = "Relego",
                Url = new Uri("https://relego.app"),
            }
        }
    );

    options.OrderActionsBy(d => d.GroupName);

    IncludeXmlCommentsIfPresent(options, Assembly.GetExecutingAssembly());
    IncludeXmlCommentsIfPresent(options, typeof(SettingsResponse).Assembly);
});

builder.Services.AddScoped<IDbConnection>(_ =>
{
    var conn = new SqliteConnection(connectionString);
    conn.Open();
    using var pragma = conn.CreateCommand();
    pragma.CommandText = "PRAGMA foreign_keys = ON;";
    pragma.ExecuteNonQuery();
    return conn;
});

builder.Services.AddScoped<UserRepository>();
builder.Services.AddScoped<SyncRepository>();
builder.Services.AddScoped<SettingsRepository>();
builder.Services.AddScoped<StatusRepository>();
builder.Services.AddScoped<ExclusionRepository>();
builder.Services.AddScoped<WeightRepository>();
builder.Services.AddScoped<RecapRepository>();
builder.Services.AddScoped<HighlightRepository>();
builder.Services.AddScoped<BookRepository>();
builder.Services.AddScoped<SmtpSettingsRepository>();
builder.Services.AddScoped<SmtpConfigurationService>();
builder.Services.AddScoped<UploadImportService>();

builder.Services.AddQuartz(q =>
{
    q.UseTimeZoneConverter();

    q.UsePersistentStore(store =>
    {
        store.UseNewtonsoftJsonSerializer();
        store.UseMicrosoftSQLite(db =>
        {
            db.ConnectionString = connectionString;
        });
    });
});
builder.Services.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);

builder.Services.AddSingleton<ISchedulerService, SchedulerService>();
builder.Services.AddTransient<RecapJob>();
builder.Services.AddScoped<HighlightSelectionService>();

if (builder.Environment.IsDevelopment())
    builder.Services.AddScoped<IMailDeliveryService, DevMailDeliveryService>();
else
    builder.Services.AddScoped<IMailDeliveryService, MailDeliveryService>();

builder.Services.AddScoped<IRecapService, RecapService>();

var app = builder.Build();

app.UseExceptionHandler(err =>
{
    err.Run(async context =>
    {
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(
            new { type = "https://datatracker.ietf.org/doc/html/rfc9110#section-15.6.1", title = "An unexpected error occurred.", status = 500 },
            cancellationToken: context.RequestAborted);
    });
});

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.DisplayRequestDuration();
    });
}

app.UseStaticFiles();

app.MapRecapEndpoints();
app.MapProbeEndpoints();
app.MapSyncEndpoints();
app.MapImportEndpoints();
app.MapSettingsEndpoints();
app.MapSmtpSettingsEndpoints();
app.MapStatusEndpoints();
app.MapExclusionEndpoints();
app.MapWeightEndpoints();
app.MapHighlightEndpoints();
app.MapBookEndpoints();
app.MapFallbackToFile("index.html");

var schemaBootstrap = new SchemaBootstrap();
await schemaBootstrap.ApplyAsync(dbPath);
await QuartzSchemaInitializer.ApplyAsync(connectionString);

// Seed the mail server configuration from SMTP_* on first boot only. Once a client saves
// settings the stored row wins, so the environment is a starting point rather than a lock.
{
    await using var scope = app.Services.CreateAsyncScope();
    var smtpRepo = scope.ServiceProvider.GetRequiredService<SmtpSettingsRepository>();

    if (await smtpRepo.GetAsync() is null)
    {
        var seed = scope.ServiceProvider.GetRequiredService<IOptions<SmtpSettings>>().Value;

        if (!string.IsNullOrWhiteSpace(seed.FromAddress) && !string.IsNullOrWhiteSpace(seed.Host))
        {
            await smtpRepo.UpsertAsync(seed);
            Log.Information("Seeded mail server configuration from the environment ({Host}:{Port}).", seed.Host, seed.Port);
        }
    }
}

// Schedule recap trigger only on first run (persistent store preserves it across restarts)
{
    await using var scope = app.Services.CreateAsyncScope();
    var schedulerService = scope.ServiceProvider.GetRequiredService<ISchedulerService>();

    if (schedulerService.GetNextFireTimeUtc() is null)
    {
        var userRepo = scope.ServiceProvider.GetRequiredService<UserRepository>();
        var settingsRepo = scope.ServiceProvider.GetRequiredService<SettingsRepository>();

        var userId = await userRepo.EnsureUserAsync();
        var settings = await settingsRepo.GetByUserIdAsync(userId);

        // Default the timezone to the host machine's local timezone on first run
        settings.Timezone = TimeZoneInfo.Local.Id;
        await settingsRepo.UpsertAsync(settings);

        await schedulerService.ScheduleAsync(settings);
    }
}

Log.Information("Relego server started. Database: {DbPath}", dbPath);

await app.RunAsync();

static Dictionary<string, string?> GetLegacySmtpEnvironmentOverrides()
{
    var overrides = new Dictionary<string, string?>();

    AddOverride(overrides, "Smtp:Host", "SMTP_HOST");
    AddOverride(overrides, "Smtp:Port", "SMTP_PORT");
    AddOverride(overrides, "Smtp:FromAddress", "SMTP_FROM_ADDRESS");
    AddOverride(overrides, "Smtp:Username", "SMTP_USER");
    AddOverride(overrides, "Smtp:Password", "SMTP_PASSWORD");

    return overrides;
}

static void IncludeXmlCommentsIfPresent(Swashbuckle.AspNetCore.SwaggerGen.SwaggerGenOptions options, Assembly assembly)
{
    var xmlFile = $"{assembly.GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);

    if (File.Exists(xmlPath))
        options.IncludeXmlComments(xmlPath);
}

static void AddOverride(Dictionary<string, string?> overrides, string configurationKey, string environmentVariableName)
{
    var value = Environment.GetEnvironmentVariable(environmentVariableName);
    if (!string.IsNullOrWhiteSpace(value))
    {
        overrides[configurationKey] = value;
        return;
    }
}
