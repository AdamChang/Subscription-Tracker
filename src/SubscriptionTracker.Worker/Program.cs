using SubscriptionTracker.Contracts;
using SubscriptionTracker.Worker;
using SubscriptionTracker.Worker.Senders;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDaprClient();
builder.Services.AddSingleton<INotificationSender, DiscordSender>();
builder.Services.AddSingleton<INotificationSender, EmailSender>();
builder.Services.AddSingleton<NotificationDispatcher>();

var app = builder.Build();

app.UseCloudEvents();
app.MapSubscribeHandler();

app.MapPost("/notifications", async (NotificationRequested evt, NotificationDispatcher dispatcher) =>
{
    await dispatcher.DispatchAsync(evt);
    return Results.Ok();
}).WithTopic("pubsub", "notifications");

app.Run();
