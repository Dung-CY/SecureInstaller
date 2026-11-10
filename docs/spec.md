# SecureInstaller — Spec v3

> Phần mềm cài đặt ứng dụng nội bộ cho Windows, gồm hai chế độ: **USB/ổ đĩa (offline)** và **Máy chủ LAN**.
> Mọi `[ASSUMPTION: ...]` cần được xác nhận. Danh sách tổng hợp nằm ở cuối file.

---

## SECTION 1: SPEC

### Mục đích
Kỹ thuật viên đăng nhập, quét máy (phần cứng, lịch sử mạng, bản quyền Windows/Office, phiên bản phần mềm), rồi cài các phần mềm được admin phê duyệt từ một kho được bảo vệ. Kho có thể nằm trên USB/ổ đĩa hoặc trên một máy chủ trong mạng LAN.

### Người dùng và tình huống sử dụng
- Là **người dùng**, tôi muốn chạy app từ USB và đăng nhập, để cài phần mềm trên máy không có mạng.
- Là **người dùng**, tôi muốn kết nối tới máy chủ LAN và đăng nhập, để cài phần mềm từ kho tập trung mới nhất.
- Là **người dùng**, tôi muốn biết Windows/Office trên máy là bản quyền loại gì, có dấu hiệu crack không, để xử lý trước khi cài.
- Là **người dùng**, tôi muốn thấy phiên bản trên máy so với phiên bản trong bộ cài, để chỉ cài khi cần.
- Là **người dùng**, tôi muốn xem tiến độ cài và đọc hướng dẫn đi kèm.
- Là **admin**, tôi muốn thêm, xóa, cập nhật phần mềm và quản lý tài khoản trên USB hoặc máy chủ.
- Là **admin**, tôi muốn nhân bản USB, hoặc gắn kho vào một máy cụ thể, để triển khai cho nhiều nơi.
- Là **admin**, tôi muốn xem log quét và log cài (tập trung trên máy chủ, hoặc đọc từ USB).

### Yêu cầu

