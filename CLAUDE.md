# CLAUDE.md

本檔提供 Claude Code 在此專案工作時的指引。全域開發者偏好（.NET 風格、繁中溝通、Token 控制）見 `~/.claude/CLAUDE.md`，此處只記錄本專案特有資訊。

## 專案概觀

Dapr 智慧訂閱與續費提醒系統 —— 管理軟體訂閱（Netflix、Spotify、AWS…），於續費前自動發送通知並統計每月花費。**以學習 Dapr 四大 building blocks 為主**：單一使用者、無驗證。

雙服務架構（design 文件「方案 A」）：

| 服務 | 角色 | Dapr 用途 |
|------|------|-----------|
| `SubscriptionTracker.Api` | 訂閱 CRUD、花費統計、Cron 觸發掃描 | State Management、Cron Input Binding、Pub/Sub Publish |
| `SubscriptionTracker.Worker` | 訂閱通知事件、實際發送 | Pub/Sub Subscribe、HTTP Output Binding（Discord）、SMTP Output Binding（Email） |

## 專案結構

```
src/
  SubscriptionTracker.Contracts/   兩服務共用的記錄型別（Subscription, NotificationRequested）
  SubscriptionTracker.Api/         Minimal API
    Domain/                        純邏輯（ExpiryScanner, MonthlyCostCalculator）— 走 TDD
    State/                         DaprSubscriptionStore（index-key pattern）
    Endpoints/                     SubscriptionEndpoints, StatsEndpoints, JobEndpoints
    Validation/                    FluentValidation
  SubscriptionTracker.Worker/      事件消費者
    Senders/                       INotificationSender + Discord/Email 實作
  web/                             Angular 19 standalone SPA
tests/                             xUnit + FluentAssertions + Moq
dapr/components/                   statestore, pubsub, cron-check, discord, smtp
docs/superpowers/                  specs/（設計）與 plans/（實作計畫）
```

## 建置與測試

```bash
# .NET（解決方案為 .slnx）
dotnet build SubscriptionTracker.slnx
dotnet test                         # 17 passed + 1 skipped（整合測試需 Docker）

# Angular（於 src/web）
cd src/web && npm install
npm run build
npm test                            # Karma/Jasmine

# 端到端（後端 + Dapr sidecar + Redis）
docker compose up --build -d
cd src/web && npm start             # 前端 http://localhost:4200
```

## 關鍵約定（修改時務必遵守）

- **目標框架固定 net8.0**。SDK 較新時預設會跳到 net10.0，新增 csproj 後請確認 `<TargetFramework>net8.0</TargetFramework>`。
- **命名空間一律 `SubscriptionTracker.*`**，避免 `Subscription` 型別與命名空間衝突。
- **Cron binding 名稱 = 路由**：`dapr/components/cron-check.yaml` 的 `metadata.name` 必須等於 `JobEndpoints` 的 `POST /cron-check`。改其一要同步改另一。
- **Dapr 元件名稱在程式碼中以字串硬編**：`statestore`、`pubsub`/topic `notifications`、binding `discord`/`smtp`。改 YAML 的 `metadata.name` 要同步改程式碼字串。
- **元件 `scopes` 必須對應 daprd 的 `-app-id`**：`subscription-api` / `notification-worker`（見 `docker-compose.yml`）。
- **JSON 序列化統一用 `JsonSerializerDefaults.Web`**（camelCase）。`DaprSubscriptionStore` 內手動序列化也須沿用，以與 DaprClient 一致。
- **DTO 一律用 record**（Contracts 與 Request/Response 模型）。
- **不在各 method 寫 try-catch**：走全域 `AddProblemDetails()` + `UseExceptionHandler()`。
- 驗證 Dapr SDK API 簽章時，讀本機原始碼 `F:\OpenSource\dapr-dotnet-sdk\src`，勿臆測。

## 埠號

| 服務 | 埠 |
|------|----|
| subscription-api | 5000 |
| notification-worker | 5001 |
| redis | 6379 |
| Angular dev server | 4200 |

## 已知刻意取捨（學習導向，可接受）

- `LastNotifiedOn` 採 fire-and-forget 更新；partial-failure 時 Dapr 重試可能重送通知。
- `dapr/components/discord.yaml`、`smtp.yaml` 目前為 placeholder 憑證——填真實值前應移出版控（env-var 或 `.example` + gitignore）。
- CORS 為 `AllowAnyOrigin`；輕量表單未含 cycle/channels 輸入欄（預設 Monthly + Discord）；PUT 無 validator。

## 收尾偏好

完成功能分支時，**squash 後合併回 master（本機，無 remote）再刪除分支**；保持 master 線性、一功能一 commit。
