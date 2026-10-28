using System.Windows;
using ToolFB.App.Update;

namespace ToolFB.App.Views;

public partial class UpdateDialog : Window
{
    private readonly UpdateManifest _manifest;
    private readonly UpdateService _updateService;
    private CancellationTokenSource? _downloadCts;

    public UpdateDialog(UpdateManifest manifest, string currentVersion, UpdateService service)
    {
        InitializeComponent();
        _manifest = manifest;
        _updateService = service;

        TxtCurrentVersion.Text = $"v{currentVersion}";
        TxtLatestVersion.Text = $"v{manifest.Version}";
        TxtReleaseNotes.Text = manifest.ReleaseNotes;

        if (manifest.Mandatory)
        {
            BadgeMandatory.Visibility = Visibility.Visible;
            BtnRemindLater.IsEnabled = false;
            BtnRemindLater.Opacity = 0.3;
        }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        if (_manifest.Mandatory)
        {
            MessageBox.Show("Đây là bản cập nhật bắt buộc. Vui lòng cập nhật để tiếp tục sử dụng!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _downloadCts?.Cancel();
        Close();
    }

    private void BtnRemindLater_Click(object sender, RoutedEventArgs e)
    {
        _downloadCts?.Cancel();
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        _downloadCts?.Cancel();
        PanelProgress.Visibility = Visibility.Collapsed;
        BtnUpdateNow.Visibility = Visibility.Visible;
        BtnRemindLater.Visibility = Visibility.Visible;
        BtnCancel.Visibility = Visibility.Collapsed;
        BtnRetry.Visibility = Visibility.Collapsed;
        TxtStatus.Text = "Đã hủy tải bản cập nhật.";
    }

    private void BtnRetry_Click(object sender, RoutedEventArgs e)
    {
        BtnRetry.Visibility = Visibility.Collapsed;
        StartDownload();
    }

    private void BtnUpdateNow_Click(object sender, RoutedEventArgs e)
    {
        StartDownload();
    }

    private async void StartDownload()
    {
        BtnUpdateNow.Visibility = Visibility.Collapsed;
        BtnRemindLater.Visibility = Visibility.Collapsed;
        BtnRetry.Visibility = Visibility.Collapsed;
        BtnCancel.Visibility = Visibility.Visible;
        PanelProgress.Visibility = Visibility.Visible;

        ProgressBarDownload.Value = 0;
        TxtPercent.Text = "0%";
        TxtProgressStatus.Text = "Đang kết nối tới GitHub...";
        TxtStatus.Text = "Đang tải gói cập nhật...";

        _downloadCts = new CancellationTokenSource();
        var progress = new Progress<DownloadProgressReport>(report =>
        {
            ProgressBarDownload.Value = report.ProgressPercentage;
            TxtPercent.Text = $"{report.ProgressPercentage:0}%";
            TxtProgressStatus.Text = report.StatusText;
        });

        try
        {
            var (success, filePath, error) = await _updateService.DownloadUpdateAsync(_manifest, progress, _downloadCts.Token);

            if (!success)
            {
                TxtStatus.Text = $"Lỗi: {error}";
                BtnCancel.Visibility = Visibility.Collapsed;
                BtnRetry.Visibility = Visibility.Visible;
                MessageBox.Show(error, "Lỗi cập nhật", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            TxtStatus.Text = "Đang khởi động trình cập nhật...";
            TxtProgressStatus.Text = "Đang áp dụng bản cập nhật...";
            await Task.Delay(800);

            // Launch Updater.exe and shutdown
            UpdateService.ApplyUpdateAndRestart(filePath);
        }
        catch (OperationCanceledException)
        {
            TxtStatus.Text = "Đã hủy tải xuống.";
            BtnCancel.Visibility = Visibility.Collapsed;
            BtnUpdateNow.Visibility = Visibility.Visible;
            if (!_manifest.Mandatory) BtnRemindLater.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            TxtStatus.Text = $"Lỗi: {ex.Message}";
            BtnCancel.Visibility = Visibility.Collapsed;
            BtnRetry.Visibility = Visibility.Visible;
            MessageBox.Show(ex.Message, "Lỗi cập nhật", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
