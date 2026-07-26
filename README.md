# HST Receipts

ASP.NET Core 10 web app that lets you upload a folder of receipt images/PDFs, extract **StoreName**, **GST/HST**, **TotalAmount**, and **Date**, save them to SQL Server, and download an Excel workbook.

## Login

Users are stored in SQL Server (`Users` table) with roles **Admin** or **Officer**.

Seed accounts are **not** hardcoded. Configure them in a gitignored local file:

1. Copy `src/HstReceipts.Web/appsettings.Development.local.json.example`
   to `src/HstReceipts.Web/appsettings.Development.local.json`
2. Set usernames/passwords (and optionally `AiLearning:ApiKey`)
3. Run the app — missing users are created on startup

Never commit real passwords or API keys. If an API key was previously committed, rotate it in the OpenAI dashboard.

## Solution structure

| Project | Role |
|---------|------|
| `src/HstReceipts.Web` | ASP.NET host + JSON API; serves React SPA |
| `client` | React + TypeScript (Vite) frontend |
| `src/HstReceipts.Core` | Entities, DTOs, interfaces |
| `src/HstReceipts.Infrastructure` | EF Core + Azure Document Intelligence (or local OCR fallback), ClosedXML |

## Prerequisites

1. **.NET 10 SDK**
2. **Azure AI Document Intelligence** (recommended) — create a Document Intelligence resource, then put `Endpoint` + `ApiKey` in gitignored `appsettings.Development.local.json` (see example). Model defaults to `prebuilt-receipt`.
3. **SQL Server** — LocalDB by default, or **Azure SQL**  
   - Local: `(localdb)\mssqllocaldb` / database `HstReceipts` (see `appsettings.json`)  
   - Azure: put the connection string in gitignored `appsettings.Development.local.json` (see example file). Provider stays `SqlServer`.  
   - SQLite: set `Database:Provider` to `Sqlite` and `DefaultConnection` to `Data Source=hstreceipts.db` (recreate EF migrations for that provider).
4. **Local OCR fallback (optional)** — only used when Document Intelligence is not enabled. Requires Tesseract English data at `src/HstReceipts.Web/tessdata/eng.traineddata` (included in this repo).

### Connect to Azure SQL

1. In Azure Portal → your SQL server → **Networking**: allow your client IP (and optionally Azure services).
2. Create a database (e.g. `HstReceipts`) and a SQL login/user with db access.
3. Copy `appsettings.Development.local.json.example` → `appsettings.Development.local.json` and set:

```json
"ConnectionStrings": {
  "DefaultConnection": "Server=YOUR_SERVER.database.windows.net,1433;Database=HstReceipts;User ID=YOUR_SQL_USER;Password=YOUR_SQL_PASSWORD;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;MultipleActiveResultSets=true"
},
"Database": {
  "Provider": "SqlServer"
}
```

4. Restart the app — startup runs `Database.Migrate()` against Azure and seeds users from that local file.

Never commit real Azure passwords. Prefer Azure AD auth / managed identity in production.

### Azure pipeline (required architecture)

`React → .NET API → Blob Storage → Azure Function → Document Intelligence → SQL → preview → Save`

1. **Azurite** (local blob) or a real Storage connection string in `BlobStorage:ConnectionString`
2. `Processing:Mode` = `Pipeline` (default)
3. `DocumentIntelligence:Enabled` = true with Endpoint + ApiKey
4. Copy `src/HstReceipts.Functions/local.settings.json.example` → `local.settings.json` (same SQL + DI + storage values)
5. Run Azurite, then the Function, then the web app:

```powershell
# Terminal 1 — Azurite (if using UseDevelopmentStorage=true)
azurite --silent --location c:\azurite --debug c:\azurite\debug.log

# Terminal 2 — Function
cd src\HstReceipts.Functions
func start

# Terminal 3 — Web
dotnet run --project src/HstReceipts.Web --urls http://localhost:5261
```

