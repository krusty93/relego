using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Relego.Core.Contracts;
using Relego.Server.Data;
using Relego.Server.Models;
using Relego.Server.Services;

namespace Relego.Server.Endpoints;

public static partial class SettingsEndpoints
{
    public static WebApplication MapSettingsEndpoints(this WebApplication app)
    {
        app.MapGet("/settings", async ([FromServices] UserRepository userRepo, [FromServices] SettingsRepository settingsRepo) =>
        {
            var userId = await userRepo.EnsureUserAsync();
            var user = await userRepo.GetByIdAsync(userId);
            var settings = await settingsRepo.GetByUserIdAsync(userId);
            return Results.Ok(ToSettingsResponse(user, settings));
        })
        .WithTags("Settings")
        .WithSummary("Read current user settings.")
        .WithDescription("Returns stored settings for the implicit MVP user, or default values when no settings row exists yet.")
        .Produces<SettingsResponse>(StatusCodes.Status200OK);

        app.MapPatch("/settings", async (UpdateSettingsRequest? request, [FromServices] UserRepository userRepo, [FromServices] SettingsRepository settingsRepo, [FromServices] ISchedulerService schedulerService) =>
        {
            if (request is null)
            {
                return Results.ValidationProblem(
                    new Dictionary<string, string[]> { { "body", ["Request body must not be null."] } },
                    statusCode: StatusCodes.Status422UnprocessableEntity);
            }

            var errors = new Dictionary<string, string[]>();
            string? normalizedSchedule = null;
            string? normalizedDeliveryTime = null;
            string? normalizedKindleEmail = null;

            if (request.Schedule is not null && !IsValidSchedule(request.Schedule, out normalizedSchedule))
                errors["schedule"] = ["Schedule must be either 'daily' or 'weekly'."];

            if (request.DeliveryTime is not null && !IsValidDeliveryTime(request.DeliveryTime, out normalizedDeliveryTime))
                errors["deliveryTime"] = ["Delivery time must use HH:mm format."];

            if (request.Count is < 1 or > 15)
                errors["count"] = ["Count must be between 1 and 15."];

            if (request.KindleEmail is not null && !IsValidEmail(request.KindleEmail, out normalizedKindleEmail))
                errors["kindleEmail"] = ["Invalid email format."];

            string? normalizedDeliveryEmail = null;
            if (request.DeliveryEmail is not null)
            {
                var trimmed = request.DeliveryEmail.Trim();
                if (trimmed.Length == 0)
                {
                    normalizedDeliveryEmail = null; // empty string → clear
                }
                else if (!IsValidEmail(request.DeliveryEmail, out normalizedDeliveryEmail))
                {
                    errors["deliveryEmail"] = ["Invalid email format."];
                }
            }

            if (request.Timezone is not null && !IsValidTimezone(request.Timezone))
                errors["timezone"] = ["Invalid IANA timezone identifier."];

            if (errors.Count > 0)
                return Results.ValidationProblem(errors, statusCode: StatusCodes.Status422UnprocessableEntity);

            var userId = await userRepo.EnsureUserAsync();
            var user = await userRepo.GetByIdAsync(userId);
            var settings = await settingsRepo.GetByUserIdAsync(userId);

            ApplySettingsUpdate(request, settings, user, normalizedSchedule, normalizedDeliveryTime, normalizedKindleEmail, normalizedDeliveryEmail);

            await userRepo.UpdateKindleEmailAsync(user.Id, user.KindleEmail);
            if (request.DeliveryEmail is not null)
                await userRepo.UpdateDeliveryEmailAsync(user.Id, normalizedDeliveryEmail);
            await settingsRepo.UpsertAsync(settings);

            await schedulerService.ScheduleAsync(settings);

            return Results.Ok(ToSettingsResponse(user, settings));
        })
        .WithTags("Settings")
        .WithSummary("Partially update user settings.")
        .WithDescription("Applies a partial update to the implicit MVP user settings. Only provided fields are changed.")
        .Accepts<UpdateSettingsRequest>("application/json")
        .Produces<SettingsResponse>(StatusCodes.Status200OK)
        .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);

