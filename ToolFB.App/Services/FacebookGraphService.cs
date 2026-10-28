using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using ToolFB.App.Models;

namespace ToolFB.App.Services;

public class FacebookGraphService
{
    public const string GraphApiVersion = "v21.0";
    public const string GraphBaseUrl = $"https://graph.facebook.com/{GraphApiVersion}";

    // Cache of HttpClient per proxy / account to keep TCP connections alive
    private static readonly ConcurrentDictionary<string, HttpClient> ClientCache = new();

    public static string? CleanTargetId(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return null;

        line = line.Trim();
        if (Regex.IsMatch(line, @"^\d+_\d+$") || Regex.IsMatch(line, @"^\d+$"))
            return line;

        var mGrp = Regex.Match(line, @"groups/(\d+)/(?:posts|permalink)/(\d+)");
        if (mGrp.Success)
            return $"{mGrp.Groups[1].Value}_{mGrp.Groups[2].Value}";

        var mStory = Regex.Match(line, @"story_fbid=(\d+).*?[?&]id=(\d+)") ?? Regex.Match(line, @"[?&]id=(\d+).*?story_fbid=(\d+)");
        if (mStory.Success)
            return $"{mStory.Groups[2].Value}_{mStory.Groups[1].Value}";

        var mPair = Regex.Match(line, @"/(\d+)/posts/(\d+)");
        if (mPair.Success)
            return $"{mPair.Groups[1].Value}_{mPair.Groups[2].Value}";

        var mSingle = Regex.Match(line, @"/(?:reel|watch|videos|photos)/(\d+)");
        if (mSingle.Success)
            return mSingle.Groups[1].Value;

        var digits = Regex.Matches(line, @"\d{8,}");
        if (digits.Count >= 2)
            return $"{digits[0].Value}_{digits[1].Value}";
        if (digits.Count == 1)
            return digits[0].Value;

        return line;
    }

    public static string MakeFullCommentLink(string targetId, string cmtId)
    {
        if (string.IsNullOrWhiteSpace(cmtId)) return string.Empty;

        if (cmtId.Contains('_'))
        {
            var parts = cmtId.Split('_');
            if (parts.Length >= 3)
            {
                return $"https://www.facebook.com/{parts[0]}_{parts[1]}?comment_id={parts[^1]}";
            }
            if (parts.Length == 2)
            {
                return $"https://www.facebook.com/{parts[0]}?comment_id={parts[1]}";
            }
        }
        if (!string.IsNullOrWhiteSpace(targetId))
        {
            return $"https://www.facebook.com/{targetId}?comment_id={cmtId.Split('_')[^1]}";
        }
        return $"https://www.facebook.com/{cmtId}";
    }

