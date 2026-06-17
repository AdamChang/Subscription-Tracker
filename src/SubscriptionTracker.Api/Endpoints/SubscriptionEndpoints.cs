using FluentValidation;
using SubscriptionTracker.Api.Contracts;
using SubscriptionTracker.Api.State;
using SubscriptionTracker.Contracts;

namespace SubscriptionTracker.Api.Endpoints;

public static class SubscriptionEndpoints
{
    public static void MapSubscriptions(this WebApplication app)
    {
        var g = app.MapGroup("/subscriptions");

        g.MapGet("/", async (ISubscriptionStore store) =>
            Results.Ok(await store.GetAllAsync()));

        g.MapGet("/{id:guid}", async (Guid id, ISubscriptionStore store) =>
            await store.GetAsync(id) is { } s ? Results.Ok(s) : Results.NotFound());

        g.MapPost("/", async (CreateSubscriptionRequest req,
            IValidator<CreateSubscriptionRequest> validator, ISubscriptionStore store) =>
        {
            var validation = await validator.ValidateAsync(req);
            if (!validation.IsValid)
                return Results.ValidationProblem(validation.ToDictionary());

            var sub = new Subscription(Guid.NewGuid(), req.ServiceName, req.Cost,
                req.Currency, req.Cycle, req.NextRenewalDate, req.NotifyDaysBefore,
                req.Channels, null);
            await store.SaveAsync(sub);
            return Results.Created($"/subscriptions/{sub.Id}", sub);
        });

        g.MapPut("/{id:guid}", async (Guid id, UpdateSubscriptionRequest req,
            ISubscriptionStore store) =>
        {
            var existing = await store.GetAsync(id);
            if (existing is null) return Results.NotFound();

            var updated = existing with
            {
                ServiceName = req.ServiceName,
                Cost = req.Cost,
                Currency = req.Currency,
                Cycle = req.Cycle,
                NextRenewalDate = req.NextRenewalDate,
                NotifyDaysBefore = req.NotifyDaysBefore,
                Channels = req.Channels
            };
            await store.SaveAsync(updated);
            return Results.Ok(updated);
        });

        g.MapDelete("/{id:guid}", async (Guid id, ISubscriptionStore store) =>
        {
            await store.DeleteAsync(id);
            return Results.NoContent();
        });
    }
}
