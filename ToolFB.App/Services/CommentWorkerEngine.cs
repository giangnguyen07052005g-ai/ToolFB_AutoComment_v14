using System.Collections.Concurrent;
using System.IO;
using System.Text;
using ToolFB.App.Models;

namespace ToolFB.App.Services;

public class LogEventArgs : EventArgs
{
    public string Message { get; set; } = string.Empty;
    public string Level { get; set; } = "info"; // info, success, warning, error
    public DateTime Time { get; set; } = DateTime.Now;
}

public class CommentWorkerEngine
{
    private readonly FacebookGraphService _fbService = new();
    private readonly object _lock = new();

    private CancellationTokenSource? _cts;
    private ManualResetEventSlim _pauseEvent = new(true);

    public bool IsRunning { get; private set; }
    public bool IsPaused => !_pauseEvent.IsSet;

    public int TotalSuccess { get; private set; }
    public int TotalFail { get; private set; }
    public int TotalSavedLinks { get; private set; }

    public event EventHandler<LogEventArgs>? LogReceived;
    public event EventHandler? StatsChanged;

    private void EmitLog(string message, string level = "info")
    {
        LogReceived?.Invoke(this, new LogEventArgs { Message = message, Level = level });
    }

    public void Pause()
    {
        if (IsRunning && !IsPaused)
        {
            _pauseEvent.Reset();
            EmitLog("Đã tạm dừng tiến trình", "warning");
        }
    }

    public void Resume()
    {
        if (IsRunning && IsPaused)
        {
            _pauseEvent.Set();
            EmitLog("Tiếp tục tiến trình", "info");
        }
    }

