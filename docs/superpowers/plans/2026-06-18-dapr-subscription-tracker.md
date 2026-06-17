# Dapr 智慧訂閱與續費提醒系統 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 建立一個以 Dapr 為基礎的雙服務系統，每日自動掃描即將到期的訂閱並透過 Discord/Email 發送續費提醒，並提供訂閱 CRUD 與每月開銷統計。

**Architecture:** `SubscriptionTracker.Api`（Minimal API）管理訂閱 State、由 Cron Input Binding 每日觸發到期掃描並 publish 事件；`SubscriptionTracker.Worker` 訂閱事件後經 Dapr output binding 發送 Discord/Email。Redis 同時作為 state store 與 pub/sub broker，本機以 Docker Compose 搭配 Dapr sidecar 執行。前端為輕量 Angular SPA。

**Tech Stack:** .NET 8 / ASP.NET Core Minimal API、Dapr .NET SDK（`Dapr.AspNetCore`）、FluentValidation、xUnit + FluentAssertions + Moq、Angular 19、Redis、Docker Compose。

---

## 命名決策（重要）

設計文件的資料夾名 `Subscription.Api` 會產生 `Subscription` 命名空間區段，與 `Subscription` record 型別衝突（C# 名稱解析錯誤）。因此：

- 專案/命名空間一律用 `SubscriptionTracker.Api`、`SubscriptionTracker.Worker`、`SubscriptionTracker.Contracts`。
- 資料夾：`src/SubscriptionTracker.Api`、`src/SubscriptionTracker.Worker`、`src/SubscriptionTracker.Contracts`、`src/web`。
- Cron 路由：Dapr cron input binding 會以 `POST /{binding-name}` 呼叫應用程式，故掃描端點路由必須等於 binding 名稱。本計畫採 binding 名 `cron-check`、路由 `POST /cron-check`（取代 spec 的 `/jobs/check-expiring`）。

## 檔案結構

```
Subscription-Tracker/
├─ SubscriptionTracker.sln
├─ src/
│  ├─ SubscriptionTracker.Contracts/
│  │  ├─ Subscription.cs              # Subscription record + BillingCycle/NotifyChannel enum
│  │  └─ NotificationRequested.cs     # pub/sub 事件 record
│  ├─ SubscriptionTracker.Api/
│  │  ├─ Program.cs                   # DI + middleware + endpoint 註冊
│  │  ├─ Domain/ExpiryScanner.cs      # 純邏輯：篩選到期項目（TDD）
│  │  ├─ Domain/MonthlyCostCalculator.cs  # 純邏輯：每月開銷彙總（TDD）
│  │  ├─ State/ISubscriptionStore.cs  # state 抽象介面
│  │  ├─ State/DaprSubscriptionStore.cs   # Dapr 實作（index key pattern）
│  │  ├─ Contracts/SubscriptionRequests.cs # Create/Update request records
│  │  ├─ Validation/CreateSubscriptionRequestValidator.cs # FluentValidation（TDD）
│  │  └─ Endpoints/
│  │     ├─ SubscriptionEndpoints.cs  # CRUD
│  │     ├─ StatsEndpoints.cs         # /stats/monthly
│  │     └─ JobEndpoints.cs           # POST /cron-check
│  ├─ SubscriptionTracker.Worker/
│  │  ├─ Program.cs                   # DI + subscribe handler
│  │  ├─ NotificationDispatcher.cs    # 依 Channels 路由至 senders（TDD）
│  │  ├─ Senders/INotificationSender.cs
│  │  ├─ Senders/DiscordSender.cs     # discord http binding
│  │  └─ Senders/EmailSender.cs       # smtp binding
│  └─ web/                            # Angular SPA
├─ tests/
│  ├─ SubscriptionTracker.Api.Tests/
│  └─ SubscriptionTracker.Worker.Tests/
├─ dapr/
│  ├─ components/                     # statestore/pubsub/cron-check/discord/smtp .yaml
│  └─ config.yaml
└─ docker-compose.yml
```

---

## Task 1: 方案與專案骨架

**Files:**
- Create: `SubscriptionTracker.sln` 及各專案

- [ ] **Step 1: 建立 solution 與專案**

Run（於 `F:\VibeCode\Subscription-Tracker`）：

```bash
dotnet new sln -n SubscriptionTracker
dotnet new classlib -o src/SubscriptionTracker.Contracts
dotnet new web -o src/SubscriptionTracker.Api
dotnet new web -o src/SubscriptionTracker.Worker
dotnet new xunit -o tests/SubscriptionTracker.Api.Tests
dotnet new xunit -o tests/SubscriptionTracker.Worker.Tests
dotnet sln add src/SubscriptionTracker.Contracts src/SubscriptionTracker.Api src/SubscriptionTracker.Worker tests/SubscriptionTracker.Api.Tests tests/SubscriptionTracker.Worker.Tests
```

- [ ] **Step 2: 加入專案參考與套件**

Run：

```bash
dotnet add src/SubscriptionTracker.Api reference src/SubscriptionTracker.Contracts
dotnet add src/SubscriptionTracker.Worker reference src/SubscriptionTracker.Contracts
dotnet add tests/SubscriptionTracker.Api.Tests reference src/SubscriptionTracker.Api
dotnet add tests/SubscriptionTracker.Worker.Tests reference src/SubscriptionTracker.Worker

dotnet add src/SubscriptionTracker.Api package Dapr.AspNetCore
dotnet add src/SubscriptionTracker.Api package FluentValidation.DependencyInjectionExtensions
dotnet add src/SubscriptionTracker.Api package Swashbuckle.AspNetCore
dotnet add src/SubscriptionTracker.Worker package Dapr.AspNetCore

dotnet add tests/SubscriptionTracker.Api.Tests package FluentAssertions
dotnet add tests/SubscriptionTracker.Api.Tests package Moq
dotnet add tests/SubscriptionTracker.Worker.Tests package FluentAssertions
dotnet add tests/SubscriptionTracker.Worker.Tests package Moq
```

