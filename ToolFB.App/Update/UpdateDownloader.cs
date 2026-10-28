using System.IO;
using System.Net.Http;

namespace ToolFB.App.Update;

public class DownloadProgressReport
{
    public long BytesReceived { get; set; }
    public long? TotalBytes { get; set; }
    public double ProgressPercentage { get; set; }
    public double SpeedKbPerSec { get; set; }
    public string StatusText { get; set; } = string.Empty;
}

public class UpdateDownloader
{
    private static readonly HttpClient HttpClient = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        ConnectTimeout = TimeSpan.FromSeconds(15)
    })
    {
        Timeout = TimeSpan.FromMinutes(10)
    };

    public static async Task DownloadFileWithProgressAsync(
        string downloadUrl,
        string destinationFilePath,
        IProgress<DownloadProgressReport> progress,
        CancellationToken ct)
    {
        if (!downloadUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Chỉ chấp nhận đường dẫn cập nhật an toàn qua giao thức HTTPS!");

        // Ensure target directory exists
        var dir = Path.GetDirectoryName(destinationFilePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        // Check available disk space (require at least 150 MB free)
        var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(destinationFilePath)) ?? "C:\\");
        if (drive.AvailableFreeSpace < 150 * 1024 * 1024)
        {
            throw new IOException($"Không đủ dung lượng ổ đĩa {drive.Name} để tải bản cập nhật! Yêu cầu tối thiểu 150MB trống.");
        }

        using var response = await HttpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        long? totalBytes = response.Content.Headers.ContentLength;

        using var contentStream = await response.Content.ReadAsStreamAsync(ct);
        using var fileStream = new FileStream(destinationFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

        var buffer = new byte[81920]; // 80 KB buffer
        long totalBytesRead = 0;
        int bytesRead;

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        long lastBytes = 0;
        double lastTime = 0;
        double speedKb = 0;

        while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
            totalBytesRead += bytesRead;

            double elapsedSec = stopwatch.Elapsed.TotalSeconds;
            if (elapsedSec - lastTime >= 0.3)
            {
                speedKb = (totalBytesRead - lastBytes) / (elapsedSec - lastTime) / 1024.0;
                lastBytes = totalBytesRead;
                lastTime = elapsedSec;

                double percent = totalBytes.HasValue && totalBytes.Value > 0
                    ? Math.Min(100.0, (double)totalBytesRead / totalBytes.Value * 100.0)
                    : 0;

                string mbReceived = (totalBytesRead / (1024.0 * 1024.0)).ToString("0.0");
                string mbTotal = totalBytes.HasValue ? (totalBytes.Value / (1024.0 * 1024.0)).ToString("0.0") : "?";

                progress.Report(new DownloadProgressReport
                {
                    BytesReceived = totalBytesRead,
                    TotalBytes = totalBytes,
                    ProgressPercentage = Math.Round(percent, 1),
                    SpeedKbPerSec = Math.Round(speedKb, 1),
                    StatusText = $"Đang tải {mbReceived} MB / {mbTotal} MB ({speedKb:0.0} KB/s)"
                });
            }
        }

        progress.Report(new DownloadProgressReport
        {
            BytesReceived = totalBytesRead,
            TotalBytes = totalBytes,
            ProgressPercentage = 100.0,
            SpeedKbPerSec = 0,
            StatusText = "Tải xuống hoàn tất, đang kiểm tra tính toàn vẹn..."
        });
    }
}
