# Pocket CPA

AI-powered personal spending analyzer. Upload bank or credit-card statements (PDF / CSV) and get
categorized spending breakdowns, fun stats, and a household ledger (가계부) you can search, filter,
and visualize — all from a mobile-friendly PWA that installs to your home screen as **Pocket CPA**.

## What it does

- Drop in one or more statements (Chase, American Express, Bank of America, Capital One, Discover,
  Citi, Wells Fargo, USAA, …). Each file is read, uploaded, and analyzed independently.
- Azure OpenAI (`gpt-5.4-mini`) extracts every transaction with strict JSON-schema output. The
  backend does all the math deterministically — totals, percentages, dedup, refund handling.
- Per-month-per-bank records are auto-overwritten when re-uploaded, so the latest run wins.
- Household Ledger view: every transaction across every analysis, with sortable columns, filters
  (search / category / month / bank / amount range), and a GitHub-style spending heatmap.
- Fun Stats: biggest single purchase, most-visited merchant, hottest spending day, top category,
  small-purchase tally, unique merchant count.
- Mobile-friendly app shell: bottom tab bar, navigation locked while an analysis is in flight,
  per-file animated progress (read → upload → analyze → done). Selected month is shared across
  Dashboard and Ledger and persisted across reloads.
- Personal greetings from the AI using your chosen display name.

## Architecture

