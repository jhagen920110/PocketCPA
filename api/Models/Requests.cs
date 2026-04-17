using System.Text.Json.Serialization;

namespace api.Models;

public class UploadRequest
{
    [JsonPropertyName("fileName")]
    public string FileName { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    [JsonPropertyName("month")]
    public string? Month { get; set; }
}

public class AnalyzeRequest
{
    [JsonPropertyName("month")]
    public string? Month { get; set; }

    [JsonPropertyName("statementIds")]
    public List<string> StatementIds { get; set; } = [];
}
