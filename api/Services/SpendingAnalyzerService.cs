using System.Text;
using System.Text.Json;
using api.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace api.Services;

public class MonthNotDeterminedException : Exception
{
    public MonthNotDeterminedException() : base("AI could not determine the month from the statement data.") { }
}

public class SpendingAnalyzerService
{
    private readonly HttpClient _httpClient;
    private readonly AzureAiOptions _options;
    private readonly ILogger<SpendingAnalyzerService> _logger;

    private static readonly string[] SpendingCategories =
    [
        "Groceries", "Eat Out", "Transport",
        "Shopping", "Entertainment", "Subscription",
        "Health", "Travel", "Personal",
        "Education", "Maintenance", "Cash", "Bills", "Other"
    ];

    // Non-discretionary categories — tracked and displayed, but excluded from
    // the "what you spent daily" totals, donut, and fun stats. Pocket CPA is
    // a discretionary-spending coach; rent, utilities, insurance, and loan
    // payments just muddy the signal.
    private static readonly HashSet<string> NonDiscretionaryCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        "Bills"
    };

    private static readonly string SystemPrompt = $$"""
        You extract transactions from raw bank or credit-card statement text (Chase, American Express,
        Bank of America, Capital One, Discover, Citi, Wells Fargo, etc.). Every issuer formats their
        statements differently, so you must be flexible: look for rows that have a date, a merchant
        description, and a dollar amount.

        You DO NOT compute totals. You DO NOT group into categories. You simply return a flat list
        of transactions. The backend will do all the math.

        # Step 1: Find every transaction
        A transaction row has: <date> <description> <amount>. Dates may be MM/DD, MM/DD/YY, MM/DD/YYYY,
        or "Dec 18". Amounts are dollars and cents, possibly with thousands separators and an optional
        leading "-". PDF extraction often removes spaces; the AMOUNT is the LAST decimal number on the row.

        Examples (Chase):
            "12/07     WAL-MART #1833 FREDERICKSBUR VA10.41"        → 12/07, "WAL-MART #1833 FREDERICKSBUR VA", 10.41
            "12/18     LG ELECTRONICS USA INC ENGLEWOOD NJ-271.71"  → 12/18, "LG ELECTRONICS...", -271.71
        Examples (Amex):
            "12/15/25 TESLA SUPERCHARGER US 877-7983752 CA     $12.34" → 12/15/25, "TESLA SUPERCHARGER...", 12.34
        Examples (BofA):
            "12/19/2025 CHECKCARD 1218 WALMART GROCERY  -54.27" → 12/19/2025, "CHECKCARD 1218 WALMART GROCERY", -54.27

        # Step 2: Classify and filter
        For each transaction, decide:
          - PURCHASE: a merchant charge (spending). Return the amount as POSITIVE.
          - REFUND / RETURN / MERCHANT CREDIT: a negative amount clearly tied to a merchant
            ("RETURN WAL-MART", "AMAZON.COM REFUND", "AMAZON MKTPLACE PMTS ... -17.89",
            "LG ELECTRONICS ... -271.71"). Return the amount as NEGATIVE. Do NOT drop these —
            the backend uses them to cancel out the matching original purchase.
            IMPORTANT: On Chase Amazon (Prime Visa) statements, the "PAYMENTS AND OTHER CREDITS"
            section contains BOTH card payments AND merchant refunds. Any row whose merchant is
            "AMAZON MKTPLACE PMTS" or "AMAZON.COM" (with negative amount) is a REFUND — you MUST
            include it as a NEGATIVE transaction, even though it sits in that section. Only the
            "Payment Thank You", "AUTOPAY", or generic card-payment rows get skipped.
          - SKIP entirely (do not include in the output):
              * Card payments to the issuer: "Payment Thank You", "AUTOPAY", "PAYMENT - THANK YOU",
                "PAYMENT RECEIVED", "ONLINE PAYMENT", "MOBILE PAYMENT".
              * Balance transfers, cash advances treated as payments.
              * Deposits, payroll, transfers from other accounts.
              * Rewards redemptions, cashback credits, interest PAID to you, "SHOP WITH POINTS"
                rewards-redemption rows (Chase Amazon "Shop with Points" section).
              * Statement summaries, running balances, section totals.
              * Foreign-currency exchange-rate detail lines. Chase and other issuers follow every
                international charge with a sub-line like:
                    "WON   17,300 X 0.000721965 (EXCHG RATE)"
                    "EUR       25.50 X 1.086234 (EXCHG RATE)"
                These are NOT transactions — they are annotations for the USD charge immediately
                above them. The real merchant row (with its USD amount) is already in the list, so
                SKIP any row whose description contains "(EXCHG RATE)" or "EXCHG RATE" regardless
                of the amount shown.
              * For CHECKING accounts: any POSITIVE amount is a deposit — SKIP.

        # Step 3: Output
        Return a flat `transactions` array. Each row has:
          date:        the date as shown on the statement (keep original format)
          merchant:    a SHORT, CLEAN brand name a human would recognize. Strip store numbers, city/state,
                       processor prefixes ("TST*", "SQ *", "CHECKCARD"), and phone numbers. Examples:
                         "WAL-MART #1833 FREDERICKSBUR VA"    → "Walmart"
                         "TST* PARIS BAGUETTE - FAI FAIRFAX VA" → "Paris Baguette"
                         "SQ *H MART PLANO Plano TX"          → "H Mart"
                         "AIRBNB * HMP5MDDSQT AIRBNB.COM CA"  → "Airbnb"
                         "OPENAI *CHATGPT SUBSCR OPENAI.COM CA" → "ChatGPT"
                         "NTTA CSC - PLANO 972-818-6882 TX"   → "NTTA Tolls"
                         "CHECKCARD 1218 WALMART GROCERY"     → "Walmart"
          description: the original row text, unchanged (for reference). IF the next line in the
                       statement shows an "Order Number" (common on Chase Amazon statements),
                       append it verbatim so refunds can be matched back to their purchases.
                       Example: "AMAZON MKTPLACE PMTS Amzn.com/bill WA Order Number 111-3992656-4285019"
          amount:      dollars and cents. POSITIVE for purchases. NEGATIVE for refunds/returns.
          category:    EXACTLY one of: {{string.Join(", ", SpendingCategories)}}

        # Categorization guide
        - Groceries: Walmart (grocery), Target, Wegmans, Giant, H Mart, Kroger, Safeway, Trader Joe's,
          Whole Foods, Aldi, Costco (grocery), Sprouts, Publix.
        - Eat Out: any restaurant, cafe, fast food, coffee shop, DoorDash, Uber Eats,
          Grubhub, Starbucks, Chipotle, McDonald's, Subway, Panda Express, etc.
        - Transport (includes gas/fuel): EV charging (Tesla Supercharger, EVgo, Electrify America),
          Uber, Lyft, NTTA / tolls, parking, taxis, rental cars, gas / fuel stations (Exxon, Shell, Chevron,
          BP, Sunoco, Circle K, Wawa, Fast Stop, 7-Eleven when fuel), public transit.
        - Shopping: Amazon, eBay, Best Buy, Barnes & Noble, Office Depot, Staples, Home Depot,
          Lowe's, Apple Store (hardware), department stores, clothing, electronics.
        - Entertainment: movies, concerts, events, games, streaming rentals, Xbox/PlayStation purchases,
          gaming, ticket purchases.
        - Subscription: recurring monthly/yearly subscriptions like Spotify, Netflix, Hulu, Disney+,
          Apple.com/Bill, ChatGPT, Tesla Subscription, Audible, YouTube Premium, iCloud. Also small
          retail protection / warranty / membership fees (Storeplan, Asurion, SquareTrade, AppleCare,
          Costco Membership, Amazon Prime, Sam's Club) — these are optional subscriptions, NOT bills.
        - Bills (NON-DISCRETIONARY, tracked but excluded from daily-spending totals): ONLY real
          life-expense payments. Use this category STRICTLY for:
            * Utility providers — electric / gas / water / sewer / trash companies, Comcast/Xfinity,
              Spectrum, Cox, Fios, AT&T Fiber, cellular carriers (AT&T, Verizon, T-Mobile, Mint, Visible).
            * Insurance carriers — USAA, Progressive, Geico, State Farm, Allstate, Liberty Mutual,
              Farmers, Nationwide, health insurance premiums.
            * Rent / mortgage / HOA / property-management companies.
            * Auto-loan payments, student-loan servicers (Nelnet, Sallie Mae, Navient, MOHELA, AES),
              explicit loan-payment rows.
          DO NOT put small retail protection plans, warranty add-ons, memberships, or anything you're
          uncertain about into Bills — when in doubt, use Subscription or the merchant's natural category.
        - Health: CVS, Walgreens, doctor offices, hospitals, labs, dental.
        - Travel: Airbnb, Booking.com, Expedia, Marriott, Hilton, airline tickets, Super.com *Hotels.
        - Personal: salons, spas, barbershops, gyms, Planet Fitness.
        - Education: tuition, courses, textbooks, Udemy, Coursera.
        - Maintenance: U-Haul, Goodyear, AutoZone, tire shops, hardware stores, cleaning services,
          plumber, electrician, landscaping.
        - Cash: ATM withdrawals, cash advances.
        - Other: genuinely doesn't fit, or fees/interest/DMV/USPS — use sparingly.

        # Month detection
        Return "YYYY-MM" based on the statement's CLOSING date (preferred) or most common transaction month.
        - "Opening/Closing Date 12/08/25 - 01/07/26" → "2026-01"
        - If the user provides a month hint, use it.
        - If no dates exist and no hint is given, return "".

        # Bank detection
        Return the card-product name (co-brand aware) in the `bank` field so different cards from
        the same issuer don't collide. Look at the statement header, the card name on page 1, any
        "ACCOUNT ENDING IN XXXX" block, or reward-program branding.

        Canonical labels to use when the product is identifiable:
          - Chase cards: "Chase Sapphire", "Chase Freedom", "Chase Freedom Unlimited",
            "Chase Amazon" (Amazon Prime Visa), "Chase Ink", "Chase United", "Chase Southwest",
            "Chase Marriott", "Chase Hyatt", "Chase Disney". If only "Chase" is visible with
            no product branding, return "Chase".
          - American Express: "Amex Gold", "Amex Platinum", "Amex Green", "Amex Blue Cash",
            "Amex Delta", "Amex Hilton", "Amex Marriott". Otherwise "American Express".
          - Capital One: "Capital One Venture", "Capital One Quicksilver", "Capital One Savor",
            "Capital One Walmart". Otherwise "Capital One".
          - Citi: "Citi Double Cash", "Citi Costco", "Citi Premier", "Citi AAdvantage". Otherwise "Citi".
          - Discover: "Discover it". Otherwise "Discover".
          - Others: "Bank of America", "Wells Fargo", "USAA", "U.S. Bank", "Apple Card", "PayPal".

        If you cannot identify the product confidently, return the issuer short name alone.
        If you cannot identify the issuer at all, return "" (empty string).

        # Absolute rules
        - Extract EVERY transaction. Do not skip rows to save effort. Large statements may have 50-200 rows.
        - Do NOT invent transactions; only use rows literally present in the input.
        - Do NOT output the same row twice.
        - NEVER output a transaction from marketing/rewards copy. Red flags: merchant is a single
          generic verb/noun like "WON", "EARNED", "BONUS", "CONGRATULATIONS", "POINTS"; the row
          lives in a "You've earned / you've won / rewards summary / sapphire preferred benefits"
          block; the amount is suspiciously round (e.g., exactly 60000 points worth $600, or a
          five/six-digit dollar amount on a consumer card). When in doubt, SKIP.
        - No single consumer-card transaction exceeds ~$10,000. If you're about to output an
          amount ≥ $10,000, it is almost certainly a balance, points total, or marketing line —
          do NOT output it.
        - Sign discipline: if the original row shows a leading "-" or the issuer marks it as a credit/return,
          you MUST output a NEGATIVE amount. Sign-flipping a single refund can shift the user's total by
          hundreds of dollars and is the #1 source of incorrect totals.
        - When in doubt about a payment/credit row (e.g., "AUTOPAY", "PAYMENT - THANK YOU", "BALANCE
          TRANSFER", "CASHBACK REDEMPTION", "STATEMENT CREDIT", "REWARDS REDEMPTION"), SKIP it. Better
          to miss one payment than to count it as spending.
        - Ignore legal boilerplate (APR disclosures, payment instructions, rewards summaries, terms).
        - Do NOT compute totals; the backend does that.
        """;

    public SpendingAnalyzerService(HttpClient httpClient, IOptions<AzureAiOptions> options, ILogger<SpendingAnalyzerService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    private string PrepareContent(string rawContent, string fileName)
    {
        const string prefix = "[PDF:base64]";
        if (!rawContent.StartsWith(prefix))
            return rawContent;

        try
        {
            var base64 = rawContent.Substring(prefix.Length);
            var bytes = Convert.FromBase64String(base64);
            var sb = new StringBuilder();
            using var stream = new MemoryStream(bytes);
            using var pdf = PdfDocument.Open(stream);
            foreach (var page in pdf.GetPages())
            {
                sb.AppendLine(page.Text);
            }
            var text = sb.ToString();
            _logger.LogInformation("Extracted {Chars} chars from PDF {File}", text.Length, fileName);
            return text;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to extract text from PDF {File}", fileName);
            return $"[PDF extraction failed for {fileName}: {ex.Message}]";
        }
    }

    // DTO the AI fills in. Flat list only — backend does all math.
    private class AiFlatResponse
    {
        public string month { get; set; } = string.Empty;
        public string bank { get; set; } = string.Empty;
        public List<AiFlatTx> transactions { get; set; } = new();
        public List<string> insights { get; set; } = new();
        public List<string> suggestions { get; set; } = new();
    }

    private class AiFlatTx
    {
        public string date { get; set; } = string.Empty;
        public string merchant { get; set; } = string.Empty;
        public string description { get; set; } = string.Empty;
        public decimal amount { get; set; }
        public string category { get; set; } = string.Empty;
    }

    public async Task<Analysis> AnalyzeAsync(List<Statement> statements, string? month)
    {
        if (string.IsNullOrEmpty(_options.Endpoint) || string.IsNullOrEmpty(_options.ApiKey))
            throw new InvalidOperationException("AI service not configured.");

        // Process each statement INDEPENDENTLY so the model isn't asked to juggle
        // multiple bank formats in one prompt (which can cause duplicates / inflated totals).
        var allTx = new List<AiFlatTx>();
        var allInsights = new List<string>();
        var allSuggestions = new List<string>();
        var detectedMonths = new List<string>();
        var detectedBanks = new List<string>();
        int rawCount = 0;

        foreach (var statement in statements)
        {
            // Self-verification: extract each statement up to 3 times and take the
            // consensus. This catches rare AI drift (e.g. one pass hallucinating a
            // $10k/$100k row, or missing a whole page of transactions) without
            // doubling cost for the common case where two runs already agree.
            var perStatement = await ExtractFromOneVerifiedAsync(statement, month);
            rawCount += perStatement.transactions.Count;
            allTx.AddRange(perStatement.transactions);
            if (perStatement.insights != null) allInsights.AddRange(perStatement.insights);
            if (perStatement.suggestions != null) allSuggestions.AddRange(perStatement.suggestions);
            if (!string.IsNullOrWhiteSpace(perStatement.month))
                detectedMonths.Add(perStatement.month);
            if (!string.IsNullOrWhiteSpace(perStatement.bank))
                detectedBanks.Add(perStatement.bank);
        }

        // Deduplicate across all statements: same (normalized description + signed amount) is a dup.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unique = new List<AiFlatTx>(allTx.Count);
        int duplicatesRemoved = 0;
        foreach (var t in allTx)
        {
            var normDesc = System.Text.RegularExpressions.Regex.Replace(
                (t.description ?? string.Empty).Trim(), @"\s+", " ");
            var amt = Math.Round(t.amount, 2);
            var key = $"{normDesc}|{(amt >= 0 ? "+" : "-")}{Math.Abs(amt)}|{t.date}";
            if (seen.Add(key))
                unique.Add(t);
            else
                duplicatesRemoved++;
        }

        // Sanity guard: a consumer credit-card charge of >= $10,000 is almost
        // always an AI hallucination — typically marketing text ("YOU'VE WON 60,000
        // POINTS") or a balance/summary row misread as a transaction. Drop these
        // before any aggregation so a single bad row can't nuke a whole month.
        //
        // Also drop phantom "currency-only" rows where the merchant name itself
        // is literally a currency code ("WON", "EUR", "JPY", etc.). These come
        // from Chase's exchange-rate sub-lines ("WON 17,300 X 0.000721965 EXCHG RATE")
        // that the AI occasionally parses as separate transactions. We match only
        // on the merchant field (not description) because the description of a
        // REAL international purchase often ALSO contains "EXCHG RATE" sub-line text.
        const decimal SingleTxCap = 10_000m;
        var currencyOnlyMerchants = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "WON", "KRW", "EUR", "EURO", "JPY", "YEN", "GBP", "CNY", "RMB",
            "HKD", "TWD", "THB", "INR", "SGD", "AUD", "CAD", "MXN", "BRL",
            "CHF", "SEK", "NOK", "DKK", "PLN", "TRY", "ZAR", "AED", "IDR", "VND", "PHP"
        };
        int droppedOversized = 0;
        int droppedFxLine = 0;
        var guarded = new List<AiFlatTx>(unique.Count);
        foreach (var t in unique)
        {
            var merchantTrim = (t.merchant ?? string.Empty).Trim();
            if (currencyOnlyMerchants.Contains(merchantTrim))
            {
                droppedFxLine++;
                _logger.LogWarning(
                    "Dropping currency-only phantom row: merchant=\"{Merchant}\" amount={Amount:N2} date={Date}",
                    t.merchant, t.amount, t.date);
                continue;
            }
            if (Math.Abs(t.amount) >= SingleTxCap)
            {
                droppedOversized++;
                _logger.LogWarning(
                    "Dropping oversized transaction as likely AI hallucination: merchant=\"{Merchant}\" amount={Amount:C2} date={Date} desc=\"{Desc}\"",
                    t.merchant, t.amount, t.date, (t.description ?? "").Length > 80 ? t.description![..80] + "…" : t.description);
                continue;
            }
            guarded.Add(t);
        }
        unique = guarded;

        // Chase Amazon refund netting: group by Amazon Order Number and cancel matching
        // purchases with refunds. A fully refunded order is dropped entirely (no longer
        // shows in ledger / heatmap / fun stats). A partially refunded order keeps a
        // single net positive transaction.
        var detectedBankHint = detectedBanks
            .GroupBy(b => b, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .FirstOrDefault() ?? string.Empty;
        int refundsNetted = 0;
        if (detectedBankHint.Contains("Amazon", StringComparison.OrdinalIgnoreCase))
        {
            (unique, refundsNetted) = NetAmazonRefunds(unique);
        }

        // Group by category, sum deterministically. No trust in AI arithmetic.
        var grouped = unique
            .GroupBy(t => string.IsNullOrWhiteSpace(t.category) ? "Other" : t.category)
            .Select(g => new SpendingCategory
            {
                Name = g.Key,
                Total = g.Sum(t => t.amount),
                Transactions = g.Select(t => new Transaction
                {
                    Date = t.date,
                    Merchant = t.merchant,
                    Description = t.description,
                    Amount = t.amount
                }).ToList()
            })
            .Where(c => c.Total > 0)
            .ToList();

        // Discretionary total — this is what the dashboard shows as "spending".
        // Bills (rent, utilities, loan/insurance payments) are tracked in their
        // own category but do NOT count toward daily spending totals or percentages.
        var discretionary = grouped.Where(c => !NonDiscretionaryCategories.Contains(c.Name)).ToList();
        var billsTotal = grouped.Where(c => NonDiscretionaryCategories.Contains(c.Name)).Sum(c => c.Total);
        var totalSpent = discretionary.Sum(c => c.Total);

        foreach (var c in grouped)
        {
            // Percentage is relative to discretionary total so the donut math adds
            // to 100%. Bills get 0% (they're shown separately on the dashboard).
            if (NonDiscretionaryCategories.Contains(c.Name))
            {
                c.Percentage = 0;
            }
            else
            {
                c.Percentage = totalSpent > 0 ? Math.Round(c.Total / totalSpent * 100m, 1) : 0;
            }
        }
        // Discretionary sorted first (biggest to smallest), Bills always last.
        grouped = grouped
            .OrderBy(c => NonDiscretionaryCategories.Contains(c.Name) ? 1 : 0)
            .ThenByDescending(c => c.Total)
            .ToList();

        // Pick the month: user-provided > most common detected
        var finalMonth = !string.IsNullOrWhiteSpace(month)
            ? month!
            : detectedMonths
                .GroupBy(m => m)
                .OrderByDescending(g => g.Count())
                .ThenByDescending(g => g.Key)
                .Select(g => g.Key)
                .FirstOrDefault() ?? string.Empty;

        // Pick bank: most common detected, joined with " + " if multiple unique
        var finalBank = string.Empty;
        if (detectedBanks.Count > 0)
        {
            var uniqueBanks = detectedBanks
                .GroupBy(b => b, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count())
                .Select(g => g.Key)
                .ToList();
            finalBank = uniqueBanks.Count == 1 ? uniqueBanks[0] : string.Join(" + ", uniqueBanks);
        }

        _logger.LogInformation(
            "AI extraction: statements={N}, month={Month}, bank={Bank}, rawTx={Raw}, duplicatesRemoved={Dupes}, oversizedDropped={Oversized}, fxLinesDropped={Fx}, keptTx={Kept}, categories={Cats}, totalSpent={Total}, billsTotal={Bills}, amazonRefundsNetted={Netted}",
            statements.Count, finalMonth, finalBank, rawCount, duplicatesRemoved, droppedOversized, droppedFxLine, unique.Count, grouped.Count, totalSpent, billsTotal, refundsNetted);

        if (string.IsNullOrWhiteSpace(finalMonth))
            throw new MonthNotDeterminedException();

        // Fun stats should reflect discretionary spending only — hottest day,
        // biggest purchase, top category, etc. all exclude Bills.
        var discretionaryTxs = unique
            .Where(t => !NonDiscretionaryCategories.Contains(
                string.IsNullOrWhiteSpace(t.category) ? "Other" : t.category))
            .ToList();
        var funStats = BuildFunStats(discretionaryTxs, discretionary, totalSpent);

        return new Analysis
        {
            Month = finalMonth,
            Bank = finalBank,
            TotalSpent = totalSpent,
            BillsTotal = billsTotal,
            Categories = grouped,
            Insights = allInsights.Distinct().Take(8).ToList(),
            Suggestions = allSuggestions.Distinct().Take(8).ToList(),
            FunStats = funStats
        };
    }

    // Shape of one extraction pass used for agreement checks. Sum-of-abs amounts
    // + tx count is a strong fingerprint: two runs that agree on both almost
    // always have the same transactions, regardless of AI tie-breaking order.
    private static (decimal SumAbs, int Count) Fingerprint(AiFlatResponse r)
    {
        decimal sum = 0m;
        foreach (var t in r.transactions) sum += Math.Abs(t.amount);
        return (Math.Round(sum, 2), r.transactions.Count);
    }

    private static bool Agree((decimal SumAbs, int Count) a, (decimal SumAbs, int Count) b)
    {
        // Allow slightly more tx drift on large statements (big months naturally
        // have more borderline rows the AI might include or drop).
        var maxCount = Math.Max(a.Count, b.Count);
        var countTolerance = maxCount >= 80 ? 5 : 3;
        if (Math.Abs(a.Count - b.Count) > countTolerance) return false;
        if (a.SumAbs == 0m && b.SumAbs == 0m) return true;
        var scale = Math.Max(a.SumAbs, b.SumAbs);
        if (scale == 0m) return true;
        var diff = Math.Abs(a.SumAbs - b.SumAbs);
        // within 2% of the larger total, OR within $5 absolute (whichever is larger).
        return diff <= Math.Max(scale * 0.02m, 5m);
    }

    // Runs ExtractFromOneAsync twice; if the two runs agree (fingerprints match
    // within tolerance) we trust the first. If they disagree we run a third
    // tiebreaker pass and pick the pair that agree — returning whichever of
    // that pair has the MORE transactions (rows are more likely to be missed
    // than invented, and our oversized-row guard already drops hallucinated
    // big-ticket rows downstream).
    private async Task<AiFlatResponse> ExtractFromOneVerifiedAsync(Statement statement, string? month)
    {
        var run1 = await ExtractFromOneAsync(statement, month);
        var fp1 = Fingerprint(run1);

        var run2 = await ExtractFromOneAsync(statement, month);
        var fp2 = Fingerprint(run2);

        if (Agree(fp1, fp2))
        {
            _logger.LogInformation(
                "Verification OK for {File}: run1={Sum1:C2}/{N1}tx ≈ run2={Sum2:C2}/{N2}tx",
                statement.FileName, fp1.SumAbs, fp1.Count, fp2.SumAbs, fp2.Count);
            // Prefer the run with more transactions (missed rows > invented rows in practice).
            return run2.transactions.Count > run1.transactions.Count ? run2 : run1;
        }

        _logger.LogWarning(
            "Verification MISMATCH for {File}: run1={Sum1:C2}/{N1}tx vs run2={Sum2:C2}/{N2}tx — running tiebreaker",
            statement.FileName, fp1.SumAbs, fp1.Count, fp2.SumAbs, fp2.Count);

        AiFlatResponse? run3 = null;
        (decimal SumAbs, int Count)? fp3 = null;
        try
        {
            run3 = await ExtractFromOneAsync(statement, month);
            fp3 = Fingerprint(run3);
        }
        catch (Exception ex)
        {
            // Tiebreaker crashed (commonly: model hit output-token cap on a very
            // large month and returned truncated JSON). Don't fail the whole
            // statement — fall back to whichever of runs 1/2 has more txs.
            _logger.LogError(ex,
                "Tiebreaker run crashed for {File}; falling back to the better of runs 1 and 2",
                statement.FileName);
            return run1.transactions.Count >= run2.transactions.Count ? run1 : run2;
        }

        var fp3Val = fp3!.Value;
        // Pick the pair that agree; within that pair, prefer more transactions.
        (AiFlatResponse Pick, AiFlatResponse Partner, string Which) picked;
        if (Agree(fp1, fp3Val)) picked = (run1, run3!, "runs 1+3");
        else if (Agree(fp2, fp3Val)) picked = (run2, run3!, "runs 2+3");
        else if (Agree(fp1, fp2)) picked = (run1, run2, "runs 1+2");
        else
        {
            // No pair agreed — all three drifted. Fall back to the median-sum run.
            var sorted = new[] {
                (Run: run1, Fp: fp1),
                (Run: run2, Fp: fp2),
                (Run: run3!, Fp: fp3Val)
            }.OrderBy(x => x.Fp.SumAbs).ToList();
            var median = sorted[1];
            _logger.LogError(
                "Verification FAILED for {File}: all 3 runs disagree " +
                "(run1={S1:C2}/{N1} run2={S2:C2}/{N2} run3={S3:C2}/{N3}) — using median-total run",
                statement.FileName,
                fp1.SumAbs, fp1.Count, fp2.SumAbs, fp2.Count, fp3Val.SumAbs, fp3Val.Count);
            return median.Run;
        }

        _logger.LogInformation(
            "Verification resolved for {File} via {Which}: picked {N}tx sumAbs={Sum:C2}",
            statement.FileName, picked.Which,
            Math.Max(picked.Pick.transactions.Count, picked.Partner.transactions.Count),
            Fingerprint(picked.Pick).SumAbs);
        return picked.Pick.transactions.Count >= picked.Partner.transactions.Count
            ? picked.Pick : picked.Partner;
    }

    private async Task<AiFlatResponse> ExtractFromOneAsync(Statement statement, string? month)
    {
        var url = $"{_options.Endpoint}/openai/v1/chat/completions";
        var stmtText = PrepareContent(statement.RawContent, statement.FileName);

        var userMessage = !string.IsNullOrEmpty(month)
            ? $"Extract transactions from the following bank/credit-card statement for the month of {month}. Only include transactions from that month.\n\n--- {statement.FileName} ---\n{stmtText}"
            : $"Extract transactions from the following bank/credit-card statement. Determine the month from the statement's closing/transaction dates.\n\n--- {statement.FileName} ---\n{stmtText}";

        var requestBody = new
        {
            model = _options.DeploymentName,
            messages = new object[]
            {
                new { role = "system", content = SystemPrompt },
                new { role = "user", content = userMessage }
            },
            // temperature 0 + fixed seed makes re-runs of the same statement
            // produce the same transaction list (same total) instead of drifting
            // by a few hundred dollars between runs.
            temperature = 0,
            seed = 42,
            // 32k covers the largest real statements we've seen (~200 tx → ~22k chars of JSON).
            // Previously 16k occasionally truncated mid-array on big months.
            max_completion_tokens = 32000,
            response_format = new
            {
                type = "json_schema",
                json_schema = new
                {
                    name = "transaction_extraction",
                    strict = true,
                    schema = new
                    {
                        type = "object",
                        properties = new Dictionary<string, object>
                        {
                            ["month"] = new { type = "string" },
                            ["bank"] = new { type = "string", description = "Issuer name detected from the statement (e.g., Chase, American Express, Bank of America, Capital One, Discover, Citi, Wells Fargo, USAA). Empty string if unknown." },
                            ["transactions"] = new
                            {
                                type = "array",
                                items = new
                                {
                                    type = "object",
                                    properties = new Dictionary<string, object>
                                    {
                                        ["date"] = new { type = "string" },
                                        ["merchant"] = new { type = "string" },
                                        ["description"] = new { type = "string" },
                                        ["amount"] = new { type = "number" },
                                        ["category"] = new { type = "string", @enum = SpendingCategories }
                                    },
                                    required = new[] { "date", "merchant", "description", "amount", "category" },
                                    additionalProperties = false
                                }
                            },
                            ["insights"] = new { type = "array", items = new { type = "string" } },
                            ["suggestions"] = new { type = "array", items = new { type = "string" } }
                        },
                        required = new[] { "month", "bank", "transactions", "insights", "suggestions" },
                        additionalProperties = false
                    }
                }
            }
        };

        var json = JsonSerializer.Serialize(requestBody);
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("api-key", _options.ApiKey);

        var response = await _httpClient.SendAsync(request);
        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new Exception($"AI request failed for {statement.FileName} ({response.StatusCode}): {responseBody}");

        using var doc = JsonDocument.Parse(responseBody);
        var choice = doc.RootElement.GetProperty("choices")[0];
        var content = choice.GetProperty("message").GetProperty("content").GetString();
        var finishReason = choice.TryGetProperty("finish_reason", out var fr) ? fr.GetString() : null;

        AiFlatResponse? parsed = null;
        try
        {
            parsed = JsonSerializer.Deserialize<AiFlatResponse>(content!,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException jex)
        {
            // Response was truncated mid-array (usually finish_reason="length" on
            // very large statements). Recover whatever transactions we got by
            // closing the JSON at the last complete transaction, so we keep ~150
            // good txs instead of losing the entire statement.
            _logger.LogWarning(jex,
                "Truncated AI JSON for {File} (finish_reason={FR}); attempting recovery",
                statement.FileName, finishReason ?? "null");
            parsed = TryRecoverTruncatedJson(content!, statement.FileName);
        }

        if (parsed == null)
            throw new Exception($"Failed to parse AI response for {statement.FileName} (finish_reason={finishReason}).");

        _logger.LogInformation(
            "Per-statement extraction: file={File}, tx={Tx}, month={Month}",
            statement.FileName, parsed.transactions.Count, parsed.month);

        return parsed;
    }

    // Amazon order-number extraction: Chase Amazon statements list the Order Number on the
    // row immediately after the transaction, e.g. "Order Number 111-3992656-4285019". Both
    // the original purchase and any refund share the same order number — we use that to net
    // purchase + refund pairs into a single (possibly zero or positive) entry.
    private static readonly System.Text.RegularExpressions.Regex OrderNumberRegex =
        new(@"\b(?:Order\s*Number[:\s]*)?(\d{3}-\d{7}-\d{7})\b",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    // Salvage as many transactions as possible from an AI response that was cut
    // off mid-JSON (usually because max_completion_tokens was reached on a very
    // large statement). Strategy: find the transactions array, walk the brace
    // balance forward, and keep every COMPLETE {...} object inside it. Return
    // an AiFlatResponse with those txs and empty insights/suggestions — we'd
    // rather have 150 correct txs than zero.
    private AiFlatResponse? TryRecoverTruncatedJson(string content, string fileName)
    {
        try
        {
            var arrStart = content.IndexOf("\"transactions\"", StringComparison.Ordinal);
            if (arrStart < 0) return null;
            arrStart = content.IndexOf('[', arrStart);
            if (arrStart < 0) return null;

            // Also try to pull month + bank out of the JSON head (they come before transactions).
            string month = "";
            string bank = "";
            var mMatch = System.Text.RegularExpressions.Regex.Match(
                content[..arrStart], "\"month\"\\s*:\\s*\"([^\"]*)\"");
            if (mMatch.Success) month = mMatch.Groups[1].Value;
            var bMatch = System.Text.RegularExpressions.Regex.Match(
                content[..arrStart], "\"bank\"\\s*:\\s*\"([^\"]*)\"");
            if (bMatch.Success) bank = bMatch.Groups[1].Value;

            var txs = new List<AiFlatTx>();
            int i = arrStart + 1;
            int depth = 0;
            int objStart = -1;
            bool inStr = false;
            bool esc = false;
            while (i < content.Length)
            {
                char c = content[i];
                if (inStr)
                {
                    if (esc) esc = false;
                    else if (c == '\\') esc = true;
                    else if (c == '"') inStr = false;
                }
                else
                {
                    if (c == '"') inStr = true;
                    else if (c == '{')
                    {
                        if (depth == 0) objStart = i;
                        depth++;
                    }
                    else if (c == '}')
                    {
                        depth--;
                        if (depth == 0 && objStart >= 0)
                        {
                            var objJson = content.Substring(objStart, i - objStart + 1);
                            try
                            {
                                var tx = JsonSerializer.Deserialize<AiFlatTx>(objJson,
                                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                                if (tx != null) txs.Add(tx);
                            }
                            catch { /* skip malformed tx */ }
                            objStart = -1;
                        }
                    }
                    else if (c == ']' && depth == 0)
                    {
                        break; // reached natural end of transactions array
                    }
                }
                i++;
            }

            _logger.LogWarning(
                "Recovered {N} transactions from truncated JSON for {File} (month={Month} bank={Bank})",
                txs.Count, fileName, month, bank);

            return new AiFlatResponse
            {
                month = month,
                bank = bank,
                transactions = txs,
                insights = new List<string>(),
                suggestions = new List<string>()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Recovery of truncated JSON failed for {File}", fileName);
            return null;
        }
    }

    private static (List<AiFlatTx> txs, int netted) NetAmazonRefunds(List<AiFlatTx> input)
    {
        var byOrder = new Dictionary<string, List<AiFlatTx>>();
        var passthrough = new List<AiFlatTx>();
        foreach (var t in input)
        {
            var m = OrderNumberRegex.Match(t.description ?? string.Empty);
            if (!m.Success)
            {
                passthrough.Add(t);
                continue;
            }
            var key = m.Groups[1].Value;
            if (!byOrder.TryGetValue(key, out var list))
            {
                list = new List<AiFlatTx>();
                byOrder[key] = list;
            }
            list.Add(t);
        }

        int netted = 0;
        var outList = new List<AiFlatTx>(passthrough);
        foreach (var kvp in byOrder)
        {
            var group = kvp.Value;
            if (group.Count == 1)
            {
                outList.Add(group[0]);
                continue;
            }
            // Multiple rows share this order number — at least one is a refund.
            var net = group.Sum(t => t.amount);
            netted++;
            if (net <= 0.01m)
            {
                // Fully refunded (or net credit). Drop everything so it doesn't pollute
                // ledger / heatmap / fun stats.
                continue;
            }
            // Partial refund — keep a single merged entry with the NET amount. Prefer the
            // original purchase row as the template (positive amount, original date/merchant).
            var anchor = group.OrderByDescending(t => t.amount).First();
            outList.Add(new AiFlatTx
            {
                date = anchor.date,
                merchant = anchor.merchant,
                description = anchor.description,
                amount = Math.Round(net, 2),
                category = anchor.category
            });
        }

        return (outList, netted);
    }

    private static List<FunStat> BuildFunStats(List<AiFlatTx> txs, List<SpendingCategory> categories, decimal totalSpent)
    {
        var stats = new List<FunStat>();
        var purchases = txs.Where(t => t.amount > 0).ToList();
        if (purchases.Count == 0) return stats;

        // 1) Biggest single purchase
        var biggest = purchases.OrderByDescending(t => t.amount).First();
        stats.Add(new FunStat
        {
            Emoji = "💸",
            Label = "Biggest single purchase",
            Value = $"${biggest.amount:F2} at {CleanMerchant(biggest)}"
        });

        // 2) Most-visited merchant (by count, tiebreak by spend)
        var merchantGroups = purchases
            .GroupBy(t => CleanMerchant(t), StringComparer.OrdinalIgnoreCase)
            .Where(g => !string.IsNullOrWhiteSpace(g.Key))
            .Select(g => new { Name = g.Key, Count = g.Count(), Spend = g.Sum(x => x.amount) })
            .ToList();
        if (merchantGroups.Count > 0)
        {
            var top = merchantGroups
                .OrderByDescending(m => m.Count)
                .ThenByDescending(m => m.Spend)
                .First();
            if (top.Count >= 2)
            {
                stats.Add(new FunStat
                {
                    Emoji = "🏆",
                    Label = "Most-visited merchant",
                    Value = $"{top.Name} — {top.Count} times, ${top.Spend:F2}"
                });
            }
        }

        // 3) Biggest spending day
        var dayGroups = purchases
            .Where(t => !string.IsNullOrWhiteSpace(t.date))
            .GroupBy(t => t.date.Trim())
            .Select(g => new { Date = g.Key, Spend = g.Sum(x => x.amount), Count = g.Count() })
            .OrderByDescending(d => d.Spend)
            .ToList();
        if (dayGroups.Count > 0)
        {
            var hot = dayGroups.First();
            stats.Add(new FunStat
            {
                Emoji = "🔥",
                Label = "Hottest spending day",
                Value = $"{hot.Date} — ${hot.Spend:F2} across {hot.Count} {(hot.Count == 1 ? "purchase" : "purchases")}"
            });
        }

        // 4) Top category share
        if (categories.Count > 0)
        {
            var topCat = categories[0]; // already sorted desc
            stats.Add(new FunStat
            {
                Emoji = "🥇",
                Label = "Top category",
                Value = $"{topCat.Name} — {topCat.Percentage}% of spend"
            });
        }

        // 5) Small transactions count (under $10)
        var smallCount = purchases.Count(t => t.amount < 10m);
        if (smallCount >= 3)
        {
            var smallSum = purchases.Where(t => t.amount < 10m).Sum(t => t.amount);
            stats.Add(new FunStat
            {
                Emoji = "🪙",
                Label = "Small purchases (< $10)",
                Value = $"{smallCount} transactions totaling ${smallSum:F2}"
            });
        }

        // 6) Unique merchants
        var uniqueMerchants = purchases
            .Select(t => CleanMerchant(t))
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        stats.Add(new FunStat
        {
            Emoji = "🛒",
            Label = "Unique merchants",
            Value = $"{uniqueMerchants} different places"
        });

        // 7) Average transaction
        var avgTx = purchases.Average(t => t.amount);
        stats.Add(new FunStat
        {
            Emoji = "📏",
            Label = "Average transaction",
            Value = $"${avgTx:F2}"
        });

        // 8) Median transaction
        var sorted = purchases.OrderBy(t => t.amount).ToList();
        var med = sorted.Count % 2 == 1
            ? sorted[sorted.Count / 2].amount
            : (sorted[sorted.Count / 2 - 1].amount + sorted[sorted.Count / 2].amount) / 2m;
        stats.Add(new FunStat
        {
            Emoji = "⚖️",
            Label = "Median transaction",
            Value = $"${med:F2}"
        });

        // 9) Number of spending days
        var uniqueDays = dayGroups.Count;
        stats.Add(new FunStat
        {
            Emoji = "📅",
            Label = "Days with spending",
            Value = $"{uniqueDays} {(uniqueDays == 1 ? "day" : "days")}"
        });

        // 10) Transactions per active day
        if (uniqueDays > 0)
        {
            var txPerDay = (double)purchases.Count / uniqueDays;
            stats.Add(new FunStat
            {
                Emoji = "🧾",
                Label = "Transactions per active day",
                Value = $"{txPerDay:F1}"
            });
        }

        // 11) Weekend vs weekday split
        var weekendSpend = 0m;
        var weekdaySpend = 0m;
        foreach (var t in purchases)
        {
            if (TryParseTxDate(t.date, out var dt))
            {
                if (dt.DayOfWeek == DayOfWeek.Saturday || dt.DayOfWeek == DayOfWeek.Sunday)
                    weekendSpend += t.amount;
                else
                    weekdaySpend += t.amount;
            }
        }
        if (weekendSpend + weekdaySpend > 0)
        {
            var weekendPct = weekendSpend / (weekendSpend + weekdaySpend) * 100m;
            stats.Add(new FunStat
            {
                Emoji = "🎉",
                Label = "Weekend share",
                Value = $"{weekendPct:F0}% spent on weekends (${weekendSpend:F2})"
            });
        }

        // 12) Favorite weekday
        var byWeekday = purchases
            .Select(t => TryParseTxDate(t.date, out var dt) ? (DayOfWeek?)dt.DayOfWeek : null)
            .Where(d => d.HasValue)
            .GroupBy(d => d!.Value)
            .Select(g => new { Day = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .ToList();
        if (byWeekday.Count > 0)
        {
            stats.Add(new FunStat
            {
                Emoji = "🗓️",
                Label = "Most-active weekday",
                Value = $"{byWeekday[0].Day}s — {byWeekday[0].Count} purchases"
            });
        }

        // 13) Longest no-spend streak
        var spendDates = purchases
            .Select(t => TryParseTxDate(t.date, out var dt) ? (DateTime?)dt.Date : null)
            .Where(d => d.HasValue)
            .Select(d => d!.Value)
            .OrderBy(d => d)
            .ToList();
        if (spendDates.Count >= 2)
        {
            var maxGap = 0;
            for (int i = 1; i < spendDates.Count; i++)
            {
                var gap = (int)(spendDates[i] - spendDates[i - 1]).TotalDays;
                if (gap > maxGap) maxGap = gap;
            }
            if (maxGap >= 2)
            {
                stats.Add(new FunStat
                {
                    Emoji = "🧘",
                    Label = "Longest no-spend streak",
                    Value = $"{maxGap} {(maxGap == 1 ? "day" : "days")} in a row"
                });
            }
        }

        // 14) Spending per day (calendar)
        if (spendDates.Count >= 2)
        {
            var span = (spendDates.Last() - spendDates.First()).TotalDays + 1;
            if (span > 0)
            {
                var perDay = totalSpent / (decimal)span;
                stats.Add(new FunStat
                {
                    Emoji = "📈",
                    Label = "Daily average (calendar)",
                    Value = $"${perDay:F2}/day over {span:F0} days"
                });
            }
        }

        // 15) Top 3 categories share
        if (categories.Count >= 3)
        {
            var top3Sum = categories.Take(3).Sum(c => c.Percentage);
            stats.Add(new FunStat
            {
                Emoji = "🎯",
                Label = "Top 3 categories",
                Value = $"{top3Sum:F0}% of your spend"
            });
        }

        // 16) Biggest non-groceries non-bills purchase (discretionary splurge)
        var splurge = purchases
            .Where(t =>
            {
                var c = (t.category ?? string.Empty).ToLowerInvariant();
                return !c.Contains("grocer") && !c.Contains("utilit") && !c.Contains("bill") &&
                       !c.Contains("health") && !c.Contains("educat") && !c.Contains("fee");
            })
            .OrderByDescending(t => t.amount)
            .FirstOrDefault();
        if (splurge != null)
        {
            stats.Add(new FunStat
            {
                Emoji = "✨",
                Label = "Biggest splurge",
                Value = $"${splurge.amount:F2} at {CleanMerchant(splurge)}"
            });
        }

        // 17) 80/20 rule
        if (purchases.Count >= 5)
        {
            var sortedDesc = purchases.OrderByDescending(t => t.amount).ToList();
            var top20Count = Math.Max(1, (int)Math.Ceiling(sortedDesc.Count * 0.2));
            var top20Sum = sortedDesc.Take(top20Count).Sum(t => t.amount);
            var top20Pct = top20Sum / totalSpent * 100m;
            stats.Add(new FunStat
            {
                Emoji = "📊",
                Label = "80/20 rule",
                Value = $"Top {top20Count} buys = {top20Pct:F0}% of spend"
            });
        }

        // 18) Categories touched
        stats.Add(new FunStat
        {
            Emoji = "🧭",
            Label = "Categories touched",
            Value = $"{categories.Count} of 14"
        });

        // 19) Refunds received
        var refunds = txs.Where(t => t.amount < 0).ToList();
        if (refunds.Count > 0)
        {
            var refundSum = refunds.Sum(t => -t.amount);
            stats.Add(new FunStat
            {
                Emoji = "↩️",
                Label = "Refunds / returns",
                Value = $"{refunds.Count} totaling ${refundSum:F2}"
            });
        }

        return stats;
    }

    private static bool TryParseTxDate(string s, out DateTime dt)
    {
        dt = default;
        if (string.IsNullOrWhiteSpace(s)) return false;
        var trimmed = s.Trim();
        // Full ISO
        if (DateTime.TryParseExact(trimmed, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out dt)) return true;
        // US long
        if (DateTime.TryParseExact(trimmed, new[] { "M/d/yyyy", "MM/dd/yyyy", "M/d/yy", "MM/dd/yy" },
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out dt)) return true;
        // Fallback: best-effort
        return DateTime.TryParse(trimmed, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out dt);
    }

    private static string CleanMerchant(AiFlatTx t) =>
        !string.IsNullOrWhiteSpace(t.merchant) ? t.merchant.Trim() : (t.description ?? string.Empty).Trim();
}
