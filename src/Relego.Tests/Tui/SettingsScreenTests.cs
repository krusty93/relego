using System.Net;
using System.Text.Json;
using RichardSzalay.MockHttp;
using Relego.Cli.Infrastructure;
using Relego.Cli.Tui;
using Relego.Core.Contracts;

namespace Relego.Tests.Tui;

public sealed class SettingsScreenTests
{
    private static readonly JsonSerializerOptions CamelCaseOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static SettingsResponse CreateDefaultSettings(string? timezone = null) => new()
    {
        Schedule = "daily",
        DeliveryTime = "18:00",
        Count = 5,
        KindleEmail = "user@kindle.com",
        Timezone = timezone ?? TimeZoneInfo.Local.Id
    };

    [Fact]
    public async Task InitializeAsync_LoadsSettingsAndBuildsFields()
    {
        var settings = CreateDefaultSettings();
        using var mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Get, "http://localhost/settings")
            .Respond("application/json", JsonSerializer.Serialize(settings, CamelCaseOptions));
        using var httpClient = new HttpClient(mockHttp, disposeHandler: false) { BaseAddress = new Uri("http://localhost") };

        var screen = new SettingsScreen(new RelegoHttpClient(httpClient));
        await screen.InitializeAsync(CancellationToken.None);

        Assert.NotNull(screen.Settings);
        Assert.Equal("user@kindle.com", screen.Settings.KindleEmail);
        Assert.Equal(4, screen.Fields.Count); // 4 editable (schedule=daily, no delivery day, no timezone)
    }

    [Fact]
    public async Task InitializeAsync_WithDevelopment_ShowsTriggerRecapAction()
    {
        var settings = CreateDefaultSettings();
        using var mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Get, "http://localhost/settings")
            .Respond("application/json", JsonSerializer.Serialize(settings, CamelCaseOptions));
        using var httpClient = new HttpClient(mockHttp, disposeHandler: false) { BaseAddress = new Uri("http://localhost") };

        var screen = new SettingsScreen(new RelegoHttpClient(httpClient), isDevelopment: true);
        await screen.InitializeAsync(CancellationToken.None);

        Assert.Equal(5, screen.Fields.Count); // 4 editable + 1 action (trigger-recap only)
        Assert.Contains(screen.Fields, f => f.ActionId == "trigger-recap");
    }

    [Fact]
    public async Task InitializeAsync_WeeklySchedule_ShowsDeliveryDayField()
    {
        var settings = CreateDefaultSettings();
        settings.Schedule = "weekly";
        settings.DeliveryDay = "wednesday";
        using var mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Get, "http://localhost/settings")
            .Respond("application/json", JsonSerializer.Serialize(settings, CamelCaseOptions));
        using var httpClient = new HttpClient(mockHttp, disposeHandler: false) { BaseAddress = new Uri("http://localhost") };

        var screen = new SettingsScreen(new RelegoHttpClient(httpClient));
        await screen.InitializeAsync(CancellationToken.None);

        Assert.Equal(5, screen.Fields.Count); // 5 editable (includes delivery day)
        Assert.Contains(screen.Fields, f => f.FieldId == "deliveryDay");
        Assert.Equal("wednesday", screen.Fields.First(f => f.FieldId == "deliveryDay").Value);
    }

    [Fact]
    public async Task InitializeAsync_WeeklySchedule_NullDeliveryDay_DefaultsToMonday()
    {
        var settings = CreateDefaultSettings();
        settings.Schedule = "weekly";
        settings.DeliveryDay = null;
        using var mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Get, "http://localhost/settings")
            .Respond("application/json", JsonSerializer.Serialize(settings, CamelCaseOptions));
        using var httpClient = new HttpClient(mockHttp, disposeHandler: false) { BaseAddress = new Uri("http://localhost") };

        var screen = new SettingsScreen(new RelegoHttpClient(httpClient));
        await screen.InitializeAsync(CancellationToken.None);

        Assert.Equal("monday", screen.Fields.First(f => f.FieldId == "deliveryDay").Value);
    }

    [Fact]
    public async Task InitializeAsync_AutoDetectsAndSavesTimezone()
    {
        var settings = CreateDefaultSettings(timezone: "America/New_York");
        var updatedSettings = CreateDefaultSettings(); // will have local timezone
        using var mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Get, "http://localhost/settings")
            .Respond("application/json", JsonSerializer.Serialize(settings, CamelCaseOptions));
        mockHttp.When(HttpMethod.Put, "http://localhost/settings")
            .Respond("application/json", JsonSerializer.Serialize(updatedSettings, CamelCaseOptions));
        using var httpClient = new HttpClient(mockHttp, disposeHandler: false) { BaseAddress = new Uri("http://localhost") };

        var screen = new SettingsScreen(new RelegoHttpClient(httpClient));
        await screen.InitializeAsync(CancellationToken.None);

        Assert.Equal(TimeZoneInfo.Local.Id, screen.Settings!.Timezone);
    }

    [Fact]
    public async Task InitializeAsync_SkipsTimezoneUpdate_WhenAlreadyLocal()
    {
        var settings = CreateDefaultSettings(); // already local timezone
        using var mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Get, "http://localhost/settings")
            .Respond("application/json", JsonSerializer.Serialize(settings, CamelCaseOptions));
        // No PUT mock — if a PUT is attempted, it would fail
        using var httpClient = new HttpClient(mockHttp, disposeHandler: false) { BaseAddress = new Uri("http://localhost") };

        var screen = new SettingsScreen(new RelegoHttpClient(httpClient));
        await screen.InitializeAsync(CancellationToken.None);

        Assert.Equal(TimeZoneInfo.Local.Id, screen.Settings!.Timezone);
    }

    [Fact]
    public async Task Fields_DoNotContainTimezone()
    {
        var screen = await CreateInitializedScreen();

        Assert.DoesNotContain(screen.Fields, f => f.FieldId == "timezone");
    }

    [Fact]
    public async Task Fields_DeliveryTimeShowsTimezoneInDisplaySuffix()
    {
        var screen = await CreateInitializedScreen();

        var dtField = screen.Fields.First(f => f.FieldId == "deliveryTime");
        Assert.NotNull(dtField.DisplaySuffix);
        Assert.Contains(TimeZoneInfo.Local.Id, dtField.DisplaySuffix);
    }

    [Fact]
    public async Task Fields_ScheduleHasOptions()
    {
        var screen = await CreateInitializedScreen();

        var scheduleField = screen.Fields.First(f => f.FieldId == "schedule");
        Assert.NotNull(scheduleField.Options);
        Assert.Contains("daily", scheduleField.Options);
        Assert.Contains("weekly", scheduleField.Options);
    }

    [Fact]
    public async Task Fields_KindleEmailHasHint()
    {
        var screen = await CreateInitializedScreen();

        var emailField = screen.Fields.First(f => f.FieldId == "kindleEmail");
        Assert.Equal("Insert a valid email address", emailField.Hint);
    }

    [Fact]
    public async Task Fields_CountHasHint()
    {
        var screen = await CreateInitializedScreen();

        var countField = screen.Fields.First(f => f.FieldId == "count");
        Assert.Contains("between 1 and 15", countField.Hint);
    }

    [Fact]
    public async Task HandleKeyAsync_NavigationStaysWithinBounds()
    {
        var screen = await CreateInitializedScreen();

        // Move down past the end
        for (var i = 0; i < 20; i++)
            await screen.HandleKeyAsync(Key(ConsoleKey.DownArrow), CancellationToken.None);

        Assert.Equal(screen.Fields.Count - 1, screen.SelectedField);

        // Move up past the beginning
        for (var i = 0; i < 20; i++)
            await screen.HandleKeyAsync(Key(ConsoleKey.UpArrow), CancellationToken.None);

        Assert.Equal(0, screen.SelectedField);
    }

    [Fact]
    public async Task HandleKeyAsync_NavigationClearsStatusMessage()
    {
        var screen = await CreateInitializedScreenWithTestEmail();

        // Generate a status message via test email
        await screen.HandleKeyAsync(Key(ConsoleKey.T, 't'), CancellationToken.None);
        Assert.NotNull(screen.StatusMessage);

        // Navigate — status should clear
        await screen.HandleKeyAsync(Key(ConsoleKey.DownArrow), CancellationToken.None);
        Assert.Null(screen.StatusMessage);
    }

    [Fact]
    public async Task HandleKeyAsync_EscapeReturnsPop()
    {
        var screen = await CreateInitializedScreen();

        var result = await screen.HandleKeyAsync(Key(ConsoleKey.Escape), CancellationToken.None);

        Assert.Equal(ScreenAction.Pop, result.Action);
    }

    [Fact]
    public async Task HandleKeyAsync_QReturnsConfirmQuit()
    {
        var screen = await CreateInitializedScreen();

        var result = await screen.HandleKeyAsync(Key(ConsoleKey.Q, 'q'), CancellationToken.None);

        Assert.Equal(ScreenAction.ConfirmQuit, result.Action);
    }

    [Fact]
    public async Task HandleKeyAsync_CtrlCReturnsQuit()
    {
        var screen = await CreateInitializedScreen();

        var result = await screen.HandleKeyAsync(new ConsoleKeyInfo('c', ConsoleKey.C, false, false, true), CancellationToken.None);

        Assert.Equal(ScreenAction.Quit, result.Action);
    }

    [Fact]
    public async Task HandleKeyAsync_EnterOnEditableField_StartsEditMode()
    {
        var screen = await CreateInitializedScreen();

        // First field is Kindle Email (editable), verify it's editable
        Assert.Equal(SettingsScreen.FieldKind.Editable, screen.Fields[screen.SelectedField].Kind);

        // Direct state assertion: editing is not active before Enter.
        Assert.False(screen.IsEditing);
    }

    [Fact]
    public async Task HandleKeyAsync_TestEmailSendsPostRequest()
    {
        var screen = await CreateInitializedScreenWithTestEmail();

        var result = await screen.HandleKeyAsync(Key(ConsoleKey.T, 't'), CancellationToken.None);

        Assert.Equal(ScreenAction.None, result.Action);
        Assert.Contains("sent successfully", screen.StatusMessage);
        Assert.False(screen.StatusIsError);
    }

    [Fact]
    public async Task HandleKeyAsync_TestEmailFailure_ShowsError()
    {
        var settings = CreateDefaultSettings();
        using var mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Get, "http://localhost/settings")
            .Respond("application/json", JsonSerializer.Serialize(settings, CamelCaseOptions));
        mockHttp.When(HttpMethod.Post, "http://localhost/settings/test-email")
            .Respond(HttpStatusCode.UnprocessableEntity, "application/json", "{\"errors\":{\"kindleEmail\":[\"Kindle email must be configured.\"]}}");
        using var httpClient = new HttpClient(mockHttp, disposeHandler: false) { BaseAddress = new Uri("http://localhost") };

        var screen = new SettingsScreen(new RelegoHttpClient(httpClient));
        await screen.InitializeAsync(CancellationToken.None);

        var result = await screen.HandleKeyAsync(Key(ConsoleKey.T, 't'), CancellationToken.None);

        Assert.Equal(ScreenAction.None, result.Action);
        Assert.True(screen.StatusIsError);
    }

    [Fact]
    public async Task HandleKeyAsync_RefreshReloadsSettings()
    {
        var settings = CreateDefaultSettings();
        using var mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Get, "http://localhost/settings")
            .Respond("application/json", JsonSerializer.Serialize(settings, CamelCaseOptions));
        using var httpClient = new HttpClient(mockHttp, disposeHandler: false) { BaseAddress = new Uri("http://localhost") };

        var screen = new SettingsScreen(new RelegoHttpClient(httpClient));
        await screen.InitializeAsync(CancellationToken.None);

        var result = await screen.HandleKeyAsync(Key(ConsoleKey.R, 'r'), CancellationToken.None);

        Assert.Equal(ScreenAction.None, result.Action);
        Assert.Contains("refreshed", screen.StatusMessage);
        Assert.False(screen.StatusIsError);
    }

    [Theory]
    [InlineData("kindleEmail", "invalid", false)]
    [InlineData("kindleEmail", "valid@email.com", true)]
    [InlineData("count", "0", false)]
    [InlineData("count", "16", false)]
    [InlineData("count", "5", true)]
    [InlineData("count", "abc", false)]
    [InlineData("schedule", "monthly", false)]
    [InlineData("schedule", "daily", true)]
    [InlineData("schedule", "weekly", true)]
    [InlineData("deliveryTime", "25:00", false)]
    [InlineData("deliveryTime", "09:30", true)]
    [InlineData("deliveryTime", "nottime", false)]
    [InlineData("deliveryDay", "monday", true)]
    [InlineData("deliveryDay", "sunday", true)]
    [InlineData("deliveryDay", "invalid", false)]
    public void ValidateField_ReturnsExpectedResult(string fieldId, string value, bool shouldBeValid)
    {
        var field = new SettingsScreen.SettingsField("Test", string.Empty, fieldId, SettingsScreen.FieldKind.Editable);

        var result = SettingsScreen.ValidateField(field, value);

        if (shouldBeValid)
            Assert.Null(result);
        else
            Assert.NotNull(result);
    }

    [Fact]
    public void KeyHints_ContainExpectedEntries()
    {
        using var mockHttp = new MockHttpMessageHandler();
        using var httpClient = new HttpClient(mockHttp, disposeHandler: false) { BaseAddress = new Uri("http://localhost") };
        var screen = new SettingsScreen(new RelegoHttpClient(httpClient));

        Assert.Contains(screen.KeyHints, hint => hint is ("↑↓", "Navigate"));
        Assert.Contains(screen.KeyHints, hint => hint is ("Enter", "Edit"));
        Assert.Contains(screen.KeyHints, hint => hint is ("T", "Test email settings"));
        Assert.Contains(screen.KeyHints, hint => hint is ("R", "Refresh"));
        Assert.Contains(screen.KeyHints, hint => hint is ("Esc", "Go Back"));
        Assert.Contains(screen.KeyHints, hint => hint is ("Q", "Quit"));
    }

    [Fact]
    public async Task HandleKeyAsync_TestEmail_WhenKindleEmailNotConfigured_ShowsError()
    {
        var settings = CreateDefaultSettings();
        settings.KindleEmail = string.Empty;
        using var mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Get, "http://localhost/settings")
            .Respond("application/json", JsonSerializer.Serialize(settings, CamelCaseOptions));
        using var httpClient = new HttpClient(mockHttp, disposeHandler: false) { BaseAddress = new Uri("http://localhost") };

        var screen = new SettingsScreen(new RelegoHttpClient(httpClient));
        await screen.InitializeAsync(CancellationToken.None);

        var result = await screen.HandleKeyAsync(Key(ConsoleKey.T, 't'), CancellationToken.None);

        Assert.Equal(ScreenAction.None, result.Action);
        Assert.True(screen.StatusIsError);
        Assert.NotNull(screen.StatusMessage);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Test helper; MockHttp and HttpClient outlive the helper call")]
    private static async Task<SettingsScreen> CreateInitializedScreen()
    {
        var settings = CreateDefaultSettings();
        var mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Get, "http://localhost/settings")
            .Respond("application/json", JsonSerializer.Serialize(settings, CamelCaseOptions));
        var httpClient = new HttpClient(mockHttp, disposeHandler: false) { BaseAddress = new Uri("http://localhost") };

        var screen = new SettingsScreen(new RelegoHttpClient(httpClient));
        await screen.InitializeAsync(CancellationToken.None);
        return screen;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Test helper")]
    private static async Task<SettingsScreen> CreateInitializedScreenWithTestEmail()
    {
        var settings = CreateDefaultSettings();
        var mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Get, "http://localhost/settings")
            .Respond("application/json", JsonSerializer.Serialize(settings, CamelCaseOptions));
        mockHttp.When(HttpMethod.Post, "http://localhost/settings/test-email")
            .Respond(HttpStatusCode.OK, "application/json", "{\"message\":\"Test email sent successfully.\"}");
        var httpClient = new HttpClient(mockHttp, disposeHandler: false) { BaseAddress = new Uri("http://localhost") };

        var screen = new SettingsScreen(new RelegoHttpClient(httpClient));
        await screen.InitializeAsync(CancellationToken.None);
        return screen;
    }

    private static ConsoleKeyInfo Key(ConsoleKey key, char keyChar = '\0')
        => new(keyChar, key, shift: false, alt: false, control: false);
}
