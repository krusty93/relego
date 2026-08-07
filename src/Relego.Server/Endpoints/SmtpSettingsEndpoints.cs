using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Relego.Core.Contracts;
using Relego.Server.Data;
using Relego.Server.Infrastructure.Smtp;
using Relego.Server.Services;

namespace Relego.Server.Endpoints;

/// <summary>
/// Outgoing mail server configuration. Kept separate from <see cref="SettingsEndpoints"/>
/// because these values are server-wide infrastructure, not per-user recap preferences.
/// </summary>
public static partial class SmtpSettingsEndpoints
{
    public static WebApplication MapSmtpSettingsEndpoints(this WebApplication app)
    {
        app.MapGet("/settings/smtp", async ([FromServices] SmtpConfigurationService smtp) =>
        {
            var effective = await smtp.GetEffectiveAsync();
            return Results.Ok(SmtpConfigurationService.ToResponse(effective));
        })
        .WithTags("Settings")
        .WithSummary("Get the outgoing mail server configuration.")
        .WithDescription(
            "Returns the mail server settings currently in effect and where they come from. " +
            "The password is write-only and is reported only as a boolean.")
        .Produces<SmtpSettingsResponse>(StatusCodes.Status200OK);

        app.MapPut("/settings/smtp", async (
            UpdateSmtpSettingsRequest? request,
            [FromServices] SmtpConfigurationService smtp) =>
        {
            if (request is null)
            {
                return Results.ValidationProblem(
                    new Dictionary<string, string[]> { ["request"] = ["A request body is required."] },
                    statusCode: StatusCodes.Status422UnprocessableEntity);
            }

            var errors = new Dictionary<string, string[]>();

            if (request.Host is not null && string.IsNullOrWhiteSpace(request.Host))
                errors["host"] = ["Host must not be empty."];

            if (request.Port is { } port && port is < 1 or > 65535)
                errors["port"] = ["Port must be between 1 and 65535."];

            if (request.FromAddress is not null && !IsValidEmail(request.FromAddress))
                errors["fromAddress"] = ["From address must be a valid email address."];

            if (errors.Count > 0)
                return Results.ValidationProblem(errors, statusCode: StatusCodes.Status422UnprocessableEntity);

            var saved = await smtp.SaveAsync(request);
            return Results.Ok(SmtpConfigurationService.ToResponse(saved));
        })
        .WithTags("Settings")
        .WithSummary("Save the outgoing mail server configuration.")
        .WithDescription(
            "Stores the mail server settings in the database. Once saved, the stored values take " +
            "precedence over the SMTP_* environment variables. Omit 'password' to keep the stored " +
            "password; send an empty string to clear it.")
        .Accepts<UpdateSmtpSettingsRequest>("application/json")
        .Produces<SmtpSettingsResponse>(StatusCodes.Status200OK)
        .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);

        app.MapPost("/settings/smtp/test", async (
            SmtpTestRequest? request,
            [FromServices] SmtpConfigurationService smtp,
            [FromServices] UserRepository userRepo,
            [FromServices] IMailDeliveryService mailService) =>
        {
            var effective = await smtp.GetEffectiveAsync();

            if (string.IsNullOrWhiteSpace(effective.Settings.Host) ||
                string.IsNullOrWhiteSpace(effective.Settings.FromAddress))
            {
                return Results.ValidationProblem(
                    new Dictionary<string, string[]>
                    {
                        ["smtp"] = ["Set a host and a from address before sending a test message."],
                    },
                    statusCode: StatusCodes.Status422UnprocessableEntity);
            }

            var userId = await userRepo.EnsureUserAsync();
            var user = await userRepo.GetByIdAsync(userId);

            var toAddress = FirstNonEmpty(
                request?.ToAddress,
                user.DeliveryEmail,
                user.KindleEmail,
                effective.Settings.FromAddress);

            if (toAddress is null || !IsValidEmail(toAddress))
            {
                return Results.ValidationProblem(
                    new Dictionary<string, string[]>
                    {
                        ["toAddress"] = ["Provide a valid address to send the test message to."],
                    },
                    statusCode: StatusCodes.Status422UnprocessableEntity);
            }

            try
            {
                await mailService.SendTestEmailAsync(toAddress);
                return Results.Ok(new SmtpTestResponse
                {
                    Success = true,
                    Message = $"Test message sent to {toAddress}.",
                });
            }
            catch (Exception ex) when (IsSmtpException(ex))
            {
                return Results.Problem(
                    detail: ex.Message,
                    title: "The mail server rejected the connection.",
                    statusCode: StatusCodes.Status502BadGateway);
            }
        })
        .WithTags("Settings")
        .WithSummary("Send a test message using the stored mail server configuration.")
        .WithDescription(
            "Sends a short plain-text message to verify the mail server settings. Defaults to the " +
            "configured delivery email, then the Kindle email, then the sender address.")
        .Accepts<SmtpTestRequest>("application/json")
        .Produces<SmtpTestResponse>(StatusCodes.Status200OK)
        .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity)
        .ProducesProblem(StatusCodes.Status502BadGateway);

        return app;
    }

    private static string? FirstNonEmpty(params string?[] candidates)
        => candidates.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c))?.Trim();

    private static bool IsValidEmail(string value)
        => !string.IsNullOrWhiteSpace(value) && EmailRegex().IsMatch(value.Trim());

    [GeneratedRegex(
        "^[A-Z0-9.!#$%&'*+/=?^_`{|}~-]+@[A-Z0-9](?:[A-Z0-9-]{0,61}[A-Z0-9])?(?:\\.[A-Z0-9](?:[A-Z0-9-]{0,61}[A-Z0-9])?)+$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EmailRegex();

    private static bool IsSmtpException(Exception ex) => ex switch
    {
        MailKit.Net.Smtp.SmtpCommandException or
        MailKit.Net.Smtp.SmtpProtocolException or
        MailKit.Security.AuthenticationException or
        System.Net.Sockets.SocketException or
        IOException => true,
        _ => false,
    };
}
