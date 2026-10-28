using System.Text.Json.Serialization;

namespace ToolFB.App.Update;

public class UpdateManifest
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("downloadUrl")]
    public string DownloadUrl { get; set; } = string.Empty;

    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = string.Empty;

    [JsonPropertyName("signature")]
    public string Signature { get; set; } = string.Empty;

    [JsonPropertyName("releaseNotes")]
    public string ReleaseNotes { get; set; } = string.Empty;

    [JsonPropertyName("minimumVersion")]
    public string? MinimumVersion { get; set; }

    [JsonPropertyName("mandatory")]
    public bool Mandatory { get; set; } = false;

    [JsonPropertyName("publishedAt")]
    public DateTime PublishedAt { get; set; } = DateTime.UtcNow;

    public string GetSigningPayload()
    {
        // Standardized canonical payload to prevent tampering with any critical field
        return $"{Version}|{Sha256}|{DownloadUrl}|{Mandatory}";
    }
}
