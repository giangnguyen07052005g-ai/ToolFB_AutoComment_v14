# HƯỚNG DẪN PHÁT HÀNH PHIÊN BẢN MỚI (GITHUB AUTO-UPDATE)
**GIANG DEV — TOOL AUTO COMMENT FACEBOOK PRO 2026**

Tài liệu này hướng dẫn chi tiết quy trình phát hành bản cập nhật mới lên GitHub Releases để toàn bộ người dùng đang sử dụng tool tự động nhận diện và cập nhật từ xa.

---

## 1. Cấu trúc Kho lưu trữ GitHub (Repository)

```text
giangdev/ToolFB_Releases/
├── .github/
├── docs/                           # Tài liệu hướng dẫn & bảo mật
├── latest.json                     # File manifest phiên bản mới nhất (khách đọc từ đây)
└── README.md
```

---

## 2. Quy trình Phát hành Phiên bản Mới (5 Bước)

### Bước 1: Build phiên bản mới (Release)
Mở PowerShell tại thư mục dự án và chạy lệnh build tối ưu:
```powershell
dotnet publish ToolFB.App/ToolFB.App.csproj -c Release -r win-x64 --no-self-contained -o ./publish/
dotnet publish ToolFB.Updater/ToolFB.Updater.csproj -c Release -r win-x64 --no-self-contained -o ./publish/
```

### Bước 2: Đóng gói thành file ZIP
Nén toàn bộ các file trong thư mục `./publish/` thành file ZIP (ví dụ: `ToolFB_v14.1.0_Update.zip`).

> [!IMPORTANT]
> **KHÔNG ĐƯA VÀO FILE ZIP**:
> - Các file dữ liệu riêng của bạn: `config_pro.dat`, `config_pro.json`, `comment_links.txt`, `commented_posts.txt`, file báo cáo `.csv`.
> - Khóa bí mật `private_key.pem`.

### Bước 3: Dùng ToolFB.Signer để Ký số và sinh file `latest.json`
Chạy công cụ ký số đi kèm:
```powershell
dotnet run --project ToolFB.Signer -- sign `
  --zip "./ToolFB_v14.1.0_Update.zip" `
  --version "14.1.0" `
  --url "https://github.com/giangdev/ToolFB_Releases/releases/download/v14.1.0/ToolFB_v14.1.0_Update.zip" `
  --key "ToolFB.Signer/private_key.pem" `
  --notes "Nâng cấp giao diện mượt mà, tối ưu thuật toán Round-Robin và chống checkpoint Facebook."
```

Lệnh trên sẽ tự động:
1. Tính mã băm **SHA-256** của file ZIP.
2. Dùng **Private Key ECDSA** tạo chữ ký số độc nhất.
3. Sinh file `latest.json` chuẩn hóa.

### Bước 4: Tạo GitHub Release
1. Vào kho lưu trữ GitHub của bạn: `https://github.com/giangdev/ToolFB_Releases`
2. Bấm **Releases** ➔ **Draft a new release**.
3. Điền:
   - **Tag version**: `v14.1.0`
   - **Release title**: `Bản Cập Nhật v14.1.0 - Giang Dev`
   - **Description**: Dán nội dung ghi chú cập nhật.
4. Kéo thả file `ToolFB_v14.1.0_Update.zip` vào mục **Attach binaries by dropping them here**.
5. Bấm **Publish release**.

### Bước 5: Đẩy file `latest.json` lên GitHub
Commit và Push file `latest.json` vừa tạo lên nhánh chính (`main`) của kho GitHub.

```powershell
git add latest.json
git commit -m "Release v14.1.0"
git push origin main
```

---

## 3. Trải nghiệm của Khách hàng
Ngay khi mở tool (hoặc bấm nút **"🔄 Kiểm tra cập nhật"** trên thanh tiêu đề):
1. Tool tải `latest.json` từ GitHub.
2. Xác minh chữ ký số ECDSA (khớp với Public Key tích hợp trong tool).
3. Hiển thị cửa sổ popup Cyber Dark thông báo bản mới kèm ghi chú cập nhật.
4. Khách bấm **"Cập nhật ngay"**: Tool tự tải file ZIP theo tiến trình %, kiểm tra SHA-256, đóng app và chạy `ToolFB.Updater.exe` để thay thế file an toàn, sau đó tự khởi động lại tool mới!
