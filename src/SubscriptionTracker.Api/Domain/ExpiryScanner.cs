using SubscriptionTracker.Contracts;

namespace SubscriptionTracker.Api.Domain;

public static class ExpiryScanner
{
    public static IReadOnlyList<NotificationRequested> FindDue(
        IEnumerable<Subscription> subscriptions, DateOnly today)
    {
        var due = new List<NotificationRequested>();
        foreach (var s in subscriptions)
        {
            var daysUntil = s.NextRenewalDate.DayNumber - today.DayNumber;
            if (daysUntil < 0 || daysUntil > s.NotifyDaysBefore) continue;
            if (s.LastNotifiedOn == today) continue;
            if (s.Channels == NotifyChannel.None) continue;

            due.Add(new NotificationRequested(
                s.Id, s.ServiceName, s.Cost, s.Currency,
                s.NextRenewalDate, daysUntil, s.Channels));
        }
        return due;
    }
}
