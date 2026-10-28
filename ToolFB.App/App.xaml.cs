using System.IO;
using System.Text;
using System.Windows;
using ToolFB.App.Services;
using ToolFB.App.Views;

namespace ToolFB.App;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Global crash handling with alert throttling to prevent cascading dialogs
        DateTime lastCrashAlert = DateTime.MinValue;
        DispatcherUnhandledException += (s, args) =>
        {
            RecordCrash(args.Exception);
            if ((DateTime.Now - lastCrashAlert).TotalSeconds > 2)
            {
                lastCrashAlert = DateTime.Now;
                MessageBox.Show($"Đã xảy ra lỗi không mong muốn:\n{args.Exception.Message}\n\nChi tiết đã ghi vào file crash_log.txt",
                    "GIANG DEV — Cảnh Báo", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                RecordCrash(ex);
            }
        };

        // 1. Kiểm tra bản quyền
        string hwid = LicenseService.GetStrongHwid();
        bool isLicensed = false;
        string licReason = "Đang kiểm tra...";

        try
        {
            var (valid, reason, info) = await LicenseService.CheckLicenseRealtimeAsync(hwid);
            isLicensed = valid;
            licReason = reason;

            if (valid && info != null)
            {
                LicenseService.CurrentLicense.Status = "active";
                LicenseService.CurrentLicense.ExpiryDate = info["expiry_date"]?.ToString() ?? "";
                LicenseService.CurrentLicense.RemainingText = info["remaining_text"]?.ToString() ?? "";
                LicenseService.CurrentLicense.CustomerName = info["name"]?.ToString() ?? "Khách hàng";
            }
        }
        catch (Exception ex)
        {
            licReason = $"Lỗi kiểm tra bản quyền: {ex.Message}";
            isLicensed = false;
        }

        // 2. Nếu chưa kích hoạt -> Hiện bảng ActivationDialog
        if (!isLicensed)
        {
            // Báo Telegram Admin
            LicenseService.NotifyAdmin(hwid, licReason);

            var actDlg = new ActivationDialog(hwid, licReason);
            actDlg.ShowDialog();

            if (!actDlg.IsActivated)
            {
                Shutdown();
                return;
            }
        }

        // 3. Khởi động giao diện chính
        var mainWindow = new Views.MainWindow();
        MainWindow = mainWindow;
        mainWindow.Show();
    }

    private static void RecordCrash(Exception ex)
    {
        try
        {
            string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash_log.txt");
            string content = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\n\n";
            File.AppendAllText(logPath, content, Encoding.UTF8);
        }
        catch { }
    }
}
