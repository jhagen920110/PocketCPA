using System.Text.Json;
using api.Models;
using api.Services;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

// Quick local runner — hits the REAL SpendingAnalyzerService.
//
// Modes:
//   dotnet run                         → analyze default local PDFs once
//   dotnet run -- file1.pdf file2.pdf  → analyze the provided files once
//   dotnet run -- --runs 3             → analyze default local PDFs 3 times, show variance
//   dotnet run -- --runs 3 a.pdf b.pdf → analyze given files 3 times, show variance
//   dotnet run -- --cosmos <userId>    → pull every statement from Cosmos for that user,
//                                         group by month, and re-analyze each bucket
//   dotnet run -- --cosmos <userId> --bank "Sapphire"
//                                       → same, but only statements whose filename matches

// ---- args parsing ----
var argList = args.ToList();
int runs = 1;
string? cosmosUser = null;
string? cleanupUser = null;
string? bankFilter = null;
string? monthFilter = null;
bool dumpTx = false;
bool apply = false;
for (int i = argList.Count - 1; i >= 0; i--)
{
    if (argList[i] == "--runs" && i + 1 < argList.Count)
    {
        runs = int.Parse(argList[i + 1]);
        argList.RemoveRange(i, 2);
    }
    else if (argList[i] == "--cosmos" && i + 1 < argList.Count)
    {
        cosmosUser = argList[i + 1];
        argList.RemoveRange(i, 2);
    }
    else if (argList[i] == "--cleanup" && i + 1 < argList.Count)
    {
        cleanupUser = argList[i + 1];
        argList.RemoveRange(i, 2);
    }
    else if (argList[i] == "--bank" && i + 1 < argList.Count)
    {
        bankFilter = argList[i + 1];
        argList.RemoveRange(i, 2);
    }
    else if (argList[i] == "--month" && i + 1 < argList.Count)
    {
        monthFilter = argList[i + 1];
        argList.RemoveRange(i, 2);
    }
    else if (argList[i] == "--dump-tx")
    {
        dumpTx = true;
        argList.RemoveAt(i);
    }
    else if (argList[i] == "--apply")
    {
        apply = true;
        argList.RemoveAt(i);
    }
}

// ---- secrets from api/local.settings.json ----
// Walk up from either cwd or project dir to find the repo root.
string FindSettings()
{
    foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
    {
        var dir = new DirectoryInfo(start);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "api", "local.settings.json");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
    }
    throw new FileNotFoundException("Could not locate api/local.settings.json from cwd or bin dir.");
}
var settingsPath = FindSettings();
using var settingsDoc = JsonDocument.Parse(File.ReadAllText(settingsPath));
var values = settingsDoc.RootElement.GetProperty("Values");
string Get(string key) => values.GetProperty(key).GetString() ?? throw new Exception($"Missing {key}");

var aiOpts = Options.Create(new AzureAiOptions
{
    Endpoint = Get("AzureAi__Endpoint"),
    ApiKey = Get("AzureAi__ApiKey"),
    DeploymentName = Get("AzureAi__DeploymentName")
});

using var loggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
var logger = loggerFactory.CreateLogger<SpendingAnalyzerService>();
var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
var service = new SpendingAnalyzerService(http, aiOpts, logger);

// ---- dispatch ----
if (cleanupUser != null)
{
    await RunCleanupMode(cleanupUser, apply, monthFilter);
    return;
}

if (cosmosUser != null)
{
    await RunCosmosMode(cosmosUser, bankFilter, monthFilter, dumpTx);
    return;
}

var files = argList.Count > 0 ? argList.ToArray() : new[] {
    @"..\amazon-chase-feb-2026.pdf",
    @"..\amex-feb-2026.pdf"
};

if (runs > 1)
{
    await RunConsistencyMode(files, runs);
}
else
{
    foreach (var file in files)
        await AnalyzeLocalFile(file);
}

// =====================================================================
// Helpers
// =====================================================================

