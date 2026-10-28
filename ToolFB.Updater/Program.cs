using System.Diagnostics;
using System.IO.Compression;

namespace ToolFB.Updater;

internal class Program
{
    private static readonly HashSet<string> ExcludedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".csv", ".json", ".dat", ".log", ".bak"
    };

    private static readonly HashSet<string> ExcludedFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "comment_links.txt", "commented_posts.txt", "config_pro.dat", "config_pro.json", "update_settings.json", "devices_db.json"
    };

    static int Main(string[] args)
    {
        Console.Title = "ToolFB Auto Updater - GIANG DEV";
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("=================================================");
        Console.WriteLine("   GIANG DEV - TRÌNH CẬP NHẬT TỰ ĐỘNG TOOL FB    ");
        Console.WriteLine("=================================================\n");
        Console.ResetColor();

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

        if (string.IsNullOrEmpty(packageZip) || string.IsNullOrEmpty(targetDir) || string.IsNullOrEmpty(launchExe))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("[LỖI] Tham số dòng lệnh không hợp lệ!");
            Console.ResetColor();
            Thread.Sleep(3000);
            return 1;
        }

        // 1. Chờ tiến trình chính thoát hoàn toàn
        if (pid > 0)
        {
            Console.WriteLine($"[*] Đang chờ tiến trình chính (PID: {pid}) đóng...");
            try
            {
                var proc = Process.GetProcessById(pid);
                if (!proc.WaitForExit(15000))
                {
                    Console.WriteLine("[!] Quá thời gian chờ, đang đóng tiến trình...");
                    proc.Kill();
                    proc.WaitForExit(5000);
                }
            }
            catch (ArgumentException)
            {
                // Process already exited
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[!] Cảnh báo chờ tiến trình: {ex.Message}");
            }
        }

        Thread.Sleep(1000); // Đảm bảo toàn bộ file handle được giải phóng

        // 2. Tạo bản sao lưu dự phòng (Backup)
        backupDir ??= Path.Combine(Path.GetTempPath(), "ToolFB_Backup");
        Console.WriteLine($"[*] Đang sao lưu các file hiện tại vào thư mục dự phòng...");
        try
        {
            if (Directory.Exists(backupDir))
                Directory.Delete(backupDir, true);
            Directory.CreateDirectory(backupDir);

            BackupDirectory(targetDir, backupDir);
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("[+] Sao lưu an toàn thành công!");
            Console.ResetColor();
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"[!] Cảnh báo sao lưu: {ex.Message} (vẫn tiếp tục cập nhật)");
            Console.ResetColor();
        }

        // 3. Tiến hành giải nén và ghi đè bản cập nhật
        bool updateSuccess = false;
        Console.WriteLine($"[*] Đang cài đặt gói cập nhật mới từ: {Path.GetFileName(packageZip)}...");

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
                            Console.WriteLine($"[-] Bỏ qua bảo vệ dữ liệu người dùng: {entry.Name}");
                            continue;
                        }
                    }

                    // Không tự ghi đè Updater.exe nếu đang chạy
                    if (entry.Name.Equals("ToolFB.Updater.exe", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string destinationPath = Path.Combine(targetDir, entry.FullName);
                    string? destDir = Path.GetDirectoryName(destinationPath);
                    if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
                        Directory.CreateDirectory(destDir);

                    entry.ExtractToFile(destinationPath, true);
                }
            }

            updateSuccess = true;
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("[+] Cài đặt bản cập nhật thành công 100%!");
            Console.ResetColor();
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n[LỖI NGHIÊM TRỌNG KHI CẬP NHẬT] {ex.Message}");
            Console.WriteLine("[*] Đang tiến hành khôi phục lại phiên bản cũ (Rollback)...");
            Console.ResetColor();

            try
            {
                RestoreDirectory(backupDir, targetDir);
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("[+] Khôi phục phiên bản trước thành công!");
                Console.ResetColor();
            }
            catch (Exception rEx)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[LỖI KHÔI PHỤC] {rEx.Message}");
                Console.ResetColor();
            }
        }

        // 4. Khởi động lại ứng dụng
        Console.WriteLine($"[*] Đang khởi động lại ứng dụng: {Path.GetFileName(launchExe)}...");
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = launchExe,
                WorkingDirectory = targetDir,
                UseShellExecute = true
            };
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[!] Không thể tự động khởi động ứng dụng: {ex.Message}");
            Console.WriteLine($"Vui lòng khởi động thủ công từ: {launchExe}");
            Console.ResetColor();
            Thread.Sleep(5000);
            return 1;
        }

        // Dọn dẹp file tạm
        try
        {
            if (updateSuccess && File.Exists(packageZip))
                File.Delete(packageZip);
        }
        catch { }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("[+] Hoàn tất! Cửa sổ này sẽ tự đóng trong giây lát...");
        Console.ResetColor();
        Thread.Sleep(1500);

        return updateSuccess ? 0 : 2;
    }

    private static void BackupDirectory(string sourceDir, string targetDir)
    {
        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var name = Path.GetFileName(file);
            var ext = Path.GetExtension(file);

            if (ExcludedFileNames.Contains(name) || ExcludedExtensions.Contains(ext))
                continue;

            var dest = Path.Combine(targetDir, name);
            File.Copy(file, dest, true);
        }

        foreach (var subDir in Directory.GetDirectories(sourceDir))
        {
            var name = Path.GetFileName(subDir);
            if (name.Equals("logs", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("temp", StringComparison.OrdinalIgnoreCase))
                continue;

            var dest = Path.Combine(targetDir, name);
            Directory.CreateDirectory(dest);
            BackupDirectory(subDir, dest);
        }
    }

    private static void RestoreDirectory(string sourceDir, string targetDir)
    {
        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var name = Path.GetFileName(file);
            var dest = Path.Combine(targetDir, name);
            File.Copy(file, dest, true);
        }

        foreach (var subDir in Directory.GetDirectories(sourceDir))
        {
            var name = Path.GetFileName(subDir);
            var dest = Path.Combine(targetDir, name);
            if (!Directory.Exists(dest)) Directory.CreateDirectory(dest);
            RestoreDirectory(subDir, dest);
        }
    }
}