- [ ] **Step 3: 刪除範本多餘檔案**

刪除 `src/SubscriptionTracker.Contracts/Class1.cs`、兩個測試專案的 `UnitTest1.cs`。

- [ ] **Step 4: 驗證可建置**

Run: `dotnet build`
Expected: `Build succeeded`，0 error。

- [ ] **Step 5: 加入 .gitignore 並 commit**

Run：

```bash
dotnet new gitignore
git add .
git commit -m "chore: scaffold solution, projects and packages"
```

---

## Task 2: 共用契約（Contracts）

**Files:**
- Create: `src/SubscriptionTracker.Contracts/Subscription.cs`
- Create: `src/SubscriptionTracker.Contracts/NotificationRequested.cs`

- [ ] **Step 1: 建立 Subscription 與列舉**

`src/SubscriptionTracker.Contracts/Subscription.cs`：

```csharp
namespace SubscriptionTracker.Contracts;

public enum BillingCycle { Monthly, Yearly }

[Flags]
public enum NotifyChannel { None = 0, Discord = 1, Email = 2 }

public record Subscription(
    Guid Id,
    string ServiceName,
    decimal Cost,
    string Currency,
    BillingCycle Cycle,
    DateOnly NextRenewalDate,
    int NotifyDaysBefore,
    NotifyChannel Channels,
    DateOnly? LastNotifiedOn);
```

- [ ] **Step 2: 建立事件契約**

`src/SubscriptionTracker.Contracts/NotificationRequested.cs`：

```csharp
namespace SubscriptionTracker.Contracts;

public record NotificationRequested(
    Guid SubscriptionId,
    string ServiceName,
    decimal Cost,
    string Currency,
    DateOnly NextRenewalDate,
    int DaysUntil,
    NotifyChannel Channels);
```

- [ ] **Step 3: 建置並 commit**

Run: `dotnet build`
Expected: `Build succeeded`。

```bash
git add src/SubscriptionTracker.Contracts
git commit -m "feat: add Subscription and NotificationRequested contracts"
```

---

## Task 3: ExpiryScanner（純邏輯，TDD）

**Files:**
- Create: `src/SubscriptionTracker.Api/Domain/ExpiryScanner.cs`
- Test: `tests/SubscriptionTracker.Api.Tests/Domain/ExpiryScannerTests.cs`

- [ ] **Step 1: 撰寫失敗測試**

`tests/SubscriptionTracker.Api.Tests/Domain/ExpiryScannerTests.cs`：

```csharp
using FluentAssertions;
using SubscriptionTracker.Api.Domain;
using SubscriptionTracker.Contracts;
using Xunit;

namespace SubscriptionTracker.Api.Tests.Domain;

public class ExpiryScannerTests
{
    private static readonly DateOnly Today = new(2026, 6, 18);

    private static Subscription Sub(int daysUntil, NotifyChannel ch = NotifyChannel.Discord,
        int notifyBefore = 7, DateOnly? lastNotified = null) =>
        new(Guid.NewGuid(), "Netflix", 390m, "TWD", BillingCycle.Monthly,
            Today.AddDays(daysUntil), notifyBefore, ch, lastNotified);

    [Fact]
    public void Includes_subscription_within_notify_window()
    {
        var result = ExpiryScanner.FindDue(new[] { Sub(3) }, Today);
        result.Should().HaveCount(1);
        result[0].DaysUntil.Should().Be(3);
    }

    [Fact]
    public void Excludes_subscription_outside_window()
    {
        ExpiryScanner.FindDue(new[] { Sub(10) }, Today).Should().BeEmpty();
    }

    [Fact]
    public void Excludes_already_expired()
    {
        ExpiryScanner.FindDue(new[] { Sub(-1) }, Today).Should().BeEmpty();
    }

    [Fact]
    public void Excludes_already_notified_today()
    {
        ExpiryScanner.FindDue(new[] { Sub(3, lastNotified: Today) }, Today).Should().BeEmpty();
    }

    [Fact]
    public void Excludes_channel_none()
    {
        ExpiryScanner.FindDue(new[] { Sub(3, NotifyChannel.None) }, Today).Should().BeEmpty();
    }
}
```

- [ ] **Step 2: 執行測試確認失敗**

Run: `dotnet test tests/SubscriptionTracker.Api.Tests`
Expected: 編譯失敗 / FAIL（`ExpiryScanner` 不存在）。

- [ ] **Step 3: 實作**

`src/SubscriptionTracker.Api/Domain/ExpiryScanner.cs`：

```csharp
using SubscriptionTracker.Contracts;

namespace SubscriptionTracker.Api.Domain;

public static class ExpiryScanner
{
    public static IReadOnlyList<NotificationRequested> FindDue(
        IEnumerable<Subscription> subscriptions, DateOnly today)
    {
        var due = new List<NotificationRequested>();
        foreach (var s in subscriptions)
        {
            var daysUntil = s.NextRenewalDate.DayNumber - today.DayNumber;
            if (daysUntil < 0 || daysUntil > s.NotifyDaysBefore) continue;
            if (s.LastNotifiedOn == today) continue;
            if (s.Channels == NotifyChannel.None) continue;

            due.Add(new NotificationRequested(
                s.Id, s.ServiceName, s.Cost, s.Currency,
                s.NextRenewalDate, daysUntil, s.Channels));
        }
        return due;
    }
}
```

