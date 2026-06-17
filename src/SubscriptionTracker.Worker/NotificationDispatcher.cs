using Microsoft.Extensions.Logging;
using SubscriptionTracker.Contracts;
using SubscriptionTracker.Worker.Senders;

namespace SubscriptionTracker.Worker;

public class NotificationDispatcher
{
    private readonly IEnumerable<INotificationSender> _senders;
    private readonly ILogger<NotificationDispatcher>? _logger;

    public NotificationDispatcher(IEnumerable<INotificationSender> senders,
        ILogger<NotificationDispatcher>? logger = null)
    {
        _senders = senders;
        _logger = logger;
    }

    public async Task DispatchAsync(NotificationRequested evt)
    {
        var failures = new List<Exception>();
        foreach (var sender in _senders)
        {
            if (!evt.Channels.HasFlag(sender.Channel)) continue;
            try
            {
                await sender.SendAsync(evt);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "通知管道 {Channel} 發送失敗", sender.Channel);
                failures.Add(ex);
            }
        }
        // 單一管道失敗不阻擋其他管道；最終拋出以觸發 Dapr 重試 → 超限進 dead-letter。
        // 取捨：重試會重送已成功的管道（可能重複），學習為主可接受。
        if (failures.Count > 0) throw new AggregateException(failures);
    }
}
