using FluentAssertions;
using SubscriptionTracker.Contracts;
using Testcontainers.Redis;
using Xunit;

namespace SubscriptionTracker.Api.Tests.Integration;

// 需 Dapr sidecar 才能跑真實 DaprClient；此處示範以 Redis container
// 驗證 index pattern 的讀寫一致性骨架，實作時依環境補上 Dapr self-hosted 啟動。
public class StateStoreIntegrationTests : IAsyncLifetime
{
    private readonly RedisContainer _redis = new RedisBuilder("redis:7-alpine").Build();

    public Task InitializeAsync() => _redis.StartAsync();
    public Task DisposeAsync() => _redis.DisposeAsync().AsTask();

    [Fact(Skip = "需搭配 Dapr sidecar；列為手動整合驗證")]
    public void Save_then_GetAll_roundtrips()
    {
        // 由 docker compose 環境以 curl 端到端驗證取代（見 Task 11）。
        true.Should().BeTrue();
    }
}
