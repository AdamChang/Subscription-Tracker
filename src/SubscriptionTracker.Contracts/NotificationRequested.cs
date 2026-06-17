namespace SubscriptionTracker.Contracts;

public record NotificationRequested(
    Guid SubscriptionId,
    string ServiceName,
    decimal Cost,
    string Currency,
    DateOnly NextRenewalDate,
    int DaysUntil,
    NotifyChannel Channels);
