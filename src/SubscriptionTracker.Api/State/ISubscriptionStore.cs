using SubscriptionTracker.Contracts;

namespace SubscriptionTracker.Api.State;

public interface ISubscriptionStore
{
    Task<IReadOnlyList<Subscription>> GetAllAsync();
    Task<Subscription?> GetAsync(Guid id);
    Task SaveAsync(Subscription subscription);
    Task DeleteAsync(Guid id);
}