async Task AnalyzeLocalFile(string file)
{
    var path = Path.GetFullPath(file);
    Console.WriteLine();
    Console.WriteLine("=====================================================");
    Console.WriteLine($"📄 {Path.GetFileName(path)}");
    Console.WriteLine("=====================================================");

    var bytes = File.ReadAllBytes(path);
    var rawContent = "[PDF:base64]" + Convert.ToBase64String(bytes);
    var stmt = new Statement
    {
        UserId = "local-test",
        FileName = Path.GetFileName(path),
        RawContent = rawContent
    };

    try
    {
        var analysis = await service.AnalyzeAsync(new List<Statement> { stmt }, null);
        PrintAnalysis(analysis);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  ERROR: {ex.Message}");
    }
}

void PrintAnalysis(Analysis analysis)
{
    Console.WriteLine();
    Console.WriteLine($"  Month:        {analysis.Month}");
    Console.WriteLine($"  Bank:         {analysis.Bank}");
    Console.WriteLine($"  Discretionary (totalSpent): ${analysis.TotalSpent:N2}");
    Console.WriteLine($"  Bills (billsTotal):         ${analysis.BillsTotal:N2}");
    Console.WriteLine($"  Grand total:  ${analysis.TotalSpent + analysis.BillsTotal:N2}");
    Console.WriteLine();
    Console.WriteLine("  Categories:");
    foreach (var c in analysis.Categories)
    {
        var tag = c.Name == "Bills" ? "  [non-discretionary]" : "";
        Console.WriteLine($"    {c.Name,-15} ${c.Total,10:N2}  ({c.Percentage,5:N1}%)  txs={c.Transactions.Count}{tag}");
    }

    var bills = analysis.Categories.FirstOrDefault(c => c.Name == "Bills");
    if (bills != null && bills.Transactions.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine("  Bills transactions:");
        foreach (var t in bills.Transactions.OrderByDescending(t => t.Amount))
            Console.WriteLine($"    {t.Date,-10} {t.Merchant,-30} ${t.Amount,8:N2}  ({Trim(t.Description, 40)})");
    }

    var negatives = analysis.Categories
        .SelectMany(c => c.Transactions.Select(t => new { Cat = c.Name, Tx = t }))
        .Where(x => x.Tx.Amount < 0)
        .ToList();
    if (negatives.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine($"  Refund / negative rows ({negatives.Count}):");
        foreach (var x in negatives)
            Console.WriteLine($"    {x.Tx.Date,-10} {x.Cat,-12} {x.Tx.Merchant,-30} ${x.Tx.Amount,9:N2}  ({Trim(x.Tx.Description, 50)})");
    }
}

async Task RunConsistencyMode(string[] files, int runCount)
{
    Console.WriteLine();
    Console.WriteLine("=====================================================");
    Console.WriteLine($"🔁 CONSISTENCY RUN — {runCount} iterations over {files.Length} file(s)");
    Console.WriteLine("=====================================================");

    var stmts = files.Select(f =>
    {
        var path = Path.GetFullPath(f);
        var bytes = File.ReadAllBytes(path);
        return new Statement
        {
            UserId = "local-test",
            FileName = Path.GetFileName(path),
            RawContent = "[PDF:base64]" + Convert.ToBase64String(bytes)
        };
    }).ToList();

    var perRun = new List<RunResult>();
    for (int r = 1; r <= runCount; r++)
    {
        Console.WriteLine();
        Console.WriteLine($"── Run {r}/{runCount} ──");
        try
        {
            var analysis = await service.AnalyzeAsync(stmts, null);
            var cats = analysis.Categories.ToDictionary(c => c.Name, c => c.Total);
            var txCount = analysis.Categories.Sum(c => c.Transactions.Count);
            perRun.Add(new RunResult(r, analysis.Month, analysis.Bank,
                analysis.TotalSpent, analysis.BillsTotal, txCount, cats));

            Console.WriteLine($"  month={analysis.Month}  bank={analysis.Bank}  " +
                              $"disc=${analysis.TotalSpent:N2}  bills=${analysis.BillsTotal:N2}  txs={txCount}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ERROR: {ex.Message}");
        }
    }

    if (perRun.Count == 0) return;

    Console.WriteLine();
    Console.WriteLine("=====================================================");
    Console.WriteLine("📊 SUMMARY");
    Console.WriteLine("=====================================================");
    Console.WriteLine($"{"Run",-5}{"Month",-10}{"Bank",-25}{"Disc",15}{"Bills",15}{"Txs",6}");
    foreach (var r in perRun)
        Console.WriteLine($"{r.Run,-5}{r.Month,-10}{Trim(r.Bank, 22),-25}{r.Disc,15:C2}{r.Bills,15:C2}{r.TxCount,6}");

    var discVals = perRun.Select(r => r.Disc).ToList();
    var billsVals = perRun.Select(r => r.Bills).ToList();
    var txVals = perRun.Select(r => r.TxCount).ToList();
    Console.WriteLine();
    Console.WriteLine($"  Discretionary:  min=${discVals.Min():N2}  max=${discVals.Max():N2}  " +
                      $"spread=${discVals.Max() - discVals.Min():N2}  mean=${discVals.Average():N2}");
    Console.WriteLine($"  Bills:          min=${billsVals.Min():N2}  max=${billsVals.Max():N2}  " +
                      $"spread=${billsVals.Max() - billsVals.Min():N2}");
    Console.WriteLine($"  Tx count:       min={txVals.Min()}  max={txVals.Max()}  spread={txVals.Max() - txVals.Min()}");

    var allCats = perRun.SelectMany(r => r.Categories.Keys).Distinct().OrderBy(k => k).ToList();
    Console.WriteLine();
    Console.WriteLine("  Per-category totals per run:");
    Console.Write($"    {"Category",-15}");
    foreach (var r in perRun) Console.Write($"Run{r.Run,-4}".PadLeft(12));
    Console.WriteLine("    spread");
    foreach (var cat in allCats)
    {
        Console.Write($"    {cat,-15}");
        var vals = new List<decimal>();
        foreach (var r in perRun)
        {
            var v = r.Categories.GetValueOrDefault(cat, 0m);
            vals.Add(v);
            Console.Write($"{v,12:N2}");
        }
        var spread = vals.Max() - vals.Min();
        Console.WriteLine($"   ${spread,9:N2}");
    }
}

