using Microsoft.AspNetCore.Mvc;
using Relego.Core.Contracts;
using Relego.Server.Data;

namespace Relego.Server.Endpoints;

public static class TagEndpoints
{
    public static WebApplication MapTagEndpoints(this WebApplication app)
    {
        app.MapGet("/tags", async (
            [FromServices] UserRepository userRepo,
            [FromServices] HighlightRepository highlightRepo) =>
        {
            var userId = await userRepo.EnsureUserAsync();
            var result = await highlightRepo.GetTagsAsync(userId);
            return Results.Ok(result);
        })
        .WithTags("Tags")
        .WithSummary("List tags.")
        .WithDescription("Returns every tag of the library, grouped case-insensitively, with the spelling of its lowest annotation id and the number of annotations carrying it. Tags are ordered by count descending, then by name.")
        .Produces<TagsResponse>(StatusCodes.Status200OK);

        return app;
    }
}
