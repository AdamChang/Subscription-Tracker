using FluentAssertions;
using Moq;
using SubscriptionTracker.Contracts;
using SubscriptionTracker.Worker;
using SubscriptionTracker.Worker.Senders;
using Xunit;

namespace SubscriptionTracker.Worker.Tests;

public class NotificationDispatcherTests
{
    private static NotificationRequested Evt(NotifyChannel ch) =>
        new(Guid.NewGuid(), "Netflix", 390m, "TWD",
            new DateOnly(2026, 7, 1), 3, ch);

    private static Mock<INotificationSender> Sender(NotifyChannel ch)
    {
        var m = new Mock<INotificationSender>();
        m.SetupGet(s => s.Channel).Returns(ch);
        m.Setup(s => s.SendAsync(It.IsAny<NotificationRequested>())).Returns(Task.CompletedTask);
        return m;
    }

    [Fact]
    public async Task Routes_to_matching_channel_only()
    {
        var discord = Sender(NotifyChannel.Discord);
        var email = Sender(NotifyChannel.Email);
        var sut = new NotificationDispatcher(new[] { discord.Object, email.Object });

        await sut.DispatchAsync(Evt(NotifyChannel.Discord));

        discord.Verify(s => s.SendAsync(It.IsAny<NotificationRequested>()), Times.Once);
        email.Verify(s => s.SendAsync(It.IsAny<NotificationRequested>()), Times.Never);
    }

    [Fact]
    public async Task Routes_to_both_channels_when_flagged()
    {
        var discord = Sender(NotifyChannel.Discord);
        var email = Sender(NotifyChannel.Email);
        var sut = new NotificationDispatcher(new[] { discord.Object, email.Object });

        await sut.DispatchAsync(Evt(NotifyChannel.Discord | NotifyChannel.Email));

        discord.Verify(s => s.SendAsync(It.IsAny<NotificationRequested>()), Times.Once);
        email.Verify(s => s.SendAsync(It.IsAny<NotificationRequested>()), Times.Once);
    }

    [Fact]
    public async Task One_channel_failure_does_not_block_other_then_throws()
    {
        var discord = Sender(NotifyChannel.Discord);
        discord.Setup(s => s.SendAsync(It.IsAny<NotificationRequested>()))
            .ThrowsAsync(new InvalidOperationException("discord down"));
        var email = Sender(NotifyChannel.Email);
        var sut = new NotificationDispatcher(new[] { discord.Object, email.Object });

        Func<Task> act = () => sut.DispatchAsync(Evt(NotifyChannel.Discord | NotifyChannel.Email));

        await act.Should().ThrowAsync<AggregateException>();
        email.Verify(s => s.SendAsync(It.IsAny<NotificationRequested>()), Times.Once);
    }
}
