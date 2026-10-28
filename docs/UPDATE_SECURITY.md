# KIẾN TRÚC BẢO MẬT HỆ THỐNG CẬP NHẬT TỪ XA (UPDATE SECURITY)
**GIANG DEV — TOOL AUTO COMMENT FACEBOOK PRO 2026**

---

## 1. Mục tiêu Bảo vệ

Hệ thống cập nhật từ xa qua GitHub được thiết kế theo tiêu chuẩn thương mại cao cấp, bảo đảm 5 nguyên tắc:
1. **Tính Toàn Vẹn (Integrity)**: File cập nhật không bị lỗi đường truyền, hỏng bit hoặc bị bên thứ ba sửa đổi nội dung.
2. **Tính Xác Thực (Authenticity)**: Bản cập nhật chỉ có thể do chính tác giả (GIANG DEV) phát hành qua chữ ký số mã hóa.
3. **Chống Tấn Công Trung Gian (Anti-MITM)**: 100% kết nối tải manifest và gói cập nhật thực hiện qua HTTPS có mã hóa TLS.
4. **Chống Hạ Cấp (Anti-Downgrade Protection)**: Ngăn chặn kẻ tấn công lừa tool cập nhật về phiên bản cũ chứa lỗ hổng.
5. **Bảo Vệ Dữ Liệu Khách Hàng**: Không bao giờ ghi đè file lưu token, proxy, tài khoản cá nhân, cơ sở dữ liệu hoặc lịch sử bình luận của người dùng.

---

## 2. Mô hình Chữ ký số ECDSA (NIST P-256)

Hệ thống sử dụng thuật toán **ECDSA (Elliptic Curve Digital Signature Algorithm)** trên đường cong chuẩn **NIST P-256 (secp256r1)** kết hợp hàm băm an toàn **SHA-256**.

```text
[Máy Tác Giả / Signer Tool]
       │
       ├─ File ZIP Cập Nhật ──── SHA-256 ───► [Mã Băm Hex]
       │                                            │
       └─ [Private Key bí mật] ─────────────────────┴─► [ECDSA Sign] ──► Chữ ký số Base64
                                                                                │
                                                                   Ghi vào latest.json
──────────────────────────────────────────────────────────────────────────────────────
[Máy Khách Hàng / Client App]
       │
       ├─ Tải latest.json qua HTTPS
       │
       ├─ [Public Key nhúng trong Client] ─────────► [ECDSA Verify]
       │                                                    │
       │                                           ┌────────┴────────┐
       │                                           ▼                 ▼
       │                                       Hợp Lệ             Không Hợp Lệ
       │                                          │                  │
       │                                          ▼                  ▼
       │                                      Cho Phép           TỪ CHỐI NGAY
       │                                      Tải File           Cảnh báo giả mạo!
       ▼
  Tải File ZIP ──► Tính SHA-256 thực tế == SHA-256 manifest? ──► Cho phép cài đặt!
```

---

## 3. Quản lý Khóa Bí Mật (Private Key Management)

> [!CAUTION]
> **QUY TẮC SỐNG CÒN**:
> - Khóa bí mật `private_key.pem` **TUYỆT ĐỐI KHÔNG ĐƯỢC**:
>   - Đưa vào thư mục xuất bản (publish).
>   - Đưa vào file ZIP cập nhật.
>   - Commit hoặc Push lên kho lưu trữ GitHub công khai.
> - Khóa công khai `EmbeddedPublicKeyPem` đã được nhúng sẵn vào mã nguồn `SignatureVerifier.cs` của ứng dụng khách để đối soát chữ ký. Bất kỳ ai không có `private_key.pem` đều không thể tạo ra file cập nhật hợp lệ.

---

## 4. Danh sách File Nhạy Cảm Được Bảo Vệ Khi Cập Nhật

Khi `ToolFB.Updater.exe` tiến hành thay thế file, các dữ liệu sau **HOÀN TOÀN ĐƯỢC GIỮ NGUYÊN**:
- `comment_links.txt`: Toàn bộ link comment đã tạo thành công.
- `commented_posts.txt`: Danh sách ID bài viết đã bình luận.
- `config_pro.dat` & `config_pro.json`: Cấu hình cài đặt và dàn tài khoản/proxy được mã hóa qua Windows DPAPI.
- `report_comment_*.csv`: Toàn bộ file báo cáo lịch sử.
- `update_settings.json`: Cài đặt cập nhật của khách.