| Layer       | Stack                                                                  |
|-------------|------------------------------------------------------------------------|
| Frontend    | SvelteKit 2 + Svelte 5 (runes) + TypeScript + Vite, static adapter     |
| Backend     | Azure Functions .NET 9 isolated worker (C#)                            |
| Storage     | Azure Cosmos DB (SQL API) — `statements`, `analyses` containers        |
| AI          | Azure AI Foundry — `gpt-5.4-mini`, strict JSON schema, temperature 0.1 |
| PDF parsing | PdfPig                                                                 |
| Hosting     | Azure Static Web Apps + linked Function App                            |
| Auth        | Static Web Apps managed Google sign-in                                 |

## Repository layout

```
api/                  Azure Functions backend (C#)
  Functions/          HTTP triggers: statements, analyze, analyses, ledger
  Models/             POCOs: Statement, Analysis, LedgerEntry, …
  Services/           SpendingAnalyzerService (AI), AnalysisService, StatementService
  Middleware/         AuthMiddleware (X-User-Email → userId)
  Program.cs          DI / Cosmos / HttpClient registration
web-svelte/           SvelteKit frontend (installs as "Pocket CPA" PWA)
  src/routes/
    +layout.svelte    App shell, splash, top bar, bottom tab nav
    +page.svelte      Dashboard: month view + insights
    ledger/+page.svelte  Household ledger (가계부)
  src/lib/
    appState.ts       Shared stores (analyses, ledger, selectedMonth)
    auth.ts           Google auth + display name
    components/       Upload, analysis results, heatmap, donut, …
```

## Running locally

### Prerequisites

- .NET 9 SDK
- Node.js 20+
- Azure Functions Core Tools v4
- An Azure Cosmos DB account + an Azure AI Foundry deployment of `gpt-5.4-mini`

### Backend

Create `api/local.settings.json` (gitignored):

```json
{
  "IsEncrypted": false,
  "Values": {
    "AzureWebJobsStorage": "UseDevelopmentStorage=true",
    "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
    "CosmosDb__Endpoint": "https://<your-cosmos>.documents.azure.com:443/",
    "CosmosDb__Key": "<cosmos-key>",
    "CosmosDb__DatabaseName": "SpendingSuggestionDb",
    "AzureAi__Endpoint": "https://<your-foundry>.services.ai.azure.com",
    "AzureAi__ApiKey": "<foundry-key>",
    "AzureAi__DeploymentName": "gpt-5.4-mini"
  },
  "Host": {
    "CORS": "*"
  }
}
```

Then:

```pwsh
cd api
func start          # http://localhost:7071/api
```

### Frontend

```pwsh
cd web-svelte
npm install
npm run dev -- --port 8080   # http://localhost:8080
```

In dev, auth falls back to a stub email; in production Google sign-in via Static Web Apps is used.

## Notes on accuracy

- Each statement is sent to the model in its own request — combining different bank formats in one
  prompt led to duplicated/inflated totals.
- The AI returns a flat transaction list; **all totals, percentages, sorting, and refund handling
  are computed in C#** to avoid LLM arithmetic drift.
- Same `(month, bank)` overwrites the previous analysis; Chase March + Amex March stay separate.
# SpendingSuggestion

AI-powered personal spending analyzer. Upload bank or credit-card statements (PDF / CSV) and get
categorized spending breakdowns, fun stats, and a household ledger (가계부) you can search, filter,
and visualize.

## What it does

- Drop in one or more statements (Chase, American Express, Bank of America, Capital One, Discover,
  Citi, Wells Fargo, USAA, …). Each file is read, uploaded, and analyzed independently.
- Azure OpenAI (`gpt-5.4-mini`) extracts every transaction with strict JSON-schema output. The
  backend does all the math deterministically — totals, percentages, dedup, refund handling.
- Per-month-per-bank records are auto-overwritten when re-uploaded, so the latest run wins.
- Household Ledger view: every transaction across every analysis, with sortable columns, filters
  (search / category / month / bank / amount range), and a GitHub-style spending heatmap.
- Fun Stats: biggest single purchase, most-visited merchant, hottest spending day, top category,
  small-purchase tally, unique merchant count.
- Mobile-friendly app shell: bottom tab bar, navigation locked while an analysis is in flight,
  per-file animated progress (read → upload → analyze → done).

## Architecture

| Layer       | Stack                                                                  |
|-------------|------------------------------------------------------------------------|
| Frontend    | SvelteKit 2 + Svelte 5 (runes) + TypeScript + Vite, static adapter     |
| Backend     | Azure Functions .NET 9 isolated worker (C#)                            |
| Storage     | Azure Cosmos DB (SQL API) — `statements`, `analyses` containers        |
| AI          | Azure AI Foundry — `gpt-5.4-mini`, strict JSON schema, temperature 0.1 |
| PDF parsing | PdfPig                                                                 |
| Hosting     | Azure Static Web Apps + linked Function App                            |
| Auth        | Static Web Apps managed Google sign-in                                 |

## Repository layout

```
api/                  Azure Functions backend (C#)
  Functions/          HTTP triggers: statements, analyze, analyses, ledger
  Models/             POCOs: Statement, Analysis, LedgerEntry, …
  Services/           SpendingAnalyzerService (AI), AnalysisService, StatementService
  Middleware/         AuthMiddleware (X-User-Email → userId)
  Program.cs          DI / Cosmos / HttpClient registration
web-svelte/           SvelteKit frontend
  src/routes/
    +layout.svelte    App shell, top bar, bottom tab nav
    +page.svelte      Home: latest result + past analyses + upload
    ledger/+page.svelte  Household ledger (가계부)
  src/lib/components/
    UploadAndAnalyze.svelte  Animated multi-file upload+analyze flow
    AnalysisResults.svelte   Donut + categories + fun stats
    PastAnalyses.svelte      List with bank pills
    SpendingHeatmap.svelte   GitHub-style daily heatmap
    DonutChart.svelte
    …
```

## Running locally

### Prerequisites

- .NET 9 SDK
- Node.js 20+
- Azure Functions Core Tools v4
- An Azure Cosmos DB account + an Azure AI Foundry deployment of `gpt-5.4-mini`

### Backend

Create `api/local.settings.json` (gitignored):

```json
{
  "IsEncrypted": false,
  "Values": {
    "AzureWebJobsStorage": "UseDevelopmentStorage=true",
    "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
    "CosmosDb__Endpoint": "https://<your-cosmos>.documents.azure.com:443/",
    "CosmosDb__Key": "<cosmos-key>",
    "CosmosDb__DatabaseName": "SpendingSuggestionDb",
    "AzureAi__Endpoint": "https://<your-foundry>.services.ai.azure.com",
    "AzureAi__ApiKey": "<foundry-key>",
    "AzureAi__DeploymentName": "gpt-5.4-mini"
  },
  "Host": {
    "CORS": "*"
  }
}
```

Then:

```pwsh
cd api
func start          # http://localhost:7071/api
```

### Frontend

```pwsh
cd web-svelte
npm install
npm run dev -- --port 8080   # http://localhost:8080
```

In dev, auth falls back to a stub email; in production Google sign-in via Static Web Apps is used.

## Notes on accuracy

- Each statement is sent to the model in its own request — combining different bank formats in one
  prompt led to duplicated/inflated totals.
- The AI returns a flat transaction list; **all totals, percentages, sorting, and refund handling
  are computed in C#** to avoid LLM arithmetic drift.
- Same `(month, bank)` overwrites the previous analysis; Chase March + Amex March stay separate.

