using SubscriptionTracker.Contracts;

namespace SubscriptionTracker.Api.Domain;

public static class MonthlyCostCalculator
{
    public static IReadOnlyDictionary<string, decimal> MonthlyTotals(
        IEnumerable<Subscription> subscriptions) =>
        subscriptions
            .GroupBy(s => s.Currency)
            .ToDictionary(
                g => g.Key,
                g => g.Sum(s => s.Cycle == BillingCycle.Yearly ? s.Cost / 12m : s.Cost));
}
