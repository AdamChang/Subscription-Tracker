# 智慧訂閱與續費提醒系統 — 設計文件

- **日期**：2026-06-18
- **狀態**：已核可（設計階段）
- **定位**：學習 Dapr 為主、單一使用者、免認證的個人工具

## 1. 目標與範圍

協助管理軟體服務訂閱（Netflix、Spotify、AWS 等），在訂閱即將到期時自動發送通知，並提供每月開銷統計。

技術主軸為實作 Dapr 四大 building blocks：

- **State Management** — 儲存訂閱資料，且抽象化到「換 store 不改程式」。
- **Cron Input Binding** — 每日定時觸發到期掃描。
- **Pub/Sub** — 到期時發布通知事件，由獨立服務消費。
- **SMTP / HTTP Output Binding** — 實際發送 Email 與 Discord 通知。

### 範圍內

- 訂閱項目 CRUD（REST API）。
- 每日自動掃描即將到期項目並發送通知。
- 通知管道：Discord Webhook、Email（SMTP）。
- 每月開銷即時統計。
- 輕量 Angular SPA 管理介面。

### 範圍外（YAGNI）

- 使用者帳號、認證、授權、多租戶。
- 付款／自動續費串接。
- 行動 App、推播。
- 通知 ack 回程與自動補發機制（以 dead-letter + log 觀察取代）。

## 2. 系統架構

```
                    ┌─────────────────────────────────────┐
   Angular SPA ───► │  Subscription.Api  (+ Dapr sidecar)  │
   (CRUD UI)        │  - 訂閱 CRUD                          │
                    │  - State Mgmt：讀寫訂閱資料           │──┐ 寫/讀
                    │  - Cron Binding：每日觸發到期掃描     │  │
                    │  - Pub/Sub publish：notify 事件       │  ▼
                    └──────────────┬───────────────────┘  ┌──────┐
                                   │ publish               │ Redis│
                                   ▼                       │state │
                    ┌──────────────────────────────────┐  │ +    │
                    │ Notification.Worker (+ sidecar)   │  │broker│
                    │  - Pub/Sub subscribe：notify 事件 │◄─┘──────┘
                    │  - HTTP binding → Discord Webhook │
                    │  - SMTP binding → Email           │
                    └──────────────────────────────────┘
```

採**雙服務**架構（方案 A）：

- **`Subscription.Api`**：訂閱 CRUD + State Management；掛 Cron Input Binding 每日觸發 `/jobs/check-expiring`，掃描即將到期項目並發布事件到 Pub/Sub。
- **`Notification.Worker`**：訂閱通知事件，依管道分別走 Discord HTTP output binding 與 SMTP output binding 發送。

職責清楚（API 管資料與排程觸發、Worker 管送通知），透過 Pub/Sub 解耦，且完整展示四大 building blocks。

### 技術堆疊

- **後端**：.NET 8 + ASP.NET Core（Minimal API）+ Dapr .NET SDK。
- **前端**：輕量 Angular SPA。
- **基礎設施**：本機 self-hosted + Docker Compose，Redis 同時作為 state store 與 pub/sub broker。
- **測試**：xUnit + FluentAssertions + Moq。

## 3. Dapr Components

位於 `./dapr/components/`，以 YAML 定義。

| Component    | 類型             | 後端    | 用途                                                    |
| ------------ | ---------------- | ------- | ------------------------------------------------------- |
| `statestore` | `state.redis`    | Redis   | 儲存訂閱項目（換 MongoDB 只改此檔的 type 與連線）       |
| `pubsub`     | `pubsub.redis`   | Redis   | `notifications` topic                                   |
| `cron-check` | `bindings.cron`  | —       | 每日 `@every 24h`，觸發 `Subscription.Api` 的掃描端點   |
| `discord`    | `bindings.http`  | Discord | output：POST Webhook                                    |
| `smtp`       | `bindings.smtp`  | SMTP    | output：寄送 Email                                      |

**可移植性關鍵**：`statestore` 的抽象化讓「今天 Redis、明天 MongoDB 不改程式」成立——只需更換 component YAML 的 `type` 與連線設定，應用程式碼不動。

## 4. 資料模型與 State 設計

### 訂閱項目（不可變 record）

```csharp
public record Subscription(
    Guid Id,
    string ServiceName,        // "Netflix"
    decimal Cost,              // 390
    string Currency,          // "TWD"
    BillingCycle Cycle,       // Monthly | Yearly
    DateOnly NextRenewalDate, // 下次續費日
    int NotifyDaysBefore,     // 提前幾天通知，預設 7
    NotifyChannel Channels,   // [Flags] Discord | Email
    DateOnly? LastNotifiedOn  // 去重用，避免重複通知
);
```

```csharp
public enum BillingCycle { Monthly, Yearly }

[Flags]
public enum NotifyChannel { None = 0, Discord = 1, Email = 2 }
```

### State key 設計（刻意跨 store 可移植）

