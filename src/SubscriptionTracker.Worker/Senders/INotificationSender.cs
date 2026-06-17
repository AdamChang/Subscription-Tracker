using SubscriptionTracker.Contracts;

namespace SubscriptionTracker.Worker.Senders;

public interface INotificationSender
{
    NotifyChannel Channel { get; }
    Task SendAsync(NotificationRequested evt);
}
