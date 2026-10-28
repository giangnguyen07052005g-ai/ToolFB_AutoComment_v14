namespace ToolFB.App.Models;

public class DeviceProfile
{
    public string UserAgent { get; set; } = string.Empty;
    public string? SecChUa { get; set; }
    public string? SecChUaPlatform { get; set; }
    public string? SecChUaMobile { get; set; }

    public static readonly List<DeviceProfile> Profiles = new()
    {
        new DeviceProfile
        {
            UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/133.0.0.0 Safari/537.36",
            SecChUa = "\"Not(A:Brand\";v=\"99\", \"Google Chrome\";v=\"133\", \"Chromium\";v=\"133\"",
            SecChUaPlatform = "\"Windows\"",
            SecChUaMobile = "?0"
        },
        new DeviceProfile
        {
            UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/132.0.0.0 Safari/537.36 Edg/132.0.0.0",
            SecChUa = "\"Not A(Brand\";v=\"8\", \"Chromium\";v=\"132\", \"Microsoft Edge\";v=\"132\"",
            SecChUaPlatform = "\"Windows\"",
            SecChUaMobile = "?0"
        },
        new DeviceProfile
        {
            UserAgent = "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/133.0.0.0 Safari/537.36",
            SecChUa = "\"Not(A:Brand\";v=\"99\", \"Google Chrome\";v=\"133\", \"Chromium\";v=\"133\"",
            SecChUaPlatform = "\"macOS\"",
            SecChUaMobile = "?0"
        },
        new DeviceProfile
        {
            UserAgent = "Mozilla/5.0 (Linux; Android 14; SM-S928B Build/UP1A.231005.007; wv) AppleWebKit/537.36 (KHTML, like Gecko) Version/4.0 Chrome/131.0.6778.200 Mobile Safari/537.36 [FB_IAB/FB4A;FBAV/495.0.0.41.111;]",
            SecChUaPlatform = "\"Android\"",
            SecChUaMobile = "?1"
        },
        new DeviceProfile
        {
            UserAgent = "Mozilla/5.0 (iPhone; CPU iPhone OS 18_1 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Mobile/22B83 [FBAN/FBIOS;FBDV/iPhone16,2;FBMD/iPhone;FBSN/iOS;FBSV/18.1;FBSS/3;FBCR/Viettel;FBID/phone;FBLC/vi_VN;FBOP/5]",
            SecChUa = null,
            SecChUaPlatform = null,
            SecChUaMobile = null
        }
    };

    public static DeviceProfile GetStickyProfile(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return Profiles[0];

        int sum = 0;
        foreach (char c in token.Trim())
            sum += c;

        return Profiles[sum % Profiles.Count];
    }
}
