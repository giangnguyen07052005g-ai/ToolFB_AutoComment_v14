using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using ToolFB.App.Models;
using ToolFB.App.Services;
using ToolFB.App.Update;

namespace ToolFB.App.Views;

public class LogItem
{
    public string Timestamp { get; set; } = DateTime.Now.ToString("HH:mm:ss");
    public string Level { get; set; } = "INFO"; // INFO, SUCCESS, WARNING, ERROR
    public string Message { get; set; } = string.Empty;
    public Brush ForegroundBrush { get; set; } = Brushes.White;
    public Brush BadgeBackground { get; set; } = new SolidColorBrush(Color.FromRgb(0x1E, 0x23, 0x29));
    public Brush BadgeBorder { get; set; } = new SolidColorBrush(Color.FromRgb(0x2B, 0x31, 0x39));
}

public partial class MainWindow : Window
{
    public ObservableCollection<TargetPost> TargetPosts { get; } = new();
    public ObservableCollection<Account> Accounts { get; } = new();
    public ObservableCollection<LogItem> Logs { get; } = new();

    private readonly ConcurrentQueue<LogItem> _pendingLogs = new();
    private readonly System.Windows.Threading.DispatcherTimer _logFlushTimer = new();
    private readonly System.Windows.Threading.DispatcherTimer _delayTimer = new();

    private readonly CommentWorkerEngine _engine = new();
    private readonly UpdateService _updateService = new();
    private readonly CancellationTokenSource _appShutdownCts = new();

    private string _selectedIconCategory = "🎲 Hỗn Hợp";
    private string _selectedImagePath = string.Empty;
    private string _logLevelFilter = "ALL";
    private string _logKeywordFilter = string.Empty;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;

        // Binds DataGrids
        GridPosts.ItemsSource = TargetPosts;
        GridAccounts.ItemsSource = Accounts;
        ListLogs.ItemsSource = Logs;
        DashMiniLogs.ItemsSource = Logs;

        // Setup Log & Stats events
        _engine.LogReceived += OnEngineLogReceived;
        _engine.StatsChanged += OnEngineStatsChanged;

        // Throttled Log Flush Timer (100ms interval for 60fps smooth UI)
        _logFlushTimer.Interval = TimeSpan.FromMilliseconds(100);
        _logFlushTimer.Tick += OnLogFlushTimerTick;
        _logFlushTimer.Start();

        // 1-second delay countdown timer
        _delayTimer.Interval = TimeSpan.FromSeconds(1);
        _delayTimer.Tick += OnDelayTimerTick;
        _delayTimer.Start();

