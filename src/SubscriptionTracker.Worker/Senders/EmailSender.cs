using Dapr.Client;
using SubscriptionTracker.Contracts;

namespace SubscriptionTracker.Worker.Senders;

public class EmailSender : INotificationSender
{
    private readonly DaprClient _dapr;
    private readonly string _emailTo;

    public EmailSender(DaprClient dapr, IConfiguration config)
    {
        _dapr = dapr;
        _emailTo = config["NOTIFY_EMAIL_TO"] ?? "me@example.com";
    }

    public NotifyChannel Channel => NotifyChannel.Email;

    public Task SendAsync(NotificationRequested e)
    {
        var body = $"{e.ServiceName} 將在 {e.DaysUntil} 天後（{e.NextRenewalDate:yyyy-MM-dd}）" +
                   $"續費，金額 {e.Cost} {e.Currency}。";
        var metadata = new Dictionary<string, string>
        {
            ["emailTo"] = _emailTo,
            ["subject"] = $"訂閱續費提醒：{e.ServiceName}"
        };
        return _dapr.InvokeBindingAsync("smtp", "create", body, metadata);
    }
}
