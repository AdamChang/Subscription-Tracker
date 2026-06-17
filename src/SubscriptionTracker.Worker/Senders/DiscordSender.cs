using Dapr.Client;
using SubscriptionTracker.Contracts;

namespace SubscriptionTracker.Worker.Senders;

public class DiscordSender : INotificationSender
{
    private readonly DaprClient _dapr;
    public DiscordSender(DaprClient dapr) => _dapr = dapr;

    public NotifyChannel Channel => NotifyChannel.Discord;

    public Task SendAsync(NotificationRequested e)
    {
        var content = $"🔔 {e.ServiceName} 將在 {e.DaysUntil} 天後（{e.NextRenewalDate:yyyy-MM-dd}）" +
                      $"續費，金額 {e.Cost} {e.Currency}";
        return _dapr.InvokeBindingAsync("discord", "post", new { content });
    }
}