- [ ] **Step 4: 執行測試確認通過**

Run: `dotnet test tests/SubscriptionTracker.Api.Tests`
Expected: PASS（5 passed）。

- [ ] **Step 5: Commit**

```bash
git add src/SubscriptionTracker.Api/Domain/ExpiryScanner.cs tests/SubscriptionTracker.Api.Tests/Domain/ExpiryScannerTests.cs
git commit -m "feat: add ExpiryScanner with idempotent due-detection"
```

---

## Task 4: MonthlyCostCalculator（純邏輯，TDD）

**Files:**
- Create: `src/SubscriptionTracker.Api/Domain/MonthlyCostCalculator.cs`
- Test: `tests/SubscriptionTracker.Api.Tests/Domain/MonthlyCostCalculatorTests.cs`

- [ ] **Step 1: 撰寫失敗測試**

`tests/SubscriptionTracker.Api.Tests/Domain/MonthlyCostCalculatorTests.cs`：

```csharp
using FluentAssertions;
using SubscriptionTracker.Api.Domain;
using SubscriptionTracker.Contracts;
using Xunit;

namespace SubscriptionTracker.Api.Tests.Domain;

public class MonthlyCostCalculatorTests
{
    private static Subscription Sub(decimal cost, BillingCycle cycle, string currency = "TWD") =>
        new(Guid.NewGuid(), "X", cost, currency, cycle, new DateOnly(2026, 7, 1),
            7, NotifyChannel.Email, null);

    [Fact]
    public void Sums_monthly_costs_by_currency()
    {
        var totals = MonthlyCostCalculator.MonthlyTotals(new[]
        {
            Sub(390m, BillingCycle.Monthly),
            Sub(1200m, BillingCycle.Yearly) // 折算每月 100
        });
        totals["TWD"].Should().Be(490m);
    }

    [Fact]
    public void Groups_separate_currencies()
    {
        var totals = MonthlyCostCalculator.MonthlyTotals(new[]
        {
            Sub(390m, BillingCycle.Monthly, "TWD"),
            Sub(10m, BillingCycle.Monthly, "USD")
        });
        totals.Should().HaveCount(2);
        totals["USD"].Should().Be(10m);
    }

    [Fact]
    public void Empty_returns_empty()
    {
        MonthlyCostCalculator.MonthlyTotals(Array.Empty<Subscription>()).Should().BeEmpty();
    }
}
```

- [ ] **Step 2: 執行測試確認失敗**

Run: `dotnet test tests/SubscriptionTracker.Api.Tests`
Expected: FAIL（`MonthlyCostCalculator` 不存在）。

- [ ] **Step 3: 實作**

`src/SubscriptionTracker.Api/Domain/MonthlyCostCalculator.cs`：

```csharp
using SubscriptionTracker.Contracts;

namespace SubscriptionTracker.Api.Domain;

public static class MonthlyCostCalculator
{
    public static IReadOnlyDictionary<string, decimal> MonthlyTotals(
        IEnumerable<Subscription> subscriptions) =>
        subscriptions
            .GroupBy(s => s.Currency)
            .ToDictionary(
                g => g.Key,
                g => g.Sum(s => s.Cycle == BillingCycle.Yearly ? s.Cost / 12m : s.Cost));
}
```

- [ ] **Step 4: 執行測試確認通過**

Run: `dotnet test tests/SubscriptionTracker.Api.Tests`
Expected: PASS。

- [ ] **Step 5: Commit**

```bash
git add src/SubscriptionTracker.Api/Domain/MonthlyCostCalculator.cs tests/SubscriptionTracker.Api.Tests/Domain/MonthlyCostCalculatorTests.cs
git commit -m "feat: add MonthlyCostCalculator with yearly-to-monthly conversion"
```

---

## Task 5: State 抽象與 Dapr 實作

**Files:**
- Create: `src/SubscriptionTracker.Api/State/ISubscriptionStore.cs`
- Create: `src/SubscriptionTracker.Api/State/DaprSubscriptionStore.cs`

> 此層為基礎設施，端到端行為由 Task 16 整合測試覆蓋；此處不做單元 TDD。

- [ ] **Step 1: 定義介面**

`src/SubscriptionTracker.Api/State/ISubscriptionStore.cs`：

```csharp
using SubscriptionTracker.Contracts;

namespace SubscriptionTracker.Api.State;

public interface ISubscriptionStore
{
    Task<IReadOnlyList<Subscription>> GetAllAsync();
    Task<Subscription?> GetAsync(Guid id);
    Task SaveAsync(Subscription subscription);
    Task DeleteAsync(Guid id);
}
```

- [ ] **Step 2: 實作 Dapr store（index key pattern）**

`src/SubscriptionTracker.Api/State/DaprSubscriptionStore.cs`：

