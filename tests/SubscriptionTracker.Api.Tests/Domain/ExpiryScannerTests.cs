using FluentAssertions;
using SubscriptionTracker.Api.Domain;
using SubscriptionTracker.Contracts;
using Xunit;

namespace SubscriptionTracker.Api.Tests.Domain;

public class ExpiryScannerTests
{
    private static readonly DateOnly Today = new(2026, 6, 18);

    private static Subscription Sub(int daysUntil, NotifyChannel ch = NotifyChannel.Discord,
        int notifyBefore = 7, DateOnly? lastNotified = null) =>
        new(Guid.NewGuid(), "Netflix", 390m, "TWD", BillingCycle.Monthly,
            Today.AddDays(daysUntil), notifyBefore, ch, lastNotified);

    [Fact]
    public void Includes_subscription_within_notify_window()
    {
        var id = Guid.NewGuid();
        var sub = new Subscription(id, "Netflix", 390m, "TWD", BillingCycle.Monthly,
            Today.AddDays(3), 7, NotifyChannel.Discord, null);

        var result = ExpiryScanner.FindDue(new[] { sub }, Today);

        result.Should().HaveCount(1);
        result[0].SubscriptionId.Should().Be(id);
        result[0].ServiceName.Should().Be("Netflix");
        result[0].Cost.Should().Be(390m);
        result[0].Currency.Should().Be("TWD");
        result[0].NextRenewalDate.Should().Be(Today.AddDays(3));
        result[0].DaysUntil.Should().Be(3);
        result[0].Channels.Should().Be(NotifyChannel.Discord);
    }

    [Fact]
    public void Includes_subscription_due_today()
    {
        var result = ExpiryScanner.FindDue(new[] { Sub(0) }, Today);
        result.Should().HaveCount(1);
        result[0].DaysUntil.Should().Be(0);
    }

    [Fact]
    public void Excludes_subscription_outside_window()
    {
        ExpiryScanner.FindDue(new[] { Sub(10) }, Today).Should().BeEmpty();
    }

    [Fact]
    public void Excludes_already_expired()
    {
        ExpiryScanner.FindDue(new[] { Sub(-1) }, Today).Should().BeEmpty();
    }

    [Fact]
    public void Excludes_already_notified_today()
    {
        ExpiryScanner.FindDue(new[] { Sub(3, lastNotified: Today) }, Today).Should().BeEmpty();
    }

    [Fact]
    public void Excludes_channel_none()
    {
        ExpiryScanner.FindDue(new[] { Sub(3, NotifyChannel.None) }, Today).Should().BeEmpty();
    }
}