**Chung**
1. Có hai chế độ: **Portable** (app và kho nằm chung một thư mục trên USB, đĩa hoặc ổ máy; không cần mạng) và **Máy chủ LAN** (máy trạm lấy gói từ một máy chủ sau khi được phê duyệt). App tự chọn Portable nếu thấy thư mục `vault\` nằm cạnh file exe, nếu không thì hỏi IP máy chủ.
2. Bắt buộc đăng nhập. Có hai vai trò là `user` và `admin`. Chỉ admin được quản lý kho và tài khoản.
3. Người dùng không thể mở, giải nén hoặc copy nội dung kho bằng công cụ thông thường.
4. Kho được bảo vệ chống sửa (bằng mã kiểm tra của admin). Sửa kho mà không qua admin thì app từ chối cài.
5. Kho có số phiên bản (tăng dần) và ngày cập nhật, hiển thị trên giao diện.

**Chế độ USB / ổ đĩa / thư mục copy (portable)**
6. Thư mục `SecureInstaller\` (gồm app và kho) chạy được ở bất cứ đâu: USB, ổ cứng gắn ngoài, đĩa, hoặc copy nguyên ra ổ của máy cần cài. Không gắn với thiết bị nào.
7. Copy thư mục thì chỉ copy được dữ liệu **đã mã hóa**. Không đăng nhập thì không lấy được file cài bên trong.
8. Admin đăng nhập vào app là thêm, xóa, cập nhật được gói (thay thế, hoặc giữ nhiều phiên bản). Muốn nhân bản thì chỉ cần copy nguyên thư mục.
9. Nếu nơi chạy là ổ chỉ đọc (đĩa), log được ghi vào `%ProgramData%\SecureInstaller\logs` của máy.

**Chế độ Máy chủ LAN (quy mô nhỏ, dùng tạm thời)**
10. Một máy tính chạy app ở chế độ "Máy chủ", dùng chung kho với chế độ portable. Máy trạm cắm mạng LAN và nhập IP máy chủ (hoặc tự dò).
11. Luồng: máy trạm đăng nhập → quét → gửi kết quả quét lên máy chủ → **admin bấm Phê duyệt (hoặc Từ chối) trên máy chủ** → máy trạm tải gói và cài → gửi log → xong.
12. Máy trạm cài xong là không cần mạng nữa. Tháo card/dây mạng thì phần mềm đã cài vẫn dùng bình thường.
13. Log và kết quả quét của mọi máy trạm được lưu trên máy chủ.

**Quét máy (trước khi cài)**
14. Phần cứng: serial BIOS, MAC vật lý, serial ổ cứng, thiết bị ngoại vi đang kết nối.
15. **Lịch sử kết nối mạng:** tên mạng, loại (Wi-Fi hoặc dây), lần đầu kết nối, lần cuối kết nối, và trạng thái internet hiện tại.
16. **Bản quyền Windows:** phiên bản/edition, kênh (Retail, OEM, Volume MAK, Volume KMS), trạng thái kích hoạt, đánh giá crack.
17. **Bản quyền Office/Word:** phiên bản, kênh, trạng thái kích hoạt, đánh giá crack. Không có Office thì ghi "Không cài".
18. Đánh giá crack có ba mức (Chính hãng, Nghi vấn, Có dấu hiệu crack) và luôn kèm bằng chứng. Không đọc được thông tin thì ghi "Không xác định" `[ASSUMPTION: danh sách dấu hiệu do admin cập nhật được]`.
19. File có **tên** chứa từ khóa: theo danh sách từ khóa và thư mục do admin cấu hình, có giới hạn thời gian (không đọc nội dung file).
20. **So sánh phiên bản:** mỗi gói có trạng thái Chưa cài, Cũ hơn, Bằng nhau, Mới hơn, hoặc Không so sánh được, kèm hai số phiên bản.

**Cài đặt**
21. Cài ngầm `.msi`, `.exe`, `.zip`, có tiến độ (phần trăm hoặc theo bước) và timeout cho từng gói.
22. Admin cấu hình điều kiện chặn cho từng gói: Windows/Office có dấu hiệu crack, đã cài bản bằng hoặc mới hơn, có file chứa từ khóa, yêu cầu internet.
23. Mỗi gói có thể có thao tác sau cài: copy hoặc xóa file.
24. Mỗi gói có thể kèm tài liệu `.docx`/`.pdf` để mở xem.
25. File cài đã giải mã bị xóa sau khi cài, dù thành công hay thất bại.
26. Mọi lần quét và cài đều được ghi log.

### Các trường hợp đặc biệt
| Tình huống | Hành vi mong đợi |
|---|---|
| Thư mục được copy sang USB/máy khác | Chạy bình thường, vẫn phải đăng nhập |
| File trong kho bị sửa bằng tay | "Kho bị sửa đổi", không cho cài |
| Admin chưa phê duyệt (chế độ máy chủ) | Máy trạm hiện "Đang chờ phê duyệt"; quá 30 phút thì hủy `[ASSUMPTION]` |
| Admin từ chối | Máy trạm hiện lý do, không cài |
| Sai mật khẩu 5 lần | Khóa tài khoản 15 phút `[ASSUMPTION]` |
| Rút USB khi đang cài | Bộ cài chạy tiếp, bước sau cài bị bỏ, log lưu tạm trên máy |
| Đĩa chỉ đọc | Log ghi vào `%ProgramData%\SecureInstaller\logs` và có thông báo |
| Máy chủ mất kết nối khi đang tải gói | Hủy, xóa file tạm, báo lỗi, cho phép thử lại |
| Máy chủ mất kết nối sau khi cài xong | Log lưu tạm, gửi lại khi có kết nối |
| Không có quyền admin Windows (UAC bị từ chối) | Thoát và báo lý do |
| WMI/registry lỗi khi đọc bản quyền | "Không xác định", không bao giờ kết luận crack |
| Phần mềm có cả bản x86 và x64 | So với bản có phiên bản cao nhất, hiển thị cả hai |
| Chuỗi phiên bản không chuẩn | "Không so sánh được" |
| Bộ cài trả về mã 3010 | Thành công, nhắc khởi động lại |
| Bộ cài trả về mã khác 0 hoặc quá timeout | Thất bại, kill tiến trình, không chạy bước sau cài |
| Hash của gói không khớp | Từ chối cài |
| Lịch sử mạng rỗng | Danh sách rỗng kèm "Không có dữ liệu" |

### Tiêu chí chấp nhận
```
Given USB chứa app và kho hợp lệ, máy không có mạng
When chạy SecureInstaller.exe
Then app đề xuất chế độ USB và hiện màn hình đăng nhập

