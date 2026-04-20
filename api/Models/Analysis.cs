using System.Text.Json.Serialization;

namespace api.Models;

public class Analysis
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("userId")]
    public string UserId { get; set; } = string.Empty;

    [JsonPropertyName("analyzedAt")]
    public string AnalyzedAt { get; set; } = DateTime.UtcNow.ToString("o");

    [JsonPropertyName("month")]
    public string Month { get; set; } = string.Empty;

    [JsonPropertyName("bank")]
    public string Bank { get; set; } = string.Empty;

    [JsonPropertyName("statementIds")]
    public List<string> StatementIds { get; set; } = [];

    [JsonPropertyName("totalSpent")]
    public decimal TotalSpent { get; set; }

    [JsonPropertyName("billsTotal")]
    public decimal BillsTotal { get; set; }

    [JsonPropertyName("categories")]
    public List<SpendingCategory> Categories { get; set; } = [];

    [JsonPropertyName("insights")]
    public List<string> Insights { get; set; } = [];

    [JsonPropertyName("suggestions")]
    public List<string> Suggestions { get; set; } = [];

    [JsonPropertyName("funStats")]
    public List<FunStat> FunStats { get; set; } = [];
}

public class FunStat
{
    [JsonPropertyName("emoji")]
    public string Emoji { get; set; } = string.Empty;

    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    [JsonPropertyName("value")]
    public string Value { get; set; } = string.Empty;
}

public class SpendingCategory
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("total")]
    public decimal Total { get; set; }

    [JsonPropertyName("percentage")]
    public decimal Percentage { get; set; }

    [JsonPropertyName("transactions")]
    public List<Transaction> Transactions { get; set; } = [];
}

public class Transaction
{
    [JsonPropertyName("date")]
    public string Date { get; set; } = string.Empty;

    [JsonPropertyName("merchant")]
    public string Merchant { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }
}