Flow: upload → API writes `ProcessingBatches` in SQL and blobs to inbox → Function runs `prebuilt-receipt` → writes `ProcessingBatchResults` in SQL → React polls `/api/receipts/batches/{id}` → you validate → **Save to database** (Receipts table).

## Frontend (React)

The UI is a **React + TypeScript (Vite)** SPA in `client/`, served by ASP.NET at `/client/`.

```powershell
cd client
npm install
npm run build
cd ..
dotnet run --project src/HstReceipts.Web --urls http://localhost:5261
```

Open http://localhost:5261/ (redirects to `/client/`).

JSON APIs live under `/api/auth/*` and `/api/receipts/*` (cookie authentication).

For Vite HMR during UI work (API must already be running on 5261):

```powershell
cd client
npm run dev
```

Then open the Vite URL and use the proxy to `/api`.

## Run

```powershell
cd "c:\Users\Miranda Kie\cursor_project\HST_Markdown"
dotnet run --project src/HstReceipts.Web
```

Open the URL shown in the console (typically `http://localhost:5261`). Build the React client first (`cd client && npm run build`); `dotnet publish` also builds it when Node 18+ is on PATH. Use **Node 18+** (this repo was verified with Node 22 via nvm).

On startup the app applies EF Core migrations and creates/updates the `HstReceipts` database on LocalDB.

## Usage

1. Drag and drop a **folder** of receipts onto the drop zone, or click to choose a folder.
2. You can also choose individual `.jpg` / `.png` / `.webp` / `.pdf` files.
3. Click **Extract receipt data** to analyze each file with Azure Document Intelligence (or local OCR if DI is not configured). Files with multiple receipts produce multiple rows.
4. Review the preview table (warnings appear when a field could not be parsed).
5. Click **Save to database** to persist rows in SQL Server.
6. Click **Export Excel** for an `.xlsx` with columns: `InvoiceNumber`, `StoreName`, `Currency`, `Subtotal`, `HST/GST`, `TotalAmount`, `Date`, `TransactionTime`.

## Configuration

Committed settings live in `src/HstReceipts.Web/appsettings.json` (no secrets).

Local secrets (passwords, API keys) go in `appsettings.Development.local.json` (gitignored).

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=(localdb)\\mssqllocaldb;Database=HstReceipts;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true"
  },
  "Database": {
    "Provider": "SqlServer"
  },
  "ReceiptProcessing": {
    "TessDataPath": "tessdata",
    "MaxUploadBytes": 104857600
  },
  "SeedUsers": {
    "Users": []
  },
  "AiLearning": {
    "Enabled": false,
    "ApiKey": "",
    "BaseUrl": "https://api.openai.com/v1",
    "Model": "gpt-4o-mini",
    "MaxSourceChars": 3500,
    "FillMissingFields": true,
    "MaxFillPerBatch": 20
  }
}
```

For SQLite instead, set `Database:Provider` to `Sqlite` and `DefaultConnection` to `Data Source=hstreceipts.db` (recreate EF migrations for that provider).

### Check the database

```powershell
sqlcmd -S "(localdb)\mssqllocaldb" -d HstReceipts -Q "SELECT InvoiceNumber, StoreName, Subtotal, GstHst, TotalAmount, ReceiptDate, MatchStatus FROM Receipts;"
```

### Invoice upsert & field corrections

**Export Excel and save** (Admin/Officer — not Demo) upserts into SQL Server by **InvoiceNumber only** (required):

| Match | When | Key |
|-------|------|-----|
| **Strong** | Invoice already in DB | `InvoiceNumber` |
| **New** | No match | Insert row (`MatchStatus = New`) |

`InvoiceNumber` is required for export/save. Field changes on overwrite are written to **`ReceiptCorrections`** (`FieldName`, `OldValue`, `NewValue`, `MatchKind`, `Username`, `CreatedAtEst`) so OCR/human edits stay auditable. The export response header `X-Save-Result` reports `inserted` / `updated` / `skipped` / `corrections`.

```powershell
sqlcmd -S "(localdb)\mssqllocaldb" -d HstReceipts -Q "SELECT TOP 20 CreatedAtEst, Username, FieldName, OldValue, NewValue, MatchKind FROM ReceiptCorrections ORDER BY CreatedAtEst DESC;"
```

## AI pipeline (hybrid)

```
OCR / PDF text
  → rule + store-specific extractors
  → apply learned profiles (store / currency / invoice hints from prior Export+save)
  → LLM structured fill for still-missing fields (validated against OCR)
  → preview edits
  → Export Excel and save → correction learning (Admin + toggle)
