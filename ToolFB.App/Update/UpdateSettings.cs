using System.IO;
using System.Text.Json;

namespace ToolFB.App.Update;

public class UpdateSettings
{
    private static readonly string SettingsFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "github_update.json");
    private static readonly string LegacySettingsFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "update_settings.json");

    // Thông tin cấu hình GitHub Releases Repository (Tài khoản chính thức của chủ tool)
    public string GitHubOwner { get; set; } = "giangnguyen07052005g-ai";
    public string GitHubRepo { get; set; } = "ToolFB_AutoComment_v14";
    public string CurrentVersion { get; set; } = "14.0.0";
    public bool UseGitHubReleasesApi { get; set; } = true;

    // Đường dẫn manifest dự phòng (nếu dùng static hosting hoặc raw GitHub)
    public string ManifestUrl { get; set; } = "https://raw.githubusercontent.com/giangnguyen07052005g-ai/ToolFB_AutoComment_v14/main/latest.json";

    public bool AutoCheckOnStartup { get; set; } = true;
    public bool AutoDownload { get; set; } = false;
    public string? SkipVersion { get; set; }
    public DateTime? LastChecked { get; set; }

    public static UpdateSettings Load()
    {
        try
        {
            if (File.Exists(SettingsFile))
            {
                var json = File.ReadAllText(SettingsFile);
                var s = JsonSerializer.Deserialize<UpdateSettings>(json);
                if (s != null) return s;
            }
            else if (File.Exists(LegacySettingsFile))
            {
                var json = File.ReadAllText(LegacySettingsFile);
                var s = JsonSerializer.Deserialize<UpdateSettings>(json);
                if (s != null) return s;
            }
        }
        catch { }

        var def = new UpdateSettings();
        def.Save();
        return def;
    }

    public void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsFile, json);
        }
        catch { }
    }
}