        app.MapPost("/settings/test-email", async ([FromServices] UserRepository userRepo, [FromServices] IMailDeliveryService mailService) =>
        {
            var userId = await userRepo.EnsureUserAsync();
            var user = await userRepo.GetByIdAsync(userId);

            if (string.IsNullOrWhiteSpace(user.KindleEmail))
            {
                return Results.ValidationProblem(
                    new Dictionary<string, string[]> { { "kindleEmail", ["Kindle email must be configured before sending a test email."] } },
                    statusCode: StatusCodes.Status422UnprocessableEntity);
            }

            try
            {
                await mailService.SendTestEmailAsync(user.KindleEmail);
                return Results.Ok(new { message = "Test email sent successfully." });
            }
            catch (Exception ex) when (ex is MailKit.Net.Smtp.SmtpCommandException or MailKit.Net.Smtp.SmtpProtocolException or System.Net.Sockets.SocketException or IOException)
            {
                return Results.Problem(
                    detail: ex.Message,
                    title: "SMTP delivery failed.",
                    statusCode: StatusCodes.Status502BadGateway);
            }
            catch (Exception)
            {
                return Results.Problem(
                    detail: null,
                    title: "Test email failed due to an internal error.",
                    statusCode: StatusCodes.Status500InternalServerError);
            }
        })
        .WithTags("Settings")
        .WithSummary("Send a plain-text test email.")
        .WithDescription("Sends a simple verification email to the configured Kindle email address without generating a recap.")
        .Produces(StatusCodes.Status200OK)
        .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity)
        .ProducesProblem(StatusCodes.Status502BadGateway)
        .ProducesProblem(StatusCodes.Status500InternalServerError);

        return app;
    }

    private static void ApplySettingsUpdate(
        UpdateSettingsRequest request,
        Settings settings,
        User user,
        string? normalizedSchedule,
        string? normalizedDeliveryTime,
        string? normalizedKindleEmail,
        string? normalizedDeliveryEmail)
    {
        settings.Schedule = normalizedSchedule ?? settings.Schedule;
        settings.DeliveryDay = request.DeliveryDay is null ? settings.DeliveryDay : NormalizeDeliveryDay(request.DeliveryDay);
        settings.DeliveryTime = normalizedDeliveryTime ?? settings.DeliveryTime;
        settings.Count = request.Count ?? settings.Count;
        settings.Timezone = request.Timezone?.Trim() ?? settings.Timezone;
        user.KindleEmail = normalizedKindleEmail ?? user.KindleEmail;
        if (request.DeliveryEmail is not null)
            user.DeliveryEmail = normalizedDeliveryEmail;
    }

    private static SettingsResponse ToSettingsResponse(User user, Settings settings)
    {
        return new SettingsResponse
        {
            Schedule = settings.Schedule,
            DeliveryDay = settings.DeliveryDay,
            DeliveryTime = settings.DeliveryTime,
            Count = settings.Count,
            KindleEmail = user.KindleEmail,
            Timezone = settings.Timezone,
            DeliveryEmail = user.DeliveryEmail
        };
    }

    private static bool IsValidSchedule(string value, out string normalized)
    {
        normalized = value.Trim().ToLowerInvariant();
        return normalized is "daily" or "weekly";
    }

    private static bool IsValidDeliveryTime(string value, out string normalized)
    {
        normalized = value.Trim();
        return TimeOnly.TryParseExact(normalized, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
    }

    private static bool IsValidEmail(string value, out string normalized)
    {
        normalized = value.Trim();

        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        return EmailRegex().IsMatch(normalized);
    }

    [GeneratedRegex("^[A-Z0-9.!#$%&'*+/=?^_`{|}~-]+@[A-Z0-9](?:[A-Z0-9-]{0,61}[A-Z0-9])?(?:\\.[A-Z0-9](?:[A-Z0-9-]{0,61}[A-Z0-9])?)+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EmailRegex();

    private static string? NormalizeDeliveryDay(string value)
    {
        var normalized = value.Trim();
        return string.IsNullOrWhiteSpace(normalized)
            ? null
            : normalized.ToLowerInvariant();
    }

    private static bool IsValidTimezone(string value)
    {
        try
        {
            TimeZoneInfo.FindSystemTimeZoneById(value.Trim());
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            return false;
        }
    }
}
