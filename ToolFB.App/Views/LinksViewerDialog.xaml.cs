using System.Diagnostics;
using System.IO;
using System.Windows;

namespace ToolFB.App.Views;

public partial class LinksViewerDialog : Window
{
    private readonly string _filePath;

    public LinksViewerDialog(string title, string filePath)
    {
        InitializeComponent();
        TxtTitle.Text = title;
        _filePath = filePath;

        LoadData();
    }

    private void LoadData()
    {
        if (File.Exists(_filePath))
        {
            try
            {
                var lines = File.ReadAllLines(_filePath).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
                TxtCount.Text = $"({lines.Count} mục)";
                TxtContent.Text = string.Join(Environment.NewLine, lines);
            }
            catch (Exception ex)
            {
                TxtContent.Text = $"Lỗi đọc file: {ex.Message}";
            }
        }
        else
        {
            TxtCount.Text = "(0 mục)";
            TxtContent.Text = "File chưa có dữ liệu.";
        }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void BtnCopyAll_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(TxtContent.Text))
        {
            Clipboard.SetText(TxtContent.Text);
            MessageBox.Show("Đã sao chép toàn bộ danh sách vào bộ nhớ tạm!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void BtnClear_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("Bạn có chắc chắn muốn xóa toàn bộ dữ liệu file này?", "Xác nhận", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            try
            {
                if (File.Exists(_filePath)) File.WriteAllText(_filePath, string.Empty);
                LoadData();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void BtnOpenExternal_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!File.Exists(_filePath))
                File.WriteAllText(_filePath, string.Empty);

            Process.Start(new ProcessStartInfo
            {
                FileName = _filePath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
