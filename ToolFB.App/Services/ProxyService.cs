using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ToolFB.App.Services;

public static class ProxyService
{
    private static readonly ConcurrentDictionary<string, string> IpCache = new();

    public static WebProxy? ParseProxy(string? rawProxy)
    {
        if (string.IsNullOrWhiteSpace(rawProxy))
            return null;

        var raw = rawProxy.Trim();
        var partsTab = Regex.Split(raw, @"[\t\s]+");
        if (partsTab.Length > 0 && !string.IsNullOrWhiteSpace(partsTab[0]))
            raw = partsTab[0].Trim();

        string protocol = "http";
        string[] prefixes = { "socks5h://", "socks5://", "socks4://", "http://", "https://" };
        foreach (var p in prefixes)
        {
            if (raw.StartsWith(p, StringComparison.OrdinalIgnoreCase))
            {
                protocol = p.Replace("://", "").ToLowerInvariant();
                raw = raw[p.Length..];
                break;
            }
        }

        // SocketsHttpHandler supports socks5:// with authentication
        if (protocol == "socks5h")
            protocol = "socks5";

        var parts = raw.Split(':');
        try
        {
            if (parts.Length == 2)
            {
                var ip = parts[0].Trim();
                var port = int.Parse(parts[1].Trim());
                return new WebProxy($"{protocol}://{ip}:{port}");
            }
            if (parts.Length == 4)
            {
                string ip, port, user, pwd;
                if (parts[0].Contains('.') && int.TryParse(parts[1].Trim(), out _))
                {
                    ip = parts[0].Trim();
                    port = parts[1].Trim();
                    user = parts[2].Trim();
                    pwd = parts[3].Trim();
                }
                else
                {
                    user = parts[0].Trim();
                    pwd = parts[1].Trim();
                    ip = parts[2].Trim();
                    port = parts[3].Trim();
                }

                var wp = new WebProxy($"{protocol}://{ip}:{port}")
                {
                    Credentials = new NetworkCredential(user, pwd)
                };
                return wp;
            }
            if (raw.Contains('@'))
            {
                return new WebProxy($"{protocol}://{raw}");
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    public static async Task<string> FetchRealExitIpAsync(WebProxy? proxy, bool forceRefresh = false)
    {
        if (proxy == null)
            return "Direct (Không proxy)";

        string key = proxy.Address?.ToString() ?? "unknown";
        if (!forceRefresh && IpCache.TryGetValue(key, out var cachedIp))
            return cachedIp;

        using var handler = new SocketsHttpHandler
        {
            Proxy = proxy,
            UseProxy = true,
            ConnectTimeout = TimeSpan.FromSeconds(6)
        };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(8) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0");

        // Try 1: ipwho.is (IPv4 + IPv6 + City/Country)
        try
        {
            var res = await client.GetStringAsync("https://ipwho.is/");
            using var doc = JsonDocument.Parse(res);
            var root = doc.RootElement;
            if (root.TryGetProperty("success", out var succ) && succ.GetBoolean())
            {
                var ip = root.GetProperty("ip").GetString() ?? "-";
                var city = root.TryGetProperty("city", out var c) ? c.GetString() : "";
                var cc = root.TryGetProperty("country_code", out var code) ? code.GetString() : "";
                var loc = !string.IsNullOrEmpty(city) ? $" ({city})" : (!string.IsNullOrEmpty(cc) ? $" ({cc})" : "");
                var full = $"{ip}{loc}";
                IpCache[key] = full;
                return full;
            }
        }
        catch { }

        // Try 2: ip-api.com
        try
        {
            var res = await client.GetStringAsync("http://ip-api.com/json/?fields=status,query,city,countryCode");
            using var doc = JsonDocument.Parse(res);
            var root = doc.RootElement;
            if (root.TryGetProperty("status", out var s) && s.GetString() == "success")
            {
                var ip = root.GetProperty("query").GetString() ?? "-";
                var city = root.TryGetProperty("city", out var c) ? c.GetString() : "";
                var loc = !string.IsNullOrEmpty(city) ? $" ({city})" : "";
                var full = $"{ip}{loc}";
                IpCache[key] = full;
                return full;
            }
        }
        catch { }

        // Try 3: api64.ipify.org
        try
        {
            var res = await client.GetStringAsync("https://api64.ipify.org?format=json");
            using var doc = JsonDocument.Parse(res);
            if (doc.RootElement.TryGetProperty("ip", out var ipElem))
            {
                var ip = ipElem.GetString() ?? "Không kết nối";
                IpCache[key] = ip;
                return ip;
            }
        }
        catch { }

        return "Không kết nối";
    }
}