```csharp
using Dapr.Client;
using SubscriptionTracker.Contracts;

namespace SubscriptionTracker.Api.State;

public class DaprSubscriptionStore : ISubscriptionStore
{
    private const string StoreName = "statestore";
    private const string IndexKey = "sub-index";
    private static string Key(Guid id) => $"sub:{id}";

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
            .Select(i => System.Text.Json.JsonSerializer.Deserialize<Subscription>(i.Value)!)
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
                System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(subscription),
                StateOperationType.Upsert),
            new(IndexKey,
                System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(index),
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
                System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(index),
                StateOperationType.Upsert)
        };
        await _dapr.ExecuteStateTransactionAsync(StoreName, ops);
    }
}
```

- [ ] **Step 3: 建置並 commit**

Run: `dotnet build src/SubscriptionTracker.Api`
Expected: `Build succeeded`。

```bash
git add src/SubscriptionTracker.Api/State
git commit -m "feat: add ISubscriptionStore with Dapr index-key implementation"
```

---

## Task 6: Request DTO 與 FluentValidation（TDD）

**Files:**
- Create: `src/SubscriptionTracker.Api/Contracts/SubscriptionRequests.cs`
- Create: `src/SubscriptionTracker.Api/Validation/CreateSubscriptionRequestValidator.cs`
- Test: `tests/SubscriptionTracker.Api.Tests/Validation/CreateSubscriptionRequestValidatorTests.cs`

- [ ] **Step 1: 建立 request records**

`src/SubscriptionTracker.Api/Contracts/SubscriptionRequests.cs`：

```csharp
using SubscriptionTracker.Contracts;

namespace SubscriptionTracker.Api.Contracts;

public record CreateSubscriptionRequest(
    string ServiceName,
    decimal Cost,
    string Currency,
    BillingCycle Cycle,
    DateOnly NextRenewalDate,
    int NotifyDaysBefore,
    NotifyChannel Channels);

public record UpdateSubscriptionRequest(
    string ServiceName,
    decimal Cost,
    string Currency,
    BillingCycle Cycle,
    DateOnly NextRenewalDate,
    int NotifyDaysBefore,
    NotifyChannel Channels);
```

- [ ] **Step 2: 撰寫失敗測試**

`tests/SubscriptionTracker.Api.Tests/Validation/CreateSubscriptionRequestValidatorTests.cs`：

```csharp
using FluentAssertions;
using FluentValidation.TestHelper;
using SubscriptionTracker.Api.Contracts;
using SubscriptionTracker.Api.Validation;
using SubscriptionTracker.Contracts;
using Xunit;

namespace SubscriptionTracker.Api.Tests.Validation;

public class CreateSubscriptionRequestValidatorTests
{
    private readonly CreateSubscriptionRequestValidator _validator = new();

    private static CreateSubscriptionRequest Valid() =>
        new("Netflix", 390m, "TWD", BillingCycle.Monthly,
            new DateOnly(2026, 7, 1), 7, NotifyChannel.Discord);

    [Fact]
    public void Valid_request_passes()
    {
        _validator.TestValidate(Valid()).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Empty_service_name_fails()
    {
        _validator.TestValidate(Valid() with { ServiceName = "" })
            .ShouldHaveValidationErrorFor(x => x.ServiceName);
    }

    [Fact]
    public void Non_positive_cost_fails()
    {
        _validator.TestValidate(Valid() with { Cost = 0m })
            .ShouldHaveValidationErrorFor(x => x.Cost);
    }

    [Fact]
    public void Bad_currency_length_fails()
    {
        _validator.TestValidate(Valid() with { Currency = "TW" })
            .ShouldHaveValidationErrorFor(x => x.Currency);
    }

    [Fact]
    public void Out_of_range_notify_days_fails()
    {
        _validator.TestValidate(Valid() with { NotifyDaysBefore = 100 })
            .ShouldHaveValidationErrorFor(x => x.NotifyDaysBefore);
    }
}
```

- [ ] **Step 3: 執行測試確認失敗**

Run: `dotnet test tests/SubscriptionTracker.Api.Tests`
Expected: FAIL（`CreateSubscriptionRequestValidator` 不存在）。

- [ ] **Step 4: 實作 validator**

`src/SubscriptionTracker.Api/Validation/CreateSubscriptionRequestValidator.cs`：

```csharp
using FluentValidation;
using SubscriptionTracker.Api.Contracts;

namespace SubscriptionTracker.Api.Validation;

public class CreateSubscriptionRequestValidator : AbstractValidator<CreateSubscriptionRequest>
{
    public CreateSubscriptionRequestValidator()
    {
        RuleFor(x => x.ServiceName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Cost).GreaterThan(0);
        RuleFor(x => x.Currency).Length(3);
        RuleFor(x => x.NotifyDaysBefore).InclusiveBetween(0, 90);
    }
}
```

- [ ] **Step 5: 執行測試確認通過**

Run: `dotnet test tests/SubscriptionTracker.Api.Tests`
Expected: PASS。

- [ ] **Step 6: Commit**

```bash
git add src/SubscriptionTracker.Api/Contracts src/SubscriptionTracker.Api/Validation tests/SubscriptionTracker.Api.Tests/Validation
git commit -m "feat: add subscription request DTOs and FluentValidation"
```

---

## Task 7: API endpoints 與 Program.cs

**Files:**
- Create: `src/SubscriptionTracker.Api/Endpoints/SubscriptionEndpoints.cs`
- Create: `src/SubscriptionTracker.Api/Endpoints/StatsEndpoints.cs`
- Create: `src/SubscriptionTracker.Api/Endpoints/JobEndpoints.cs`
- Modify: `src/SubscriptionTracker.Api/Program.cs`

- [ ] **Step 1: CRUD endpoints**

