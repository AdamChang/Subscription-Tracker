using SubscriptionTracker.Contracts;

namespace SubscriptionTracker.Api.Contracts;

public record CreateSubscriptionRequest(
    string ServiceName,
    decimal Cost,
    string Currency,
    BillingCycle Cycle,
    DateOnly NextRenewalDate,
    int NotifyDaysBefore,
    NotifyChannel Channels);

public record UpdateSubscriptionRequest(
    string ServiceName,
    decimal Cost,
    string Currency,
    BillingCycle Cycle,
    DateOnly NextRenewalDate,
    int NotifyDaysBefore,
    NotifyChannel Channels);
