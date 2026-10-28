# HƯỚNG DẪN XỬ LÝ SỰ CỐ CẬP NHẬT (TROUBLESHOOTING)
**GIANG DEV — TOOL AUTO COMMENT FACEBOOK PRO 2026**

---

## 1. Bảng Mã Lỗi & Cách Khắc Phục Nhanh

| Hiện Tượng / Thông Báo Lỗi | Nguyên Nhân Thường Gặp | Hướng Xử Lý |
| :--- | :--- | :--- |
| **"CẢNH BÁO: Chữ ký số của bản cập nhật không hợp lệ!"** | 1. File `latest.json` được tạo thủ công mà không dùng `ToolFB.Signer`.<br>2. Ký bằng file `private_key.pem` khác với khóa đã nhúng trong bản build của khách.<br>3. Đường dẫn download hoặc mã SHA256 bị thay đổi sau khi đã ký. | Chạy lại lệnh `ToolFB.Signer sign` bằng đúng file `private_key.pem` chuẩn và upload lại file `latest.json` mới lên GitHub. |
| **"Mã băm SHA-256 của file tải về không khớp!"** | 1. Quá trình tải file ZIP qua mạng bị đứt gãy giữa chừng.<br>2. File ZIP trên GitHub Release đã bị thay thế nhưng chưa cập nhật mã SHA-256 mới trong `latest.json`. | 1. Bấm nút **"Thử lại"** trên tool.<br>2. Kiểm tra lại mã SHA-256 của file ZIP thực tế trên máy chủ xem có khớp với trường `"sha256"` trong `latest.json` không. |
| **"Không thể tải manifest từ GitHub (Mã lỗi HTTP: 404)"** | Đường dẫn `ManifestUrl` trong cài đặt tool bị sai hoặc file `latest.json` chưa được push lên nhánh chính của repo. | Kiểm tra lại URL `latest.json` trên trình duyệt web xem có xem được trực tiếp không. |
| **"Không đủ dung lượng ổ đĩa để tải bản cập nhật!"** | Ổ đĩa hệ thống (thường là ổ C:) còn dưới 150MB trống. | Dọn dẹp ổ đĩa, xóa các file tạm (`%TEMP%`) để giải phóng dung lượng. |
| **Ứng dụng không tự khởi động lại sau khi cập nhật** | File chạy chính bị Antivirus chặn hành vi khởi động tiến trình con. | 1. Mở tool thủ công bằng file `ToolFB.App.exe` trong thư mục cài đặt.<br>2. Thêm thư mục cài đặt tool vào danh sách loại trừ (Exclusion) của Windows Defender. |

---

## 2. Cơ Chế Tự Động Khôi Phục Bản Cũ (Rollback)

Nếu trong quá trình cập nhật, máy tính bị mất điện đột ngột hoặc file ZIP bị lỗi giải nén:
1. `ToolFB.Updater.exe` đã tự động sao lưu toàn bộ phiên bản cũ vào thư mục `%TEMP%\ToolFB_Backup`.
2. Khi phát hiện ngoại lệ, trình cập nhật sẽ tự động khôi phục nguyên vẹn 100% các file cũ từ thư mục sao lưu.
3. Khởi động lại ứng dụng cũ an toàn, không làm gián đoạn công việc của người dùng.
