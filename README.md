# EventHub

讓主辦方上架活動、報名者線上報名並以綠界付款的平台（手機版網頁）。
金流模式：款項先進平台的綠界帳戶，平台扣除手續費後再撥款給主辦方。

## 技術

- .NET 8（SDK 版本由 `global.json` 固定）／ASP.NET Core MVC + Razor
- EF Core 8 + MySQL 8（Pomelo）
- ASP.NET Core Identity：角色 `Admin`、`Organizer`（主辦方）、`Attendee`（報名者）
- MailKit 寄信
- xUnit + WebApplicationFactory 整合測試

## 目錄

```
src/EventHub.Web/
  Controllers/        MVC 控制器
  Data/               AppDbContext、ApplicationUser、角色、Migrations
  Infrastructure/     設定類別（EcpayOptions、SmtpOptions）
  Views/              Razor 頁面（手機版版型在 Shared/_Layout）
tests/EventHub.Tests/ 整合測試（InMemory DB）
```

## 本機開發

前置：Laragon 已啟動 MySQL（root、無密碼、port 3306）。

```powershell
dotnet tool install -g dotnet-ef              # 首次（本機已裝）
dotnet ef database update -p src/EventHub.Web  # 建立／更新資料庫 eventhub
dotnet run --project src/EventHub.Web          # http://localhost:5117
dotnet test
```

### 帳號

- 註冊頁（`/Account/Register`）可選「報名者」或「主辦方」，不能註冊成管理員。
- 管理員由設定檔 `AdminAccount` 指定，網站啟動時若帳號不存在就自動建立。
  開發環境預設 `admin@eventhub.local` / `Admin12345`；正式環境請以環境變數或 user-secrets 設定。

新增資料表後建立 migration：

```powershell
dotnet ef migrations add <名稱> -p src/EventHub.Web -o Data/Migrations
```

### 綠界（測試環境）

`appsettings.Development.json` 已填入綠界公開的測試特店資料；請以
[綠界開發者文件](https://developers.ecpay.com.tw/) 核對最新值。
正式金鑰**不要**寫進 repo，改用 user-secrets 或環境變數：

```powershell
dotnet user-secrets init --project src/EventHub.Web
dotnet user-secrets set "Ecpay:HashKey" "xxxx" --project src/EventHub.Web
```

付款結果通知（ReturnURL）是綠界伺服器直接呼叫，必須是公開網址。本機用 ngrok：

```powershell
C:\laragon\bin\ngrok\ngrok.exe http 5117
```

再把產生的 https 網址填到 `Ecpay:PublicBaseUrl`。

### 寄信

開發環境預設寄到 `localhost:1025`，建議裝 [Mailpit](https://mailpit.axllent.org/) 收測試信
（網頁介面 http://localhost:8025）。
