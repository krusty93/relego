using Microsoft.AspNetCore.Mvc;
using Relego.Core.Contracts;
using Relego.Server.Sync;

namespace Relego.Server.Endpoints;

/// <summary>
/// Maps the cloud sync REST endpoints. Providers are resolved through
/// <see cref="IEnumerable{ICloudSyncProvider}"/>; nothing branches on a provider id.
/// </summary>
public static class CloudSyncEndpoints
{
    /// <summary>Registers the cloud sync endpoints on the application.</summary>
    /// <param name="app">The web application.</param>
    /// <returns>The same application for chaining.</returns>
    public static WebApplication MapCloudSyncEndpoints(this WebApplication app)
    {
        app.MapGet("/sync/providers", ([FromServices] IEnumerable<ICloudSyncProvider> providers) =>
        {
            var summaries = providers
                .Select(provider => new SyncProviderSummary
                {
                    ProviderId = provider.Descriptor.Id,
                    DisplayName = provider.Descriptor.DisplayName,
                    Capabilities = new SyncProviderCapabilitiesDto
                    {
                        PushIngest = provider.Capabilities.HasFlag(CloudSyncCapabilities.PushIngest),
                        FullResync = provider.Capabilities.HasFlag(CloudSyncCapabilities.FullResync),
                        ProgressReporting = provider.Capabilities.HasFlag(CloudSyncCapabilities.ProgressReporting)
                    },
                    InactivityReminderDays = provider.InactivityReminderDays,
                    DisclosureVersion = provider.Disclosure.Version
                })
                .ToList();

            return Results.Ok(summaries);
        })
        .WithTags("Sync")
        .WithSummary("List registered cloud sync providers.")
        .WithDescription("Returns every registered cloud sync provider with its capabilities, disclosure version and inactivity reminder threshold.")
        .Produces<IReadOnlyList<SyncProviderSummary>>(StatusCodes.Status200OK);

        return app;
    }
}
