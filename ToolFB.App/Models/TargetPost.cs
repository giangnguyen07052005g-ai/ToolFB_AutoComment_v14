using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ToolFB.App.Models;

public class TargetPost : INotifyPropertyChanged
{
    private int _index;
    private string _targetId = string.Empty;
    private string _originalUrl = string.Empty;
    private int _cmtCount;
    private int _maxComments = 1;
    private string _status = "Chờ chạy"; // Chờ chạy, Đang chạy, Hoàn thành, Lỗi
    private bool _isSelected = true;

    public int Index
    {
        get => _index;
        set => SetField(ref _index, value);
    }

    public string TargetId
    {
        get => _targetId;
        set => SetField(ref _targetId, value);
    }

    public string OriginalUrl
    {
        get => _originalUrl;
        set => SetField(ref _originalUrl, value);
    }

    public int CmtCount
    {
        get => _cmtCount;
        set => SetField(ref _cmtCount, value);
    }

    public int MaxComments
    {
        get => _maxComments;
        set => SetField(ref _maxComments, value);
    }

    public string Status
    {
        get => _status;
        set => SetField(ref _status, value);
    }

    private string _errorDetail = string.Empty;
    public string ErrorDetail
    {
        get => _errorDetail;
        set => SetField(ref _errorDetail, value);
    }

    public string ProgressText => $"{CmtCount}/{MaxComments}";

    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }

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
