using System.ComponentModel;
using System.Net;
using System.Runtime.CompilerServices;

namespace ToolFB.App.Models;

public class Account : INotifyPropertyChanged
{
    private int _index;
    private string _token = string.Empty;
    private string _name = "Chưa kiểm tra";
    private string _uid = string.Empty;
    private string _proxyStr = string.Empty;
    private string _realExitIp = "-";
    private string _status = "Chưa check"; // Live, Die, Blocked, RateLimit
    private string _note = string.Empty;
    private int _cmtCount;
    private bool _isSelected = true;
    private string _delayStatus = "Sẵn sàng";

    public int Index
    {
        get => _index;
        set => SetField(ref _index, value);
    }

    public string Token
    {
        get => _token;
        set => SetField(ref _token, value);
    }

    public string Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }

    public string Uid
    {
        get => _uid;
        set => SetField(ref _uid, value);
    }

    public string ProxyStr
    {
        get => _proxyStr;
        set => SetField(ref _proxyStr, value);
    }

    public string RealExitIp
    {
        get => _realExitIp;
        set => SetField(ref _realExitIp, value);
    }

    public string Status
    {
        get => _status;
        set => SetField(ref _status, value);
    }

    public string Note
    {
        get => _note;
        set => SetField(ref _note, value);
    }

    public int CmtCount
    {
        get => _cmtCount;
        set => SetField(ref _cmtCount, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }

    public string DelayStatus
    {
        get => _delayStatus;
        set => SetField(ref _delayStatus, value);
    }

    public string MaskedToken
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Token)) return "-";
            var t = Token.Trim();
            if (t.Length <= 12) return t[..Math.Min(2, t.Length)] + "••••" + t[^Math.Min(2, t.Length)..];
            return $"{t[..4]}••••••••{t[^4..]}";
        }
    }

    // Runtime state (non-UI bound or internal)
    public WebProxy? ParsedProxy { get; set; }
    public bool InUse { get; set; }
    public double NextReadyTime { get; set; }
    public double LastCommentTime { get; set; }
    public int SessionCmtCount { get; set; }
    public int BlockStrikes { get; set; }
    public int RateLimitCount { get; set; }
    public bool ReactionsUnsupported { get; set; }
    public bool WarmedUp { get; set; }
    public string TokenError { get; set; } = string.Empty;
    public HashSet<string> BlockedTargets { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
