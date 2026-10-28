using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using ToolFB.App.Services;

namespace ToolFB.App.Views;

public partial class ActivationDialog : Window
{
    private readonly string _hwid;
    private readonly CancellationTokenSource _cts = new();
    public bool IsActivated { get; private set; }

    public ActivationDialog(string hwid, string initialReason)
    {
        InitializeComponent();
        _hwid = hwid;
        TxtHwid.Text = _hwid;

        LblStatus.Text = string.IsNullOrEmpty(initialReason) ? "Vui lòng nhập mã kích hoạt hoặc liên hệ Admin" : initialReason;

        // Tự động sao chép HWID vào Clipboard khi mở bảng
        try
        {
            Clipboard.SetText(_hwid);
        }
        catch { }

        Loaded += ActivationDialog_Loaded;
        Closed += ActivationDialog_Closed;
    }

    private void ActivationDialog_Loaded(object sender, RoutedEventArgs e)
    {
        // Kiểm tra ngay xem hiện tại máy đã có key hợp lệ chưa
        _ = CheckCurrentLicenseAsync();

        // Tự động thăm dò máy chủ mỗi 5 giây
        StartAutoPollCloud();
    }

    private void ActivationDialog_Closed(object? sender, EventArgs e)
    {
        _cts.Cancel();
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        _cts.Cancel();
        Close();
    }

    private async void BtnCopyHwid_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(_hwid);
            BtnCopyHwid.Content = "✓ Đã Copy!";
            BtnCopyHwid.Foreground = (SolidColorBrush)FindResource("SuccessGreen");
            await Task.Delay(2000);
            BtnCopyHwid.Content = "📋 Copy HWID";
            BtnCopyHwid.Foreground = (SolidColorBrush)FindResource("TextBody");
        }
        catch { }
    }

    private async void BtnActivate_Click(object sender, RoutedEventArgs e)
    {
        string key = TxtKey.Text.Trim();
        if (string.IsNullOrEmpty(key))
        {
            LblStatus.Text = "⚠️ Vui lòng nhập mã kích hoạt vào ô bên dưới!";
            LblStatus.Foreground = (SolidColorBrush)FindResource("PrimaryGold");
            return;
        }

        BtnActivate.IsEnabled = false;
        BtnActivate.Content = "⏳ Đang xác thực Key...";

        var (success, msg) = await LicenseService.ActivateByKeyAsync(_hwid, key);

        BtnActivate.IsEnabled = true;
        BtnActivate.Content = "⚡ KÍCH HOẠT / GIA HẠN BẢN QUYỀN NGAY";

        if (success)
        {
            IsActivated = true;
            LblStatus.Text = $"✅ {msg}";
            LblStatus.Foreground = (SolidColorBrush)FindResource("SuccessGreen");
            await Task.Delay(1000);
            Close();
        }
        else
        {
            LblStatus.Text = $"❌ {msg}";
            LblStatus.Foreground = (SolidColorBrush)FindResource("DangerRed");
        }
    }

    private async void BtnRecheck_Click(object sender, RoutedEventArgs e)
    {
        BtnRecheck.IsEnabled = false;
        BtnRecheck.Content = "⏳ Đang đồng bộ với máy chủ...";

        var (valid, reason, info) = await LicenseService.CheckLicenseRealtimeAsync(_hwid);

        BtnRecheck.IsEnabled = true;
        BtnRecheck.Content = "🔄 Kiểm Tra Lại (Nếu Admin Đã Bấm Cấp Trên Bot)";

        if (valid)
        {
            IsActivated = true;
            string exp = info?["expiry_date"]?.ToString() ?? "";
            string rem = info?["remaining_text"]?.ToString() ?? "";
            LicenseService.CurrentLicense.Status = "active";
            LicenseService.CurrentLicense.ExpiryDate = exp;
            LicenseService.CurrentLicense.RemainingText = rem;
            LblStatus.Text = $"✅ Bản quyền hợp lệ! Hạn: {exp} (Còn: {rem})";
            LblStatus.Foreground = (SolidColorBrush)FindResource("SuccessGreen");
            await Task.Delay(900);
            Close();
        }
        else
        {
            LblStatus.Text = $"❌ Chưa được kích hoạt: {reason}";
            LblStatus.Foreground = (SolidColorBrush)FindResource("DangerRed");
        }
    }

    private async Task CheckCurrentLicenseAsync()
    {
        var (valid, _, info) = await LicenseService.CheckLicenseRealtimeAsync(_hwid);
        if (valid && info != null)
        {
            string exp = info["expiry_date"]?.ToString() ?? "";
            string rem = info["remaining_text"]?.ToString() ?? "";
            LicenseService.CurrentLicense.Status = "active";
            LicenseService.CurrentLicense.ExpiryDate = exp;
            LicenseService.CurrentLicense.RemainingText = rem;
            LblStatus.Text = $"✅ Đang kích hoạt • Hạn dùng: {exp} (Còn: {rem})";
            LblStatus.Foreground = (SolidColorBrush)FindResource("SuccessGreen");
            IsActivated = true;
        }
    }

    private void StartAutoPollCloud()
    {
        Task.Run(async () =>
        {
            while (!_cts.Token.IsCancellationRequested && !IsActivated)
            {
                await Task.Delay(5000, _cts.Token);
                if (_cts.Token.IsCancellationRequested) break;

                var (valid, _, info) = await LicenseService.CheckLicenseRealtimeAsync(_hwid);
                if (valid && info != null)
                {
                    _ = Dispatcher.InvokeAsync(async () =>
                    {
                        if (IsActivated) return;
                        IsActivated = true;
                        string exp = info["expiry_date"]?.ToString() ?? "";
                        string rem = info["remaining_text"]?.ToString() ?? "";
                        LicenseService.CurrentLicense.Status = "active";
                        LicenseService.CurrentLicense.ExpiryDate = exp;
                        LicenseService.CurrentLicense.RemainingText = rem;
                        LblStatus.Text = $"✅ Admin đã kích hoạt bản quyền! Hạn: {exp}";
                        LblStatus.Foreground = (SolidColorBrush)FindResource("SuccessGreen");
                        await Task.Delay(900);
                        Close();
                    });
                    break;
                }
            }
        }, _cts.Token);
    }

    private void BtnOpenTelegram_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://t.me/abc_abc_05",
                UseShellExecute = true
            });
        }
        catch { }
    }
}
