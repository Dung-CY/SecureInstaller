# Mẫu gói 7-Zip (MSI)

`manifest.json` là mẫu; JSON không có chú thích nên giải thích nằm ở đây. Không commit file cài vào repo.

Cấu trúc thư mục gói:

```
7zip/
  manifest.json
  installer/7z2408-x64.msi   <- tải từ https://www.7-zip.org, tự đặt vào (đã bị .gitignore)
  docs/                      <- tài liệu hướng dẫn (tùy chọn)
  extras/                    <- tệp dùng cho post_actions (tùy chọn)
```

Các trường:

- `id`, `version`: chỉ gồm chữ, số, `.`, `_`, `-`.
- `installer_type`: `msi`, `exe` hoặc `zip`.
- `installer_file`: tên tệp nằm trong `installer\`.
- `silent_args`: tham số cài ngầm. MSI dùng `/qn /norestart`; EXE thường là `/S`.
- `timeout_sec`: thời gian chờ tối đa (mặc định 1800).
- `detect.display_name_regex`: so khớp DisplayName trong Programs and Features để biết đã cài chưa. Có thể thêm `product_code`.
- `post_actions`: ví dụ `{ "type": "copy", "src": "extras/a.ini", "dst": "C:\App\a.ini" }`.
- `rules`: ví dụ `{ "type": "block_if_windows_cracked" }`.

Thêm vào kho:

```
tools\SecureInstaller.Packager.exe add-package . <thư mục 7zip> --user admin
```