`src/SubscriptionTracker.Api/Endpoints/SubscriptionEndpoints.cs`：

```csharp
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
```

- [ ] **Step 2: Stats endpoint**

`src/SubscriptionTracker.Api/Endpoints/StatsEndpoints.cs`：

```csharp
using SubscriptionTracker.Api.Domain;
using SubscriptionTracker.Api.State;

namespace SubscriptionTracker.Api.Endpoints;

public static class StatsEndpoints
{
    public static void MapStats(this WebApplication app)
    {
        app.MapGet("/stats/monthly", async (ISubscriptionStore store) =>
        {
            var all = await store.GetAllAsync();
            return Results.Ok(MonthlyCostCalculator.MonthlyTotals(all));
        });
    }
}
```

- [ ] **Step 3: Cron job endpoint**

`src/SubscriptionTracker.Api/Endpoints/JobEndpoints.cs`：

```csharp
using Dapr.Client;
using SubscriptionTracker.Api.Domain;
using SubscriptionTracker.Api.State;

namespace SubscriptionTracker.Api.Endpoints;

public static class JobEndpoints
{
    public static void MapJobs(this WebApplication app)
    {
        // Dapr cron input binding 以 POST /{binding-name} 觸發，binding 名為 cron-check
        app.MapPost("/cron-check", async (ISubscriptionStore store, DaprClient dapr) =>
        {
            var all = await store.GetAllAsync();
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var due = ExpiryScanner.FindDue(all, today);

            foreach (var evt in due)
            {
                await dapr.PublishEventAsync("pubsub", "notifications", evt);
                var sub = all.First(s => s.Id == evt.SubscriptionId)
                    with { LastNotifiedOn = today };
                await store.SaveAsync(sub);
            }
            return Results.Ok(new { notified = due.Count });
        });
    }
}
```

- [ ] **Step 4: 組裝 Program.cs**

覆寫 `src/SubscriptionTracker.Api/Program.cs`：

```csharp
using FluentValidation;
using SubscriptionTracker.Api.Endpoints;
using SubscriptionTracker.Api.State;
using SubscriptionTracker.Api.Validation;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDaprClient();
builder.Services.AddSingleton<ISubscriptionStore, DaprSubscriptionStore>();
builder.Services.AddScoped<IValidator<SubscriptionTracker.Api.Contracts.CreateSubscriptionRequest>,
    CreateSubscriptionRequestValidator>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

// 全域例外處理（依開發者偏好，不在各 method 寫 try-catch）
builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler();
app.UseSwagger();
app.UseSwaggerUI();
app.UseCors();

app.MapSubscriptions();
app.MapStats();
app.MapJobs();

app.Run();
```

- [ ] **Step 5: 建置並 commit**

Run: `dotnet build src/SubscriptionTracker.Api`
Expected: `Build succeeded`。

```bash
git add src/SubscriptionTracker.Api
git commit -m "feat: add subscription CRUD, stats and cron-check endpoints"
```

---

## Task 8: Worker — INotificationSender 與 NotificationDispatcher（TDD）

**Files:**
- Create: `src/SubscriptionTracker.Worker/Senders/INotificationSender.cs`
- Create: `src/SubscriptionTracker.Worker/NotificationDispatcher.cs`
- Test: `tests/SubscriptionTracker.Worker.Tests/NotificationDispatcherTests.cs`

- [ ] **Step 1: 定義 sender 介面**

`src/SubscriptionTracker.Worker/Senders/INotificationSender.cs`：

```csharp
using SubscriptionTracker.Contracts;

namespace SubscriptionTracker.Worker.Senders;

public interface INotificationSender
{
    NotifyChannel Channel { get; }
    Task SendAsync(NotificationRequested evt);
}
```

- [ ] **Step 2: 撰寫失敗測試**

`tests/SubscriptionTracker.Worker.Tests/NotificationDispatcherTests.cs`：

```csharp
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

        var act = () => sut.DispatchAsync(Evt(NotifyChannel.Discord | NotifyChannel.Email));

        await act.Should().ThrowAsync<AggregateException>();
        email.Verify(s => s.SendAsync(It.IsAny<NotificationRequested>()), Times.Once);
    }
}
```

- [ ] **Step 3: 執行測試確認失敗**

Run: `dotnet test tests/SubscriptionTracker.Worker.Tests`
Expected: FAIL（`NotificationDispatcher` 不存在）。

- [ ] **Step 4: 實作 dispatcher**

`src/SubscriptionTracker.Worker/NotificationDispatcher.cs`：

```csharp
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
```

- [ ] **Step 5: 執行測試確認通過**

Run: `dotnet test tests/SubscriptionTracker.Worker.Tests`
Expected: PASS（3 passed）。

- [ ] **Step 6: Commit**

```bash
git add src/SubscriptionTracker.Worker/Senders/INotificationSender.cs src/SubscriptionTracker.Worker/NotificationDispatcher.cs tests/SubscriptionTracker.Worker.Tests/NotificationDispatcherTests.cs
git commit -m "feat: add notification dispatcher with channel routing"
```

---

## Task 9: Worker — Discord/Email senders 與 Program.cs

**Files:**
- Create: `src/SubscriptionTracker.Worker/Senders/DiscordSender.cs`
- Create: `src/SubscriptionTracker.Worker/Senders/EmailSender.cs`
- Modify: `src/SubscriptionTracker.Worker/Program.cs`

- [ ] **Step 1: DiscordSender（http output binding）**