    public static HttpClient GetOrCreateClient(Account acc)
    {
        string key = acc.ParsedProxy?.Address?.ToString() ?? "DIRECT";
        return ClientCache.GetOrAdd(key, _ =>
        {
            var handler = new SocketsHttpHandler
            {
                Proxy = acc.ParsedProxy,
                UseProxy = acc.ParsedProxy != null,
                PooledConnectionLifetime = TimeSpan.FromMinutes(15),
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
                MaxConnectionsPerServer = 50,
                EnableMultipleHttp2Connections = true,
                SslOptions = new System.Net.Security.SslClientAuthenticationOptions
                {
                    RemoteCertificateValidationCallback = delegate { return true; } // Bypass SSL check for proxies
                }
            };

            var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(45)
            };

            return client;
        });
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, string url, Account acc)
    {
        var req = new HttpRequestMessage(method, url);
        var profile = DeviceProfile.GetStickyProfile(acc.Token);

        req.Headers.UserAgent.Clear();
        req.Headers.TryAddWithoutValidation("User-Agent", profile.UserAgent);
        req.Headers.Accept.Clear();
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));

        if (!string.IsNullOrEmpty(profile.SecChUa))
            req.Headers.TryAddWithoutValidation("sec-ch-ua", profile.SecChUa);
        if (!string.IsNullOrEmpty(profile.SecChUaPlatform))
            req.Headers.TryAddWithoutValidation("sec-ch-ua-platform", profile.SecChUaPlatform);
        if (!string.IsNullOrEmpty(profile.SecChUaMobile))
            req.Headers.TryAddWithoutValidation("sec-ch-ua-mobile", profile.SecChUaMobile);

        return req;
    }

    public async Task<(bool success, string name, string uid, string note)> CheckTokenAsync(Account acc)
    {
        if (string.IsNullOrWhiteSpace(acc.Token))
            return (false, "Không có", "", "Token rỗng");

        var client = GetOrCreateClient(acc);
        string url = $"{GraphBaseUrl}/me?access_token={Uri.EscapeDataString(acc.Token)}&fields=id,name";

        for (int attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                using var req = CreateRequest(HttpMethod.Get, url, acc);
                using var res = await client.SendAsync(req);
                var content = await res.Content.ReadAsStringAsync();

                if (string.IsNullOrWhiteSpace(content))
                    continue;

                using var doc = JsonDocument.Parse(content);
                var root = doc.RootElement;

                if (res.IsSuccessStatusCode && root.TryGetProperty("id", out var idElem))
                {
                    string uid = idElem.GetString() ?? "";
                    string name = root.TryGetProperty("name", out var nElem) ? nElem.GetString() ?? "Facebook User" : "Facebook User";
                    return (true, name, uid, "Hoạt động");
                }

                if (root.TryGetProperty("error", out var errElem))
                {
                    int? code = errElem.TryGetProperty("code", out var cElem) ? cElem.GetInt32() : null;
                    int? subcode = errElem.TryGetProperty("error_subcode", out var scElem) ? scElem.GetInt32() : null;
                    string? type = errElem.TryGetProperty("type", out var tElem) ? tElem.GetString() : null;
                    string? msg = errElem.TryGetProperty("message", out var mElem) ? mElem.GetString() : null;

                    string kind = ApiErrorClassification.Classify(code, type, msg, (int)res.StatusCode, subcode);
                    if (kind == ApiErrorClassification.TokenInvalid)
                        return (false, "Die", "", $"Token hỏng: {msg}");
                    if (kind == ApiErrorClassification.ActionBlocked)
                        return (false, "Blocked", "", $"Bị chặn tính năng: {msg}");

                    return (false, "Error", "", $"{kind}: {msg}");
                }
            }
            catch (Exception ex)
            {
                if (attempt == 1)
                    return (false, "Error", "", $"Lỗi mạng: {ex.Message}");
                await Task.Delay(500);
            }
        }

        return (false, "Error", "", "Không có phản hồi");
    }

    public async Task WarmupAccountAsync(Account acc)
    {
        if (acc.WarmedUp || string.IsNullOrWhiteSpace(acc.Token))
            return;

        var client = GetOrCreateClient(acc);
        try
        {
            // 1. Check profile
            using var req1 = CreateRequest(HttpMethod.Get, $"{GraphBaseUrl}/me?fields=id,name&access_token={Uri.EscapeDataString(acc.Token)}", acc);
            await client.SendAsync(req1);
            await Task.Delay(Random.Shared.Next(1200, 2000));

            // 2. Read feed
            using var req2 = CreateRequest(HttpMethod.Get, $"{GraphBaseUrl}/me/feed?fields=id&limit=3&access_token={Uri.EscapeDataString(acc.Token)}", acc);
            await client.SendAsync(req2);
            await Task.Delay(Random.Shared.Next(1000, 1800));

            acc.WarmedUp = true;
        }
        catch
        {
            acc.WarmedUp = true;
        }
    }

    public async Task<(bool success, string error)> SendReactionAsync(Account acc, string targetId, string reactionType)
    {
        if (acc.ReactionsUnsupported)
            return (false, "REACTION_UNSUPPORTED: Đã tự động tắt reaction cho nick này");

        var client = GetOrCreateClient(acc);
        try
        {
            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("type", reactionType),
                new KeyValuePair<string, string>("access_token", acc.Token)
            });

            using var req = CreateRequest(HttpMethod.Post, $"{GraphBaseUrl}/{targetId}/reactions", acc);
            req.Content = content;

            using var res = await client.SendAsync(req);
            var resStr = await res.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(resStr);
            var root = doc.RootElement;

            if (res.IsSuccessStatusCode && !root.TryGetProperty("error", out _))
                return (true, string.Empty);

            if (root.TryGetProperty("error", out var errElem))
            {
                int? code = errElem.TryGetProperty("code", out var cElem) ? cElem.GetInt32() : null;
                int? subcode = errElem.TryGetProperty("error_subcode", out var scElem) ? scElem.GetInt32() : null;
                string? type = errElem.TryGetProperty("type", out var tElem) ? tElem.GetString() : null;
                string? msg = errElem.TryGetProperty("message", out var mElem) ? mElem.GetString() : null;

                if (code is 3 or 100 or 200 or 368)
                    acc.ReactionsUnsupported = true;

                string kind = ApiErrorClassification.Classify(code, type, msg, (int)res.StatusCode, subcode);
                return (false, $"{kind}: {msg}");
            }

            return (false, $"HTTP_{res.StatusCode}");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public async Task<string?> UploadPhotoAsync(Account acc, byte[] imageBytes)
    {
        if (imageBytes == null || imageBytes.Length == 0)
            return null;

        var client = GetOrCreateClient(acc);
        for (int attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                using var multipart = new MultipartFormDataContent();
                multipart.Add(new StringContent("false"), "published");
                multipart.Add(new StringContent(acc.Token), "access_token");

                var byteContent = new ByteArrayContent(imageBytes);
                byteContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
                multipart.Add(byteContent, "source", "upload.jpg");

                using var req = CreateRequest(HttpMethod.Post, $"{GraphBaseUrl}/me/photos", acc);
                req.Content = multipart;

                using var res = await client.SendAsync(req);
                var str = await res.Content.ReadAsStringAsync();

                using var doc = JsonDocument.Parse(str);
                if (doc.RootElement.TryGetProperty("id", out var idElem))
                    return idElem.GetString();
            }
            catch
            {
                await Task.Delay(1000);
            }
        }
        return null;
    }

    public async Task<(bool success, string commentIdOrError)> PostCommentAsync(Account acc, string targetId, string message, string? attachmentId)
    {
        var client = GetOrCreateClient(acc);
        try
        {
            var kvp = new List<KeyValuePair<string, string>>
            {
                new("message", message),
                new("access_token", acc.Token)
            };
            if (!string.IsNullOrEmpty(attachmentId))
                kvp.Add(new("attachment_id", attachmentId));

            using var req = CreateRequest(HttpMethod.Post, $"{GraphBaseUrl}/{targetId}/comments", acc);
            req.Content = new FormUrlEncodedContent(kvp);

            using var res = await client.SendAsync(req);
            var resStr = await res.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(resStr);
            var root = doc.RootElement;

            if (root.TryGetProperty("id", out var idElem))
                return (true, idElem.GetString() ?? "");

            if (root.TryGetProperty("error", out var errElem))
            {
                int? code = errElem.TryGetProperty("code", out var cElem) ? cElem.GetInt32() : null;
                int? subcode = errElem.TryGetProperty("error_subcode", out var scElem) ? scElem.GetInt32() : null;
                string? type = errElem.TryGetProperty("type", out var tElem) ? tElem.GetString() : null;
                string? msg = errElem.TryGetProperty("message", out var mElem) ? mElem.GetString() : null;

                string kind = ApiErrorClassification.Classify(code, type, msg, (int)res.StatusCode, subcode);
                return (false, $"{kind}: {msg}");
            }

            return (false, $"HTTP_{(int)res.StatusCode}");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
}
