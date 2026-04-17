using System.Text.Json.Serialization;

namespace api.Models;

public class Statement
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("userId")]
    public string UserId { get; set; } = string.Empty;

    [JsonPropertyName("fileName")]
    public string FileName { get; set; } = string.Empty;

    [JsonPropertyName("rawContent")]
    public string RawContent { get; set; } = string.Empty;

    [JsonPropertyName("month")]
    public string? Month { get; set; }

    [JsonPropertyName("uploadedAt")]
    public string UploadedAt { get; set; } = DateTime.UtcNow.ToString("o");
}
