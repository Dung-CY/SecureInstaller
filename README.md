# SecureInstaller

Ứng dụng Windows quét máy tính và cài đặt ngầm các phần mềm đã được duyệt từ một kho mã hóa. Chạy được từ USB, ổ đĩa, hoặc thư mục chép sang máy khác, không cần mạng.

Tác giả: DungCY

## Tính năng

- Quét máy: phần cứng, thiết bị ngoại vi, lịch sử mạng và Internet, USB đã cắm, bản quyền Windows/Office (dấu hiệu crack), file chứa từ khóa.
- Cài ngầm phần mềm từ kho: tự nhận diện loại bộ cài (MSI, Inno Setup, NSIS, WiX Burn, zip, script), cài nhiều phần mềm theo hàng đợi, bộ cài đặt (nhóm phần mềm).
- Nhận diện phiên bản đã cài trên máy, gỡ cài đặt ngầm không cần viết script.
- Kho mã hóa AES-256-GCM, mỗi phần mềm một khóa riêng; danh mục ký số ECDSA; mật khẩu bảo vệ bằng Argon2id; tên file trong kho ngẫu nhiên.

## Dùng bản build sẵn

1. Tải file zip trong mục [Releases](../../releases) và giải nén. Bản này **chưa có kho** (chưa có phần mềm nào).
2. Tạo kho và tài khoản quản trị (lệnh sẽ hỏi mật khẩu):
   ```
   cd SecureInstaller
   tools\SecureInstaller.Packager.exe init . --user admin
   ```
3. Chạy `SecureInstaller.exe` (cần quyền quản trị), đăng nhập `admin`, vào **Quản trị → Thêm phần mềm** để đưa bộ cài vào kho.
4. Chép nguyên thư mục `SecureInstaller` sang USB hoặc máy khác để dùng.

Yêu cầu: Windows 10/11 64-bit. Không cần cài .NET (đã đóng gói sẵn).

## Nâng cấp lên bản mới

Kho (`vault`) không phụ thuộc phiên bản chương trình. Tải bản mới, giải nén, rồi chép đè thư mục `SecureInstallerault` (và `DuLieu` nếu có) từ bản cũ sang. Phần mềm, tài khoản và mật khẩu giữ nguyên.

## Build từ mã nguồn

Cần .NET SDK 8.

```
dotnet build
dotnet test
powershell -ExecutionPolicy Bypass -File build/build-portable.ps1 -Out D:\temp\usb -InitVault -Admin admin
```

Thêm `-WithDemo` để tải sẵn bộ phần mềm thử nghiệm (`-DemoSet office`: Chrome, Edge, LibreOffice, UniKey, 7-Zip).

Danh sách kiểm thử thủ công: [docs/e2e-checklist.md](docs/e2e-checklist.md).
