using Microsoft.AspNetCore.Mvc;
using Relego.Core.Contracts;
using Relego.Server.Data;
using Relego.Server.Services;

namespace Relego.Server.Endpoints;

public static class StatusEndpoints
{
    public static WebApplication MapStatusEndpoints(this WebApplication app)
    {
        app.MapGet("/status", async ([FromServices] UserRepository userRepo, [FromServices] StatusRepository statusRepo, [FromServices] ISchedulerService schedulerService) =>
        {
            var userId = await userRepo.EnsureUserAsync();
            var user = await userRepo.GetByIdAsync(userId);
            var status = await statusRepo.GetStatusAsync(userId);

            var nextFire = schedulerService.GetNextFireTimeUtc();
            status.NextRecap = nextFire?.ToString("O");
            status.KindleEmailConfigured = !string.IsNullOrWhiteSpace(user.KindleEmail);

            return Results.Ok(status);
        })
        .WithTags("Status")
        .WithSummary("Get server status.")
        .WithDescription("Returns aggregate counts for highlights, books, authors, and exclusions for the implicit MVP user, along with the next scheduled recap time.")
        .Produces<StatusResponse>(StatusCodes.Status200OK);

        return app;
    }
}
