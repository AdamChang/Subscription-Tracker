# Subscription Tracker — Dapr 智慧訂閱與續費提醒系統

管理軟體訂閱（Netflix、Spotify、AWS…），於續費前自動發送通知，並統計每月花費。

本專案以 **學習 Dapr 四大 building blocks** 為核心目標：單一使用者、無身分驗證，刻意保持精簡。

> 設計與實作文件見 [`docs/superpowers/`](docs/superpowers/)（`specs/` 設計、`plans/` 實作計畫）。
> 開發指引見 [`CLAUDE.md`](CLAUDE.md)。

## 架構

雙服務 + Dapr sidecar，透過 Redis 作為 state store 與 pub/sub broker：

| 服務 | 角色 | 使用的 Dapr Building Block |
|------|------|---------------------------|
| `SubscriptionTracker.Api` | 訂閱 CRUD、花費統計、Cron 觸發到期掃描 | State Management、Cron Input Binding、Pub/Sub Publish |
| `SubscriptionTracker.Worker` | 消費通知事件並實際發送 | Pub/Sub Subscribe、HTTP Output Binding（Discord）、SMTP Output Binding（Email） |

```
Cron Binding ──► API /cron-check ──► 掃描到期訂閱 ──► Pub/Sub「notifications」
                                                              │
                                                              ▼
                                          Worker 訂閱 ──► Discord / Email 發送
```

## 技術棧

- **後端**：.NET 8、ASP.NET Core Minimal API、Dapr .NET SDK
- **前端**：Angular 19（standalone SPA）
- **基礎設施**：Dapr、Redis、Docker Compose
- **測試**：xUnit、FluentAssertions、Moq（Domain 層走 TDD）

## 專案結構

```
src/
  SubscriptionTracker.Contracts/   兩服務共用記錄型別（Subscription, NotificationRequested）
  SubscriptionTracker.Api/         Minimal API
    Domain/                        純邏輯（ExpiryScanner, MonthlyCostCalculator）
    State/                         DaprSubscriptionStore（index-key pattern）
    Endpoints/                     Subscription / Stats / Job 端點
    Validation/                    FluentValidation
  SubscriptionTracker.Worker/      事件消費者
    Senders/                       INotificationSender + Discord / Email 實作
  web/                             Angular 19 SPA
tests/                             xUnit + FluentAssertions + Moq
dapr/components/                   statestore、pubsub、cron-check、discord、smtp
docs/superpowers/                  specs/（設計）、plans/（實作計畫）
```

## API 端點

| 方法 | 路由 | 說明 |
|------|------|------|
| GET | `/subscriptions` | 取得全部訂閱 |
| GET | `/subscriptions/{id}` | 取得單一訂閱 |
| POST | `/subscriptions` | 新增訂閱（FluentValidation） |
| PUT | `/subscriptions/{id}` | 更新訂閱 |
| DELETE | `/subscriptions/{id}` | 刪除訂閱 |
| GET | `/stats/monthly` | 每月花費統計 |
| POST | `/cron-check` | Cron binding 觸發到期掃描（名稱須對應 `dapr/components/cron-check.yaml`） |

## 快速開始

### 後端 + Dapr + Redis（端到端）

```bash
docker compose up --build -d
```

### 前端（開發模式）

```bash
cd src/web
npm install
npm start          # http://localhost:4200
```

## 建置與測試

```bash
# .NET（解決方案為 .slnx）
dotnet build SubscriptionTracker.slnx
dotnet test                          # 17 passed + 1 skipped（整合測試需 Docker）

# Angular
cd src/web
npm run build
npm test                             # Karma / Jasmine
```

## 埠號

| 服務 | 埠 |
|------|----|
| subscription-api | 5000 |
| notification-worker | 5001 |
| redis | 6379 |
| Angular dev server | 4200 |

## 注意事項（學習導向的刻意取捨）

- `dapr/components/discord.yaml`、`smtp.yaml` 目前為 **placeholder 憑證**，填入真實值前應移出版控（env-var 或 `.example` + gitignore）。
- `LastNotifiedOn` 採 fire-and-forget 更新；partial-failure 時 Dapr 重試可能重送通知。
- CORS 設為 `AllowAnyOrigin`；前端表單未含 cycle/channels 欄位（預設 Monthly + Discord）；PUT 無 validator。