Given thư mục SecureInstaller được copy nguyên ra C:\ của máy cần cài
When chạy C:\SecureInstaller\SecureInstaller.exe và đăng nhập đúng
Then thấy đủ các gói và cài được

Given người dùng mở file .pkg.enc trong kho bằng 7-Zip/WinRAR
When thử giải nén
Then không giải nén được (dữ liệu đã mã hóa)

Given một file trong kho bị thay thế
When mở app
Then báo "Kho bị sửa đổi"

Given máy chủ LAN đang chạy
When máy trạm nhập IP, đăng nhập đúng và gửi kết quả quét
Then máy chủ hiện yêu cầu "Chờ phê duyệt" kèm báo cáo quét

Given admin bấm Phê duyệt trên máy chủ
When máy trạm nhận phê duyệt
Then máy trạm tải gói, cài, và gửi log lên máy chủ

Given máy trạm chưa được phê duyệt
When yêu cầu tải gói
Then máy chủ trả 403

Given máy trạm đã cài xong
When tháo mạng LAN
Then phần mềm đã cài vẫn chạy bình thường

Given admin thêm gói trên máy chủ
When client tải lại danh sách
Then gói mới xuất hiện

Given máy từng kết nối 3 mạng Wi-Fi
When quét
Then bảng lịch sử mạng có 3 dòng gồm tên, lần đầu, lần cuối kết nối

Given Windows kích hoạt bằng key OEM
When quét
Then hiện "OEM – Đã kích hoạt – Chính hãng"

Given Windows kích hoạt KMS trỏ về 127.0.0.1
When quét
Then hiện "Có dấu hiệu crack" kèm bằng chứng "KMS host = 127.0.0.1"

Given máy không cài Office
When quét
Then mục Office hiện "Không cài"

Given máy có 7-Zip 22.01, bộ cài có 24.08
When xem danh sách
Then gói hiện "Cũ hơn: 22.01 → 24.08"

Given máy có phiên bản mới hơn bộ cài
When bấm Cài
Then cảnh báo hạ cấp và chỉ cài khi xác nhận (hoặc chặn nếu có rule cấm)

Given gói có rule "chặn nếu Windows crack" và máy có dấu hiệu crack
When bấm Cài
Then việc cài bị chặn kèm lý do

Given gói .msi hợp lệ
When cài
Then tiến độ tăng 0 → 100% và phần mềm xuất hiện trong Programs and Features

Given gói có bước sau cài copy config.ini
When cài thành công
Then file đích tồn tại và trùng hash

Given cài xong (thành công hoặc thất bại)
When kiểm tra thư mục tạm
Then không còn file cài đã giải mã