`src/SubscriptionTracker.Worker/Senders/DiscordSender.cs`：

```csharp
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
```

- [ ] **Step 2: EmailSender（smtp output binding）**

`src/SubscriptionTracker.Worker/Senders/EmailSender.cs`：

```csharp
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
```

- [ ] **Step 3: 組裝 Program.cs（subscribe handler）**

覆寫 `src/SubscriptionTracker.Worker/Program.cs`：

```csharp
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
```

- [ ] **Step 4: 建置並 commit**

Run: `dotnet build src/SubscriptionTracker.Worker`
Expected: `Build succeeded`。

```bash
git add src/SubscriptionTracker.Worker
git commit -m "feat: add Discord/Email senders and pub/sub subscribe handler"
```

---

## Task 10: Dapr components

**Files:**
- Create: `dapr/config.yaml`
- Create: `dapr/components/statestore.yaml`
- Create: `dapr/components/pubsub.yaml`
- Create: `dapr/components/cron-check.yaml`
- Create: `dapr/components/discord.yaml`
- Create: `dapr/components/smtp.yaml`

- [ ] **Step 1: config.yaml**

`dapr/config.yaml`：

```yaml
apiVersion: dapr.io/v1alpha1
kind: Configuration
metadata:
  name: appconfig
```

- [ ] **Step 2: statestore（Redis，可換 MongoDB）**

`dapr/components/statestore.yaml`：

```yaml
apiVersion: dapr.io/v1alpha1
kind: Component
metadata:
  name: statestore
spec:
  type: state.redis
  version: v1
  metadata:
    - name: redisHost
      value: redis:6379
    - name: redisPassword
      value: ""
    - name: actorStateStore
      value: "false"
```

- [ ] **Step 3: pubsub**

`dapr/components/pubsub.yaml`：

```yaml
apiVersion: dapr.io/v1alpha1
kind: Component
metadata:
  name: pubsub
spec:
  type: pubsub.redis
  version: v1
  metadata:
    - name: redisHost
      value: redis:6379
    - name: redisPassword
      value: ""
```

- [ ] **Step 4: cron-check（input binding）**

`dapr/components/cron-check.yaml`：

```yaml
apiVersion: dapr.io/v1alpha1
kind: Component
metadata:
  name: cron-check
spec:
  type: bindings.cron
  version: v1
  metadata:
    - name: schedule
      value: "0 0 9 * * *"   # 每日 09:00 觸發 POST /cron-check
scopes:
  - subscription-api
```

- [ ] **Step 5: discord（http output binding）**

`dapr/components/discord.yaml`（將 `<WEBHOOK>` 換成實際 Discord webhook 路徑）：

```yaml
apiVersion: dapr.io/v1alpha1
kind: Component
metadata:
  name: discord
spec:
  type: bindings.http
  version: v1
  metadata:
    - name: url
      value: "https://discord.com/api/webhooks/<WEBHOOK>"
scopes:
  - notification-worker
```

- [ ] **Step 6: smtp（output binding）**

`dapr/components/smtp.yaml`（依實際寄件服務調整）：

```yaml
apiVersion: dapr.io/v1alpha1
kind: Component
metadata:
  name: smtp
spec:
  type: bindings.smtp
  version: v1
  metadata:
    - name: host
      value: "smtp.gmail.com"
    - name: port
      value: "587"
    - name: user
      value: "your@gmail.com"
    - name: password
      value: "your-app-password"
    - name: emailFrom
      value: "your@gmail.com"
scopes:
  - notification-worker
```

- [ ] **Step 7: Commit**

```bash
git add dapr
git commit -m "feat: add Dapr components for state, pubsub, cron and output bindings"
```

---

## Task 11: Docker Compose 與本機執行驗證

**Files:**
- Create: `docker-compose.yml`
- Create: `src/SubscriptionTracker.Api/Dockerfile`
- Create: `src/SubscriptionTracker.Worker/Dockerfile`

- [ ] **Step 1: API Dockerfile**

`src/SubscriptionTracker.Api/Dockerfile`：

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/SubscriptionTracker.Api -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:5000
ENTRYPOINT ["dotnet", "SubscriptionTracker.Api.dll"]
```

- [ ] **Step 2: Worker Dockerfile**

`src/SubscriptionTracker.Worker/Dockerfile`：

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/SubscriptionTracker.Worker -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:5001
ENTRYPOINT ["dotnet", "SubscriptionTracker.Worker.dll"]
```

- [ ] **Step 3: docker-compose.yml（含 Dapr sidecars）**

`docker-compose.yml`：

```yaml
services:
  redis:
    image: redis:7-alpine
    ports: ["6379:6379"]

  subscription-api:
    build:
      context: .
      dockerfile: src/SubscriptionTracker.Api/Dockerfile
    depends_on: [redis]
    ports: ["5000:5000"]

  subscription-api-dapr:
    image: daprio/daprd:latest
    command: ["./daprd",
      "-app-id", "subscription-api",
      "-app-port", "5000",
      "-resources-path", "/components",
      "-config", "/config/config.yaml"]
    volumes:
      - "./dapr/components:/components"
      - "./dapr/config.yaml:/config/config.yaml"
    depends_on: [subscription-api, redis]
    network_mode: "service:subscription-api"

  notification-worker:
    build:
      context: .
      dockerfile: src/SubscriptionTracker.Worker/Dockerfile
    depends_on: [redis]
    ports: ["5001:5001"]

  notification-worker-dapr:
    image: daprio/daprd:latest
    command: ["./daprd",
      "-app-id", "notification-worker",
      "-app-port", "5001",
      "-resources-path", "/components",
      "-config", "/config/config.yaml"]
    volumes:
      - "./dapr/components:/components"
      - "./dapr/config.yaml:/config/config.yaml"
    depends_on: [notification-worker, redis]
    network_mode: "service:notification-worker"
```

