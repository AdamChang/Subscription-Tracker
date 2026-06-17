namespace SubscriptionTracker.Contracts;

public enum BillingCycle { Monthly, Yearly }

[Flags]
public enum NotifyChannel { None = 0, Discord = 1, Email = 2 }

public record Subscription(
    Guid Id,
    string ServiceName,
    decimal Cost,
    string Currency,
    BillingCycle Cycle,
    DateOnly NextRenewalDate,
    int NotifyDaysBefore,
    NotifyChannel Channels,
    DateOnly? LastNotifiedOn);