| Key          | Value          | 說明                                       |
| ------------ | -------------- | ------------------------------------------ |
| `sub:{id}`   | `Subscription` | 單筆訂閱                                   |
| `sub-index`  | `List<Guid>`   | 所有訂閱 id 清單，供掃描／列表使用         |

不依賴特定 store 的 query API（Redis 有、MongoDB 行為不同），改用 index key pattern 確保可移植。寫入／刪除時以 Dapr 的 bulk／transaction API 同時更新 `sub:{id}` 與 `sub-index`，維持兩者一致。

### 每月開銷統計

不另外儲存，由 `GET /stats/monthly` 即時彙總（Yearly 折算為每月）。避免維護重複狀態（YAGNI）。

### 去重機制

掃描時若 `LastNotifiedOn == today` 則跳過，避免同日重複發送；發送事件發布後寫回 `LastNotifiedOn`。

## 5. 資料流

### 每日到期掃描流程

```
Cron Binding (每日)
   │ POST /jobs/check-expiring
   ▼
Subscription.Api
   1. 讀 sub-index → bulk-get 所有 Subscription
   2. 篩選：0 ≤ (NextRenewalDate − today) ≤ NotifyDaysBefore
            且 LastNotifiedOn ≠ today        ← 冪等去重
   3. 逐筆 publish 「NotificationRequested」→ pubsub/notifications
   4. 標記 LastNotifiedOn = today
   ▼ (解耦)
Notification.Worker  訂閱 notifications
   5. 依 event.Channels 走 discord binding / smtp binding
```

### 事件 schema

```csharp
public record NotificationRequested(
    Guid SubscriptionId, string ServiceName, decimal Cost,
    string Currency, DateOnly NextRenewalDate, int DaysUntil,
    NotifyChannel Channels);
```

### 設計取捨：fire-and-forget

API 在 publish 成功後即標記 `LastNotifiedOn`，不等 Worker ack。學習為主，避免引入 ack 回程事件的複雜度；代價是「事件已發但 Worker 最終發送失敗」時不會自動補發——以 dead-letter topic + log 觀察為主。

## 6. 錯誤處理

| 環節                    | 策略                                                                       |
| ----------------------- | -------------------------------------------------------------------------- |
| API 例外                | .NET 8 `IExceptionHandler` 全域處理，不在各 method 寫 try-catch             |
| Pub/Sub 消費失敗        | Worker 回非 2xx → Dapr 自動重試（resiliency policy）；超限進 dead-letter topic |
| Discord/SMTP binding 失敗 | Worker 內捕捉並 log，回失敗狀態交由 Dapr 重試；單一管道失敗不影響另一管道  |
| 掃描中斷重跑            | 靠 `LastNotifiedOn` 去重 → 整個流程冪等，可安全重試                         |

## 7. 專案結構

```
Subscription-Tracker/
├─ src/
│  ├─ Subscription.Api/        # ASP.NET Core Minimal API + Dapr
│  ├─ Notification.Worker/     # ASP.NET Core (pub/sub subscriber)
│  ├─ Shared.Contracts/        # 共用 records（事件、DTO）
│  └─ web/                     # Angular SPA
├─ dapr/
│  ├─ components/              # statestore / pubsub / cron / discord / smtp .yaml
│  └─ config.yaml
├─ tests/
│  ├─ Subscription.Api.Tests/
│  └─ Notification.Worker.Tests/
└─ docker-compose.yml          # redis + 2 services + 2 dapr sidecars
```

### 分層深度

採**輕量分層**：每個服務內以資料夾分層（`Endpoints / Domain / Services / State`），不引入完整四專案 Clean Architecture，亦**不使用 MediatR/CQRS**，直接以 minimal API + service 類別實作。理由是 YAGNI——避免分層樣板蓋過 Dapr 學習重點。

## 8. 測試策略

採 TDD：核心篩選／彙總邏輯先寫測試再實作。

| 層級             | 對象                                                                                       |
| ---------------- | ------------------------------------------------------------------------------------------ |
| 單元測試（重點） | 到期篩選邏輯、每月開銷彙總、去重判斷、Worker 的 channel 路由；將 `DaprClient` 包在介面後以便 mock |
| 整合測試（stretch） | Testcontainers 起 Redis + Dapr 跑端到端，列為加分非必須                                  |

## 9. API 端點概要

| 方法   | 路徑                    | 說明                               |
| ------ | ----------------------- | ---------------------------------- |
| GET    | `/subscriptions`        | 列出所有訂閱                       |
| GET    | `/subscriptions/{id}`   | 取得單筆                           |
| POST   | `/subscriptions`        | 新增（FluentValidation 驗證）      |
| PUT    | `/subscriptions/{id}`   | 更新                               |
| DELETE | `/subscriptions/{id}`   | 刪除                               |
| GET    | `/stats/monthly`        | 每月開銷彙總                       |
| POST   | `/jobs/check-expiring`  | Cron binding 觸發的到期掃描（內部）|
