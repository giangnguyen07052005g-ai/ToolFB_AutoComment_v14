using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ToolFB.App.Models;

namespace ToolFB.App.Services;

public static class SecureStorageService
{
    private static readonly string ConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config_pro.dat");
    private static readonly string PlainConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config_pro.json");

    public static void SaveConfig(AppConfig config)
    {
        try
        {
            var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            var plainBytes = Encoding.UTF8.GetBytes(json);
            
            // Protect data using Windows DPAPI
            var encrypted = ProtectedData.Protect(plainBytes, null, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(ConfigPath, encrypted);

            // Also keep plain config for non-credential preferences if helpful
            var sanitized = new AppConfig
            {
                FixedComment = config.FixedComment,
                RandomComments = config.RandomComments,
                OrderMode = config.OrderMode,
                DelayMin = config.DelayMin,
                DelayMax = config.DelayMax,
                MaxCommentsPerToken = config.MaxCommentsPerToken,
                MaxCommentsPerPost = config.MaxCommentsPerPost,
                Slots = config.Slots,
                TrueRoundRobin = config.TrueRoundRobin,
                AutoWarmup = config.AutoWarmup,
                SendReactions = config.SendReactions,
                ReactionType = config.ReactionType,
                UseMicroSalt = config.UseMicroSalt,
                IconCategory = config.IconCategory,
                IconNewline = config.IconNewline,
                SmartRestAfter = config.SmartRestAfter,
                SmartRestDuration = config.SmartRestDuration,
                SelectedImagePath = config.SelectedImagePath,
                TargetUrls = config.TargetUrls
                // Exclude RawAccountLines from plaintext json
            };
            var sanitizedJson = JsonSerializer.Serialize(sanitized, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(PlainConfigPath, sanitizedJson, Encoding.UTF8);
        }
        catch
        {
            // Fallback plain write if DPAPI fails
            try
            {
                var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(PlainConfigPath, json, Encoding.UTF8);
            }
            catch { }
        }
    }

    public static AppConfig LoadConfig()
    {
        // Try DPAPI first
        if (File.Exists(ConfigPath))
        {
            try
            {
                var cipherBytes = File.ReadAllBytes(ConfigPath);
                var plainBytes = ProtectedData.Unprotect(cipherBytes, null, DataProtectionScope.CurrentUser);
                var json = Encoding.UTF8.GetString(plainBytes);
                var cfg = JsonSerializer.Deserialize<AppConfig>(json);
                if (cfg != null) return cfg;
            }
            catch { }
        }

        // Try plain json
        if (File.Exists(PlainConfigPath))
        {
            try
            {
                var json = File.ReadAllText(PlainConfigPath, Encoding.UTF8);
                var cfg = JsonSerializer.Deserialize<AppConfig>(json);
                if (cfg != null) return cfg;
            }
            catch { }
        }

        return new AppConfig();
    }
}
