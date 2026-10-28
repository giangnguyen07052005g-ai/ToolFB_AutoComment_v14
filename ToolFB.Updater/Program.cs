using System.Diagnostics;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace ToolFB.Updater;

internal class Program
{
    private static readonly HashSet<string> ExcludedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".dat", ".bak", ".log"
    };

    private static readonly HashSet<string> ExcludedFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "comment_links.txt", "commented_posts.txt", "config_pro.dat", "config_pro.json", "devices_db.json"
    };

    private static string? _logPath;

    private static void Log(string message)
    {
        try
        {
            if (!string.IsNullOrEmpty(_logPath))
            {
                File.AppendAllText(_logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
            }
        }
        catch { }
    }

    static int Main(string[] args)
    {
        int pid = 0;
        string? packageZip = null;
        string? targetDir = null;
        string? launchExe = null;
        string? backupDir = null;

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--pid" && i + 1 < args.Length) int.TryParse(args[++i], out pid);
            else if (args[i] == "--package" && i + 1 < args.Length) packageZip = args[++i];
            else if (args[i] == "--target" && i + 1 < args.Length) targetDir = args[++i];
            else if (args[i] == "--launch" && i + 1 < args.Length) launchExe = args[++i];
            else if (args[i] == "--backup" && i + 1 < args.Length) backupDir = args[++i];
        }

        if (string.IsNullOrEmpty(targetDir))
        {
            targetDir = AppDomain.CurrentDomain.BaseDirectory;
        }

        try
        {
            if (!Directory.Exists(targetDir))
                Directory.CreateDirectory(targetDir);
        }
        catch { }

        _logPath = Path.Combine(targetDir, "updater.log");
        Log("=================================================");
        Log("   GIANG DEV - TRÌNH CẬP NHẬT TỰ ĐỘNG TOOL FB    ");
        Log("=================================================");
        Log($"Tham số nhận được: pid={pid}, package={packageZip}, target={targetDir}, launch={launchExe}");

        if (string.IsNullOrEmpty(packageZip) || !File.Exists(packageZip) || string.IsNullOrEmpty(launchExe))
        {
            Log("[LỖI] Tham số dòng lệnh không hợp lệ hoặc không tìm thấy file zip cập nhật!");
            return 1;
        }

        // 1. Chờ tiến trình chính thoát hoàn toàn
        if (pid > 0)
        {
            Log($"[*] Đang chờ tiến trình chính (PID: {pid}) đóng...");
            try
            {
                var proc = Process.GetProcessById(pid);
                if (!proc.WaitForExit(10000))
                {
                    Log("[!] Quá thời gian chờ, đang đóng tiến trình...");
                    proc.Kill();
                    proc.WaitForExit(3000);
                }
                Log("[+] Tiến trình chính đã thoát thành công.");
            }
            catch (ArgumentException)
            {
                Log("[*] Tiến trình chính đã thoát trước đó.");
            }
            catch (Exception ex)
            {
                Log($"[!] Cảnh báo chờ tiến trình: {ex.Message}");
            }
        }

        Thread.Sleep(800); // Đảm bảo toàn bộ file handle được Windows giải phóng

        // 2. Tạo bản sao lưu dự phòng (Backup)
        backupDir ??= Path.Combine(Path.GetTempPath(), "ToolFB_Backup");
        Log($"[*] Đang sao lưu các file hiện tại vào thư mục dự phòng: {backupDir}...");
        try
        {
            if (Directory.Exists(backupDir))
                Directory.Delete(backupDir, true);
            Directory.CreateDirectory(backupDir);

            BackupDirectory(targetDir, backupDir);
            Log("[+] Sao lưu an toàn thành công!");
        }
        catch (Exception ex)
        {
            Log($"[!] Cảnh báo sao lưu: {ex.Message} (vẫn tiếp tục cập nhật)");
        }

        // 3. Tiến hành giải nén và ghi đè bản cập nhật với cơ chế Retry
        bool updateSuccess = false;
        string newVer = "14.1.0";
        try
        {
            string zipName = Path.GetFileNameWithoutExtension(packageZip);
            var match = Regex.Match(zipName, @"\d+\.\d+(\.\d+)?");
            if (match.Success)
            {
                newVer = match.Value;
            }
        }
        catch { }

        Log($"[*] Đang cài đặt gói cập nhật mới v{newVer} từ: {Path.GetFileName(packageZip)}...");

        try
        {
            using (var archive = ZipFile.OpenRead(packageZip))
            {
                foreach (var entry in archive.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name)) continue; // Directory entry

                    // Không ghi đè các file dữ liệu người dùng
                    if (ExcludedFileNames.Contains(entry.Name) || ExcludedExtensions.Contains(Path.GetExtension(entry.Name)))
                    {
                        var destCheck = Path.Combine(targetDir, entry.FullName);
                        if (File.Exists(destCheck))
                        {
                            Log($"[-] Bỏ qua bảo vệ dữ liệu người dùng: {entry.Name}");
                            continue;
                        }
                    }

                    string destinationPath = Path.Combine(targetDir, entry.FullName);
                    string? destDir = Path.GetDirectoryName(destinationPath);
                    if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
                        Directory.CreateDirectory(destDir);

                    // Neu la tep cua Updater, thu cap nhat nhe nhang; neu tep dang bi khoa thi bo qua khong rollback app chinh
                    if (entry.Name.StartsWith("ToolFB.Updater", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            ExtractEntryWithRetry(entry, destinationPath, maxRetries: 2, delayMs: 100);
                            Log("[+] Da cap nhat file: " + entry.FullName);
                        }
                        catch (Exception uEx)
                        {
                            Log("[*] Bo qua ghi de tep Updater dang chay: " + entry.Name + " (" + uEx.Message + ")");
                        }
                        continue;
                    }

                    ExtractEntryWithRetry(entry, destinationPath);
                    Log($"[+] Đã cập nhật file: {entry.FullName}");
                }
            }

            updateSuccess = true;
            Log("[+] Cài đặt bản cập nhật thành công 100%!");

            // Cập nhật trường CurrentVersion trong github_update.json
            try
            {
                string cfgPath = Path.Combine(targetDir, "github_update.json");
                if (File.Exists(cfgPath))
                {
                    string json = File.ReadAllText(cfgPath);
                    json = Regex.Replace(
                        json,
                        @"""CurrentVersion""\s*:\s*""[^""]*""",
                        $"\"CurrentVersion\": \"{newVer}\"");
                    json = Regex.Replace(
                        json,
                        @"""LastChecked""\s*:\s*""[^""]*""",
                        $"\"LastChecked\": \"{DateTime.UtcNow:O}\"");
                    File.WriteAllText(cfgPath, json);
                    Log($"[+] Đã cập nhật CurrentVersion trong github_update.json thành: {newVer}");
                }
                else
                {
                    string defaultJson = $"{{\n  \"GitHubOwner\": \"giangnguyen07052005g-ai\",\n  \"GitHubRepo\": \"ToolFB_AutoComment_v14\",\n  \"CurrentVersion\": \"{newVer}\",\n  \"UseGitHubReleasesApi\": true,\n  \"AutoCheckOnStartup\": true\n}}";
                    File.WriteAllText(cfgPath, defaultJson);
                    Log($"[+] Đã tạo mới github_update.json với CurrentVersion: {newVer}");
                }
            }
            catch (Exception jEx)
            {
                Log($"[!] Lỗi ghi github_update.json: {jEx.Message}");
            }
        }
        catch (Exception ex)
        {
            Log($"[LỖI NGHIÊM TRỌNG KHI CẬP NHẬT] {ex.Message}");
            Log("[*] Đang tiến hành khôi phục lại phiên bản cũ (Rollback)...");

            try
            {
                RestoreDirectory(backupDir, targetDir);
                Log("[+] Khôi phục phiên bản trước thành công!");
            }
            catch (Exception rEx)
            {
                Log($"[LỖI KHÔI PHỤC] {rEx.Message}");
            }
        }

        // 4. Khởi động lại ứng dụng
        Log($"[*] Đang khởi động lại ứng dụng: {Path.GetFileName(launchExe)} với tham số --updated {newVer}...");
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = launchExe,
                WorkingDirectory = targetDir,
                Arguments = $"--updated {newVer}",
                UseShellExecute = true
            };
            Process.Start(psi);
            Log("[+] Đã khởi động lại ứng dụng thành công!");
        }
        catch (Exception ex)
        {
            Log($"[!] Không thể tự động khởi động ứng dụng: {ex.Message}");
        }

        // Dọn dẹp file tạm
        try
        {
            if (updateSuccess && File.Exists(packageZip))
                File.Delete(packageZip);
        }
        catch { }

        Log("[+] Hoàn tất quá trình cập nhật.");
        return updateSuccess ? 0 : 2;
    }

    private static void ExtractEntryWithRetry(ZipArchiveEntry entry, string destinationPath, int maxRetries = 15, int delayMs = 300)
    {
        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                if (File.Exists(destinationPath))
                {
                    try
                    {
                        File.Delete(destinationPath);
                    }
                    catch
                    {
                        // File might be briefly locked, will retry
                    }
                }

                entry.ExtractToFile(destinationPath, overwrite: true);
                return;
            }
            catch (Exception ex) when (i < maxRetries - 1 && (ex is IOException || ex is UnauthorizedAccessException))
            {
                Log($"[Thử lại {i + 1}/{maxRetries}] File đang bận: {entry.Name} - Chờ {delayMs}ms...");
                Thread.Sleep(delayMs);
            }
        }

        // Final attempt
        entry.ExtractToFile(destinationPath, overwrite: true);
    }

    private static void BackupDirectory(string sourceDir, string targetDir)
    {
        if (!Directory.Exists(sourceDir)) return;
        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var name = Path.GetFileName(file);
            var ext = Path.GetExtension(file);

            if (ExcludedFileNames.Contains(name) || ExcludedExtensions.Contains(ext))
                continue;

            try
            {
                var dest = Path.Combine(targetDir, name);
                File.Copy(file, dest, true);
            }
            catch { }
        }

        foreach (var subDir in Directory.GetDirectories(sourceDir))
        {
            var name = Path.GetFileName(subDir);
            if (name.Equals("logs", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("temp", StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                var dest = Path.Combine(targetDir, name);
                Directory.CreateDirectory(dest);
                BackupDirectory(subDir, dest);
            }
            catch { }
        }
    }

    private static void RestoreDirectory(string sourceDir, string targetDir)
    {
        if (!Directory.Exists(sourceDir)) return;
        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var name = Path.GetFileName(file);
            var dest = Path.Combine(targetDir, name);
            try
            {
                File.Copy(file, dest, true);
            }
            catch { }
        }

        foreach (var subDir in Directory.GetDirectories(sourceDir))
        {
            var name = Path.GetFileName(subDir);
            var dest = Path.Combine(targetDir, name);
            try
            {
                if (!Directory.Exists(dest)) Directory.CreateDirectory(dest);
                RestoreDirectory(subDir, dest);
            }
            catch { }
        }
    }
}
