using FluentAssertions;
using SubscriptionTracker.Api.Domain;
using SubscriptionTracker.Contracts;
using Xunit;

namespace SubscriptionTracker.Api.Tests.Domain;

public class MonthlyCostCalculatorTests
{
    private static Subscription Sub(decimal cost, BillingCycle cycle, string currency = "TWD") =>
        new(Guid.NewGuid(), "X", cost, currency, cycle, new DateOnly(2026, 7, 1),
            7, NotifyChannel.Email, null);

    [Fact]
    public void Sums_monthly_costs_by_currency()
    {
        var totals = MonthlyCostCalculator.MonthlyTotals(new[]
        {
            Sub(390m, BillingCycle.Monthly),
            Sub(1200m, BillingCycle.Yearly) // 折算每月 100
        });
        totals["TWD"].Should().Be(490m);
    }

    [Fact]
    public void Groups_separate_currencies()
    {
        var totals = MonthlyCostCalculator.MonthlyTotals(new[]
        {
            Sub(390m, BillingCycle.Monthly, "TWD"),
            Sub(10m, BillingCycle.Monthly, "USD")
        });
        totals.Should().HaveCount(2);
        totals["USD"].Should().Be(10m);
    }

    [Fact]
    public void Empty_returns_empty()
    {
        MonthlyCostCalculator.MonthlyTotals(Array.Empty<Subscription>()).Should().BeEmpty();
    }
}
