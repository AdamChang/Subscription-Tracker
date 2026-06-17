using Dapr.Client;
using SubscriptionTracker.Api.Domain;
using SubscriptionTracker.Api.State;

namespace SubscriptionTracker.Api.Endpoints;

public static class JobEndpoints
{
    public static void MapJobs(this WebApplication app)
    {
        // Dapr cron input binding 以 POST /{binding-name} 觸發，binding 名為 cron-check
        app.MapPost("/cron-check", async (ISubscriptionStore store, DaprClient dapr) =>
        {
            var all = await store.GetAllAsync();
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var due = ExpiryScanner.FindDue(all, today);

            foreach (var evt in due)
            {
                await dapr.PublishEventAsync("pubsub", "notifications", evt);
                var sub = all.First(s => s.Id == evt.SubscriptionId)
                    with { LastNotifiedOn = today };
                await store.SaveAsync(sub);
            }
            return Results.Ok(new { notified = due.Count });
        });
    }
}