Given user thường đăng nhập
When mở menu
Then không có chức năng Admin
```

---

## SECTION 2: PLAN

### Công nghệ và kiến trúc
- **C# / .NET 8**. Client dùng **WPF** (MVVM, CommunityToolkit.Mvvm). Chế độ máy chủ là **ASP.NET Core minimal API nhúng ngay trong app** (Kestrel), bật bằng `--server`, không cần cài service. Log và yêu cầu phê duyệt lưu trong **SQLite** (`SecureInstaller\server.db`).
- Client publish dạng single-file self-contained win-x64, manifest đặt `requireAdministrator`.
- `[ASSUMPTION: chỉ hỗ trợ Windows 10/11 x64]`

```
src/Installer.Core      vault, crypto, scanning, licensing, versioning, install, logging
src/Installer.App       WPF client (cả 2 chế độ + màn hình Admin)
src/Installer.Server    minimal API + SQLite, được App nạp khi chạy --server
src/Installer.Packager  CLI cho admin
tests/Installer.Core.Tests, tests/Installer.Server.Tests
```

**Lớp trừu tượng nguồn kho**: `IRepositorySource`, có hai bản cài đặt là `UsbRepositorySource` và `ServerRepositorySource`. Toàn bộ phần quét, rules, cài đặt và giao diện dùng chung.

### Cấu trúc thư mục portable (USB, đĩa, hoặc copy ra máy đều giống nhau)
```
SecureInstaller\SecureInstaller.exe
SecureInstaller\vault\vault.meta            salt, version, wrapped keys
SecureInstaller\vault\catalog.json.enc      gói, rules, crack_signatures, keyword_scan
SecureInstaller\vault\catalog.sig           chữ ký ECDSA P-256 của admin
SecureInstaller\vault\docs\<id>-<ver>\<tên>.enc   tài liệu hướng dẫn (mã hóa riêng, xem được mà không cần giải mã gói)
SecureInstaller\vault\packages\<id>-<ver>.pkg.enc
SecureInstaller\logs\*.log.enc
```
Chế độ máy chủ: chạy `SecureInstaller.exe --server` (hoặc chọn "Chạy làm máy chủ" trong menu Admin) trên chính thư mục này.

### Cơ chế khóa (đơn giản)
- **Vault Key (VK)** dùng AES-256-GCM để mã hóa toàn bộ kho.
- VK được bọc riêng cho từng user bằng `Argon2id(mật khẩu, salt)`. Đăng nhập đúng thì mở được VK. **Không gắn với thiết bị**, nên copy thư mục đi đâu cũng chạy.
- **Chống sửa kho:** catalog được ký bằng ECDSA P-256. Khóa công khai nằm trong `vault.meta` để mọi user kiểm tra; khóa bí mật được bọc bằng mật khẩu của từng admin. Chỉ admin đăng nhập mới ký lại được `[ASSUMPTION: chấp nhận mức bảo mật vừa phải để đổi lấy sự đơn giản]`. Danh sách user (không chứa mật khẩu) nằm trong `vault.meta`.
- **Chế độ máy chủ:** máy chủ dùng chung kho với chế độ portable. Gói chỉ được gửi cho máy trạm có yêu cầu đã được admin phê duyệt. Gói được truyền ở dạng mã hóa, khóa của gói đi kèm phản hồi phê duyệt và chỉ giữ trong bộ nhớ.
- Thư mục tạm `%TEMP%\SI_<guid>` có ACL chỉ cho Administrators/SYSTEM, và bị xóa trong `finally`.

### Nguồn dữ liệu khi quét
| Mục | Nguồn |
|---|---|
| Phần cứng | WMI `Win32_BIOS`, `Win32_DiskDrive`, `Win32_NetworkAdapter (PhysicalAdapter)`, `Win32_PnPEntity` |
| Lịch sử mạng | Registry `HKLM\...\NetworkList\Profiles` (DateCreated/DateLastConnected là SYSTEMTIME 16 byte), `netsh wlan show profiles`, Event log `Microsoft-Windows-NetworkProfile/Operational` (10000/10001) |
| Bản quyền Windows | WMI `SoftwareLicensingProduct` (LicenseStatus, ProductKeyChannel, PartialProductKey), `SoftwareLicensingService` (KeyManagementServiceMachine) |
| Bản quyền Office | `SoftwareLicensingProduct`/`OfficeSoftwareProtectionProduct`, registry `ClickToRun\Configuration`, `%LOCALAPPDATA%\Microsoft\Office\Licenses` |
| Dấu hiệu crack | `crack_signatures` trong catalog: host KMS loopback hoặc lạ, service/task/file của tool lậu, GVLK trên máy không thuộc domain, file hosts chặn domain kích hoạt |
| Phần mềm đã cài | Registry Uninstall (HKLM, HKLM\WOW6432Node, HKCU): DisplayName, DisplayVersion, ProductCode |

### Mô hình dữ liệu
**catalog.json** (USB), tương đương các bảng `packages`, `post_actions`, `rules` trên máy chủ:
```jsonc
{
  "version": 13, "updated_at": "2026-11-10T00:00:00Z",
  "packages": [{
    "id": "7zip", "name": "7-Zip", "version": "24.08",
    "installer_type": "msi", "file": "7zip-24.08.pkg.enc", "sha256": "...",
    "silent_args": "/qn", "timeout_sec": 600,
    "detect": { "product_code": "{...}", "display_name_regex": "^7-Zip" },
    "docs": ["HuongDan.docx"],
    "post_actions": [{ "type": "copy", "src": "extras/a.ini", "dst": "C:\\App\\a.ini" }],
    "rules": [{ "type": "block_if_windows_cracked" }]
  }],
  "keyword_scan": { "keywords": [], "paths": [], "max_seconds": 60 },
  "crack_signatures": [{ "kind": "kms_host", "pattern": "^(127\\.|localhost)" }]
}
```
**Các bảng SQLite trên máy chủ:** `users(id, username, password_hash, role, failed_attempts, locked_until)`, `packages(id, name, version, installer_type, silent_args, timeout_sec, detect_json, blob_path, sha256, wrapped_key, created_at)`, `package_files`, `post_actions`, `rules`, `settings(keyword_scan_json, crack_signatures_json)`, `machines(id, bios_serial, disk_serials, macs, hostname, first_seen, last_seen)`, `scans(id, machine_id, user_id, result_json, allowed, created_at)`, `install_logs(id, machine_id, user_id, package_id, status, exit_code, message, started_at, finished_at)`.

### API máy chủ (HTTPS, JWT)
| Method | Path | Đầu ra | Lỗi |
|---|---|---|---|
| POST | `/api/auth/login` | JWT 8h, role | 401, 423 |
| GET | `/api/catalog` | danh sách gói, rules, settings | 401 |
| GET | `/api/packages/{id}/blob` | stream file mã hóa | 401, 404 |
| POST | `/api/requests` (scan + danh sách gói muốn cài) | request_id, trạng thái `pending` | 400, 401 |
| GET | `/api/requests/{id}` | `pending` / `approved` (kèm khóa gói) / `rejected` (kèm lý do) | 401, 404 |
| POST | `/api/admin/requests/{id}/approve` hoặc `/reject` | — | 403 |
| GET | `/api/packages/{id}/blob?request={id}` | stream file mã hóa | 401, 403 nếu chưa phê duyệt |
| POST | `/api/install-logs` | 201 | 401 |
| CRUD | `/api/admin/packages`, `/users`, `/rules`, `/settings` | — | 403 |
| GET | `/api/admin/machines`, `/api/admin/logs?export=csv` | — | 403 |
| UDP 47800 | dò máy chủ trong LAN | tên và URL máy chủ | — |

### Lệnh Packager (admin, cũng làm được trong màn hình Admin)
`init <thư mục>` (tạo kho mới cùng admin đầu tiên), `add-package`, `update-package`, `remove-package`, `add-user`, `remove-user`, `export-logs`. Nhân bản kho = copy nguyên thư mục.

### Kiểm thử
- **Unit test:** crypto, key wrap, chữ ký catalog, VersionComparer (≥ 30 trường hợp), CrackDetector, RuleEvaluator, ánh xạ mã trả về, PostActionRunner. Coverage của Core ≥ 90%.
- **Integration test:** luồng USB với thư mục giả và serial mock; luồng máy chủ với WebApplicationFactory và SQLite chạy trong bộ nhớ.
- **E2E thủ công trên máy ảo:** Windows OEM/Retail; KMS loopback được dựng chỉ bằng cấu hình registry (không dùng tool crack thật); có và không có Office; copy kho bằng tay; rút USB khi đang cài; mất mạng tới máy chủ.

### Bảo mật và hiệu năng
- Argon2id (64MB, t = 3). AES-256-GCM. ECDSA P-256. Ký số file exe là tùy chọn khi phát hành.
- Máy chủ dùng HTTPS với chứng chỉ tự ký, client ghim (pin) chứng chỉ khi kết nối lần đầu `[ASSUMPTION]`.
- Giới hạn đăng nhập 10 lần/phút/IP.
- Quét phần cứng, mạng và bản quyền dưới 10 giây. Quét từ khóa tối đa `max_seconds`.
- `[ASSUMPTION: chỉ dùng trên máy của tổ chức, có quy định nội bộ cho phép thu thập dữ liệu thiết bị]`

---

## SECTION 3: TASKS

| # | Task | Phụ thuộc |
|---|---|---|
| 1 | Khung solution .NET 8 (Core, App, Server, Packager, Tests), app.manifest | — |
| 2 | Crypto: AES-GCM stream, Argon2id key wrap, chữ ký catalog | 1 |
| 3 | Vault portable (open, unlock, verify chữ ký, lockout, phát hiện ổ chỉ đọc) | 2 |
| 4 | Packager: init, add/update/remove-package, users | 3 |
| 5 | (gộp vào Task 4: nhân bản = copy thư mục, không cần task riêng) | — |
| 6 | Quét phần cứng | 1 |
| 7 | Quét lịch sử mạng | 1 |
| 8 | Quét bản quyền Windows/Office và CrackDetector | 1 |
| 9 | Phần mềm đã cài và VersionComparer | 1 |
| 10 | Quét file từ khóa và RuleEvaluator | 6–9 |
| 11 | Bộ cài MSI/EXE/ZIP, post-actions, TempWorkspace | 2 |
| 12 | Log mã hóa, fallback, xuất CSV | 3 |
| 13 | IRepositorySource và UsbRepositorySource | 3, 12 |
| 14 | App WPF: chọn chế độ, đăng nhập, báo cáo quét, danh sách gói, cài, hướng dẫn | 10, 11, 13 |
| 15 | Server: host HTTP nhúng trong app (`--server`), auth, SQLite cho log và yêu cầu | 3 |
| 16 | Server: catalog, yêu cầu phê duyệt, blob, logs, dò LAN | 10, 15 |
| 17 | ServerRepositorySource, chờ phê duyệt, gửi lại log | 13, 16 |
| 18 | App WPF: màn hình Admin (quản lý gói/user, danh sách chờ phê duyệt, log) | 14, 17 |
| 19 | Đóng gói: build-portable.ps1 (publish ra thư mục chạy được ở bất cứ đâu) | 14 |

Tiêu chí chấp nhận chi tiết cho từng task nằm trong `docs/plan.md` (sinh bằng `/writing-plans`).

**Review checkpoint:** Trước khi làm Task 8, hãy chạy `slmgr /dlv` và `cscript ospp.vbs /dstatus` trên vài máy thật để chốt danh sách dấu hiệu crack.

---

## Assumptions to review
> **Đã chốt (2026-11-10):** không gắn kho với thiết bị; chạy portable từ USB, đĩa hoặc thư mục copy; máy chủ LAN quy mô nhỏ có bước admin phê duyệt, cài xong tháo mạng.

1. Chống sửa kho bằng chữ ký ECDSA, khóa bí mật bọc bằng mật khẩu admin (bảo mật vừa phải, đổi lấy sự đơn giản) — **MEDIUM**
2. Client chạy với quyền admin Windows (UAC), vì cài .msi/.exe ngầm bắt buộc phải có quyền này — **MEDIUM**
3. Đánh giá crack dựa trên dấu hiệu, có ba mức kèm bằng chứng — **MEDIUM**
4. Chỉ dùng trên máy của tổ chức — **MEDIUM**
5. Máy chủ là chính app chạy `--server`, dùng SQLite — **LOW**
6. Máy chủ LAN dùng HTTP thường; gói đã được mã hóa sẵn nên không lộ nội dung — **LOW**
7. Yêu cầu chờ phê duyệt quá 3 phút thì hủy — **LOW**
8. Lịch sử mạng lấy từ registry và event log (có thể đã bị xóa) — **MEDIUM**
9. So sánh phiên bản ưu tiên ProductCode, sau đó mới đến regex tên — **MEDIUM**
10. Khóa tài khoản sau 5 lần sai trong 15 phút, JWT 8h — **LOW**