```

- **Rules first** for speed, cost, and reproducibility.
- **LLM fill** only when fields are missing and an API key is configured (`AiLearning:FillMissingFields`).
- Proposals are **rejected** if the invoice/money value is not found in the OCR text (hallucination guard).
- Preview warnings include `AI filled N missing field(s)…` when enrichment applies.

### Correction learning (Export Excel and save)

When Admin has **AI learning** on and corrects preview fields, export learns:

| Field | Stored as |
|-------|-----------|
| Store / currency | Canonical values + aliases |
| Invoice | Label or POS pattern (`label:Receipt Number`, `\b(P\d{13})\b`, …) |
| Date | `label:Invoice Date` (etc.) when the corrected date appears near that label in OCR |
| Subtotal / HST / Total | `label:Sub Total`, `label:HST`, `label:Total after tax`, … |

On the next similar upload, those labels are used to re-read values from OCR (never copies the previous bill’s dollar amounts). The export response header `X-Ai-Learning-Result` shows whether learning ran or was skipped.

### OpenAI usage & cost log

Every chat/completions call writes:

1. An **Information** log line with Eastern date/time, signed-in **user**, operation (`field_fill` / `correction_learning`), model, prompt/completion/total tokens, and **estimated USD cost**.
2. A row in SQL table **`AiApiUsageLogs`** (same fields) for auditing.

Cost is estimated from `AiLearning:InputUsdPer1MTokens` / `OutputUsdPer1MTokens` (defaults match gpt-4o-mini list pricing — update if you change models).

### Per-user daily rate limits

Before each OpenAI call, usage for the signed-in user is summed for the current **Eastern calendar day** against:

| Setting | Default | Meaning |
|---------|---------|---------|
| `MaxCallsPerUserPerDay` | `50` | Max API calls (set `0` = unlimited) |
| `MaxTokensPerUserPerDay` | `200000` | Max total tokens (`0` = unlimited) |
| `MaxEstimatedCostUsdPerUserPerDay` | `1.00` | Max estimated USD (`0` = unlimited) |

When a limit is hit, field-fill/learning is skipped (rule extraction still runs) and a warning is logged. Preview may show: *AI field fill skipped: daily OpenAI usage limit reached…*

```powershell
sqlcmd -S "(localdb)\mssqllocaldb" -d HstReceipts -Q "SELECT TOP 20 CreatedAtEst, Username, Operation, Model, PromptTokens, CompletionTokens, TotalTokens, EstimatedCostUsd, Context FROM AiApiUsageLogs ORDER BY CreatedAtEst DESC;"
```

## Evaluation harness

Golden OCR cases live in `tests/HstReceipts.Tests/golden/`. Run:

```powershell
dotnet test tests/HstReceipts.Tests
```

This scores the rule extractor field-by-field (target ≥ 70% on the sample set) and unit-tests the LLM proposal validator (accepts values present in OCR, rejects invented ids/totals). Add more `.json` fixtures as you collect real receipts.

## Notes

- Image text is extracted with **Tesseract**; PDF text with **PdfPig**.
- Field parsing uses heuristics for Canadian GST/HST labels (`GST`, `HST`, `TVH`, `TPS`). Accuracy varies with receipt quality; treat OCR results as a starting point.
- Upload limit defaults to **100 MB** per batch.