async Task RunCosmosMode(string userId, string? bank, string? monthOnly, bool dumpTopTx)
{
    // Also write to a log file so long runs survive terminal quirks.
    var logFile = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "cosmos-retest.log");
    logFile = Path.GetFullPath(logFile);
    using var logWriter = new StreamWriter(logFile, append: false) { AutoFlush = true };
    void Log(string line) { Console.WriteLine(line); logWriter.WriteLine(line); }
    Log("");
    Log("=====================================================");
    Log($"☁️  COSMOS RE-TEST  user={userId}  bank={bank ?? "(all)"}");
    Log("=====================================================");

    using var client = new CosmosClient(Get("CosmosDb__Endpoint"), Get("CosmosDb__Key"));
    var db = client.GetDatabase(Get("CosmosDb__DatabaseName"));
    var stmtContainer = db.GetContainer("statements");
    var analysisContainer = db.GetContainer("analyses");

    var allStmts = new List<Statement>();
    var q = new QueryDefinition("SELECT c.id, c.userId, c.fileName, c.month, c.rawContent, c.uploadedAt FROM c WHERE c.userId = @u")
        .WithParameter("@u", userId);
    using (var it = stmtContainer.GetItemQueryIterator<Statement>(q,
        requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(userId) }))
    {
        while (it.HasMoreResults)
        {
            var batch = await it.ReadNextAsync();
            allStmts.AddRange(batch);
        }
    }
    Log($"  Fetched {allStmts.Count} statements from Cosmos.");

    var stored = new Dictionary<string, StoredAnalysis>();
    var qa = new QueryDefinition("SELECT c.id, c.month, c.bank, c.totalSpent, c.billsTotal FROM c WHERE c.userId = @u")
        .WithParameter("@u", userId);
    using (var it = analysisContainer.GetItemQueryIterator<StoredAnalysis>(qa,
        requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(userId) }))
    {
        while (it.HasMoreResults)
        {
            var batch = await it.ReadNextAsync();
            foreach (var a in batch)
                stored[$"{a.month}|{a.bank}"] = a;
        }
    }
    Log($"  Fetched {stored.Count} stored analyses for comparison.");

    var filtered = allStmts.AsEnumerable();
    if (!string.IsNullOrWhiteSpace(bank))
    {
        var needle = bank.ToLowerInvariant();
        filtered = filtered.Where(s => (s.FileName ?? "").ToLowerInvariant().Contains(needle));
    }
    if (!string.IsNullOrWhiteSpace(monthOnly))
    {
        // Support YYYY-MM (e.g. 2025-10) by also matching the compact YYYYMM form
        // used in Chase filenames like "20251007-statements-...pdf".
        var compact = monthOnly.Replace("-", "");
        filtered = filtered.Where(s => (s.Month ?? "") == monthOnly
                                        || (s.FileName ?? "").Contains(monthOnly)
                                        || (s.FileName ?? "").Contains(compact));
    }
    // Skip obvious junk (the "test.txt" row, empty raw content).
    filtered = filtered.Where(s =>
        !string.IsNullOrEmpty(s.RawContent) &&
        !string.Equals(s.FileName, "test.txt", StringComparison.OrdinalIgnoreCase));

    var stmtList = filtered.ToList();
    Log($"  Statements to analyze individually: {stmtList.Count}");

    // Analyze each statement on its own. This mirrors how the API now classifies
    // per-statement (bank/month detection happens per file) and keeps the AI prompt
    // small enough to stay under the output-token cap.
    var individual = new List<(Statement Stmt, Analysis? Analysis, string? Error)>();
    int i = 0;
    foreach (var s in stmtList)
    {
        i++;
        Log("");
        Log($"── [{i}/{stmtList.Count}] {s.FileName}");
        try
        {
            var a = await service.AnalyzeAsync(new List<Statement> { s }, null);
            var txCount = a.Categories.Sum(c => c.Transactions.Count);
            Log($"   month={a.Month}  bank={a.Bank}  " +
                $"disc=${a.TotalSpent:N2}  bills=${a.BillsTotal:N2}  txs={txCount}");

            // Flag suspicious totals and always dump top txs when --dump-tx is set
            // or whenever the monthly total looks crazy (> $10k disc on a single statement).
            bool suspicious = a.TotalSpent > 10_000m;
            if (dumpTopTx || suspicious)
            {
                var allTx = a.Categories.SelectMany(c => c.Transactions.Select(t => (Cat: c.Name, Tx: t)))
                    .OrderByDescending(x => x.Tx.Amount)
                    .Take(10)
                    .ToList();
                Log(suspicious ? "   ⚠️  SUSPICIOUS — top 10 transactions:" : "   top 10 transactions:");
                foreach (var x in allTx)
                    Log($"     {x.Tx.Date,-10} {x.Cat,-12} {Trim(x.Tx.Merchant, 32),-32} ${x.Tx.Amount,12:N2}");
            }
            individual.Add((s, a, null));
        }
        catch (Exception ex)
        {
            Log($"   ERROR: {ex.Message}");
            individual.Add((s, null, ex.Message));
        }
    }

    // Aggregate per (month, bank) so a month covered by 2 cards is summed properly.
    var buckets = individual
        .Where(x => x.Analysis != null)
        .GroupBy(x => (x.Analysis!.Month, x.Analysis!.Bank))
        .Select(g => new ReRunResult(
            g.Key.Month,
            g.Key.Bank,
            g.Sum(x => x.Analysis!.TotalSpent),
            g.Sum(x => x.Analysis!.BillsTotal),
            g.Sum(x => x.Analysis!.Categories.Sum(c => c.Transactions.Count))))
        .OrderBy(r => r.Month).ThenBy(r => r.Bank)
        .ToList();
    var results = buckets;

    Log("");
    Log("=====================================================");
    Log("📊 SUMMARY");
    Log("=====================================================");
    Log($"{"Month",-10}{"Bank",-22}{"NewDisc",13}{"NewBills",13}{"OldDisc",13}{"OldBills",13}{"ΔDisc",14}{"ΔBills",14}");
    decimal sumNewDisc = 0, sumNewBills = 0, sumOldDisc = 0, sumOldBills = 0;
    foreach (var r in results.OrderBy(r => r.Month).ThenBy(r => r.Bank))
    {
        StoredAnalysis? old = null;
        if (stored.TryGetValue($"{r.Month}|{r.Bank}", out var exact)) old = exact;
        else
        {
            var monthMatches = stored.Values.Where(s => s.month == r.Month).ToList();
            if (monthMatches.Count == 1) old = monthMatches[0];
        }
        decimal oldDisc = old?.totalSpent ?? 0, oldBills = old?.billsTotal ?? 0;
        decimal dD = r.Disc - oldDisc, dB = r.Bills - oldBills;
        sumNewDisc += r.Disc; sumNewBills += r.Bills; sumOldDisc += oldDisc; sumOldBills += oldBills;
        Log($"{r.Month,-10}{Trim(r.Bank, 20),-22}{r.Disc,13:C2}{r.Bills,13:C2}" +
            $"{oldDisc,13:C2}{oldBills,13:C2}  {Sign(dD)}${Math.Abs(dD),9:N2}  {Sign(dB)}${Math.Abs(dB),9:N2}");
    }
    Log(new string('─', 130));
    Log($"{"TOTAL",-32}{sumNewDisc,13:C2}{sumNewBills,13:C2}{sumOldDisc,13:C2}{sumOldBills,13:C2}" +
        $"  {Sign(sumNewDisc - sumOldDisc)}${Math.Abs(sumNewDisc - sumOldDisc),9:N2}" +
        $"  {Sign(sumNewBills - sumOldBills)}${Math.Abs(sumNewBills - sumOldBills),9:N2}");
}

