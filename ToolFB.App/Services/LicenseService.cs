using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace ToolFB.App.Services;

public class LicenseInfo
{
    public string Hwid { get; set; } = string.Empty;
    public string Status { get; set; } = "unknown";
    public string ExpiryDate { get; set; } = string.Empty;
    public string RemainingText { get; set; } = string.Empty;
    public string CustomerName { get; set; } = "Khách hàng";
    public string ToolName { get; set; } = LicenseService.ToolName;
}

public static class LicenseService
{
    public const string ToolName = "TOOL AUTO COMMENT v14";
    public const string ToolTag = "v14";
    public const string ToolFullTitle = "TOOL AUTO COMMENT v14 PRO - NUÔI & TƯƠNG TÁC TỰ ĐỘNG";

    private const string BotToken = "8723652390:AAHrj3kpvykTqxUwEL7aQ20M-6bCdXTnh9s";
    private const string ChatId = "6425719277";
    private const string CloudDbUrl = "https://superjsonblob.com/api/jsonBlob/24f81984-3f33-452a-abbf-f2bb026f8403";
    private const string OfflineSecret = "GIANG_DEV_VIP_TOOL_FB_SECRET_KEY_2026";

    private static readonly HttpClient HttpClient = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        ConnectTimeout = TimeSpan.FromSeconds(6),
        SslOptions = new System.Net.Security.SslClientAuthenticationOptions
        {
            RemoteCertificateValidationCallback = delegate { return true; }
        }
    })
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    private static string? _cachedHwid;
    private static long _lastNotifyTimestamp;
    private static readonly object DbFileLock = new();

    public static LicenseInfo CurrentLicense { get; } = new();

    static LicenseService()
    {
        HttpClient.DefaultRequestHeaders.UserAgent.Clear();
        HttpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "GiangDevTool/14.0 (Windows NT 10.0; Win64; x64)");
        HttpClient.DefaultRequestHeaders.Accept.Clear();
        HttpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    #region 1. HWID Generation (Exact Match with Python)
    public static string GetStrongHwid()
    {
        if (!string.IsNullOrEmpty(_cachedHwid))
            return _cachedHwid;

        var components = new List<string>();

        // 1. Windows MachineGuid trong Registry
        try
        {
            using var rk = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var subKey = rk.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
            var guid = subKey?.GetValue("MachineGuid")?.ToString()?.Trim();
            if (!string.IsNullOrEmpty(guid) && guid.Length > 8)
            {
                components.Add($"GUID:{guid}");
            }
        }
        catch { }

        // 2. Số Serial ổ cứng vật lý
        try
        {
            string diskSerial = RunCommandSilent("wmic diskdrive get serialnumber");
            var lines = diskSerial.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .Where(l => !string.IsNullOrEmpty(l) && !l.Contains("SerialNumber", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (lines.Count > 0)
            {
                string firstDisk = lines[0];
                if (firstDisk.Length > 3 && !firstDisk.Equals("none", StringComparison.OrdinalIgnoreCase) &&
                    !firstDisk.Equals("string", StringComparison.OrdinalIgnoreCase) &&
                    !firstDisk.Equals("0", StringComparison.OrdinalIgnoreCase) &&
                    !firstDisk.Contains("to be filled by o.e.m.", StringComparison.OrdinalIgnoreCase))
                {
                    components.Add($"DISK:{firstDisk}");
                }
            }
        }
        catch { }

        // 3. CPU Processor ID
        try
        {
            string cpuOut = RunCommandSilent("wmic cpu get processorid");
            var lines = cpuOut.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .Where(l => !string.IsNullOrEmpty(l) && !l.Contains("ProcessorId", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (lines.Count > 0 && lines[0].Length > 4)
            {
                components.Add($"CPU:{lines[0]}");
            }
        }
        catch { }

        // 4. BIOS UUID (Loại bỏ các dummy UUID của main OEM)
        var dummyUuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "03000200-0400-0500-0006-000700080009",
            "FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF",
            "00000000-0000-0000-0000-000000000000",
            "DEFAULT STRING", "NONE", "STRING", "TO BE FILLED BY O.E.M."
        };

        try
        {
            string uuidOut = RunCommandSilent("wmic csproduct get uuid");
            var lines = uuidOut.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .Where(l => !string.IsNullOrEmpty(l) && !l.Contains("UUID", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (lines.Count > 0)
            {
                string rawUuid = lines[0].ToUpperInvariant();
                if (!dummyUuids.Contains(rawUuid) && rawUuid.Length > 10)
                {
                    components.Add($"UUID:{rawUuid}");
                }
            }
        }
        catch { }

        // 5. Dự phòng nếu thiếu (Registry CPU Identifier + MAC address)
        if (components.Count < 2)
        {
            try
            {
                using var rk = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
                using var kCpu = rk.OpenSubKey(@"HARDWARE\Description\System\CentralProcessor\0");
                var procName = kCpu?.GetValue("ProcessorNameString")?.ToString()?.Trim();
                if (!string.IsNullOrEmpty(procName))
                {
                    components.Add($"CPUNAME:{procName}");
                }
            }
            catch { }
        }

        if (components.Count < 2)
        {
            try
            {
                var nics = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces();
                foreach (var nic in nics)
                {
                    var mac = nic.GetPhysicalAddress().ToString();
                    if (!string.IsNullOrEmpty(mac) && mac.Length >= 8)
                    {
                        components.Add($"MAC:{mac}");
                        break;
                    }
                }
            }
            catch { }
        }

        string combined = components.Count > 0
            ? string.Join("|", components)
            : $"FALLBACK_{Environment.MachineName}";

        using var sha = SHA256.Create();
        var hashBytes = sha.ComputeHash(Encoding.UTF8.GetBytes(combined));
        string hex = Convert.ToHexString(hashBytes).ToUpperInvariant();
        _cachedHwid = hex[..10];
        CurrentLicense.Hwid = _cachedHwid;
        return _cachedHwid;
    }

    private static string RunCommandSilent(string cmd)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c {cmd}",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var proc = Process.Start(psi);
            if (proc == null) return string.Empty;
            string outStr = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(3000);
            return outStr;
        }
        catch
        {
            return string.Empty;
        }
    }
    #endregion

    #region 2. Database Paths & Persistence
    public static List<string> GetDbPaths()
    {
        var paths = new List<string>();

        // 1. AppData (Khách hàng)
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (!string.IsNullOrEmpty(appData))
        {
            paths.Add(Path.Combine(appData, "GiangDevTool", "devices_db.json"));
        }

        // 2. Thư mục hiện tại của App
        paths.Add(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "devices_db.json"));

        // 3. Các thư mục Admin nếu tồn tại
        string[] adminDirs = { @"D:\ToolFB", @"D:\ToolFB\Bot_QuanLy_Key", @"D:\ToolFB\v14_license", @"D:\ToolFB\toolVIP14" };
        foreach (var d in adminDirs)
        {
            if (Directory.Exists(d))
            {
                paths.Add(Path.Combine(d, "devices_db.json"));
            }
        }

        return paths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static JsonObject LoadLocalDb()
    {
        lock (DbFileLock)
        {
            foreach (var p in GetDbPaths())
            {
                if (File.Exists(p))
                {
                    try
                    {
                        string json = File.ReadAllText(p, Encoding.UTF8);
                        var node = JsonNode.Parse(json);
                        if (node is JsonObject obj)
                            return obj;
                    }
                    catch { }
                }
            }
            return new JsonObject();
        }
    }

    public static void SaveLocalDb(JsonObject db)
    {
        lock (DbFileLock)
        {
            string json = db.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            foreach (var p in GetDbPaths())
            {
                try
                {
                    string? dir = Path.GetDirectoryName(p);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                        Directory.CreateDirectory(dir);

                    string tmp = p + ".tmp";
                    File.WriteAllText(tmp, json, Encoding.UTF8);
                    if (File.Exists(p)) File.Delete(p);
                    File.Move(tmp, p);
                }
                catch { }
            }
        }
    }
    #endregion

    #region 3. Cloud Database Sync (SuperJsonBlob)
    public static async Task<JsonObject?> FetchCloudDbAsync()
    {
        try
        {
            string url = $"{CloudDbUrl}?_t={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
            using var resp = await HttpClient.GetAsync(url);
            if (resp.IsSuccessStatusCode)
            {
                string json = await resp.Content.ReadAsStringAsync();
                var node = JsonNode.Parse(json);
                if (node is JsonObject obj)
                {
                    SaveLocalDb(obj);
                    return obj;
                }
            }
        }
        catch { }
        return null;
    }

    public static async Task<bool> SyncCloudDbAsync(JsonObject dbData)
    {
        try
        {
            JsonObject payload = dbData;
            var cloudLatest = await FetchCloudDbAsync();
            if (cloudLatest != null)
            {
                foreach (var kvp in dbData)
                {
                    if (kvp.Key is "_license_keys" or "_used_offline_keys" && kvp.Value is JsonObject subObj)
                    {
                        if (cloudLatest[kvp.Key] is not JsonObject existingSub)
                        {
                            cloudLatest[kvp.Key] = new JsonObject();
                            existingSub = (JsonObject)cloudLatest[kvp.Key]!;
                        }
                        foreach (var inner in subObj)
                        {
                            existingSub[inner.Key] = inner.Value?.DeepClone();
                        }
                    }
                    else
                    {
                        cloudLatest[kvp.Key] = kvp.Value?.DeepClone();
                    }
                }
                payload = cloudLatest;
            }

            SaveLocalDb(payload);

            string contentStr = payload.ToJsonString();
            using var req = new HttpRequestMessage(HttpMethod.Put, CloudDbUrl)
            {
                Content = new StringContent(contentStr, Encoding.UTF8, "application/json")
            };
            using var putResp = await HttpClient.SendAsync(req);
            return putResp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
    #endregion

    #region 4. Offline Key HMAC Verification
    public static string GenerateOfflineKey(string hwid, int days, string toolTag = ToolTag)
    {
        hwid = hwid.Trim().ToUpperInvariant();
        toolTag = toolTag.Trim().ToUpperInvariant();
        string dataStr = $"{toolTag}:{hwid}:{days}:{OfflineSecret}";

        using var sha = SHA256.Create();
        string chk = Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(dataStr)))[..6].ToUpperInvariant();
        return $"{toolTag}-OFF-{days}D-{hwid}-{chk}";
    }

    public static (bool success, int days, string targetHwid, string keyToolTag) VerifyOfflineKey(string keyStr, string currentHwid)
    {
        keyStr = keyStr.Trim().ToUpperInvariant();
        currentHwid = currentHwid.Trim().ToUpperInvariant();

        // Format mới: V14-OFF-30D-FE4AC89C09-A1B2C3
        var matchNew = Regex.Match(keyStr, @"^([A-Z0-9]+)-OFF-(\d+)D-([A-Z0-9]+)-([A-Z0-9]{6})$");
        if (matchNew.Success)
        {
            string keyTag = matchNew.Groups[1].Value.ToLowerInvariant();
            int days = int.Parse(matchNew.Groups[2].Value);
            string targetHwid = matchNew.Groups[3].Value;
            string chk = matchNew.Groups[4].Value;

            if (keyTag != ToolTag.ToLowerInvariant())
                return (false, 0, targetHwid, keyTag);

            if (targetHwid != currentHwid)
                return (false, 0, targetHwid, keyTag);

            using var sha = SHA256.Create();
            string expectedChk = Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes($"{keyTag.ToUpperInvariant()}:{targetHwid}:{days}:{OfflineSecret}")))[..6].ToUpperInvariant();
            if (chk == expectedChk)
                return (true, days, targetHwid, keyTag);

            return (false, 0, string.Empty, string.Empty);
        }

        // Format cũ: GIANG-30D-HWID-CHK
        var matchOld = Regex.Match(keyStr, @"^GIANG-(\d+)D-([A-Z0-9]+)-([A-Z0-9]{6})$");
        if (matchOld.Success)
        {
            int days = int.Parse(matchOld.Groups[1].Value);
            string targetHwid = matchOld.Groups[2].Value;
            string chk = matchOld.Groups[3].Value;

            if (targetHwid != currentHwid)
                return (false, 0, targetHwid, "old");

            using var sha = SHA256.Create();
            string expectedChk = Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes($"{targetHwid}:{days}:{OfflineSecret}")))[..6].ToUpperInvariant();
            if (chk == expectedChk)
                return (true, days, targetHwid, ToolTag.ToLowerInvariant());
        }

        return (false, 0, string.Empty, string.Empty);
    }
    #endregion

    #region 5. Date Parsing & Remaining Time
    public static DateTime? ParseExpiryDatetime(string? expiryStr)
    {
        if (string.IsNullOrWhiteSpace(expiryStr))
            return null;

        string[] formats = { "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm", "yyyy-MM-dd" };
        foreach (var fmt in formats)
        {
            if (DateTime.TryParseExact(expiryStr.Trim(), fmt, null, System.Globalization.DateTimeStyles.None, out var dt))
            {
                if (fmt == "yyyy-MM-dd")
                    dt = new DateTime(dt.Year, dt.Month, dt.Day, 23, 59, 59);
                return dt;
            }
        }
        return null;
    }

    public static (bool isValid, string textDisplay, double totalSeconds) GetRemainingTimeInfo(string? expiryStr)
    {
        var expDt = ParseExpiryDatetime(expiryStr);
        if (expDt == null)
            return (false, "Không xác định", 0);

        var now = DateTime.Now;
        var diff = expDt.Value - now;
        double totalSec = diff.TotalSeconds;

        if (totalSec <= 0)
            return (false, "Đã hết hạn", 0);

        int days = diff.Days;
        int hours = diff.Hours;
        int minutes = diff.Minutes;
        int seconds = diff.Seconds;

        string text;
        if (days > 0)
            text = $"{days} ngày {hours:D2}h {minutes:D2}m {seconds:D2}s";
        else if (hours > 0)
            text = $"{hours:D2}h {minutes:D2}m {seconds:D2}s";
        else
            text = $"{minutes:D2}m {seconds:D2}s";

        return (true, text, totalSec);
    }

    private static string CalculateNewExpiry(JsonObject? existingDev, double addSeconds)
    {
        DateTime baseDt = DateTime.Now;
        if (existingDev != null && existingDev["status"]?.ToString().ToLowerInvariant() == "active")
        {
            var oldExp = ParseExpiryDatetime(existingDev["expiry_date"]?.ToString());
            if (oldExp != null && oldExp.Value > baseDt)
                baseDt = oldExp.Value;
        }
        return baseDt.AddSeconds(addSeconds).ToString("yyyy-MM-dd HH:mm:ss");
    }
    #endregion

    #region 6. Realtime License Check
    public static async Task<(bool isValid, string reason, JsonObject? data)> CheckLicenseRealtimeAsync(string hwid)
    {
        hwid = hwid.Trim().ToUpperInvariant();
        JsonObject? data = null;

        // 1. Fetch Cloud DB
        var cloudDb = await FetchCloudDbAsync();
        if (cloudDb != null)
        {
            if (cloudDb[hwid] is JsonObject devObj)
                data = devObj;
            else
                return (false, "Thiết bị chưa được kích hoạt hoặc đã bị xóa khỏi hệ thống!", null);
        }
        else
        {
            var localDb = LoadLocalDb();
            if (localDb[hwid] is JsonObject devObj)
                data = devObj;
            else
                return (false, "Không thể kết nối máy chủ bản quyền và thiết bị chưa kích hoạt!", null);
        }

        // Global locked check
        string globalStatus = data["status"]?.ToString().Trim().ToLowerInvariant() ?? "";
        if (globalStatus == "locked")
            return (false, "Thiết bị này đã bị Admin KHÓA bản quyền toàn hệ thống.", data);

        // Check tool in 'tools' dictionary
        if (data["tools"] is JsonObject toolsObj && toolsObj[ToolTag.ToLowerInvariant()] is JsonObject toolObj)
        {
            string tStatus = toolObj["status"]?.ToString().Trim().ToLowerInvariant() ?? "";
            if (tStatus != "active")
                return (false, $"Bản quyền {ToolName} của máy này đang bị khóa.", toolObj);

            string tExpiry = toolObj["expiry_date"]?.ToString() ?? "";
            var (isValid, remText, _) = GetRemainingTimeInfo(tExpiry);
            if (!isValid)
                return (false, $"Bản quyền {ToolName} đã hết hạn vào lúc: {tExpiry}", toolObj);

            toolObj["remaining_text"] = remText;
            toolObj["name"] = data["name"]?.ToString() ?? "Khách hàng";
            return (true, "Hợp lệ", toolObj);
        }

        // Fallback cho dữ liệu cũ
        string devTool = (data["tool"]?.ToString() ?? data["last_tool"]?.ToString() ?? "").ToLowerInvariant();
        string devStatus = data["status"]?.ToString().Trim().ToLowerInvariant() ?? "";
        string devExpiry = data["expiry_date"]?.ToString() ?? "";

        if (!string.IsNullOrEmpty(devTool) && !devTool.Contains(ToolName.ToLowerInvariant()) && !devTool.Contains(ToolTag.ToLowerInvariant()))
        {
            return (false, $"Thiết bị này chưa được kích hoạt cho '{ToolName}' (chỉ có bản quyền cho {data["tool"]})!", null);
        }

        if (devStatus != "active")
            return (false, $"Bản quyền {ToolName} của máy này đang bị khóa.", data);

        var (validOld, remOld, _) = GetRemainingTimeInfo(devExpiry);
        if (!validOld)
            return (false, $"Bản quyền {ToolName} đã hết hạn vào lúc: {devExpiry}", data);

        data["remaining_text"] = remOld;
        return (true, "Hợp lệ", data);
    }
    #endregion

    #region 7. Activation by Key
    public static async Task<(bool success, string message)> ActivateByKeyAsync(string hwid, string keyInput)
    {
        hwid = hwid.Trim().ToUpperInvariant();
        keyInput = keyInput.Trim().ToUpperInvariant();

        if (string.IsNullOrWhiteSpace(keyInput))
            return (false, "Vui lòng nhập mã kích hoạt!");

        if (keyInput.StartsWith("V10-") && ToolTag.ToLowerInvariant() != "v10")
            return (false, "Mã key này là của bản FB TOOLKIT PRO v10, không thể dùng cho TOOL AUTO COMMENT v14!");

        var db = await FetchCloudDbAsync() ?? LoadLocalDb();

        // 1. Thử xác thực mã Offline HMAC
        var (isOffline, offDays, offTarget, keyToolTag) = VerifyOfflineKey(keyInput, hwid);
        if (isOffline)
        {
            if (!string.IsNullOrEmpty(keyToolTag) && keyToolTag != ToolTag.ToLowerInvariant())
                return (false, $"Mã key Offline này dành cho '{keyToolTag.ToUpperInvariant()}', không thể dùng cho '{ToolName}'!");

            var usedOffline = db["_used_offline_keys"] as JsonObject ?? new JsonObject();
            if (usedOffline.ContainsKey(keyInput))
            {
                string usedAt = usedOffline[keyInput]?["used_at"]?.ToString() ?? "trước đó";
                return (false, $"Mã Key Offline này đã được sử dụng vào lúc {usedAt}!");
            }

            var devEntry = db[hwid] as JsonObject ?? new JsonObject();
            var toolsDict = devEntry["tools"] as JsonObject ?? new JsonObject();
            var curToolData = toolsDict[ToolTag.ToLowerInvariant()] as JsonObject ?? new JsonObject();

            string expStr = CalculateNewExpiry(curToolData.Count > 0 ? curToolData : devEntry, offDays * 86400);

            toolsDict[ToolTag.ToLowerInvariant()] = new JsonObject
            {
                ["status"] = "active",
                ["expiry_date"] = expStr,
                ["tool"] = ToolName,
                ["key_used"] = keyInput,
                ["updated_at"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            };

            devEntry["status"] = "active";
            devEntry["expiry_date"] = expStr;
            devEntry["tool"] = ToolName;
            devEntry["last_tool"] = ToolName;
            devEntry["tools"] = toolsDict;
            devEntry["updated_at"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            if (!devEntry.ContainsKey("name"))
                devEntry["name"] = $"Khách ({ToolName})";

            db[hwid] = devEntry;

            usedOffline[keyInput] = new JsonObject
            {
                ["hwid"] = hwid,
                ["days"] = offDays,
                ["tool"] = ToolName,
                ["tool_tag"] = ToolTag.ToLowerInvariant(),
                ["used_at"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            };
            db["_used_offline_keys"] = usedOffline;

            SaveLocalDb(db);
            await SyncCloudDbAsync(db);

            CurrentLicense.Status = "active";
            CurrentLicense.ExpiryDate = expStr;
            var (_, remTextOff, _) = GetRemainingTimeInfo(expStr);
            CurrentLicense.RemainingText = remTextOff;

            return (true, $"Kích hoạt thành công {ToolName}! Hạn dùng đến: {expStr}");
        }
        else if (!string.IsNullOrEmpty(offTarget) && offTarget != hwid)
        {
            return (false, $"Mã key này được tạo riêng cho máy khác ({offTarget})!");
        }

        // 2. Thử xác thực mã Online từ Bot Telegram (_license_keys)
        var licenseKeys = db["_license_keys"] as JsonObject ?? new JsonObject();
        if (licenseKeys[keyInput] is not JsonObject keyInfo)
            return (false, "Mã kích hoạt không đúng hoặc không tồn tại trên hệ thống!");

        string keyTagStr = keyInfo["tool_tag"]?.ToString()?.Trim().ToLowerInvariant() ?? "";
        string keyToolStr = keyInfo["tool"]?.ToString()?.Trim().ToLowerInvariant() ?? "";

        if (!string.IsNullOrEmpty(keyTagStr) && keyTagStr != ToolTag.ToLowerInvariant())
            return (false, $"Mã key này dành riêng cho bản khác, không thể dùng cho '{ToolName}'!");

        if (!string.IsNullOrEmpty(keyToolStr) && !keyToolStr.Contains(ToolName.ToLowerInvariant()) && !keyToolStr.Contains(ToolTag.ToLowerInvariant()))
            return (false, $"Mã key này dành riêng cho '{keyToolStr}', không thể dùng cho '{ToolName}'!");

        string? usedBy = keyInfo["used_by"]?.ToString();
        if (!string.IsNullOrEmpty(usedBy))
        {
            if (usedBy == hwid)
                return (false, $"Mã key này đã được máy bạn nạp trước đó ({keyInfo["used_at"]})!");
            return (false, $"Mã key này đã được sử dụng cho máy khác ({usedBy})!");
        }

        var devOnline = db[hwid] as JsonObject ?? new JsonObject();
        var toolsOnline = devOnline["tools"] as JsonObject ?? new JsonObject();
        var curToolOnline = toolsOnline[ToolTag.ToLowerInvariant()] as JsonObject ?? new JsonObject();

        double durationSec = double.TryParse(keyInfo["duration_seconds"]?.ToString(), out var ds) ? ds : 2592000;
        string expOnlineStr = CalculateNewExpiry(curToolOnline.Count > 0 ? curToolOnline : devOnline, durationSec);

        toolsOnline[ToolTag.ToLowerInvariant()] = new JsonObject
        {
            ["status"] = "active",
            ["expiry_date"] = expOnlineStr,
            ["tool"] = ToolName,
            ["key_used"] = keyInput,
            ["updated_at"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
        };

        devOnline["status"] = "active";
        devOnline["expiry_date"] = expOnlineStr;
        devOnline["tool"] = ToolName;
        devOnline["last_tool"] = ToolName;
        devOnline["tools"] = toolsOnline;
        devOnline["updated_at"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        if (!devOnline.ContainsKey("name"))
            devOnline["name"] = $"Khách ({ToolName})";

        db[hwid] = devOnline;

        keyInfo["used_by"] = hwid;
        keyInfo["used_at"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        keyInfo["tool"] = ToolName;
        keyInfo["tool_tag"] = ToolTag.ToLowerInvariant();
        licenseKeys[keyInput] = keyInfo;
        db["_license_keys"] = licenseKeys;

        SaveLocalDb(db);
        await SyncCloudDbAsync(db);

        CurrentLicense.Status = "active";
        CurrentLicense.ExpiryDate = expOnlineStr;
        var (_, remTextOn, _) = GetRemainingTimeInfo(expOnlineStr);
        CurrentLicense.RemainingText = remTextOn;

        return (true, $"Kích hoạt thành công {ToolName}! Hạn dùng đến: {expOnlineStr}");
    }
    #endregion

    #region 8. Notify Telegram Admin
    public static void NotifyAdmin(string hwid, string reason)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (now - _lastNotifyTimestamp < 60)
            return;
        _lastNotifyTimestamp = now;

        _ = Task.Run(async () =>
        {
            try
            {
                string pcName = Environment.MachineName;
                string nowStr = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                string message =
                    $"🚨 <b>YÊU CẦU BẢN QUYỀN MỚI</b>\n" +
                    $"━━━━━━━━━━━━━━━━━━━━\n" +
                    $"🛠️ <b>CÔNG CỤ:</b> <b>{ToolName}</b>\n" +
                    $"💻 <b>Tên máy:</b> <code>{pcName}</code>\n" +
                    $"🔑 <b>MÃ MÁY HWID:</b>\n" +
                    $"<code>{hwid}</code>\n" +
                    $"👆 <i>(Chạm vào mã trên để Copy nhanh)</i>\n\n" +
                    $"⚠️ <b>Trạng thái:</b> {reason}\n" +
                    $"⏰ <b>Thời gian:</b> {nowStr}\n" +
                    $"━━━━━━━━━━━━━━━━━━━━\n" +
                    $"⚡ <i>Bấm nút bên dưới để cấp key ngay cho tool này:</i>";

                var payload = new
                {
                    chat_id = ChatId,
                    text = message,
                    parse_mode = "HTML",
                    reply_markup = new
                    {
                        inline_keyboard = new[]
                        {
                            new[]
                            {
                                new { text = $"⏱️ Cấp 30 Phút ({ToolTag})", callback_data = $"quickact:{hwid}:30m:{ToolTag}" },
                                new { text = $"⏳ Cấp 2 Giờ ({ToolTag})", callback_data = $"quickact:{hwid}:2h:{ToolTag}" },
                                new { text = $"⏳ Cấp 6 Giờ ({ToolTag})", callback_data = $"quickact:{hwid}:6h:{ToolTag}" }
                            },
                            new[]
                            {
                                new { text = $"⏳ Cấp 1 Ngày ({ToolTag})", callback_data = $"quickact:{hwid}:1d:{ToolTag}" },
                                new { text = $"⏳ Cấp 7 Ngày ({ToolTag})", callback_data = $"quickact:{hwid}:7d:{ToolTag}" },
                                new { text = $"➕ Cấp 30 Ngày ({ToolTag})", callback_data = $"quickact:{hwid}:30d:{ToolTag}" }
                            },
                            new[]
                            {
                                new { text = $"✏️ Gõ Số Giờ / Phút Tùy Ý ({ToolTag})", callback_data = $"quickact:{hwid}:custom:{ToolTag}" }
                            }
                        }
                    }
                };

                string json = JsonSerializer.Serialize(payload);
                using var req = new HttpRequestMessage(HttpMethod.Post, $"https://api.telegram.org/bot{BotToken}/sendMessage")
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };
                await HttpClient.SendAsync(req);
            }
            catch { }
        });
    }
    #endregion

    #region 9. Realtime License Watchdog
    public static void StartWatchdog(Action<string> onRemainingTimeUpdated, Action<string> onLicenseRevoked, CancellationToken appShutdownToken)
    {
        Task.Run(async () =>
        {
            string hwid = GetStrongHwid();
            int failStrikes = 0;

            // Immediate check upon start
            try
            {
                var (initOk, _, initData) = await CheckLicenseRealtimeAsync(hwid);
                if (initOk && initData != null)
                {
                    string exp = initData["expiry_date"]?.ToString() ?? "";
                    if (!string.IsNullOrEmpty(exp)) CurrentLicense.ExpiryDate = exp;
                    string rem = initData["remaining_text"]?.ToString() ?? "";
                    if (!string.IsNullOrEmpty(rem))
                    {
                        CurrentLicense.RemainingText = rem;
                        onRemainingTimeUpdated(rem);
                    }
                }
            }
            catch { }

            while (!appShutdownToken.IsCancellationRequested)
            {
                // Sleep 45s in small increments to exit promptly on shutdown
                for (int i = 0; i < 45 && !appShutdownToken.IsCancellationRequested; i++)
                {
                    await Task.Delay(1000, appShutdownToken).ConfigureAwait(false);
                }
                if (appShutdownToken.IsCancellationRequested) break;

                try
                {
                    var (valid, reason, info) = await CheckLicenseRealtimeAsync(hwid);
                    string reasonLower = (reason ?? "").ToLowerInvariant();

                    bool isNetErr = reasonLower.Contains("network") || reasonLower.Contains("timeout") ||
                                    reasonLower.Contains("kết nối") || reasonLower.Contains("mạng") ||
                                    reasonLower.Contains("offline") || reasonLower.Contains("unreachable");

                    bool isRevoke = reasonLower.Contains("khóa") || reasonLower.Contains("xóa") ||
                                    reasonLower.Contains("hết hạn") || reasonLower.Contains("hủy") ||
                                    reasonLower.Contains("block") || reasonLower.Contains("invalid") ||
                                    reasonLower.Contains("chưa kích hoạt");

                    if (!valid)
                    {
                        if (isNetErr && !isRevoke)
                        {
                            onRemainingTimeUpdated("Đang đồng bộ mạng...");
                            continue;
                        }

                        failStrikes++;
                        if (isRevoke || failStrikes >= 2)
                        {
                            onLicenseRevoked(reason ?? "Bản quyền đã hết hạn hoặc bị thu hồi!");
                            break;
                        }
                    }
                    else
                    {
                        failStrikes = 0;
                        string exp = info?["expiry_date"]?.ToString() ?? "";
                        if (!string.IsNullOrEmpty(exp)) CurrentLicense.ExpiryDate = exp;
                        string remText = info?["remaining_text"]?.ToString() ?? "";
                        if (!string.IsNullOrEmpty(remText))
                        {
                            CurrentLicense.RemainingText = remText;
                            onRemainingTimeUpdated(remText);
                        }
                    }
                }
                catch { }
            }
        }, appShutdownToken);
    }
    #endregion
}
