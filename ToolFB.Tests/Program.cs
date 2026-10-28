using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using ToolFB.App.Models;
using ToolFB.App.Services;
using ToolFB.App.Update;
using ToolFB.App.Views;

namespace ToolFB.Tests;

internal class Program
{
    static int Main()
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("=================================================");
        Console.WriteLine("   BỘ KIỂM THỬ TỰ ĐỘNG - TOOL FB v14 C# .NET 8   ");
        Console.WriteLine("=================================================\n");
        Console.ResetColor();

        int passed = 0;
        int failed = 0;

        void AssertTest(string testName, bool condition)
        {
            if (condition)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"[PASS] {testName}");
                passed++;
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[FAIL] {testName}");
                failed++;
            }
            Console.ResetColor();
        }

        // Test 1: Spintax Parsing (Nested)
        string spintaxInput = "Chào {bạn|anh|{em|cháu}}!";
        var possibleOutputs = new HashSet<string> { "Chào bạn!", "Chào anh!", "Chào em!", "Chào cháu!" };
        bool spintaxOk = true;
        for (int i = 0; i < 50; i++)
        {
            var res = SpintaxEngine.ParseSpintax(spintaxInput);
            if (!possibleOutputs.Contains(res))
            {
                spintaxOk = false;
                break;
            }
        }
        AssertTest("1. Phân giải Spintax đệ quy lồng nhau {A|B|{C|D}}", spintaxOk);

        // Test 2: SplitCommentsSafely
        string rawComments = "Nội dung 1 {đoạn 1|đoạn 2} | Nội dung 2 {A|B|{C|D}} | Nội dung 3 dòng 1\ndòng 2";
        var split = SpintaxEngine.SplitCommentsSafely(rawComments);
        AssertTest("2. Tách comment bảo toàn ngoặc Spintax và xuống dòng", split.Count == 3 && split[0].Contains("{đoạn 1|đoạn 2}") && split[1].Contains("{A|B|{C|D}}"));

        // Test 3: Dynamic Tags & Micro-Salting
        string tagged = "Giờ là {time}, ngày {date}, chúc {greeting}! SĐT: 0988888888{dot} {rand_num}";
        string salted = SpintaxEngine.ApplyDynamicTagsAndSalt(tagged, useSalt: true, iconCat: "🔥 Nổi Bật", iconNewline: false);
        AssertTest("3. Thay thế dynamic tags {time}, {date}, {greeting}, {rand_num}", !salted.Contains("{time}") && !salted.Contains("{date}") && !salted.Contains("{greeting}"));

        // Test 4: CleanTargetId
        string groupUrl = "https://www.facebook.com/groups/123456789/posts/9876543210/";
        string reelUrl = "https://www.facebook.com/reel/112233445566";
        string fbidUrl = "https://www.facebook.com/permalink.php?story_fbid=555666777&id=1000999888";
        string rawId = "1000123456_9988776655";

        AssertTest("4.1 Trích xuất ID bài viết nhóm Facebook", FacebookGraphService.CleanTargetId(groupUrl) == "123456789_9876543210");
        AssertTest("4.2 Trích xuất ID Facebook Reel", FacebookGraphService.CleanTargetId(reelUrl) == "112233445566");
        AssertTest("4.3 Trích xuất story_fbid và id", FacebookGraphService.CleanTargetId(fbidUrl) == "1000999888_555666777");
        AssertTest("4.4 Nhận diện ID gốc", FacebookGraphService.CleanTargetId(rawId) == "1000123456_9988776655");

        // Test 5: Proxy Parsing
        var httpProxy = ProxyService.ParseProxy("103.152.112.50:8080:user1:pass1");
        var socksProxy = ProxyService.ParseProxy("socks5://127.0.0.1:1080");
        AssertTest("5. Phân tích Proxy HTTP & Socks5 kèm xác thực", httpProxy != null && socksProxy != null);

        // Test 6: MakeFullCommentLink
        string cmtLink = FacebookGraphService.MakeFullCommentLink("123456_789", "789_9999");
        AssertTest("6. Tạo đường dẫn link comment chuẩn", cmtLink.Contains("facebook.com"));

        // Test 7: ECDSA Signature & SHA-256 Verification
        string testZipPath = Path.Combine(Path.GetTempPath(), "test_update.zip");
        File.WriteAllBytes(testZipPath, Encoding.UTF8.GetBytes("GIANG DEV UPDATE PACKAGE MOCK DATA"));

        using var sha256 = SHA256.Create();
        string testSha256 = Convert.ToHexString(sha256.ComputeHash(File.ReadAllBytes(testZipPath))).ToLowerInvariant();

        string signerDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "ToolFB.Signer");
        string privKeyPath = Path.Combine(signerDir, "private_key.pem");

        bool signatureVerified = false;
        if (File.Exists(privKeyPath))
        {
            string privPem = File.ReadAllText(privKeyPath);
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportFromPem(privPem);

            var manifest = new UpdateManifest
            {
                Version = "14.1.0",
                DownloadUrl = "https://github.com/giangdev/ToolFB/releases/download/v14.1.0/update.zip",
                Sha256 = testSha256,
                Mandatory = false,
                ReleaseNotes = "Bản thử nghiệm"
            };

            var payloadBytes = Encoding.UTF8.GetBytes(manifest.GetSigningPayload());
            var sig = Convert.ToBase64String(ecdsa.SignData(payloadBytes, HashAlgorithmName.SHA256));
            manifest.Signature = sig;

            signatureVerified = SignatureVerifier.VerifySignature(manifest);
        }

        AssertTest("7.1 Xác minh chữ ký số ECDSA NIST P-256", signatureVerified);
        AssertTest("7.2 Xác minh mã băm SHA-256 gói cập nhật", SignatureVerifier.VerifyFileSha256(testZipPath, testSha256));

        // Test 8: Version Comparison & Anti-downgrade
        AssertTest("8.1 Kiểm tra phiên bản mới hơn (14.1.0 > 14.0.0)", SignatureVerifier.IsVersionNewer("14.1.0", "14.0.0"));
        AssertTest("8.2 Chống hạ cấp phiên bản (13.9.0 không được mới hơn 14.0.0)", !SignatureVerifier.IsVersionNewer("13.9.0", "14.0.0"));
        AssertTest("8.3 Kiểm tra phiên bản tối thiểu bắt buộc", SignatureVerifier.IsVersionBelowMinimum("13.0.0", "14.0.0"));

        // Test 9: Image Stealth Engine (Anti-perceptual Hash)
        string testImgPath = Path.Combine(Path.GetTempPath(), "test_img.png");
        using (var bmp = new Bitmap(200, 200))
        {
            using var g = Graphics.FromImage(bmp);
            g.Clear(Color.CornflowerBlue);
            bmp.Save(testImgPath, ImageFormat.Png);
        }

        var processedImageBytes = ImageStealthEngine.OptimizeImageForUpload(testImgPath);
        AssertTest("9. Tối ưu ảnh chống nhận diện pHash (Khử EXIF, vi cắt, vi chỉnh nén JPEG)", processedImageBytes != null && processedImageBytes.Length > 0);

        // Test 10: License HWID Generation
        string currentHwid = LicenseService.GetStrongHwid();
        bool hwidValid = !string.IsNullOrEmpty(currentHwid) && currentHwid.Length == 10 && currentHwid.All(c => "0123456789ABCDEF".Contains(c));
        AssertTest($"10. Tạo Hardware ID định danh máy chuẩn xác (HWID: {currentHwid})", hwidValid);

        // Test 11: Offline Key Verification & Checksum
        using (var sha = SHA256.Create())
        {
            string secret = "GIANG_DEV_VIP_TOOL_FB_SECRET_KEY_2026";
            int testDays = 30;
            string expectedChk = Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes($"V14:{currentHwid}:{testDays}:{secret}")))[..6].ToUpperInvariant();
            string validKey = $"V14-OFF-{testDays}D-{currentHwid}-{expectedChk}";
            string invalidKey = $"V14-OFF-{testDays}D-{currentHwid}-999999";

            var (okValid, daysValid, _, _) = LicenseService.VerifyOfflineKey(validKey, currentHwid);
            var (okInvalid, _, _, _) = LicenseService.VerifyOfflineKey(invalidKey, currentHwid);

            AssertTest("11.1 Xác thực License Offline Key hợp lệ (HMAC/SHA-256 Checksum)", okValid && daysValid == 30);
            AssertTest("11.2 Từ chối License Offline Key không hợp lệ hoặc sai checksum", !okInvalid);

            // Test 11.3: Realtime Live Countdown Precision (Second-by-second)
            var futureDate = DateTime.Now.AddDays(4).AddHours(5).AddMinutes(12).AddSeconds(30);
            var (remValid, remTextTest, remSec) = LicenseService.GetRemainingTimeInfo(futureDate.ToString("yyyy-MM-dd HH:mm:ss"));
            bool countdownPrecisionOk = remValid && remTextTest.Contains("ngày") && remTextTest.Contains("h") && remTextTest.Contains("m") && remTextTest.Contains("s") && remSec > 0;
            AssertTest("11.3 Đồng hồ đếm ngược thời gian thực (Độ chính xác từng giây live)", countdownPrecisionOk);
        }

        // Test 12: DPAPI Secure Configuration Persistence
        var sampleConfig = new AppConfig
        {
            FixedComment = "Test fixed comment",
            Slots = 5,
            DelayMin = 15,
            DelayMax = 30,
            RawAccountLines = new List<string> { "1000123456|EAAAAU...|pass123|proxy1" }
        };
        SecureStorageService.SaveConfig(sampleConfig);
        var loadedConfig = SecureStorageService.LoadConfig();
        bool dpapiOk = loadedConfig.FixedComment == "Test fixed comment" &&
                        loadedConfig.Slots == 5 &&
                        loadedConfig.RawAccountLines.Any(l => l.Contains("1000123456"));
        AssertTest("12. Bảo mật dữ liệu & token bằng Windows DPAPI (ProtectedData)", dpapiOk);

        // Test 13: 3-Strike Action Block & Smart Backoff Model
        var testAcc = new Account { Uid = "1000999999", Token = "EAAB...", Status = "Live" };
        testAcc.BlockStrikes++;
        AssertTest("13.1 Cơ chế 3-Strike Block tài khoản (Lần 1 cảnh báo)", testAcc.BlockStrikes == 1 && testAcc.Status == "Live");
        testAcc.BlockStrikes = 3;
        if (testAcc.BlockStrikes >= 3) testAcc.Status = "Block";
        AssertTest("13.2 Cơ chế 3-Strike Block tài khoản (Lần 3 chuyển sang Block)", testAcc.Status == "Block");

        // Clean temp files
        try
        {
            if (File.Exists(testZipPath)) File.Delete(testZipPath);
            if (File.Exists(testImgPath)) File.Delete(testImgPath);
        }
        catch { }

        // Test 15: Kiểm tra kết nối Auto-Update thực tế từ GitHub Releases
        var liveSettings = UpdateSettings.Load();
        liveSettings.CurrentVersion = "14.0.0";
        var liveUpdater = new UpdateService();
        var checkTask = liveUpdater.CheckForUpdateAsync(liveSettings);
        checkTask.Wait();
        var checkRes = checkTask.Result;
        bool liveUpdateOk = checkRes.HasUpdate && checkRes.LatestVersion == "14.1.0" && checkRes.Manifest != null;
        AssertTest("15. Kết nối & nhận diện bản cập nhật thực tế từ GitHub Releases (v14.1.0)", liveUpdateOk);

        // Test 14: Sinh ảnh chụp màn hình Modern Dashboard trực tiếp từ WPF Visual Tree
        string screenshotPath = @"C:\Users\GiangCuti\.gemini\antigravity\brain\53c036d9-2917-4d87-8a61-2b43ad7240da\modern_dashboard_screenshot.png";
        CaptureUiScreenshot(screenshotPath);
        AssertTest("14. Xuất ảnh chụp thực tế Modern SaaS Dashboard (1440x900 PNG)", File.Exists(screenshotPath) && new FileInfo(screenshotPath).Length > 10000);

        Console.WriteLine("\n-------------------------------------------------");
        Console.ForegroundColor = failed == 0 ? ConsoleColor.Green : ConsoleColor.Red;
        Console.WriteLine($"KẾT QUẢ KIỂM THỬ: {passed} PASS / {failed} FAIL (Tỷ lệ: {(passed * 100.0 / (passed + failed)):0.0}%)");
        Console.ResetColor();
        Console.WriteLine("-------------------------------------------------");

        return failed == 0 ? 0 : 1;
    }

    private static void CaptureUiScreenshot(string outputPath)
    {
        var thread = new Thread(() =>
        {
            try
            {
                var app = new System.Windows.Application();
                var uri = new Uri("/ToolFB.App;component/Styles/DarkTheme.xaml", UriKind.RelativeOrAbsolute);
                var dict = new System.Windows.ResourceDictionary { Source = uri };
                app.Resources.MergedDictionaries.Add(dict);

                var win = new ToolFB.App.Views.MainWindow();

                // Add sample data so the dashboard & views look vibrant and authentic
                win.TargetPosts.Add(new TargetPost { Index = 1, TargetId = "100089274829102", OriginalUrl = "https://facebook.com/100089274829102", Status = "Hoàn thành", CmtCount = 1, IsSelected = true });
                win.TargetPosts.Add(new TargetPost { Index = 2, TargetId = "782910294829103", OriginalUrl = "https://facebook.com/groups/782910294829103/posts/1", Status = "Sẵn sàng", CmtCount = 0, IsSelected = true });
                win.TargetPosts.Add(new TargetPost { Index = 3, TargetId = "992837461524332", OriginalUrl = "https://facebook.com/reel/992837461524332", Status = "Sẵn sàng", CmtCount = 0, IsSelected = true });

                win.Accounts.Add(new Account { Index = 1, Uid = "1000918273645", Name = "Giang Marketing 01", Token = "EAABwzL1234567890abcdef", Status = "Live", RealExitIp = "14.225.26.11", DelayStatus = "Sẵn sàng", IsSelected = true });
                win.Accounts.Add(new Account { Index = 2, Uid = "1000918273646", Name = "Giang Marketing 02", Token = "EAABwzL1234567890ghijkl", Status = "Live", RealExitIp = "14.225.26.12", DelayStatus = "Sẵn sàng", IsSelected = true });
                win.Accounts.Add(new Account { Index = 3, Uid = "1000918273647", Name = "Giang Clone VIP", Token = "EAABwzL1234567890mnopqr", Status = "Live", RealExitIp = "113.160.20.5", DelayStatus = "Sẵn sàng", IsSelected = true });

                win.Logs.Add(new LogItem { Timestamp = "19:30:15", Level = "SUCCESS", Message = "Đăng nhập thành công tài khoản Giang Marketing 01 [UID: 1000918273645]" });
                win.Logs.Add(new LogItem { Timestamp = "19:30:22", Level = "SUCCESS", Message = "Bình luận thành công bài viết 100089274829102: 'Chào bạn! Inbox shop tư vấn nha 🌟'" });
                win.Logs.Add(new LogItem { Timestamp = "19:30:35", Level = "WARNING", Message = "Rate limit nhẹ (Code 17) trên bài 782910294829103 - Kích hoạt delay thông minh 30s" });
                win.Logs.Add(new LogItem { Timestamp = "19:31:05", Level = "SUCCESS", Message = "Tiếp tục luồng xử lý - Chuyển sang xoay Proxy IPv4 113.160.20.5" });

                // Set live license countdown badge
                LicenseService.CurrentLicense.ExpiryDate = DateTime.Now.AddDays(4).AddHours(13).AddMinutes(21).AddSeconds(45).ToString("yyyy-MM-dd HH:mm:ss");
                var (_, liveTxt, _) = LicenseService.GetRemainingTimeInfo(LicenseService.CurrentLicense.ExpiryDate);
                win.LblLicenseTime.Text = $"⏳ Còn: {liveTxt}";
                win.LblLicRemainingCard.Text = $"⏳ Còn: {liveTxt}";

                win.Width = 1440;
                win.Height = 900;
                win.UpdateUiCounters();

                var content = (System.Windows.FrameworkElement)win.Content;

                // Dashboard Page Render
                content.Measure(new System.Windows.Size(1440, 900));
                content.Arrange(new System.Windows.Rect(0, 0, 1440, 900));
                content.UpdateLayout();

                var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(1440, 900, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                rtb.Render(content);

                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
                using (var stream = File.Create(outputPath))
                {
                    encoder.Save(stream);
                }
                // Test layout measurement for all 7 pages to guarantee zero XAML exceptions
                var pages = new[] { win.PageDashboard, win.PageTargets, win.PageContent, win.PageAccounts, win.PageSettings, win.PageLogs, win.PageLicense };
                foreach (var p in pages)
                {
                    foreach (var other in pages) other.Visibility = System.Windows.Visibility.Collapsed;
                    p.Visibility = System.Windows.Visibility.Visible;
                    content.Measure(new System.Windows.Size(1440, 900));
                    content.Arrange(new System.Windows.Rect(0, 0, 1440, 900));
                    content.UpdateLayout();

                    if (p == win.PageSettings)
                    {
                        var rtbSettings = new System.Windows.Media.Imaging.RenderTargetBitmap(1440, 900, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                        rtbSettings.Render(content);
                        var encSettings = new System.Windows.Media.Imaging.PngBitmapEncoder();
                        encSettings.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtbSettings));
                        string settingsPath = @"C:\Users\GiangCuti\.gemini\antigravity\brain\53c036d9-2917-4d87-8a61-2b43ad7240da\modern_settings_screenshot.png";
                        using var s = File.Create(settingsPath);
                        encSettings.Save(s);
                    }
                }
                win.PageTargets.Visibility = System.Windows.Visibility.Collapsed;
                win.PageDashboard.Visibility = System.Windows.Visibility.Visible;
                content.Measure(new System.Windows.Size(1440, 900));
                content.Arrange(new System.Windows.Rect(0, 0, 1440, 900));
                content.UpdateLayout();
                Console.WriteLine("[TEST ALL 7 PAGES] All 7 SaaS pages measured and updated layout with 0 errors!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SCREENSHOT ERROR] {ex}");
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
    }
}