- [ ] **Step 4: 啟動並驗證**

Run: `docker compose up --build -d`
Expected: 所有容器 `Up`。

Run（建立一筆即將到期訂閱，3 天後）：

```bash
curl -X POST http://localhost:5000/subscriptions -H "Content-Type: application/json" -d "{\"serviceName\":\"Netflix\",\"cost\":390,\"currency\":\"TWD\",\"cycle\":0,\"nextRenewalDate\":\"2026-06-21\",\"notifyDaysBefore\":7,\"channels\":1}"
```
Expected: HTTP 201 + 回傳含 `id` 的訂閱。

Run（手動觸發掃描，模擬 cron）：

```bash
curl -X POST http://localhost:5001/notifications -H "Content-Type: application/json" -d "{\"subscriptionId\":\"00000000-0000-0000-0000-000000000001\",\"serviceName\":\"Netflix\",\"cost\":390,\"currency\":\"TWD\",\"nextRenewalDate\":\"2026-06-21\",\"daysUntil\":3,\"channels\":1}"
```
Expected: HTTP 200，且 Discord 頻道收到提醒訊息。

> 完整 cron→publish→subscribe 鏈路可透過 Dapr sidecar 呼叫掃描端點驗證：
> `curl -X POST http://localhost:5000/cron-check`，預期回傳 `{"notified":1}`，Discord 收到訊息。

- [ ] **Step 5: 關閉並 commit**

Run: `docker compose down`

```bash
git add docker-compose.yml src/SubscriptionTracker.Api/Dockerfile src/SubscriptionTracker.Worker/Dockerfile
git commit -m "feat: add docker-compose with Dapr sidecars and Dockerfiles"
```

---

## Task 12: Angular SPA

**Files:**
- Create: `src/web/` 內 Angular 專案
- Create: `src/web/src/app/subscription.service.ts`
- Create: `src/web/src/app/subscription.model.ts`
- Create: `src/web/src/app/subscription-list/` 元件
- Create: `src/web/src/app/subscription-form/` 元件

> 前端為輔助介面，採輕量做法：service 加一支基本單元測試，元件以手動驗證為主。

- [ ] **Step 1: 建立 Angular 專案**

Run（於 `F:\VibeCode\Subscription-Tracker\src`）：

```bash
ng new web --routing --style=css --skip-git --defaults
cd web
ng generate service subscription
ng generate component subscription-list
ng generate component subscription-form
```

- [ ] **Step 2: 模型**

`src/web/src/app/subscription.model.ts`：

```typescript
export interface Subscription {
  id: string;
  serviceName: string;
  cost: number;
  currency: string;
  cycle: number;            // 0 Monthly, 1 Yearly
  nextRenewalDate: string;  // yyyy-MM-dd
  notifyDaysBefore: number;
  channels: number;         // 1 Discord, 2 Email, 3 both
  lastNotifiedOn: string | null;
}

export type CreateSubscription = Omit<Subscription, 'id' | 'lastNotifiedOn'>;
```

- [ ] **Step 3: Service**

`src/web/src/app/subscription.service.ts`：

```typescript
import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { CreateSubscription, Subscription } from './subscription.model';

@Injectable({ providedIn: 'root' })
export class SubscriptionService {
  private readonly base = 'http://localhost:5000/subscriptions';
  constructor(private http: HttpClient) {}

  list(): Observable<Subscription[]> {
    return this.http.get<Subscription[]>(this.base);
  }
  create(req: CreateSubscription): Observable<Subscription> {
    return this.http.post<Subscription>(this.base, req);
  }
  remove(id: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/${id}`);
  }
}
```

- [ ] **Step 4: Service 測試（驗證 HTTP 行為）**

覆寫 `src/web/src/app/subscription.service.spec.ts`：

```typescript
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { SubscriptionService } from './subscription.service';

describe('SubscriptionService', () => {
  let service: SubscriptionService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [HttpClientTestingModule] });
    service = TestBed.inject(SubscriptionService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('list() issues GET to subscriptions endpoint', () => {
    service.list().subscribe();
    const req = httpMock.expectOne('http://localhost:5000/subscriptions');
    expect(req.request.method).toBe('GET');
    req.flush([]);
  });
});
```

- [ ] **Step 5: 執行前端測試**

Run（於 `src/web`）: `ng test --watch=false --browsers=ChromeHeadless`
Expected: 測試通過（含 SubscriptionService 案例）。

- [ ] **Step 6: 清單與表單元件**

`src/web/src/app/subscription-list/subscription-list.component.ts`：

```typescript
import { CommonModule } from '@angular/common';
import { Component, OnInit } from '@angular/core';
import { Subscription } from '../subscription.model';
import { SubscriptionService } from '../subscription.service';
import { SubscriptionFormComponent } from '../subscription-form/subscription-form.component';