static string Sign(decimal v) => v >= 0 ? "+" : "-";
static string Trim(string s, int max) => s.Length > max ? s[..max] + "…" : s;

async Task RunCleanupMode(string userId, bool applyChanges, string? monthOnly)
{
    Console.WriteLine();
    Console.WriteLine("=====================================================");
    Console.WriteLine($"🧹 CLEANUP + BACKFILL  user={userId}  apply={applyChanges}  month={monthOnly ?? "(all)"}");
    Console.WriteLine("=====================================================");
    if (!applyChanges) Console.WriteLine("  (DRY RUN — pass --apply to make changes)");

    using var client = new CosmosClient(Get("CosmosDb__Endpoint"), Get("CosmosDb__Key"),
        new CosmosClientOptions
        {
            SerializerOptions = new CosmosSerializationOptions
            {
                PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase
            }
        });
    var db = client.GetDatabase(Get("CosmosDb__DatabaseName"));
    var stmtContainer = db.GetContainer("statements");
    var analysisContainer = db.GetContainer("analyses");
    var pk = new PartitionKey(userId);

    // ---- 1. Load all statements, group by fileName, keep newest uploadedAt ----
    var allStmts = new List<Statement>();
    var q = new QueryDefinition("SELECT c.id, c.userId, c.fileName, c.month, c.rawContent, c.uploadedAt FROM c WHERE c.userId = @u")
        .WithParameter("@u", userId);
    using (var it = stmtContainer.GetItemQueryIterator<Statement>(q,
        requestOptions: new QueryRequestOptions { PartitionKey = pk }))
    {
        while (it.HasMoreResults)
        {
            var batch = await it.ReadNextAsync();
            allStmts.AddRange(batch);
        }
    }
    Console.WriteLine($"  Fetched {allStmts.Count} statements.");

    // Junk filter: drop empty rawContent and the "test.txt" rows outright.
    var junk = allStmts.Where(s =>
        string.IsNullOrEmpty(s.RawContent) ||
        string.Equals(s.FileName, "test.txt", StringComparison.OrdinalIgnoreCase)).ToList();
    var good = allStmts.Except(junk).ToList();

    // Dedupe by fileName (case-insensitive, trimmed). Keep most recent uploadedAt.
    var keep = good
        .GroupBy(s => (s.FileName ?? "").Trim().ToLowerInvariant())
        .Select(g => g.OrderByDescending(s => s.UploadedAt ?? "").First())
        .ToList();
    var dupesToDelete = good.Except(keep).ToList();

    // Optional month filter: when set, narrow the re-analysis (and wipe) to a
    // single month. Used to patch a specific month without rebuilding all
    // analyses. Matches either s.Month or the compact YYYYMM in filename.
    if (!string.IsNullOrWhiteSpace(monthOnly))
    {
        var compact = monthOnly.Replace("-", "");
        keep = keep.Where(s =>
            (s.Month ?? "") == monthOnly ||
            (s.FileName ?? "").Contains(monthOnly) ||
            (s.FileName ?? "").Contains(compact)).ToList();
        Console.WriteLine($"  Month filter \"{monthOnly}\" narrowed keep to {keep.Count} statement(s).");
    }

    Console.WriteLine($"  Junk rows to delete:       {junk.Count}");
    Console.WriteLine($"  Duplicate rows to delete:  {dupesToDelete.Count}");
    Console.WriteLine($"  Unique statements keeping: {keep.Count}");
    foreach (var g in good.GroupBy(s => (s.FileName ?? "").Trim().ToLowerInvariant())
                          .Where(g => g.Count() > 1).OrderBy(g => g.Key))
    {
        Console.WriteLine($"    {g.Key}: {g.Count()} copies → deleting {g.Count() - 1}");
    }

    if (applyChanges)
    {
        int d = 0;
        foreach (var s in junk.Concat(dupesToDelete))
        {
            try { await stmtContainer.DeleteItemAsync<Statement>(s.Id, pk); d++; }
            catch (CosmosException ex) { Console.WriteLine($"    delete fail {s.Id}: {ex.StatusCode}"); }
        }
        Console.WriteLine($"  ✅ Deleted {d} statement rows.");
    }

    // ---- 2. Wipe existing analyses for this user (optionally just the target month) ----
    var existingAnalysisIds = new List<string>();
    var qa = string.IsNullOrWhiteSpace(monthOnly)
        ? new QueryDefinition("SELECT c.id FROM c WHERE c.userId = @u").WithParameter("@u", userId)
        : new QueryDefinition("SELECT c.id FROM c WHERE c.userId = @u AND c.month = @m")
            .WithParameter("@u", userId).WithParameter("@m", monthOnly);
    using (var it = analysisContainer.GetItemQueryIterator<IdRow>(qa,
        requestOptions: new QueryRequestOptions { PartitionKey = pk }))
    {
        while (it.HasMoreResults)
        {
            var batch = await it.ReadNextAsync();
            foreach (var row in batch) existingAnalysisIds.Add(row.id);
        }
    }
    Console.WriteLine($"  Existing analyses to wipe: {existingAnalysisIds.Count}");
    if (applyChanges)
    {
        int d = 0;
        foreach (var id in existingAnalysisIds)
        {
            try { await analysisContainer.DeleteItemAsync<object>(id, pk); d++; }
            catch (CosmosException ex) { Console.WriteLine($"    delete analysis fail {id}: {ex.StatusCode}"); }
        }
        Console.WriteLine($"  ✅ Deleted {d} analyses.");
    }

    // ---- 3. Re-analyze each kept statement and insert fresh analyses ----
    Console.WriteLine();
    Console.WriteLine($"  Re-analyzing {keep.Count} statements…");
    int created = 0, failed = 0;
    int i2 = 0;
    foreach (var s in keep)
    {
        i2++;
        Console.Write($"    [{i2}/{keep.Count}] {s.FileName} … ");
        try
        {
            var a = await service.AnalyzeAsync(new List<Statement> { s }, null);
            a.UserId = userId;
            a.Id = Guid.NewGuid().ToString();
            a.StatementIds = new List<string> { s.Id };
            Console.WriteLine($"month={a.Month} bank={a.Bank} disc=${a.TotalSpent:N2} bills=${a.BillsTotal:N2}");
            if (applyChanges)
            {
                await analysisContainer.CreateItemAsync(a, pk);
                created++;
            }
        }
        catch (Exception ex)
        {
            failed++;
            Console.WriteLine($"FAIL: {ex.Message}");
        }
    }
    Console.WriteLine();
    Console.WriteLine($"  Re-analysis: created={created} failed={failed}");
    if (!applyChanges) Console.WriteLine("  (dry run — nothing was written; re-run with --apply)");
}

class IdRow { public string id { get; set; } = ""; }

record RunResult(int Run, string Month, string Bank, decimal Disc, decimal Bills, int TxCount, Dictionary<string, decimal> Categories);
record ReRunResult(string Month, string Bank, decimal Disc, decimal Bills, int TxCount);
class StoredAnalysis
{
    public string id { get; set; } = "";
    public string month { get; set; } = "";
    public string bank { get; set; } = "";
    public decimal totalSpent { get; set; }
    public decimal billsTotal { get; set; }
}