        // Load persisted settings
        LoadSavedSettings();
        UpdateUiCounters();

        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
    }

    #region Window Startup, License & Shutdown
    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // 1. Hiển thị HWID máy trong Tab 6 & Sidebar
        string hwid = LicenseService.GetStrongHwid();
        TxtMachineHwid.Text = hwid;
        LblSidebarHwid.Text = $"HWID: {hwid}";

        // 2. Khởi động Realtime License Watchdog & Cập nhật đếm ngược trực tiếp
        LicenseService.StartWatchdog(OnRemainingTimeUpdated, OnLicenseRevoked, _appShutdownCts.Token);
        UpdateLiveLicenseCountdown();

        // 3. Cập nhật thông tin GitHub Updater card
        var updateCfg = UpdateSettings.Load();
        LblUpdateRepoCard.Text = $"{updateCfg.GitHubOwner}/{updateCfg.GitHubRepo}";
        ChkAutoUpdateStartup.IsChecked = updateCfg.AutoCheckOnStartup;

        if (updateCfg.AutoCheckOnStartup)
        {
            await CheckUpdateInternalAsync(silentIfNoUpdate: true);
        }

        // 4. Sinh thử 1 bình luận mẫu ban đầu
        GenerateCommentPreview();
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_engine.IsRunning)
        {
            _engine.Stop();
        }
        _appShutdownCts.Cancel();
        _delayTimer.Stop();
        _logFlushTimer.Stop();
        SaveCurrentSettings();
    }

    private void OnRemainingTimeUpdated(string remText)
    {
        Dispatcher.Invoke(() =>
        {
            UpdateLiveLicenseCountdown();
        });
    }

    private void UpdateLiveLicenseCountdown()
    {
        string expiryStr = LicenseService.CurrentLicense.ExpiryDate;
        if (string.IsNullOrEmpty(expiryStr))
        {
            if (!string.IsNullOrEmpty(LicenseService.CurrentLicense.RemainingText))
            {
                LblLicenseTime.Text = $"⏳ Còn: {LicenseService.CurrentLicense.RemainingText}";
                LblLicRemainingCard.Text = $"⏳ Còn: {LicenseService.CurrentLicense.RemainingText}";
            }
            return;
        }

        var (isValid, textDisplay, _) = LicenseService.GetRemainingTimeInfo(expiryStr);
        if (isValid)
        {
            LblLicenseTime.Text = $"⏳ Còn: {textDisplay}";
            LblLicRemainingCard.Text = $"⏳ Còn: {textDisplay}";
            if (LblLicExpiryCard != null)
                LblLicExpiryCard.Text = expiryStr;
            if (LblLicStatusCard != null)
            {
                LblLicStatusCard.Text = "✅ HỢP LỆ (ACTIVE)";
                LblLicStatusCard.Foreground = (SolidColorBrush)FindResource("SuccessGreen");
            }
        }
        else
        {
            LblLicenseTime.Text = "⛔ Đã hết hạn";
            LblLicRemainingCard.Text = "⛔ Đã hết hạn";
            if (LblLicStatusCard != null)
            {
                LblLicStatusCard.Text = "❌ HẾT HẠN (EXPIRED)";
                LblLicStatusCard.Foreground = (SolidColorBrush)FindResource("DangerRed");
            }
        }
    }

    private void OnLicenseRevoked(string reason)
    {
        Dispatcher.Invoke(() =>
        {
            _engine.Stop();
            SaveCurrentSettings();

            MessageBox.Show(
                $"⛔ BẢN QUYỀN ĐÃ BỊ HỦY HOẶC HẾT HẠN!\n\n" +
                $"Chi tiết: {reason}\n\n" +
                $"Dữ liệu cấu hình đã được bảo toàn tự động.\n" +
                $"Phần mềm sẽ đóng lại. Vui lòng liên hệ Admin (@abc_abc_05) để kích hoạt.",
                "GIANG DEV — Cảnh Báo Bản Quyền",
                MessageBoxButton.OK,
                MessageBoxImage.Stop);

            Application.Current.Shutdown();
        });
    }
    #endregion

    #region Window Titlebar Controls
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            BtnMaximize_Click(sender, e);
        }
        else if (e.LeftButton == MouseButtonState.Pressed)
        {
            try
            {
                DragMove();
            }
            catch { }
        }
    }

    private void BtnMinimize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void BtnMaximize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        BtnMaximize.Content = WindowState == WindowState.Maximized ? "⤢" : "+";
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        if (_engine.IsRunning)
        {
            if (MessageBox.Show("Chiến dịch đang chạy nền. Bạn có chắc chắn muốn dừng và thoát?", "Xác nhận đóng", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;
            _engine.Stop();
        }
        _appShutdownCts.Cancel();
        SaveCurrentSettings();
        Application.Current.Shutdown();
    }

    private void BtnTelegram_Click(object sender, RoutedEventArgs e)
    {
        OpenUrl("https://t.me/abc_abc_05");
    }

    private void BtnTelegram_MouseClick(object sender, MouseButtonEventArgs e)
    {
        OpenUrl("https://t.me/abc_abc_05");
    }

    private void BtnLicense_Click(object sender, MouseButtonEventArgs e)
    {
        NavLicense.IsChecked = true;
    }

    private void BtnCopyHwid_MouseDown(object sender, MouseButtonEventArgs e)
    {
        BtnCopyHwid_Click(sender, e);
    }
    #endregion

    #region Navigation & Sidebar Handlers (7 Modern SaaS Pages)
    private void NavPage_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        if (PageDashboard == null || PageTargets == null || PageContent == null ||
            PageAccounts == null || PageSettings == null || PageLogs == null || PageLicense == null) return;

        PageDashboard.Visibility = NavDashboard.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PageTargets.Visibility = NavTargets.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PageContent.Visibility = NavContent.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PageAccounts.Visibility = NavAccounts.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PageSettings.Visibility = NavSettings.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PageLogs.Visibility = NavLogs.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PageLicense.Visibility = NavLicense.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

        if (NavDashboard.IsChecked == true)
        {
            LblPageTitle.Text = "📊 Bảng Điều Khiển Tổng Quan";
            LblPageSubtitle.Text = "Theo dõi chỉ số chiến dịch, tiến trình thời gian thực và quản lý tương tác";
        }
        else if (NavTargets.IsChecked == true)
        {
            LblPageTitle.Text = "🎯 Quản Lý Mục Tiêu Bài Viết";
            LblPageSubtitle.Text = "Nhập danh sách bài viết, nhóm, reel, lọc trùng và theo dõi tiến độ bình luận";
        }
        else if (NavContent.IsChecked == true)
        {
            LblPageTitle.Text = "✍️ Soạn Thảo Nội Dung & Media Studio";
            LblPageSubtitle.Text = "Spintax lồng nhau đệ quy, biến động {time}/{date}, Micro-Salting và xử lý ảnh Anti-pHash";
        }
        else if (NavAccounts.IsChecked == true)
        {
            LblPageTitle.Text = "🔑 Kho Tài Khoản Facebook & Proxy";
            LblPageSubtitle.Text = "Quản lý Token ẩn EAAB••••, kiểm tra Live/Die đa luồng, gán Proxy và đo IP thực";
        }
        else if (NavSettings.IsChecked == true)
        {
            LblPageTitle.Text = "⚙️ Cấu Hình Hệ Thống & Anti-Ban";
            LblPageSubtitle.Text = "Tốc độ slots, độ trễ ngẫu nhiên, cơ chế 3-strike block, rate limit backoff & bảo mật DPAPI";
        }
        else if (NavLogs.IsChecked == true)
        {
            LblPageTitle.Text = "📜 Nhật Ký Hoạt Động & Chẩn Đoán";
            LblPageSubtitle.Text = "Ghi log thời gian thực 60 FPS, bộ lọc cấp độ (Success/Warn/Error), tìm kiếm và xuất TXT";
        }
        else if (NavLicense.IsChecked == true)
        {
            LblPageTitle.Text = "⚡ Giấy Phép Sử Dụng & Cập Nhật Tự Động";
            LblPageSubtitle.Text = "Quản lý bản quyền máy HWID, kích hoạt Offline Key và cập nhật an toàn qua GitHub";
        }
    }

    private void BtnGotoTargets_Click(object sender, RoutedEventArgs e)
    {
        NavTargets.IsChecked = true;
    }

    private void BtnGotoContent_Click(object sender, RoutedEventArgs e)
    {
        NavContent.IsChecked = true;
    }

    private void BtnGotoLogs_Click(object sender, RoutedEventArgs e)
    {
        NavLogs.IsChecked = true;
    }

    private void BtnGotoSettings_Click(object sender, RoutedEventArgs e)
    {
        NavSettings.IsChecked = true;
    }
    #endregion

    #region KPI & Realtime Counters
    public void UpdateUiCounters()
    {
        if (LblPostCountDetail == null || EntMaxCmtPost == null) return;

        int totalAcc = Accounts.Count;
        int liveAcc = Accounts.Count(a => a.Status == "Live");
        int dieAcc = Accounts.Count(a => a.Status == "Die");
        int selectedAcc = Accounts.Count(a => a.IsSelected);
        int blockedAcc = Accounts.Count(a => a.BlockStrikes >= 3);

        int totalPosts = TargetPosts.Count;
        int selectedPosts = TargetPosts.Count(p => p.IsSelected);

        int maxPosts = int.TryParse(EntMaxCmtPost.Text, out var mp) ? mp : 1;
        int totalTargetComments = selectedPosts * maxPosts;
        int currentDone = TargetPosts.Sum(p => p.CmtCount);

        // Update Dashboard KPI Cards
        if (DashTargetsCount != null) DashTargetsCount.Text = totalPosts.ToString();
        if (DashTargetsSelected != null) DashTargetsSelected.Text = $"Đã chọn: {selectedPosts} bài";
        if (DashAccountsLive != null) DashAccountsLive.Text = liveAcc.ToString();
        if (DashAccountsTotal != null) DashAccountsTotal.Text = $"Tổng số: {totalAcc} tài khoản (Chọn: {selectedAcc})";
        if (DashSuccessComments != null) DashSuccessComments.Text = _engine.TotalSuccess.ToString();
        if (DashTargetGoal != null) DashTargetGoal.Text = $"Mục tiêu: {totalTargetComments} lượt";
        if (DashFailComments != null) DashFailComments.Text = _engine.TotalFail.ToString();
        if (DashBlockedAccounts != null) DashBlockedAccounts.Text = $"Tài khoản Block: {blockedAcc}";

        // Sub-page detail counters
        LblPostCountDetail.Text = $"Tổng: {totalPosts} bài | Đã chọn: {selectedPosts} bài";
        LblAccountCountDetail.Text = $"Tổng: {totalAcc} | Live: {liveAcc} | Die: {dieAcc} | Đã chọn: {selectedAcc}";

        // Update progress strip
        double pct = totalTargetComments > 0 ? Math.Min(100.0, (double)currentDone / totalTargetComments * 100.0) : 0;
        if (LblProgressPercent != null) LblProgressPercent.Text = $"{pct:0}%";
        if (CampaignProgress != null) CampaignProgress.Value = pct;
    }

    private void OnDelayTimerTick(object? sender, EventArgs e)
    {
        // 1. Live Countdown Bản Quyền (Cập nhật trực tiếp từng giây mượt mà)
        UpdateLiveLicenseCountdown();

        // 2. Cập nhật đếm lùi Delay của từng tài khoản
        double now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
        foreach (var acc in Accounts)
        {
            if (acc.NextReadyTime > now)
            {
                int remaining = (int)Math.Ceiling(acc.NextReadyTime - now);
                acc.DelayStatus = $"⏳ {remaining}s";
            }
            else
            {
                if (acc.DelayStatus.StartsWith("⏳"))
                {
                    acc.DelayStatus = "Sẵn sàng";
                }
            }
        }
    }
    #endregion

    #region Logging & Throttled Batch Dispatcher
    private void OnEngineLogReceived(object? sender, LogEventArgs e)
    {
        Brush color = (SolidColorBrush)FindResource("TextBody");
        Brush bg = new SolidColorBrush(Color.FromRgb(0x1E, 0x23, 0x29));
        Brush bd = new SolidColorBrush(Color.FromRgb(0x2B, 0x31, 0x39));
        string lvl = e.Level.ToUpperInvariant();

        switch (e.Level.ToLowerInvariant())
        {
            case "success":
                color = (SolidColorBrush)FindResource("SuccessGreen");
                bg = new SolidColorBrush(Color.FromRgb(0x11, 0x27, 0x20));
                bd = new SolidColorBrush(Color.FromRgb(0x19, 0x48, 0x3A));
                lvl = "SUCCESS";
                break;
            case "warning":
                color = (SolidColorBrush)FindResource("PrimaryGold");
                bg = new SolidColorBrush(Color.FromRgb(0x2E, 0x27, 0x12));
                bd = new SolidColorBrush(Color.FromRgb(0x52, 0x43, 0x17));
                lvl = "WARNING";
                break;
            case "error":
                color = (SolidColorBrush)FindResource("DangerRed");
                bg = new SolidColorBrush(Color.FromRgb(0x33, 0x17, 0x1F));
                bd = new SolidColorBrush(Color.FromRgb(0x58, 0x24, 0x2F));
                lvl = "ERROR";
                break;
        }

        _pendingLogs.Enqueue(new LogItem
        {
            Timestamp = e.Time.ToString("HH:mm:ss"),
            Level = lvl,
            Message = e.Message,
            ForegroundBrush = color,
            BadgeBackground = bg,
            BadgeBorder = bd
        });
    }

    private void OnLogFlushTimerTick(object? sender, EventArgs e)
    {
        if (_pendingLogs.IsEmpty) return;

        int processed = 0;
        while (_pendingLogs.TryDequeue(out var item) && processed < 100)
        {
            processed++;
            // Check filters
            if (_logLevelFilter != "ALL" && item.Level != _logLevelFilter) continue;
            if (!string.IsNullOrWhiteSpace(_logKeywordFilter) &&
                !item.Message.Contains(_logKeywordFilter, StringComparison.OrdinalIgnoreCase)) continue;

            Logs.Add(item);
        }

        // Limit memory
        while (Logs.Count > 1500)
        {
            Logs.RemoveAt(0);
        }

        LblLogCounter.Text = $"Tổng dòng: {Logs.Count}";

        if (ChkAutoScroll.IsChecked == true && Logs.Count > 0)
        {
            ListLogs.ScrollIntoView(Logs[^1]);
            if (DashMiniLogs != null && DashMiniLogs.Items.Count > 0)
            {
                DashMiniLogs.ScrollIntoView(Logs[^1]);
            }
        }
    }

    private void OnEngineStatsChanged(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            UpdateUiCounters();

            if (_engine.IsRunning)
            {
                LblTopStatus.Text = _engine.IsPaused ? " ● Tạm dừng" : " ● Đang chạy...";
                if (LblDashStatusBanner != null)
                    LblDashStatusBanner.Text = _engine.IsPaused ? "Đã tạm dừng chiến dịch" : "Đang thực thi chiến dịch thời gian thực";
            }
            else
            {
                LblTopStatus.Text = " ● Sẵn sàng";
                if (LblDashStatusBanner != null)
                    LblDashStatusBanner.Text = "Hệ thống sẵn sàng cho chiến dịch mới";
                BtnTopStart.IsEnabled = true;
                BtnTopPause.IsEnabled = false;
                BtnTopStop.IsEnabled = false;
            }
        });
    }

    private void LogFilter_Changed(object sender, RoutedEventArgs e)
    {
        if (RadLogSuccess == null || RadLogWarning == null || RadLogError == null) return;
        if (RadLogSuccess.IsChecked == true) _logLevelFilter = "SUCCESS";
        else if (RadLogWarning.IsChecked == true) _logLevelFilter = "WARNING";
        else if (RadLogError.IsChecked == true) _logLevelFilter = "ERROR";
        else _logLevelFilter = "ALL";
    }

    private void TxtSearchLogs_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (EntSearchLogs == null) return;
        _logKeywordFilter = EntSearchLogs.Text.Trim();
    }

    private void BtnCopySelectedLog_Click(object sender, RoutedEventArgs e)
    {
        if (ListLogs.SelectedItem is LogItem log)
        {
            Clipboard.SetText($"[{log.Timestamp}] [{log.Level}] {log.Message}");
        }
    }

    private void BtnExportLogs_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var sfd = new SaveFileDialog
            {
                Filter = "Text File (*.txt)|*.txt",
                FileName = $"log_tool_fb_{DateTime.Now:yyyyMMdd_HHmmss}.txt"
            };
            if (sfd.ShowDialog() == true)
            {
                var lines = Logs.Select(l => $"[{l.Timestamp}] [{l.Level}] {l.Message}");
                File.WriteAllLines(sfd.FileName, lines);
                MessageBox.Show($"Đã xuất thành công {Logs.Count} dòng nhật ký!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Lỗi xuất file: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnClearLogs_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("Bạn có chắc chắn muốn xóa toàn bộ nhật ký?", "Xác nhận", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            Logs.Clear();
            LblLogCounter.Text = "Tổng dòng: 0";
        }
    }
    #endregion

    #region Tab 1: Bài Viết Mục Tiêu (Target Posts)
    private void TxtPostIds_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (TxtPostIds == null || LblInputPostCounter == null) return;
        var lines = TxtPostIds.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        LblInputPostCounter.Text = $"Đã nhận diện: {lines.Length} dòng";
    }

    private void BtnLoadPosts_Click(object sender, RoutedEventArgs e)
    {
        var rawLines = TxtPostIds.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        if (rawLines.Length == 0)
        {
            MessageBox.Show("Vui lòng nhập ít nhất 1 ID hoặc URL bài viết!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        int added = 0;
        int duplicates = 0;
        var existingIds = new HashSet<string>(TargetPosts.Select(p => p.TargetId));

        int maxCmt = int.TryParse(EntMaxCmtPost.Text, out var mp) ? mp : 1;

        foreach (var line in rawLines)
        {
            string trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed)) continue;

            string? cleanId = FacebookGraphService.CleanTargetId(trimmed);
            if (string.IsNullOrEmpty(cleanId)) continue;

            if (existingIds.Contains(cleanId))
            {
                duplicates++;
                continue;
            }

            existingIds.Add(cleanId);
            TargetPosts.Add(new TargetPost
            {
                Index = TargetPosts.Count + 1,
                TargetId = cleanId,
                OriginalUrl = trimmed.StartsWith("http") ? trimmed : $"https://facebook.com/{cleanId}",
                MaxComments = maxCmt,
                CmtCount = 0,
                Status = "Chờ chạy"
            });
            added++;
        }

        RenumberTargetPosts();
        UpdateUiCounters();

        MessageBox.Show($"Đã nạp thành công {added} bài viết mới!\n(Bỏ qua {duplicates} bài viết trùng lặp)", "Kết quả nạp", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void BtnImportPosts_Click(object sender, RoutedEventArgs e)
    {
        var ofd = new OpenFileDialog
        {
            Filter = "Text File (*.txt)|*.txt|All Files (*.*)|*.*",
            Title = "Chọn file danh sách bài viết"
        };
        if (ofd.ShowDialog() == true)
        {
            try
            {
                string text = File.ReadAllText(ofd.FileName);
                TxtPostIds.Text = text;
                BtnLoadPosts_Click(sender, e);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi đọc file: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void TxtSearchPosts_TextChanged(object sender, TextChangedEventArgs e)
    {
        string kw = EntSearchPosts.Text.Trim().ToLowerInvariant();
        ICollectionView view = CollectionViewSource.GetDefaultView(GridPosts.ItemsSource);
        if (view == null) return;

        if (string.IsNullOrEmpty(kw))
        {
            view.Filter = null;
        }
        else
        {
            view.Filter = item =>
            {
                if (item is TargetPost p)
                {
                    return p.TargetId.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                           p.OriginalUrl.Contains(kw, StringComparison.OrdinalIgnoreCase);
                }
                return false;
            };
        }
    }

    private void BtnPickRandomPerPage_Click(object sender, RoutedEventArgs e)
    {
        if (TargetPosts.Count == 0) return;

        var pageMap = new Dictionary<string, List<TargetPost>>();
        foreach (var p in TargetPosts)
        {
            string pageId = p.TargetId.Contains('_') ? p.TargetId.Split('_')[0] : p.TargetId;
            if (!pageMap.ContainsKey(pageId)) pageMap[pageId] = new List<TargetPost>();
            pageMap[pageId].Add(p);
        }

        int multiPageCount = pageMap.Count(kv => kv.Value.Count > 1);
        if (multiPageCount == 0)
        {
            MessageBox.Show("Mỗi bài viết hiện tại đã thuộc về một Fanpage/Người dùng riêng biệt!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var res = MessageBox.Show(
            $"Phát hiện {multiPageCount} Fanpage có nhiều bài viết trong danh sách.\n\n" +
            "Bạn có muốn tự động chọn ngẫu nhiên đúng 1 bài viết cho mỗi Fanpage và bỏ chọn các bài viết còn lại?",
            "Lọc 1 bài / Fanpage", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (res == MessageBoxResult.Yes)
        {
            foreach (var kv in pageMap)
            {
                foreach (var p in kv.Value) p.IsSelected = false;
                var chosen = kv.Value[Random.Shared.Next(kv.Value.Count)];
                chosen.IsSelected = true;
            }
            UpdateUiCounters();
        }
    }

    private void BtnCopyTargetPosts_Click(object sender, RoutedEventArgs e)
    {
        if (TargetPosts.Count == 0) return;
        var text = string.Join("\n", TargetPosts.Select(p => p.TargetId));
        Clipboard.SetText(text);
        MessageBox.Show($"Đã sao chép {TargetPosts.Count} ID bài viết vào Clipboard!", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void BtnSelectAllPosts_Click(object sender, RoutedEventArgs e)
    {
        foreach (var p in TargetPosts) p.IsSelected = true;
        UpdateUiCounters();
    }

    private void BtnDeselectAllPosts_Click(object sender, RoutedEventArgs e)
    {
        foreach (var p in TargetPosts) p.IsSelected = false;
        UpdateUiCounters();
    }

    private void BtnDeleteSelectedPosts_Click(object sender, RoutedEventArgs e)
    {
        var selected = TargetPosts.Where(p => p.IsSelected).ToList();
        if (selected.Count == 0) return;

        if (MessageBox.Show($"Bạn có chắc chắn muốn xóa {selected.Count} bài viết đã chọn?", "Xác nhận xóa", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            foreach (var p in selected) TargetPosts.Remove(p);
            RenumberTargetPosts();
            UpdateUiCounters();
        }
    }

    private void BtnClearAllPosts_Click(object sender, RoutedEventArgs e)
    {
        if (TargetPosts.Count == 0) return;
        if (MessageBox.Show("Bạn có chắc chắn muốn xóa TOÀN BỘ danh sách bài viết?", "Xác nhận xóa hết", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
        {
            TargetPosts.Clear();
            UpdateUiCounters();
        }
    }

    private void BtnDeleteCommentedPosts_Click(object sender, RoutedEventArgs e)
    {
        int threshold = int.TryParse(EntDelCmtCount.Text, out var t) ? t : 1;
        var matched = TargetPosts.Where(p => p.CmtCount >= threshold).ToList();
        if (matched.Count == 0)
        {
            MessageBox.Show($"Không tìm thấy bài viết nào có số lượt bình luận >= {threshold}!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        foreach (var p in matched) TargetPosts.Remove(p);
        RenumberTargetPosts();
        UpdateUiCounters();
    }

    private void BtnOpenPostUrl_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string url && !string.IsNullOrEmpty(url))
        {
            OpenUrl(url);
        }
    }

    private void RenumberTargetPosts()
    {
        for (int i = 0; i < TargetPosts.Count; i++)
        {
            TargetPosts[i].Index = i + 1;
        }
    }
    #endregion

    #region Tab 2: Nội Dung Bình Luận & Media (Comments & Media)
    private void BtnInsertTag_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string tag)
        {
            // Insert into Random comments textbox at cursor position
            int sel = TxtRandomComments.SelectionStart;
            TxtRandomComments.Text = TxtRandomComments.Text.Insert(sel, tag);
            TxtRandomComments.SelectionStart = sel + tag.Length;
            TxtRandomComments.Focus();
            GenerateCommentPreview();
        }
    }

    private void CboIconCategory_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CboIconCategory?.SelectedItem is ComboBoxItem item)
        {
            _selectedIconCategory = item.Content?.ToString() ?? "🎲 Hỗn Hợp";
            if (IsLoaded)
            {
                GenerateCommentPreview();
            }
        }
    }

    private void BtnPreviewComment_Click(object sender, RoutedEventArgs e)
    {
        GenerateCommentPreview();
    }

    private void GenerateCommentPreview()
    {
        if (!IsLoaded || TxtCommentPreview == null || TxtFixedComment == null || TxtRandomComments == null)
            return;

        try
        {
            var fixedText = TxtFixedComment.Text?.Trim() ?? "";
            var randomList = SpintaxEngine.SplitCommentsSafely(TxtRandomComments.Text?.Trim() ?? "");

            string baseComment = "";
            if (randomList.Count > 0)
            {
                baseComment = randomList[Random.Shared.Next(randomList.Count)];
            }
            else if (!string.IsNullOrEmpty(fixedText))
            {
                baseComment = fixedText;
            }

            if (string.IsNullOrEmpty(baseComment))
            {
                TxtCommentPreview.Text = "(Chưa có nội dung để hiển thị xem trước)";
                return;
            }

            string spintaxEvaluated = SpintaxEngine.ParseSpintax(baseComment);
            string salted = SpintaxEngine.ApplyDynamicTagsAndSalt(
                spintaxEvaluated,
                useSalt: ChkMicroSalt?.IsChecked == true,
                iconCat: _selectedIconCategory,
                iconNewline: ChkIconNewline?.IsChecked == true
            );

            if (RadFixedFirst?.IsChecked == true && !string.IsNullOrEmpty(fixedText) && randomList.Count > 0)
            {
                TxtCommentPreview.Text = $"{fixedText}\n{salted}";
            }
            else if (RadRandomFirst?.IsChecked == true && !string.IsNullOrEmpty(fixedText) && randomList.Count > 0)
            {
                TxtCommentPreview.Text = $"{salted}\n{fixedText}";
            }
            else
            {
                TxtCommentPreview.Text = salted;
            }
        }
        catch (Exception ex)
        {
            if (TxtCommentPreview != null)
                TxtCommentPreview.Text = $"Lỗi xem trước: {ex.Message}";
        }
    }

    private void BtnBrowseImage_Click(object sender, RoutedEventArgs e)
    {
        var ofd = new OpenFileDialog
        {
            Filter = "Ảnh Hợp Lệ (*.png;*.jpg;*.jpeg;*.webp)|*.png;*.jpg;*.jpeg;*.webp|All Files (*.*)|*.*",
            Title = "Chọn ảnh đính kèm bình luận"
        };
        if (ofd.ShowDialog() == true)
        {
            SetAttachedImage(ofd.FileName);
        }
    }

    private void BtnRemoveImage_Click(object sender, RoutedEventArgs e)
    {
        _selectedImagePath = string.Empty;
        ImgThumbnailPreview.Source = null;
        PnlNoImageNotice.Visibility = Visibility.Visible;
        LblImagePath.Text = "Đường dẫn: (Trống)";
        LblImageSpecs.Text = "Kích thước: 0 x 0 px  |  Dung lượng: 0 KB";
    }

    private void SetAttachedImage(string path)
    {
        try
        {
            if (!File.Exists(path)) return;

            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.UriSource = new Uri(path);
            bi.EndInit();

            ImgThumbnailPreview.Source = bi;
            PnlNoImageNotice.Visibility = Visibility.Collapsed;

            _selectedImagePath = path;
            var fi = new FileInfo(path);
            LblImagePath.Text = $"Đường dẫn: {path}";
            LblImageSpecs.Text = $"Kích thước: {bi.PixelWidth} x {bi.PixelHeight} px  |  Dung lượng: {fi.Length / 1024.0:0.0} KB";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Không thể mở tệp ảnh: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
    #endregion

    #region Tab 3: Tài Khoản & Proxy (Accounts & Proxies)
    private void BtnLoadAccounts_Click(object sender, RoutedEventArgs e)
    {
        var rawLines = TxtAccounts.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        if (rawLines.Length == 0)
        {
            MessageBox.Show("Vui lòng nhập ít nhất 1 tài khoản Facebook!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var backupProxies = TxtProxies.Text
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim())
            .Where(p => !string.IsNullOrEmpty(p))
            .ToList();

        int added = 0;
        int duplicates = 0;
        var existingTokens = new HashSet<string>(Accounts.Select(a => a.Token));

        int pIndex = 0;
        foreach (var line in rawLines)
        {
            string trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            var parts = trimmed.Split('|');
            string uid = "";
            string token = "";
            string proxy = "";

            if (parts.Length == 1)
            {
                token = parts[0].Trim();
            }
            else if (parts.Length == 2)
            {
                uid = parts[0].Trim();
                token = parts[1].Trim();
            }
            else
            {
                uid = parts[0].Trim();
                token = parts.FirstOrDefault(p => p.StartsWith("EAA", StringComparison.OrdinalIgnoreCase)) ?? parts[1].Trim();
                proxy = parts.LastOrDefault(p => p.Contains(':')) ?? "";
            }

            if (string.IsNullOrEmpty(token)) continue;

            if (existingTokens.Contains(token))
            {
                duplicates++;
                continue;
            }

            if (string.IsNullOrEmpty(proxy) && backupProxies.Count > 0)
            {
                proxy = backupProxies[pIndex % backupProxies.Count];
                pIndex++;
            }

            existingTokens.Add(token);
            var acc = new Account
            {
                Index = Accounts.Count + 1,
                Uid = uid,
                Token = token,
                ProxyStr = proxy,
                Status = "Chưa check",
                ParsedProxy = ProxyService.ParseProxy(proxy)
            };

            Accounts.Add(acc);
            added++;
        }

        RenumberAccounts();
        UpdateUiCounters();

        MessageBox.Show($"Đã nạp thành công {added} tài khoản mới!\n(Bỏ qua {duplicates} tài khoản trùng lặp)", "Kết quả nạp", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void BtnImportAccounts_Click(object sender, RoutedEventArgs e)
    {
        var ofd = new OpenFileDialog
        {
            Filter = "Text File (*.txt)|*.txt|All Files (*.*)|*.*",
            Title = "Chọn file danh sách tài khoản"
        };
        if (ofd.ShowDialog() == true)
        {
            try
            {
                string text = File.ReadAllText(ofd.FileName);
                TxtAccounts.Text = text;
                BtnLoadAccounts_Click(sender, e);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi đọc file: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private async void BtnCheckLiveDie_Click(object sender, RoutedEventArgs e)
    {
        var targets = Accounts.Where(a => a.IsSelected).ToList();
        if (targets.Count == 0)
        {
            MessageBox.Show("Vui lòng chọn ít nhất 1 tài khoản để kiểm tra!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        BtnTopStart.IsEnabled = false;
        var fbService = new FacebookGraphService();

        var tasks = targets.Select(async acc =>
        {
            acc.Status = "Checking...";
            var (valid, name, uid, err) = await fbService.CheckTokenAsync(acc);
            Dispatcher.Invoke(() =>
            {
                acc.Status = valid ? "Live" : "Die";
                if (!string.IsNullOrEmpty(name)) acc.Name = name;
                if (!string.IsNullOrEmpty(uid)) acc.Uid = uid;
                acc.Note = valid ? "Hoạt động tốt" : err;
            });
        });

        await Task.WhenAll(tasks);
        BtnTopStart.IsEnabled = true;
        UpdateUiCounters();
        MessageBox.Show("Hoàn tất kiểm tra trạng thái toàn bộ Token!", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void BtnCheckProxies_Click(object sender, RoutedEventArgs e)
    {
        var targets = Accounts.Where(a => a.IsSelected && !string.IsNullOrEmpty(a.ProxyStr)).ToList();
        if (targets.Count == 0)
        {
            MessageBox.Show("Không có tài khoản nào có cấu hình Proxy để kiểm tra!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var tasks = targets.Select(async acc =>
        {
            if (acc.ParsedProxy == null) return;
            var ip = await ProxyService.FetchRealExitIpAsync(acc.ParsedProxy, forceRefresh: true);
            Dispatcher.Invoke(() =>
            {
                acc.RealExitIp = ip ?? "Lỗi kết nối";
            });
        });

        await Task.WhenAll(tasks);
        MessageBox.Show("Hoàn tất kiểm tra và lấy IP thực của danh sách Proxy!", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void BtnCopyLiveTokens_Click(object sender, RoutedEventArgs e)
    {
        var live = Accounts.Where(a => a.Status == "Live").Select(a => a.Token).ToList();
        if (live.Count == 0)
        {
            MessageBox.Show("Không có tài khoản nào có trạng thái Live!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        Clipboard.SetText(string.Join("\n", live));
        MessageBox.Show($"Đã sao chép {live.Count} Token Live vào Clipboard!", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void BtnSelectAllAccounts_Click(object sender, RoutedEventArgs e)
    {
        foreach (var a in Accounts) a.IsSelected = true;
        UpdateUiCounters();
    }

    private void BtnDeselectAllAccounts_Click(object sender, RoutedEventArgs e)
    {
        foreach (var a in Accounts) a.IsSelected = false;
        UpdateUiCounters();
    }

    private void BtnDeleteSelectedAccounts_Click(object sender, RoutedEventArgs e)
    {
        var selected = Accounts.Where(a => a.IsSelected).ToList();
        if (selected.Count == 0) return;

        if (MessageBox.Show($"Bạn có chắc chắn muốn xóa {selected.Count} tài khoản đã chọn?", "Xác nhận xóa", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            foreach (var a in selected) Accounts.Remove(a);
            RenumberAccounts();
            UpdateUiCounters();
        }
    }

    private void BtnDeleteDieAccounts_Click(object sender, RoutedEventArgs e)
    {
        var bad = Accounts.Where(a => a.Status == "Die" || a.Status == "Block").ToList();
        if (bad.Count == 0)
        {
            MessageBox.Show("Không tìm thấy tài khoản nào bị Die hoặc Block!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (MessageBox.Show($"Bạn có chắc chắn muốn xóa {bad.Count} tài khoản Die / Block khỏi danh sách?", "Xác nhận xóa", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            foreach (var a in bad) Accounts.Remove(a);
            RenumberAccounts();
            UpdateUiCounters();
        }
    }

    private void RenumberAccounts()
    {
        for (int i = 0; i < Accounts.Count; i++)
        {
            Accounts[i].Index = i + 1;
        }
    }
    #endregion

    #region Tab 4: Cấu Hình & Anti-Ban (Settings & Persistence)
    private void LoadSavedSettings()
    {
        try
        {
            var cfg = SecureStorageService.LoadConfig();

            TxtFixedComment.Text = cfg.FixedComment;
            TxtRandomComments.Text = cfg.RandomComments;
            EntDelayMin.Text = cfg.DelayMin.ToString("0");
            EntDelayMax.Text = cfg.DelayMax.ToString("0");
            EntSlots.Text = cfg.Slots.ToString();
            EntMaxCmtToken.Text = cfg.MaxCommentsPerToken.ToString();
            EntMaxCmtPost.Text = cfg.MaxCommentsPerPost.ToString();
            ChkRoundRobin.IsChecked = cfg.TrueRoundRobin;
            ChkWarmup.IsChecked = cfg.AutoWarmup;
            ChkSendReactions.IsChecked = cfg.SendReactions;
            ChkMicroSalt.IsChecked = cfg.UseMicroSalt;
            ChkIconNewline.IsChecked = cfg.IconNewline;
            ChkAutoRotateProxy.IsChecked = cfg.AutoRotate;
            EntSmartRestAfter.Text = cfg.SmartRestAfter.ToString();
            EntSmartRestDuration.Text = cfg.SmartRestDuration.ToString("0");

            if (cfg.OrderMode == "Fixed first") RadFixedFirst.IsChecked = true;
            else RadRandomFirst.IsChecked = true;

            if (!string.IsNullOrEmpty(cfg.SelectedImagePath) && File.Exists(cfg.SelectedImagePath))
            {
                SetAttachedImage(cfg.SelectedImagePath);
            }
            UpdateDashboardConfigSummary();
        }
        catch { }
    }

    private void SaveCurrentSettings()
    {
        try
        {
            var cfg = BuildCurrentConfig();
            SecureStorageService.SaveConfig(cfg);
        }
        catch { }
    }

    private AppConfig BuildCurrentConfig()
    {
        return new AppConfig
        {
            FixedComment = TxtFixedComment.Text.Trim(),
            RandomComments = TxtRandomComments.Text.Trim(),
            OrderMode = RadFixedFirst.IsChecked == true ? "Fixed first" : "Random first",
            DelayMin = double.TryParse(EntDelayMin.Text, out var dmin) ? dmin : 30.0,
            DelayMax = double.TryParse(EntDelayMax.Text, out var dmax) ? dmax : 60.0,
            Slots = int.TryParse(EntSlots.Text, out var sl) ? Math.Max(1, sl) : 3,
            MaxCommentsPerToken = int.TryParse(EntMaxCmtToken.Text, out var mt) ? mt : 50,
            MaxCommentsPerPost = int.TryParse(EntMaxCmtPost.Text, out var mp) ? mp : 1,
            TrueRoundRobin = ChkRoundRobin.IsChecked == true,
            AutoWarmup = ChkWarmup.IsChecked == true,
            SendReactions = ChkSendReactions.IsChecked == true,
            ReactionType = (CboReactionType.SelectedItem as ComboBoxItem)?.Content.ToString()?.Split(' ').Last() ?? "LIKE",
            UseMicroSalt = ChkMicroSalt.IsChecked == true,
            IconCategory = _selectedIconCategory,
            IconNewline = ChkIconNewline.IsChecked == true,
            AutoRotate = ChkAutoRotateProxy.IsChecked == true,
            SmartRestAfter = int.TryParse(EntSmartRestAfter.Text, out var sra) ? sra : 10,
            SmartRestDuration = double.TryParse(EntSmartRestDuration.Text, out var srd) ? srd : 60.0,
            SelectedImagePath = _selectedImagePath,
            TargetUrls = TargetPosts.Select(p => p.OriginalUrl).ToList(),
            RawAccountLines = Accounts.Select(a => $"{a.Uid}|{a.Token}|{a.ProxyStr}").ToList()
        };
    }

    private void BtnSaveConfig_Click(object sender, RoutedEventArgs e)
    {
        SaveCurrentSettings();
        MessageBox.Show("Cấu hình và dữ liệu Token đã được lưu an toàn bằng mã hóa Windows DPAPI!", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void BtnReloadConfig_Click(object sender, RoutedEventArgs e)
    {
        LoadSavedSettings();
        MessageBox.Show("Đã tải lại cấu hình từ bộ lưu trữ an toàn!", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void BtnResetDefaults_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("Bạn có chắc chắn muốn khôi phục toàn bộ cấu hình về mặc định ban đầu?", "Xác nhận khôi phục", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
        {
            EntSlots.Text = "3";
            EntDelayMin.Text = "30";
            EntDelayMax.Text = "60";
            EntMaxCmtToken.Text = "50";
            EntMaxCmtPost.Text = "1";
            ChkRoundRobin.IsChecked = true;
            ChkWarmup.IsChecked = true;
            ChkSendReactions.IsChecked = false;
            ChkMicroSalt.IsChecked = true;
            ChkIconNewline.IsChecked = false;
            ChkAutoRotateProxy.IsChecked = true;
            EntSmartRestAfter.Text = "10";
            EntSmartRestDuration.Text = "60";
            RadRandomFirst.IsChecked = true;
            SaveCurrentSettings();
            UpdateDashboardConfigSummary();
            MessageBox.Show("Đã khôi phục các thông số về mặc định ban đầu!", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void UpdateDashboardConfigSummary()
    {
        if (LblDashConfigSummary == null || EntSlots == null) return;
        string slots = EntSlots.Text.Trim();
        string dMin = EntDelayMin?.Text.Trim() ?? "30";
        string dMax = EntDelayMax?.Text.Trim() ?? "60";
        string maxTok = EntMaxCmtToken?.Text.Trim() ?? "50";
        string maxPost = EntMaxCmtPost?.Text.Trim() ?? "1";
        string rr = ChkRoundRobin?.IsChecked == true ? "Bật" : "Tắt";
        string rest = $"{EntSmartRestAfter?.Text.Trim() ?? "10"}/{EntSmartRestDuration?.Text.Trim() ?? "60"}s";

        LblDashConfigSummary.Text = $"{slots} Luồng | Delay {dMin}-{dMax}s | Max {maxTok} cmt/token | Max {maxPost} cmt/bài | Nghỉ {rest} | Round-Robin: {rr}";
    }

    private void ConfigInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateDashboardConfigSummary();
    }

    private void BtnSlotsMinus_Click(object sender, RoutedEventArgs e)
    {
        int current = int.TryParse(EntSlots.Text, out var v) ? v : 3;
        if (current > 1)
        {
            EntSlots.Text = (current - 1).ToString();
            UpdateDashboardConfigSummary();
        }
    }

    private void BtnSlotsPlus_Click(object sender, RoutedEventArgs e)
    {
        int current = int.TryParse(EntSlots.Text, out var v) ? v : 3;
        if (current < 20)
        {
            EntSlots.Text = (current + 1).ToString();
            UpdateDashboardConfigSummary();
        }
    }

    private void BtnProfileSafe_Click(object sender, RoutedEventArgs e)
    {
        EntSlots.Text = "3";
        EntDelayMin.Text = "30";
        EntDelayMax.Text = "60";
        EntMaxCmtToken.Text = "30";
        EntMaxCmtPost.Text = "1";
        ChkRoundRobin.IsChecked = true;
        ChkWarmup.IsChecked = true;
        ChkAutoRotateProxy.IsChecked = true;
        EntSmartRestAfter.Text = "10";
        EntSmartRestDuration.Text = "60";
        ChkSendReactions.IsChecked = true;
        SaveCurrentSettings();
        UpdateDashboardConfigSummary();
        MessageBox.Show("Đã áp dụng cấu hình: AN TOÀN CAO (Khuyên dùng)!\n\n• 3 Luồng song song\n• Delay ngẫu nhiên: 30 - 60 giây\n• Giới hạn: 30 cmt/token & 1 cmt/bài\n• Nghỉ thông minh: 10 cmt nghỉ 60 giây\n• Bật Warmup & Round-Robin & Thả Reaction", "Áp Dụng Profile Thành Công", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void BtnProfileFast_Click(object sender, RoutedEventArgs e)
    {
        EntSlots.Text = "5";
        EntDelayMin.Text = "15";
        EntDelayMax.Text = "30";
        EntMaxCmtToken.Text = "50";
        EntMaxCmtPost.Text = "2";
        ChkRoundRobin.IsChecked = true;
        ChkWarmup.IsChecked = true;
        ChkAutoRotateProxy.IsChecked = true;
        EntSmartRestAfter.Text = "15";
        EntSmartRestDuration.Text = "30";
        SaveCurrentSettings();
        UpdateDashboardConfigSummary();
        MessageBox.Show("Đã áp dụng cấu hình: TĂNG TỐC (Turbo Fast)!\n\n• 5 Luồng song song\n• Delay ngẫu nhiên: 15 - 30 giây\n• Giới hạn: 50 cmt/token & 2 cmt/bài\n• Nghỉ thông minh: 15 cmt nghỉ 30 giây", "Áp Dụng Profile Thành Công", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void BtnProfileStealth_Click(object sender, RoutedEventArgs e)
    {
        EntSlots.Text = "1";
        EntDelayMin.Text = "60";
        EntDelayMax.Text = "120";
        EntMaxCmtToken.Text = "15";
        EntMaxCmtPost.Text = "1";
        ChkRoundRobin.IsChecked = true;
        ChkWarmup.IsChecked = true;
        ChkAutoRotateProxy.IsChecked = true;
        EntSmartRestAfter.Text = "5";
        EntSmartRestDuration.Text = "120";
        ChkSendReactions.IsChecked = true;
        SaveCurrentSettings();
        UpdateDashboardConfigSummary();
        MessageBox.Show("Đã áp dụng cấu hình: NUÔI NICK (Siêu Chậm / Stealth)!\n\n• 1 Luồng đơn lẻ\n• Delay giãn cách: 60 - 120 giây\n• Giới hạn: 15 cmt/token & 1 cmt/bài\n• Nghỉ thông minh: 5 cmt nghỉ 120 giây\n• Bật Thả Reaction tự nhiên", "Áp Dụng Profile Thành Công", MessageBoxButton.OK, MessageBoxImage.Information);
    }
    #endregion

    #region Tab 6: Bản Quyền & Cập Nhật (License & GitHub Releases)
    private void BtnCopyHwid_Click(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(TxtMachineHwid.Text);
        MessageBox.Show("Đã sao chép Hardware ID (HWID) vào Clipboard!", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void BtnActivateKey_Click(object sender, RoutedEventArgs e)
    {
        string key = TxtLicenseKeyInput.Text.Trim();
        if (string.IsNullOrEmpty(key))
        {
            MessageBox.Show("Vui lòng nhập mã kích hoạt bản quyền!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string hwid = LicenseService.GetStrongHwid();
        var (ok, msg) = await LicenseService.ActivateByKeyAsync(hwid, key);
        if (ok)
        {
            MessageBox.Show($"Kích hoạt bản quyền thành công!\n{msg}", "Chúc mừng", MessageBoxButton.OK, MessageBoxImage.Information);
            UpdateLiveLicenseCountdown();
            TxtLicenseKeyInput.Clear();
        }
        else
        {
            MessageBox.Show($"Kích hoạt thất bại: {msg}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void BtnCheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        await CheckUpdateInternalAsync(silentIfNoUpdate: false);
    }

    private async Task CheckUpdateInternalAsync(bool silentIfNoUpdate)
    {
        try
        {
            LblUpdateStatusCard.Text = "Đang kiểm tra từ GitHub Releases...";
            var settings = UpdateSettings.Load();
            var checkResult = await _updateService.CheckForUpdateAsync(settings);

            if (checkResult.HasUpdate && checkResult.Manifest != null)
            {
                LblUpdateStatusCard.Text = $"Có bản mới: v{checkResult.LatestVersion}";
                var updateDialog = new UpdateDialog(checkResult.Manifest, checkResult.CurrentVersion, _updateService);
                updateDialog.ShowDialog();
            }
            else
            {
                LblUpdateStatusCard.Text = "Bạn đang sử dụng phiên bản mới nhất";
                if (!silentIfNoUpdate)
                {
                    MessageBox.Show(
                        $"Ứng dụng đang ở phiên bản mới nhất (v{checkResult.CurrentVersion})!\nKhông có bản cập nhật nào mới hơn trên GitHub.",
                        "Kiểm tra cập nhật", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }
        catch (Exception ex)
        {
            LblUpdateStatusCard.Text = "Lỗi kết nối kiểm tra cập nhật";
            if (!silentIfNoUpdate)
            {
                MessageBox.Show($"Không thể kiểm tra cập nhật:\n{ex.Message}", "Lỗi kết nối", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    private void BtnOpenAppDir_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = AppDomain.CurrentDomain.BaseDirectory,
            UseShellExecute = true
        });
    }
    #endregion

    #region Campaign Execution Controls
    private async void BtnStart_Click(object sender, RoutedEventArgs e)
    {
        if (_engine.IsRunning) return;

        // Validation
        var liveAccs = Accounts.Where(a => a.IsSelected && (a.Status == "Live" || a.Status == "Chưa check")).ToList();
        if (liveAccs.Count == 0)
        {
            MessageBox.Show("Vui lòng chọn ít nhất 1 tài khoản Live để bắt đầu chiến dịch!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
            NavAccounts.IsChecked = true;
            return;
        }

        var targets = TargetPosts.Where(p => p.IsSelected).ToList();
        if (targets.Count == 0)
        {
            MessageBox.Show("Vui lòng chọn ít nhất 1 bài viết mục tiêu để bắt đầu chiến dịch!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
            NavTargets.IsChecked = true;
            return;
        }

        SaveCurrentSettings();
        var config = BuildCurrentConfig();

        var fixedComments = string.IsNullOrWhiteSpace(TxtFixedComment.Text)
            ? new List<string>()
            : new List<string> { TxtFixedComment.Text.Trim() };

        var randomComments = SpintaxEngine.SplitCommentsSafely(TxtRandomComments.Text.Trim());
        if (fixedComments.Count == 0 && randomComments.Count == 0)
        {
            MessageBox.Show("Vui lòng nhập nội dung bình luận (cố định hoặc ngẫu nhiên)!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
            NavContent.IsChecked = true;
            return;
        }

        // Prevent double click
        BtnTopStart.IsEnabled = false;
        BtnTopPause.IsEnabled = true;
        BtnTopStop.IsEnabled = true;
        LblTopStatus.Text = " ● Đang chạy...";
        if (LblDashStatusBanner != null)
            LblDashStatusBanner.Text = "Đang thực thi chiến dịch thời gian thực";

        var backupProxies = TxtProxies.Text
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim())
            .Where(p => !string.IsNullOrEmpty(p))
            .ToList();

        try
        {
            await _engine.StartAsync(
                TargetPosts.ToList(),
                Accounts.ToList(),
                config,
                fixedComments,
                randomComments,
                backupProxies
            );
        }
        catch (Exception ex)
        {
            OnEngineLogReceived(this, new LogEventArgs { Message = $"Lỗi chạy chiến dịch: {ex.Message}", Level = "error" });
        }
        finally
        {
            OnEngineStatsChanged(this, EventArgs.Empty);
        }
    }

    private void BtnPause_Click(object sender, RoutedEventArgs e)
    {
        if (_engine.IsPaused)
        {
            _engine.Resume();
            BtnTopPause.Content = "⏸ TẠM DỪNG";
        }
        else
        {
            _engine.Pause();
            BtnTopPause.Content = "▶ TIẾP TỤC";
        }
        OnEngineStatsChanged(this, EventArgs.Empty);
    }

    private void BtnStop_Click(object sender, RoutedEventArgs e)
    {
        if (_engine.IsRunning)
        {
            if (MessageBox.Show("Bạn có chắc chắn muốn dừng hẳn chiến dịch đang chạy?", "Xác nhận dừng", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                _engine.Stop();
                BtnTopStart.IsEnabled = true;
                BtnTopPause.IsEnabled = false;
                BtnTopStop.IsEnabled = false;
                OnEngineStatsChanged(this, EventArgs.Empty);
            }
        }
    }
    #endregion

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch { }
    }
}