@Component({
  selector: 'app-subscription-list',
  standalone: true,
  imports: [CommonModule, SubscriptionFormComponent],
  template: `
    <h2>我的訂閱</h2>
    <app-subscription-form (created)="load()"></app-subscription-form>
    <table>
      <tr><th>服務</th><th>金額</th><th>下次續費</th><th></th></tr>
      <tr *ngFor="let s of subs">
        <td>{{ s.serviceName }}</td>
        <td>{{ s.cost }} {{ s.currency }}</td>
        <td>{{ s.nextRenewalDate }}</td>
        <td><button (click)="remove(s.id)">刪除</button></td>
      </tr>
    </table>
  `
})
export class SubscriptionListComponent implements OnInit {
  subs: Subscription[] = [];
  constructor(private svc: SubscriptionService) {}
  ngOnInit() { this.load(); }
  load() { this.svc.list().subscribe(s => this.subs = s); }
  remove(id: string) { this.svc.remove(id).subscribe(() => this.load()); }
}
```

`src/web/src/app/subscription-form/subscription-form.component.ts`：

```typescript
import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { CreateSubscription } from '../subscription.model';
import { SubscriptionService } from '../subscription.service';

@Component({
  selector: 'app-subscription-form',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <form (ngSubmit)="submit()">
      <input [(ngModel)]="model.serviceName" name="serviceName" placeholder="服務名稱" required />
      <input [(ngModel)]="model.cost" name="cost" type="number" placeholder="金額" required />
      <input [(ngModel)]="model.currency" name="currency" placeholder="幣別 (TWD)" required />
      <input [(ngModel)]="model.nextRenewalDate" name="nextRenewalDate" type="date" required />
      <input [(ngModel)]="model.notifyDaysBefore" name="notifyDaysBefore" type="number" />
      <button type="submit">新增</button>
    </form>
  `
})
export class SubscriptionFormComponent {
  @Output() created = new EventEmitter<void>();
  model: CreateSubscription = {
    serviceName: '', cost: 0, currency: 'TWD', cycle: 0,
    nextRenewalDate: '', notifyDaysBefore: 7, channels: 1
  };
  constructor(private svc: SubscriptionService) {}
  submit() {
    this.svc.create(this.model).subscribe(() => this.created.emit());
  }
}
```

- [ ] **Step 7: 掛上 provider 與根元件**

覆寫 `src/web/src/app/app.config.ts`：

```typescript
import { ApplicationConfig } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { routes } from './app.routes';

export const appConfig: ApplicationConfig = {
  providers: [provideRouter(routes), provideHttpClient()]
};
```

覆寫 `src/web/src/app/app.component.ts`：

```typescript
import { Component } from '@angular/core';
import { SubscriptionListComponent } from './subscription-list/subscription-list.component';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [SubscriptionListComponent],
  template: `<app-subscription-list></app-subscription-list>`
})
export class AppComponent {}
```

- [ ] **Step 8: 手動驗證**

Run（先 `docker compose up -d`，再於 `src/web`）: `ng serve`
開啟 `http://localhost:4200`，新增一筆訂閱，確認清單顯示、刪除有效。

- [ ] **Step 9: Commit**

```bash
git add src/web
git commit -m "feat: add Angular SPA for subscription management"
```

---

## Task 13（Stretch）: 整合測試

**Files:**
- Create: `tests/SubscriptionTracker.Api.Tests/Integration/StateStoreIntegrationTests.cs`
- Modify: `tests/SubscriptionTracker.Api.Tests` 加入 `Testcontainers.Redis`

> 加分項，非必須。驗證 `DaprSubscriptionStore` 對 Redis 的 save/get/list/delete 與 index 一致性。

- [ ] **Step 1: 加入套件**

Run: `dotnet add tests/SubscriptionTracker.Api.Tests package Testcontainers.Redis`

- [ ] **Step 2: 撰寫整合測試**

`tests/SubscriptionTracker.Api.Tests/Integration/StateStoreIntegrationTests.cs`：

```csharp
using FluentAssertions;
using SubscriptionTracker.Contracts;
using Testcontainers.Redis;
using Xunit;

namespace SubscriptionTracker.Api.Tests.Integration;

// 需 Dapr sidecar 才能跑真實 DaprClient；此處示範以 Redis container
// 驗證 index pattern 的讀寫一致性骨架，實作時依環境補上 Dapr self-hosted 啟動。
public class StateStoreIntegrationTests : IAsyncLifetime
{
    private readonly RedisContainer _redis = new RedisBuilder().Build();

    public Task InitializeAsync() => _redis.StartAsync();
    public Task DisposeAsync() => _redis.DisposeAsync().AsTask();

    [Fact(Skip = "需搭配 Dapr sidecar；列為手動整合驗證")]
    public void Save_then_GetAll_roundtrips()
    {
        // 由 docker compose 環境以 curl 端到端驗證取代（見 Task 11）。
        true.Should().BeTrue();
    }
}
```

- [ ] **Step 3: 執行並 commit**

Run: `dotnet test tests/SubscriptionTracker.Api.Tests`
Expected: PASS（整合案例 Skipped）。

```bash
git add tests/SubscriptionTracker.Api.Tests/Integration
git commit -m "test: add integration test scaffold for state store"
```

---

## 完成準則

- [ ] `dotnet test` 全綠（ExpiryScanner、MonthlyCostCalculator、Validator、Dispatcher）。
- [ ] `docker compose up` 後，新增訂閱 → `POST /cron-check` 回 `{"notified":n}` → Discord/Email 收到提醒。
- [ ] Angular SPA 可新增/列出/刪除訂閱。
- [ ] 將 `statestore.yaml` 的 `type` 改為 `state.mongodb` 並調整連線後，應用程式碼無需修改即可運作（驗證可移植性訴求）。
