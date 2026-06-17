using SubscriptionTracker.Api.Domain;
using SubscriptionTracker.Api.State;

namespace SubscriptionTracker.Api.Endpoints;

public static class StatsEndpoints
{
    public static void MapStats(this WebApplication app)
    {
        app.MapGet("/stats/monthly", async (ISubscriptionStore store) =>
        {
            var all = await store.GetAllAsync();
            return Results.Ok(MonthlyCostCalculator.MonthlyTotals(all));
        });
    }
}
