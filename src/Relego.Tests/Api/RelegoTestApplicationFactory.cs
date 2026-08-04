using System.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Quartz;
using Relego.Server.Infrastructure.Database;

namespace Relego.Tests.Api;

// Uses SchemaBootstrap (a public type in the Relego.Server assembly) as the
// WebApplicationFactory entry-point marker instead of the bare `Program`.
// Both Relego.Server and Relego.Cli emit an implicit global `Program` from their
// top-level statements; once Relego.Cli exposes its internals to this test project
// (InternalsVisibleTo), referencing `Program` unqualified is ambiguous (CS0433).
public sealed class RelegoTestApplicationFactory : WebApplicationFactory<SchemaBootstrap>
{
    private readonly SqliteConnection _connection;
    private readonly Action<IWebHostBuilder>? _configureWebHost;
    private readonly string? _webRootPath;

    public RelegoTestApplicationFactory(
        Action<IWebHostBuilder>? configureWebHost = null,
        string? webRootPath = null)
    {
        _configureWebHost = configureWebHost;
        _webRootPath = webRootPath;
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        using var pragma = _connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = ON;";
        pragma.ExecuteNonQuery();
        var bootstrap = new SchemaBootstrap();
        bootstrap.ApplyAsync(_connection).GetAwaiter().GetResult();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        if (!string.IsNullOrWhiteSpace(_webRootPath))
            builder.UseWebRoot(_webRootPath);

        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IDbConnection));
            if (descriptor is not null)
                services.Remove(descriptor);

            services.AddSingleton<IDbConnection>(_ => _connection);

            // Override Quartz to use in-memory store for tests
            // (AdoJobStore can't share the in-memory SQLite connection)
            services.PostConfigure<QuartzOptions>(options =>
            {
                // Remove all persistent store and data source properties
                foreach (var key in options.Keys.Cast<string>().ToList())
                {
                    if (key.StartsWith("quartz.jobStore.") ||
                        key.StartsWith("quartz.dataSource.") ||
                        key == "quartz.serializer.type")
                    {
                        options.Remove(key);
                    }
                }

                options["quartz.jobStore.type"] = "Quartz.Simpl.RAMJobStore, Quartz";
            });
        });

        builder.UseEnvironment("Testing");

        _configureWebHost?.Invoke(builder);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _connection.Dispose();
        base.Dispose(disposing);
    }
}
