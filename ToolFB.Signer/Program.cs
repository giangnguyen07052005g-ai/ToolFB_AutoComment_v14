using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ToolFB.Signer;

internal class Program
{
    static int Main(string[] args)
    {
        Console.Title = "GIANG DEV - Tool Release Signer (ECDSA P-256)";
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("===============================================================");
        Console.WriteLine("   GIANG DEV - CÔNG CỤ TẠO CHỮ KÝ SỐ & MANIFEST GITHUB RELEASE ");
        Console.WriteLine("===============================================================\n");
        Console.ResetColor();

        if (args.Length == 0 || args[0] == "--help" || args[0] == "-h")
        {
            ShowHelp();
            return 0;
        }

        string cmd = args[0].ToLowerInvariant();

        if (cmd == "generate-keys")
        {
            GenerateKeypair(args.Skip(1).ToArray());
            return 0;
        }

        if (cmd == "sign")
        {
            return SignRelease(args.Skip(1).ToArray());
        }

        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[Lỗi] Lệnh không nhận diện: {cmd}");
        Console.ResetColor();
        ShowHelp();
        return 1;
    }

    private static void ShowHelp()
    {
        Console.WriteLine("Cách sử dụng:");
        Console.WriteLine("  1. Tạo cặp khóa ECDSA mới:");
        Console.WriteLine("     ToolFB.Signer generate-keys [--out-dir <thư_mục>]\n");
        Console.WriteLine("  2. Ký gói cập nhật và sinh file latest.json:");
        Console.WriteLine("     ToolFB.Signer sign --zip <file.zip> --version <14.1.0> --url <https://...> --key <private_key.pem> [--notes <file.txt>] [--mandatory]");
        Console.WriteLine();
    }

    private static void GenerateKeypair(string[] args)
    {
        string outDir = Directory.GetCurrentDirectory();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--out-dir" && i + 1 < args.Length) outDir = args[++i];
        }

        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string privatePem = ecdsa.ExportECPrivateKeyPem();
        string publicPem = ecdsa.ExportSubjectPublicKeyInfoPem();

        string privPath = Path.Combine(outDir, "private_key.pem");
        string pubPath = Path.Combine(outDir, "public_key.pem");

        File.WriteAllText(privPath, privatePem, Encoding.UTF8);
        File.WriteAllText(pubPath, publicPem, Encoding.UTF8);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"[+] Đã sinh cặp khóa ECDSA NIST P-256 thành công!");
        Console.WriteLine($"    - Private Key (BẢO MẬT TUYỆT ĐỐI): {privPath}");
        Console.WriteLine($"    - Public Key (Tích hợp vào app):   {pubPath}");
        Console.ResetColor();
        Console.WriteLine("\nNội dung Public Key để nhúng vào SignatureVerifier.cs:");
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine(publicPem);
        Console.ResetColor();
    }

    private static int SignRelease(string[] args)
    {
        string? zipPath = null;
        string? version = null;
        string? downloadUrl = null;
        string? keyPath = null;
        string? notes = null;
        string? minVersion = null;
        bool mandatory = false;
        string outJson = "latest.json";

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--zip" && i + 1 < args.Length) zipPath = args[++i];
            else if (args[i] == "--version" && i + 1 < args.Length) version = args[++i];
            else if (args[i] == "--url" && i + 1 < args.Length) downloadUrl = args[++i];
            else if (args[i] == "--key" && i + 1 < args.Length) keyPath = args[++i];
            else if (args[i] == "--notes" && i + 1 < args.Length) notes = args[++i];
            else if (args[i] == "--min-version" && i + 1 < args.Length) minVersion = args[++i];
            else if (args[i] == "--mandatory") mandatory = true;
            else if (args[i] == "--out" && i + 1 < args.Length) outJson = args[++i];
        }

        if (string.IsNullOrEmpty(zipPath) || string.IsNullOrEmpty(version) || string.IsNullOrEmpty(downloadUrl) || string.IsNullOrEmpty(keyPath))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("[Lỗi] Thiếu các tham số bắt buộc: --zip, --version, --url, --key");
            Console.ResetColor();
            return 1;
        }

        if (!File.Exists(zipPath))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[Lỗi] File zip không tồn tại: {zipPath}");
            Console.ResetColor();
            return 1;
        }

        if (!File.Exists(keyPath))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[Lỗi] File private key không tồn tại: {keyPath}");
            Console.ResetColor();
            return 1;
        }

        // 1. Tính SHA-256 của file zip
        Console.WriteLine($"[*] Đang tính toán mã băm SHA-256 của: {zipPath}...");
        using var sha256 = SHA256.Create();
        using var stream = File.OpenRead(zipPath);
        var hashBytes = sha256.ComputeHash(stream);
        string sha256Hex = Convert.ToHexString(hashBytes).ToLowerInvariant();
        Console.WriteLine($"[+] SHA-256: {sha256Hex}");

        // 2. Chuẩn bị Release Notes
        string releaseNotes = notes ?? "Bản cập nhật tối ưu hiệu năng và sửa lỗi.";
        if (File.Exists(notes))
        {
            releaseNotes = File.ReadAllText(notes, Encoding.UTF8);
        }

        // 3. Tạo Payload và Ký số bằng Private Key
        string payload = $"{version}|{sha256Hex}|{downloadUrl}|{mandatory}";
        byte[] payloadBytes = Encoding.UTF8.GetBytes(payload);

        string privPem = File.ReadAllText(keyPath);
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(privPem);

        byte[] signatureBytes = ecdsa.SignData(payloadBytes, HashAlgorithmName.SHA256);
        string signatureBase64 = Convert.ToBase64String(signatureBytes);

        Console.WriteLine($"[+] Chữ ký số ECDSA tạo thành công!");

        // 4. Tạo manifest latest.json
        var manifest = new
        {
            version = version,
            downloadUrl = downloadUrl,
            sha256 = sha256Hex,
            signature = signatureBase64,
            releaseNotes = releaseNotes,
            minimumVersion = minVersion,
            mandatory = mandatory,
            publishedAt = DateTime.UtcNow
        };

        string json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(outJson, json, Encoding.UTF8);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"\n[THÀNH CÔNG] Đã tạo file manifest: {Path.GetFullPath(outJson)}");
        Console.ResetColor();
        Console.WriteLine("Nội dung file:");
        Console.WriteLine(json);

        return 0;
    }
}