    public void Stop()
    {
        if (IsRunning)
        {
            _cts?.Cancel();
            _pauseEvent.Set();
            IsRunning = false;
            EmitLog("Đã dừng tiến trình chạy comment", "warning");
            StatsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public async Task StartAsync(
        List<TargetPost> targetPosts,
        List<Account> accounts,
        AppConfig config,
        List<string> fixedComments,
        List<string> randomComments,
        List<string>? backupProxies = null)
    {
        if (IsRunning) return;

        IsRunning = true;
        _pauseEvent.Set();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        TotalSuccess = 0;
        TotalFail = 0;
        StatsChanged?.Invoke(this, EventArgs.Empty);

        EmitLog("=== BẮT ĐẦU CHIẾN DỊCH AUTO COMMENT FACEBOOK PRO ===", "info");

        var postTracker = new ConcurrentDictionary<string, int>();
        var postReserved = new ConcurrentDictionary<string, int>();
        var skippedPosts = new ConcurrentDictionary<string, bool>();
        var postFailCount = new ConcurrentDictionary<string, int>();
        var postBlockCount = new ConcurrentDictionary<string, int>();

        foreach (var p in targetPosts)
        {
            postTracker[p.TargetId] = p.CmtCount;
            postReserved[p.TargetId] = p.CmtCount;
        }

        // Active accounts
        var liveAccs = accounts.Where(a => a.IsSelected && a.Status == "Live").ToList();
        if (liveAccs.Count == 0)
        {
            EmitLog("Không có tài khoản Live nào được chọn để chạy!", "error");
            IsRunning = false;
            return;
        }

        var targets = targetPosts.Where(p => p.IsSelected).ToList();
        if (targets.Count == 0)
        {
            EmitLog("Không có bài viết mục tiêu nào được chọn!", "error");
            IsRunning = false;
            return;
        }

        foreach (var a in liveAccs)
        {
            a.InUse = false;
            a.NextReadyTime = 0;
            a.SessionCmtCount = 0;
            a.BlockStrikes = 0;
            a.RateLimitCount = 0;
            a.BlockedTargets.Clear();
        }

        int randCommentIndex = 0;
        int activeSlots = Math.Max(1, Math.Min(config.Slots, liveAccs.Count));
        var workerTasks = new List<Task>();

        for (int i = 0; i < activeSlots; i++)
        {
            workerTasks.Add(Task.Run(async () =>
            {
                while (!ct.IsCancellationRequested && IsRunning)
                {
                    _pauseEvent.Wait(ct);
                    if (ct.IsCancellationRequested) break;

                    string? tid = null;
                    Account? cur = null;
                    string rawMsg = string.Empty;
                    bool wait = false;
                    double sleepDuration = 0.5;

                    lock (_lock)
                    {
                        var unfinished = targets.Where(t => !skippedPosts.ContainsKey(t.TargetId) && postTracker.GetValueOrDefault(t.TargetId, 0) < config.MaxCommentsPerPost).ToList();
                        if (unfinished.Count == 0)
                        {
                            break; // All done
                        }

                        var availableTargets = targets.Where(t => !skippedPosts.ContainsKey(t.TargetId) && postReserved.GetValueOrDefault(t.TargetId, 0) < config.MaxCommentsPerPost).ToList();
                        if (availableTargets.Count == 0)
                        {
                            wait = true;
                        }
                        else
                        {
                            double nowSec = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
                            var availableAccs = liveAccs.Where(a => a.Status == "Live" && a.CmtCount < config.MaxCommentsPerToken).ToList();
                            if (availableAccs.Count == 0)
                            {
                                break; // No accounts left
                            }

                            var pool = config.TrueRoundRobin ? availableAccs : availableAccs.Take(config.Slots).ToList();
                            var readyAccs = pool.Where(a => !a.InUse && nowSec >= a.NextReadyTime).ToList();

                            if (readyAccs.Count == 0)
                            {
                                wait = true;
                                var idleAccs = pool.Where(a => !a.InUse).ToList();
                                sleepDuration = idleAccs.Count > 0
                                    ? Math.Max(0.5, idleAccs.Min(a => Math.Max(0.1, a.NextReadyTime - nowSec)))
                                    : 1.0;
                            }
                            else
                            {
                                // True Round-Robin Load Balancer (Least cmt count, then longest idle time)
                                cur = config.TrueRoundRobin
                                    ? readyAccs.OrderBy(a => a.CmtCount).ThenBy(a => a.LastCommentTime).First()
                                    : readyAccs.OrderBy(a => a.CmtCount).First();

                                var candidateTargets = availableTargets.Where(t => !cur.BlockedTargets.Contains(t.TargetId)).ToList();
                                if (candidateTargets.Count == 0) candidateTargets = availableTargets;

                                var chosenTarget = candidateTargets.OrderBy(t => postReserved.GetValueOrDefault(t.TargetId, 0)).First();
                                tid = chosenTarget.TargetId;

                                postReserved.AddOrUpdate(tid, 1, (_, v) => v + 1);
                                cur.InUse = true;

                                string rnd = randomComments.Count > 0 ? randomComments[randCommentIndex % randomComments.Count] : "";
                                if (randomComments.Count > 0) randCommentIndex++;

                                string fc = fixedComments.Count > 0 ? fixedComments[0] : "";
                                rawMsg = config.OrderMode == "Random first"
                                    ? (!string.IsNullOrEmpty(rnd) && !string.IsNullOrEmpty(fc) ? $"{rnd}\n{fc}" : (rnd + fc))
                                    : (!string.IsNullOrEmpty(fc) && !string.IsNullOrEmpty(rnd) ? $"{fc}\n{rnd}" : (fc + rnd));
                            }
                        }
                    }

                    if (wait)
                    {
                        try { await Task.Delay(TimeSpan.FromSeconds(Math.Min(sleepDuration, 2.0)), ct); }
                        catch { break; }
                        continue;
                    }

                    if (tid == null || cur == null) continue;

                    // 1. Auto Warm-up
                    if (config.AutoWarmup)
                    {
                        await _fbService.WarmupAccountAsync(cur);
                    }

                    // 2. Spintax & Dynamic Tags & Salt
                    string finalMsg = SpintaxEngine.ApplyDynamicTagsAndSalt(rawMsg, config.UseMicroSalt, config.IconCategory, config.IconNewline);

                    // 3. Human reading delay simulation
                    await Task.Delay(Random.Shared.Next(2000, 4000), ct);

                    // 4. Send reaction if enabled
                    if (config.SendReactions && !cur.ReactionsUnsupported)
                    {
                        var (rOk, rMsg) = await _fbService.SendReactionAsync(cur, tid, config.ReactionType);
                        if (!rOk)
                        {
                            if (rMsg.Contains(ApiErrorClassification.TokenInvalid))
                            {
                                lock (_lock)
                                {
                                    postReserved.AddOrUpdate(tid, 0, (_, v) => Math.Max(0, v - 1));
                                    cur.Status = "Die";
                                    cur.Note = rMsg;
                                    cur.InUse = false;
                                }
                                EmitLog($"[{cur.Name}] Token hỏng khi reaction (Die)", "error");
                                continue;
                            }
                            else
                            {
                                cur.ReactionsUnsupported = true;
                                EmitLog($"[{cur.Name}] Bài viết không hỗ trợ reaction -> Đã tự tắt reaction để bảo vệ nick", "info");
                            }
                        }
                        else
                        {
                            await Task.Delay(Random.Shared.Next(1500, 3000), ct);
                        }
                    }

                    // 5. Anti-pHash image preparation
                    byte[]? imgBytes = null;
                    if (!string.IsNullOrEmpty(config.SelectedImagePath) && File.Exists(config.SelectedImagePath))
                    {
                        imgBytes = ImageStealthEngine.OptimizeImageForUpload(config.SelectedImagePath);
                    }

                    string? attId = null;
                    if (imgBytes != null && imgBytes.Length > 0)
                    {
                        attId = await _fbService.UploadPhotoAsync(cur, imgBytes);
                    }

                    // 6. Post Comment
                    var (succ, cmtIdOrError) = await _fbService.PostCommentAsync(cur, tid, finalMsg, attId);

                    // 7. Calculate Delay + Smart Rest Cycle
                    double baseDelay = config.DelayMin + (Random.Shared.NextDouble() * (config.DelayMax - config.DelayMin));
                    if (config.TrueRoundRobin)
                        baseDelay += Random.Shared.Next(2, 6);

                    if (succ)
                    {
                        cur.SessionCmtCount++;
                        if (cur.SessionCmtCount >= config.SmartRestAfter)
                        {
                            baseDelay += config.SmartRestDuration;
                            cur.SessionCmtCount = 0;
                            EmitLog($"[{cur.Name}] Đạt {config.SmartRestAfter} cmt liên tiếp → Nghỉ sâu {config.SmartRestDuration}s", "info");
                        }
                    }

                    // Jitter +-20%
                    double rdl = Math.Round(Math.Max(3.0, baseDelay * (0.85 + Random.Shared.NextDouble() * 0.4)), 1);

                    double now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
                    lock (_lock)
                    {
                        cur.InUse = false;
                        cur.NextReadyTime = now + rdl;
                        cur.LastCommentTime = now;

                        if (succ)
                        {
                            TotalSuccess++;
                            postTracker.AddOrUpdate(tid, 1, (_, v) => v + 1);
                            cur.CmtCount++;
                            cur.RateLimitCount = 0;
                            cur.BlockStrikes = 0;
                            postFailCount[tid] = 0;
                            postBlockCount[tid] = 0;

                            var targetObj = targets.FirstOrDefault(t => t.TargetId == tid);
                            if (targetObj != null)
                            {
                                targetObj.CmtCount = postTracker[tid];
                                targetObj.Status = targetObj.CmtCount >= config.MaxCommentsPerPost ? "Hoàn thành" : "Đang chạy";
                            }

                            string fullLink = FacebookGraphService.MakeFullCommentLink(tid, cmtIdOrError);
                            if (config.AutoSaveCommentLinks)
                            {
                                SaveLinkToTxt(fullLink, config.CommentLinksFilePath);
                                TotalSavedLinks++;
                            }
                            if (config.AutoSaveCommentedPosts)
                            {
                                SaveCommentedPost(tid);
                            }
                            if (config.AutoExportCsvReport)
                            {
                                RecordToCsv(tid, cur.Token, cur.Name, fullLink, "Thành công");
                            }

                            EmitLog($"[{cur.Name}] → CMT thành công bài {tid} (IP Thật: {cur.RealExitIp}) | Link: {fullLink}", "success");
                            if (config.AutoSaveCommentLinks)
                            {
                                string outName = Path.GetFileName(string.IsNullOrWhiteSpace(config.CommentLinksFilePath) ? "comment_links.txt" : config.CommentLinksFilePath);
                                EmitLog($"💾 Đã tự động lưu link cmt vào file {outName} (Tổng đã lưu: {TotalSavedLinks} link)", "info");
                            }
                        }
                        else
                        {
                            TotalFail++;
                            postReserved.AddOrUpdate(tid, 0, (_, v) => Math.Max(0, v - 1));
                            RecordToCsv(tid, cur.Token, cur.Name, "-", $"Lỗi: {cmtIdOrError}");

                            if (cmtIdOrError.Contains(ApiErrorClassification.TokenInvalid))
                            {
                                cur.Status = "Die";
                                cur.Note = cmtIdOrError;
                                EmitLog($"[{cur.Name}] → Token hỏng / Checkpoint (Die)", "error");
                            }
                            else if (cmtIdOrError.Contains(ApiErrorClassification.ActionBlocked))
                            {
                                cur.BlockStrikes++;
                                cur.BlockedTargets.Add(tid);
                                int blockCount = postBlockCount.AddOrUpdate(tid, 1, (_, v) => v + 1);

                                if (blockCount >= 2 && skippedPosts.TryAdd(tid, true))
                                {
                                    var targetObj = targets.FirstOrDefault(t => t.TargetId == tid);
                                    if (targetObj != null) targetObj.Status = "Bị chặn cmt";
                                    EmitLog($"Bài viết {tid} bị FB chặn cmt (Error 368) 2 lần → Tự động bỏ qua bài này để bảo vệ dàn nick!", "warning");
                                }

                                if (cur.BlockStrikes < 3)
                                {
                                    double cooloff = Math.Round(60.0 + Random.Shared.NextDouble() * 60.0, 1);
                                    cur.NextReadyTime = now + cooloff;
                                    cur.Note = $"Tạm nghỉ {cooloff:0}s (Hạn chế bài, thử lại {cur.BlockStrikes}/3)";
                                    EmitLog($"[{cur.Name}] Gặp hạn chế cmt ở bài {tid} (lần {cur.BlockStrikes}/3) → Tự nghỉ {cooloff:0}s & tự đổi bài khác (Không dừng token)", "warning");
                                }
                                else
                                {
                                    cur.Status = "Blocked";
                                    cur.Note = cmtIdOrError;
                                    EmitLog($"[{cur.Name}] Đã bị hạn chế 3 bài liên tiếp → Tạm dừng token để chống checkpoint (Blocked)", "error");
                                }
                            }
                            else if (cmtIdOrError.Contains(ApiErrorClassification.RateLimit))
                            {
                                cur.RateLimitCount++;
                                double cooloff = Math.Min(600, 120 * Math.Pow(2, cur.RateLimitCount - 1));
                                cur.NextReadyTime = now + cooloff;
                                cur.Note = $"Rate Limit (Nghỉ {cooloff:0}s)";
                                EmitLog($"[{cur.Name}] → Facebook giới hạn tốc độ (Rate Limit) -> Tự nghỉ {cooloff:0}s", "warning");
                            }
                            else if (cmtIdOrError.Contains(ApiErrorClassification.TargetUnavailable))
                            {
                                if (skippedPosts.TryAdd(tid, true))
                                {
                                    var targetObj = targets.FirstOrDefault(t => t.TargetId == tid);
                                    if (targetObj != null)
                                    {
                                        targetObj.Status = "Không thể cmt";
                                        targetObj.ErrorDetail = "Bị khóa cmt hoặc nhóm kín";
                                    }
                                    EmitLog($"Bài viết {tid} bị khóa cmt hoặc nhóm kín → Đã bỏ qua", "warning");
                                }
                            }
                            else
                            {
                                cur.BlockedTargets.Add(tid);
                                int fails = postFailCount.AddOrUpdate(tid, 1, (_, v) => v + 1);
                                EmitLog($"[{cur.Name}] → Lỗi bài {tid}: {cmtIdOrError}", "warning");

                                if (fails >= 5 && skippedPosts.TryAdd(tid, true))
                                {
                                    var targetObj = targets.FirstOrDefault(t => t.TargetId == tid);
                                    if (targetObj != null)
                                    {
                                        targetObj.Status = "Lỗi/Bỏ qua";
                                        targetObj.ErrorDetail = cmtIdOrError;
                                    }
                                    EmitLog($"Bài viết {tid} lỗi quá 5 lần → Bỏ qua", "warning");
                                }
                            }

                            // Tự xoay proxy khi lỗi mạng
                            bool isNetErr = cmtIdOrError.Contains("connection", StringComparison.OrdinalIgnoreCase) ||
                                            cmtIdOrError.Contains("proxy", StringComparison.OrdinalIgnoreCase) ||
                                            cmtIdOrError.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
                                            cmtIdOrError.Contains("network", StringComparison.OrdinalIgnoreCase) ||
                                            cmtIdOrError.Contains("reset", StringComparison.OrdinalIgnoreCase) ||
                                            cmtIdOrError.Contains("offline", StringComparison.OrdinalIgnoreCase);

                            if (config.AutoRotate && isNetErr && backupProxies is { Count: > 0 })
                            {
                                RotateProxyForAccount(cur, backupProxies);
                            }
                        }
                    }

                    StatsChanged?.Invoke(this, EventArgs.Empty);

                    // Thread pacing delay: sleep until rdl elapsed
                    double sleepEnd = now + rdl;
                    while (IsRunning && (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0) < sleepEnd)
                    {
                        if (IsPaused)
                        {
                            _pauseEvent.Wait(500);
                            continue;
                        }
                        await Task.Delay(100, ct);
                    }
                }
            }, ct));
        }

        try
        {
            await Task.WhenAll(workerTasks);
        }
        catch (OperationCanceledException) { }
        finally
        {
            IsRunning = false;
            EmitLog("=== CHIẾN DỊCH KẾT THÚC HOẶC ĐÃ DỪNG ===", "info");
            StatsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void RotateProxyForAccount(Account acc, List<string> proxyList)
    {
        var candidates = proxyList.Where(p => !p.Equals(acc.ProxyStr, StringComparison.OrdinalIgnoreCase)).ToList();
        string newProxy = candidates.Count > 0
            ? candidates[Random.Shared.Next(candidates.Count)]
            : proxyList[Random.Shared.Next(proxyList.Count)];

        var parsed = ProxyService.ParseProxy(newProxy);
        if (parsed != null)
        {
            acc.ProxyStr = newProxy;
            acc.ParsedProxy = parsed;
            acc.RealExitIp = "-";
            _ = Task.Run(async () =>
            {
                var ip = await ProxyService.FetchRealExitIpAsync(parsed, forceRefresh: true);
                acc.RealExitIp = ip;
            });
            EmitLog($"[{acc.Name}] Xoay IP thành công -> {newProxy}", "warning");
        }
    }

    private static readonly object _fileLock = new();

    public static void SaveLinkToTxt(string link, string? customPath = null)
    {
        if (string.IsNullOrWhiteSpace(link)) return;
        lock (_fileLock)
        {
            try
            {
                var filename = string.IsNullOrWhiteSpace(customPath) ? "comment_links.txt" : customPath.Trim();
                var path = Path.IsPathRooted(filename)
                    ? filename
                    : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, filename);

                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.AppendAllText(path, $"{link.Trim()}{Environment.NewLine}", Encoding.UTF8);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SaveLinkToTxt] Error: {ex.Message}");
            }
        }
    }

    public static void SaveCommentedPost(string postId)
    {
        if (string.IsNullOrWhiteSpace(postId)) return;
        lock (_fileLock)
        {
            try
            {
                var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "commented_posts.txt");
                File.AppendAllText(path, $"{postId.Trim()}{Environment.NewLine}", Encoding.UTF8);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SaveCommentedPost] Error: {ex.Message}");
            }
        }
    }

    public static void RecordToCsv(string targetId, string token, string name, string link, string status)
    {
        lock (_fileLock)
        {
            try
            {
                var filename = $"report_comment_{DateTime.Now:yyyy_MM_dd}.csv";
                var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, filename);
                bool needHeader = !File.Exists(path);

                using var sw = new StreamWriter(path, true, Encoding.UTF8);
                if (needHeader)
                {
                    sw.WriteLine("Thời Gian,Target ID,Token,Tên Tài Khoản,Link Comment,Trạng Thái");
                }
                string timeStr = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                string safeToken = token.Length > 8 ? $"{token[..4]}...{token[^4..]}" : token;
                sw.WriteLine($"\"{timeStr}\",\"{targetId}\",\"{safeToken}\",\"{name}\",\"{link}\",\"{status.Replace("\"", "\"\"")}\"");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RecordToCsv] Error: {ex.Message}");
            }
        }
    }
}
