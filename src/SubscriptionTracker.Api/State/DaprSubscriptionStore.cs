using Dapr.Client;
using SubscriptionTracker.Contracts;

namespace SubscriptionTracker.Api.State;

public class DaprSubscriptionStore : ISubscriptionStore
{
    private const string StoreName = "statestore";
    private const string IndexKey = "sub-index";
    private static string Key(Guid id) => $"sub:{id}";
    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new(System.Text.Json.JsonSerializerDefaults.Web);

    private readonly DaprClient _dapr;
    public DaprSubscriptionStore(DaprClient dapr) => _dapr = dapr;

    public async Task<IReadOnlyList<Subscription>> GetAllAsync()
    {
        var index = await _dapr.GetStateAsync<List<Guid>>(StoreName, IndexKey) ?? new();
        if (index.Count == 0) return Array.Empty<Subscription>();

        var keys = index.Select(Key).ToList();
        var items = await _dapr.GetBulkStateAsync(StoreName, keys, parallelism: null);
        return items
            .Where(i => !string.IsNullOrEmpty(i.Value))
            .Select(i => System.Text.Json.JsonSerializer.Deserialize<Subscription>(i.Value, JsonOptions)!)
            .ToList();
    }

    public Task<Subscription?> GetAsync(Guid id) =>
        _dapr.GetStateAsync<Subscription?>(StoreName, Key(id));

    public async Task SaveAsync(Subscription subscription)
    {
        var index = await _dapr.GetStateAsync<List<Guid>>(StoreName, IndexKey) ?? new();
        if (!index.Contains(subscription.Id)) index.Add(subscription.Id);

        var ops = new List<StateTransactionRequest>
        {
            new(Key(subscription.Id),
                System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(subscription, JsonOptions),
                StateOperationType.Upsert),
            new(IndexKey,
                System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(index, JsonOptions),
                StateOperationType.Upsert)
        };
        await _dapr.ExecuteStateTransactionAsync(StoreName, ops);
    }

    public async Task DeleteAsync(Guid id)
    {
        var index = await _dapr.GetStateAsync<List<Guid>>(StoreName, IndexKey) ?? new();
        index.Remove(id);

        var ops = new List<StateTransactionRequest>
        {
            new(Key(id), null, StateOperationType.Delete),
            new(IndexKey,
                System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(index, JsonOptions),
                StateOperationType.Upsert)
        };
        await _dapr.ExecuteStateTransactionAsync(StoreName, ops);
    }
}
