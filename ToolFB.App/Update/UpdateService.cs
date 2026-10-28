using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Windows;

namespace ToolFB.App.Update;

public class UpdateCheckResult
{
    public bool HasUpdate { get; set; }
    public bool IsMandatory { get; set; }
    public string CurrentVersion { get; set; } = string.Empty;
    public string LatestVersion { get; set; } = string.Empty;
    public UpdateManifest? Manifest { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class UpdateService
{
    private static readonly HttpClient HttpClient = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        ConnectTimeout = TimeSpan.FromSeconds(10)
    })
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    public string CurrentVersion { get; }

    static UpdateService()
    {
        HttpClient.DefaultRequestHeaders.UserAgent.Clear();
        HttpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "ToolFB-AutoUpdater/14.0 (Windows NT 10.0; Win64; x64)");
        HttpClient.DefaultRequestHeaders.Accept.Clear();
        HttpClient.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));
    }

    public UpdateService()
    {
        // 1. Đọc phiên bản từ github_update.json trước
        try
        {
            var s = UpdateSettings.Load();
            if (!string.IsNullOrWhiteSpace(s.CurrentVersion))
            {
                CurrentVersion = s.CurrentVersion.TrimStart('v', 'V');
                return;
            }
        }
        catch { }

        // 2. Fallback sang FileVersionInfo nếu không có file cấu hình
        string? procPath = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(procPath) && File.Exists(procPath))
        {
            var fvi = FileVersionInfo.GetVersionInfo(procPath);
            CurrentVersion = !string.IsNullOrEmpty(fvi.ProductVersion) ? fvi.ProductVersion.Split('+')[0] : "14.0.0";
        }
        else
        {
            var asm = Assembly.GetExecutingAssembly();
            var ver = asm.GetName().Version;
            CurrentVersion = ver != null ? $"{ver.Major}.{ver.Minor}.{ver.Build}" : "14.0.0";
        }
    }

    public async Task<UpdateCheckResult> CheckForUpdateAsync(UpdateSettings settings, CancellationToken ct = default)
    {
        var result = new UpdateCheckResult
        {
            CurrentVersion = CurrentVersion
        };

        // 1. Thử qua GitHub Releases API trước nếu được bật
        if (settings.UseGitHubReleasesApi && !string.IsNullOrWhiteSpace(settings.GitHubOwner) && !string.IsNullOrWhiteSpace(settings.GitHubRepo))
        {
            try
            {
                string apiUrl = $"https://api.github.com/repos/{settings.GitHubOwner.Trim()}/{settings.GitHubRepo.Trim()}/releases/latest";
                using var req = new HttpRequestMessage(HttpMethod.Get, apiUrl);
                req.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };

                using var resp = await HttpClient.SendAsync(req, ct);
                if (resp.IsSuccessStatusCode)
                {
                    string json = await resp.Content.ReadAsStringAsync(ct);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    string tagName = root.TryGetProperty("tag_name", out var tElem) ? tElem.GetString() ?? "" : "";
                    string cleanVer = tagName.TrimStart('v', 'V');
                    string releaseNotes = root.TryGetProperty("body", out var bElem) ? bElem.GetString() ?? "" : "";
                    string htmlUrl = root.TryGetProperty("html_url", out var hElem) ? hElem.GetString() ?? "" : "";

                    // Tìm file asset trong release
                    string? downloadUrl = null;
                    string? manifestAssetUrl = null;

                    if (root.TryGetProperty("assets", out var assetsElem) && assetsElem.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var asset in assetsElem.EnumerateArray())
                        {
                            string assetName = asset.TryGetProperty("name", out var anElem) ? anElem.GetString() ?? "" : "";
                            string browserUrl = asset.TryGetProperty("browser_download_url", out var duElem) ? duElem.GetString() ?? "" : "";

                            if (assetName.Equals("latest.json", StringComparison.OrdinalIgnoreCase) ||
                                assetName.Equals("manifest.json", StringComparison.OrdinalIgnoreCase))
                            {
                                manifestAssetUrl = browserUrl;
                            }
                            else if (assetName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && downloadUrl == null)
                            {
                                downloadUrl = browserUrl;
                            }
                        }
                    }

                    // Nếu có latest.json đính kèm trong Release -> Tải về để lấy SHA-256 và Signature chuẩn
                    if (!string.IsNullOrEmpty(manifestAssetUrl))
                    {
                        using var mResp = await HttpClient.GetAsync(manifestAssetUrl, ct);
                        if (mResp.IsSuccessStatusCode)
                        {
                            string mJson = await mResp.Content.ReadAsStringAsync(ct);
                            var manifestFromAsset = JsonSerializer.Deserialize<UpdateManifest>(mJson);
                            if (manifestFromAsset != null)
                            {
                                return ProcessManifest(manifestFromAsset, settings);
                            }
                        }
                    }

                    if (!string.IsNullOrEmpty(cleanVer) && !string.IsNullOrEmpty(downloadUrl))
                    {
                        var dynamicManifest = new UpdateManifest
                        {
                            Version = cleanVer,
                            DownloadUrl = downloadUrl,
                            ReleaseNotes = string.IsNullOrWhiteSpace(releaseNotes) ? $"Bản phát hành GitHub {tagName}" : releaseNotes,
                            Mandatory = false
                        };
                        return ProcessManifest(dynamicManifest, settings);
                    }
                }
            }
            catch (Exception)
            {
                // Fallback xuống kiểm tra qua ManifestUrl bên dưới
            }
        }

        // 2. Fallback: Kiểm tra qua ManifestUrl
        if (string.IsNullOrWhiteSpace(settings.ManifestUrl))
        {
            result.Message = "Chưa cấu hình đường dẫn kiểm tra cập nhật (GitHub Releases hoặc Manifest URL).";
            return result;
        }

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, settings.ManifestUrl);
            req.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };

            using var resp = await HttpClient.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                result.Message = $"Không thể tải manifest từ máy chủ (Mã HTTP: {(int)resp.StatusCode}). Vui lòng kiểm tra lại kết nối mạng.";
                return result;
            }

            var json = await resp.Content.ReadAsStringAsync(ct);
            var manifest = JsonSerializer.Deserialize<UpdateManifest>(json);

            if (manifest == null || string.IsNullOrWhiteSpace(manifest.Version) || string.IsNullOrWhiteSpace(manifest.DownloadUrl))
            {
                result.Message = "Dữ liệu bản cập nhật không hợp lệ hoặc thiếu trường bắt buộc.";
                return result;
            }

            return ProcessManifest(manifest, settings);
        }
        catch (OperationCanceledException)
        {
            result.Message = "Kiểm tra cập nhật đã bị hủy.";
        }
        catch (HttpRequestException ex)
        {
            result.Message = $"Lỗi kết nối tới GitHub / Máy chủ cập nhật: {ex.Message}";
        }
        catch (Exception ex)
        {
            result.Message = $"Lỗi kiểm tra cập nhật: {ex.Message}";
        }

        return result;
    }

    private UpdateCheckResult ProcessManifest(UpdateManifest manifest, UpdateSettings settings)
    {
        var result = new UpdateCheckResult
        {
            CurrentVersion = CurrentVersion,
            LatestVersion = manifest.Version,
            Manifest = manifest
        };

        // 1. Verify ECDSA Digital Signature nếu có chữ ký số
        if (!string.IsNullOrEmpty(manifest.Signature))
        {
            bool isSignatureValid = SignatureVerifier.VerifySignature(manifest);
            if (!isSignatureValid)
            {
                result.Message = "CẢNH BÁO: Chữ ký số của bản cập nhật không hợp lệ! Bản cập nhật có thể đã bị thay đổi.";
                return result;
            }
        }

        // 2. Anti-downgrade check
        bool isNewer = SignatureVerifier.IsVersionNewer(manifest.Version, CurrentVersion);
        bool isBelowMin = SignatureVerifier.IsVersionBelowMinimum(CurrentVersion, manifest.MinimumVersion);

        if (isNewer || isBelowMin)
        {
            result.HasUpdate = true;
            result.IsMandatory = manifest.Mandatory || isBelowMin;
            result.Message = $"Phát hiện phiên bản mới: v{manifest.Version}";
        }
        else
        {
            result.HasUpdate = false;
            result.Message = $"Bạn đang sử dụng phiên bản mới nhất (v{CurrentVersion}).";
        }

        settings.LastChecked = DateTime.UtcNow;
        settings.Save();

        return result;
    }

    public async Task<(bool success, string filePath, string error)> DownloadUpdateAsync(
        UpdateManifest manifest,
        IProgress<DownloadProgressReport> progress,
        CancellationToken ct)
    {
        try
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "ToolFB_Updates");
            if (!Directory.Exists(tempDir))
                Directory.CreateDirectory(tempDir);

            string fileName = $"ToolFB_v{manifest.Version}.zip";
            string destPath = Path.Combine(tempDir, fileName);

            // Clean previous download
            if (File.Exists(destPath))
                File.Delete(destPath);

            await UpdateDownloader.DownloadFileWithProgressAsync(manifest.DownloadUrl, destPath, progress, ct);

            // Verify SHA-256 if provided
            if (!string.IsNullOrEmpty(manifest.Sha256))
            {
                progress.Report(new DownloadProgressReport
                {
                    ProgressPercentage = 100,
                    StatusText = "Đang xác minh tính toàn vẹn (SHA-256)..."
                });

                bool isHashValid = SignatureVerifier.VerifyFileSha256(destPath, manifest.Sha256);
                if (!isHashValid)
                {
                    try { File.Delete(destPath); } catch { }
                    return (false, string.Empty, "Mã băm SHA-256 của file tải về không khớp! File có thể đã bị lỗi đường truyền.");
                }
            }

            // Verify Zip package integrity and structure
            try
            {
                using var zipCheck = System.IO.Compression.ZipFile.OpenRead(destPath);
                bool hasApp = zipCheck.Entries.Any(e => e.Name.Equals("ToolFB.App.exe", StringComparison.OrdinalIgnoreCase));
                if (!hasApp)
                {
                    try { File.Delete(destPath); } catch { }
                    return (false, string.Empty, "Gói cập nhật không chứa file thực thi ToolFB.App.exe hợp lệ!");
                }
            }
            catch (Exception zEx)
            {
                try { File.Delete(destPath); } catch { }
                return (false, string.Empty, $"File gói cập nhật bị lỗi cấu trúc nén: {zEx.Message}");
            }

            return (true, destPath, string.Empty);
        }
        catch (Exception ex)
        {
            return (false, string.Empty, ex.Message);
        }
    }

    public static void ApplyUpdateAndRestart(string packageZipPath)
    {
        string currentAppDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');
        string currentExe = Process.GetCurrentProcess().MainModule?.FileName ?? Path.Combine(currentAppDir, "ToolFB.App.exe");
        int currentPid = Environment.ProcessId;

        // Path to updater executable in current app directory
        string sourceUpdaterExe = Path.Combine(currentAppDir, "ToolFB.Updater.exe");
        if (!File.Exists(sourceUpdaterExe))
        {
            var alt = Path.Combine(currentAppDir, "..", "ToolFB.Updater", "bin", "Debug", "net8.0", "ToolFB.Updater.exe");
            if (File.Exists(alt)) sourceUpdaterExe = alt;
        }

        if (!File.Exists(sourceUpdaterExe))
        {
            throw new FileNotFoundException("Không tìm thấy chương trình cập nhật ToolFB.Updater.exe!");
        }

        // Copy updater to TEMP directory so it doesn't lock target directory files
        string tempUpdaterDir = Path.Combine(Path.GetTempPath(), "ToolFB_Updater_Bin");
        Directory.CreateDirectory(tempUpdaterDir);
        string updaterExe = Path.Combine(tempUpdaterDir, "ToolFB.Updater.exe");
        try
        {
            File.Copy(sourceUpdaterExe, updaterExe, true);
            string sourceConfig = Path.ChangeExtension(sourceUpdaterExe, ".runtimeconfig.json");
            if (File.Exists(sourceConfig))
            {
                File.Copy(sourceConfig, Path.Combine(tempUpdaterDir, Path.GetFileName(sourceConfig)), true);
            }
        }
        catch
        {
            updaterExe = sourceUpdaterExe;
        }

        string backupDir = Path.Combine(Path.GetTempPath(), "ToolFB_Backup");

        var psi = new ProcessStartInfo
        {
            FileName = updaterExe,
            Arguments = $"--pid {currentPid} --package \"{packageZipPath}\" --target \"{currentAppDir}\" --launch \"{currentExe}\" --backup \"{backupDir}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = tempUpdaterDir
        };

        Process.Start(psi);

        // Gracefully close current WPF application
        Application.Current?.Dispatcher?.Invoke(() =>
        {
            Application.Current.Shutdown(0);
        });

        // Ensure process exits promptly
        Task.Delay(1000).ContinueWith(_ => Environment.Exit(0));
    }
}
