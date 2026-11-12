# SecureInstaller — Spec Plan 1.1

> Spec này nối tiếp `docs/spec.md` (Plan 1, đã gộp vào `main`). Nguồn đầu vào: `docs/superpowers/backlog-plan-1.1.md` mục A, B, C và góp ý của người dùng sau khi chạy thử bản demo.
> Mọi `[ASSUMPTION: ...]` cần được xác nhận. Danh sách tổng hợp nằm ở cuối file.

---

## SECTION 1: SPEC

### Mục đích
Vá các lỗ hổng leo thang quyền còn tồn tại, chuẩn bị chỗ nối cho máy chủ LAN, và làm lại trải nghiệm sử dụng: báo cáo quét gọn và xem lại được, danh sách phần mềm có icon, chọn cài nhiều phần mềm hoặc cài theo bộ gói, gỡ cài đặt, và dung lượng nhỏ hơn.

### Người dùng và tình huống sử dụng
- Là **người dùng**, tôi muốn nhìn báo cáo quét gọn gàng, có phần tổng quan ở đầu, thiết bị ngoại vi tách riêng và chia theo nhóm, để đọc nhanh.
- Là **người dùng**, tôi muốn mở lại báo cáo quét bất cứ lúc nào trong phiên làm việc.
- Là **admin**, tôi muốn xem lại báo cáo quét của các lần trước, theo từng máy, để đối chiếu.
- Là **người dùng**, tôi muốn thấy icon của từng phần mềm, để nhận ra phần mềm nhanh hơn.
- Là **người dùng**, tôi muốn tick chọn nhiều phần mềm rồi cài cùng một lần, để không phải đứng chờ từng gói.
- Là **người dùng**, tôi muốn chọn một "bộ gói" (ví dụ *Máy kế toán*) để cài đủ các phần mềm theo đúng thứ tự.
- Là **admin**, tôi muốn tạo, sửa, xóa bộ gói.
- Là **người dùng**, tôi muốn gỡ một phần mềm có trong kho đã được cài trên máy.
- Là **admin**, tôi muốn thư mục phát hành nhẹ hơn để chép vào USB nhanh hơn.
- Là **tổ chức**, tôi muốn một chương trình chạy quyền thường trên máy không thể lợi dụng app (đang chạy quyền admin) để chạy mã độc hoặc xóa file hệ thống.

### Yêu cầu

**Bảo mật và thiết kế**
1. File cài đã giải mã chỉ nằm trong một thư mục mà người dùng thường không đọc, ghi, đổi tên hay xóa được. Nếu không tạo được thư mục an toàn như vậy thì không cài.
2. App không nạp thư viện từ thư mục mà người dùng thường ghi được, ngoài chính thư mục chứa app.
3. Thao tác sau cài (copy, xóa), thư mục đích của gói zip, và thao tác gỡ cài đặt đều từ chối thực hiện nếu một thư mục cha đã tồn tại trên đường dẫn đích là junction hoặc symlink.
4. Khóa giải mã gói được lấy qua nguồn kho (một hàm riêng), không đọc thẳng từ danh mục. Tài liệu hướng dẫn có khóa riêng, khác khóa của bộ cài. Log quét cũng được ghi qua nguồn kho.
5. Thông báo lỗi hiện cho người dùng luôn là tiếng Việt, dễ hiểu, và không lộ đường dẫn nội bộ hay chi tiết kỹ thuật. Chi tiết kỹ thuật chỉ ghi vào log.
6. Màn hình chính hiện **phiên bản kho** và **ngày cập nhật kho**. Khi kho ở chế độ chỉ đọc, mọi người dùng đều thấy thông báo "Kho chỉ đọc — nhật ký được lưu tạm trên máy này".
7. Nếu một file gói trong kho bị thay thế (khác kích thước so với danh mục), khi mở app sẽ báo ngay *"Kho bị sửa đổi, không thể sử dụng."*, không đợi đến lúc cài mới phát hiện.

**Báo cáo quét**
8. Đầu báo cáo có **dải tổng quan** gồm: kết luận bản quyền Windows, kết luận bản quyền Office, tình trạng internet, số file chứa từ khóa, số thiết bị ngoại vi. Mục nào đáng chú ý thì tô màu.
9. Các tab: **Máy tính** (thông tin gọn), **Thiết bị ngoại vi** (tách riêng, chia nhóm *Bàn phím & chuột / Lưu trữ USB / Máy in / Camera / Bluetooth / Âm thanh / Khác*, mỗi nhóm có số lượng, mặc định thu gọn, có ô tìm kiếm), **Mạng**, **Bản quyền**, **File từ khóa**.
10. Từ danh sách gói, người dùng mở lại được báo cáo quét của phiên hiện tại, rồi quay lại danh sách.
11. Mỗi lần quét được lưu đầy đủ, có mã hóa. Admin xem được **Lịch sử quét**: lọc theo máy và theo ngày, mở một lần quét ra xem ở chế độ chỉ đọc với cùng giao diện báo cáo `[ASSUMPTION: chỉ admin xem lịch sử; người dùng thường chỉ xem lần quét hiện tại]`.
12. Nếu máy cài cả bản x86 lẫn x64 của một phần mềm, báo cáo hiển thị cả hai phiên bản.

**Icon**
13. Mỗi gói có icon 48×48 px. Nguồn icon theo thứ tự ưu tiên: file `icon.png` trong thư mục gói; nếu không có thì lấy icon từ file `.exe` của bộ cài; nếu vẫn không có thì dùng icon mặc định. Icon được lấy lúc admin thêm gói. `[ASSUMPTION: không tự lấy icon từ file .msi; gói MSI dùng icon.png hoặc icon mặc định]`
14. Cột "Trên máy" hiện icon của phần mềm đã cài nếu Windows có lưu icon đó (giá trị `DisplayIcon`) `[ASSUMPTION: chỉ đọc icon từ đường dẫn thuộc HKLM; đường dẫn lỗi thì bỏ qua]`.

**Cài nhiều và bộ gói**
15. Mỗi dòng gói có ô tick. Dòng bị chặn thì không tick được. Nút **"Cài các mục đã chọn (n)"** cài lần lượt theo thứ tự hiển thị.
16. Nếu có gói cần xác nhận hạ cấp, app hỏi **gộp một lần trước khi bắt đầu**. Gói nào người dùng không đồng ý thì bỏ khỏi hàng đợi.
17. Màn hình cài hàng đợi hiện tiến độ "Gói i/n", thanh tiến độ của gói đang cài, và cuối cùng là bảng kết quả từng gói. Bấm **Hủy** sẽ dừng gói đang cài và bỏ qua các gói còn lại.
18. Một gói cài thất bại thì vẫn cài tiếp các gói sau `[ASSUMPTION]`, trừ khi lỗi là "cần khởi động lại" và gói sau có cờ `requires_reboot_first` (không làm trong v1.1).
19. **Bộ gói** gồm id, tên, mô tả, và danh sách (id gói + phiên bản tùy chọn, mặc định lấy bản mới nhất) theo thứ tự cài. Admin tạo, sửa, xóa bộ gói trong màn hình Admin. Bộ gói được lưu trong danh mục đã ký.
20. Người dùng chọn một bộ gói thì các gói trong bộ được tick sẵn đúng thứ tự. Gói bị chặn được liệt kê kèm lý do và bị bỏ qua.

**Gỡ cài đặt**
21. Gói nào đang được cài trên máy (theo quy tắc nhận diện, hoặc theo dấu cài đặt do app ghi) thì có nút **Gỡ cài đặt**, và app hỏi xác nhận trước khi gỡ.
22. Cách gỡ:
    - Gói MSI: gỡ theo ProductCode, chế độ im lặng.
    - Gói EXE: dùng lệnh gỡ do admin khai báo trong gói. Nếu gói không khai báo, dùng `QuietUninstallString` trong registry, **chỉ lấy từ HKLM**.
    - Gói ZIP: xóa thư mục đích và dấu cài đặt. Áp dụng đủ các kiểm tra đường dẫn cấm và junction.
23. Sau khi cài thành công, app ghi **dấu cài đặt** gồm id gói và phiên bản vào một khóa HKLM riêng của app. Gói zip nhờ dấu này mà được nhận diện là "đã cài".
24. Mỗi lần gỡ đều được ghi log, Action là `uninstall`.

**Dung lượng**
25. Tổng dung lượng thư mục phát hành nhỏ hơn hoặc bằng 120 MB (hiện 222 MB). `[ASSUMPTION: dùng nén single-file và giữ Packager là exe riêng trong tools\, KHÔNG gộp vào SecureInstaller.exe — vì app luôn yêu cầu quyền admin nên một process vừa là WPF vừa là CLI không ghi được kết quả ra console của cửa sổ gọi nó. Mục đích "giảm dung lượng" của G vẫn đạt.]`

### Các trường hợp đặc biệt
| Tình huống | Hành vi mong đợi |
|---|---|
| Thư mục `%ProgramData%\SecureInstaller\work` là junction hoặc có ACL lạ | Không cài; báo "Không tạo được thư mục làm việc an toàn" |
| Đích thao tác sau cài nằm dưới một junction do người dùng tạo | Thao tác đó lỗi, báo "Đường dẫn đích không an toàn"; gói vẫn được tính là đã cài |
| Hàng đợi có gói bị chặn | Không tick được; nếu đến từ bộ gói thì liệt kê lý do và bỏ qua |
| Hủy giữa hàng đợi | Gói đang cài bị hủy hoặc hoàn tác; các gói chưa cài ghi "Đã bỏ qua" |
| Gói EXE không có lệnh gỡ, cũng không có `QuietUninstallString` trong HKLM | Ẩn nút Gỡ, tooltip "Gói này không hỗ trợ gỡ tự động" |
| `UninstallString` chỉ có trong HKCU | Không dùng (người dùng thường ghi được HKCU, có thể bị lợi dụng để leo thang quyền) |
| Lịch sử quét quá nhiều | Danh sách phân trang, mỗi trang 200 bản ghi `[ASSUMPTION]` |
| Bộ gói trỏ tới gói đã bị xóa | Admin thấy cảnh báo khi lưu; phía người dùng hiện "Không có trong kho" và bỏ qua |
| Kho cũ từ Plan 1 (không có khóa docs riêng, không có kích thước blob) | Vẫn mở được: docs dùng khóa của gói, bỏ qua kiểm tra kích thước; admin thêm hoặc cập nhật gói thì danh mục tự nâng lên định dạng mới |
| Lấy icon từ exe bị lỗi | Dùng icon mặc định, không chặn việc thêm gói |

### Tiêu chí chấp nhận
```
Given app chạy với quyền admin
When cài một gói
Then thư mục làm việc nằm dưới %ProgramData%\SecureInstaller\work, ACL chỉ có Administrators và SYSTEM, owner là Administrators

Given một tiến trình chạy quyền thường đổi tên thư mục work\<guid> trong lúc cài
When app chạy bộ cài
Then thao tác đổi tên thất bại (Access denied)

Given C:\Tools là junction trỏ tới C:\Program Files\X
When thao tác sau cài xóa C:\Tools\Cache
Then thao tác bị từ chối và C:\Program Files\X\Cache vẫn còn

Given rút USB đúng lúc đang copy gói
When cài thất bại
Then người dùng thấy "Không đọc được dữ liệu từ kho. Hãy kiểm tra USB." và màn hình không hiện đường dẫn nội bộ nào

Given kho phiên bản 7, cập nhật ngày 11/11/2026
When đăng nhập
Then đầu màn hình hiện "Kho phiên bản 7 · cập nhật 11/11/2026"

Given một file .pkg.enc bị thay bằng file có kích thước khác
When mở app và đăng nhập
Then báo "Kho bị sửa đổi, không thể sử dụng."

Given máy có 23 thiết bị ngoại vi
When mở báo cáo quét
Then dải tổng quan hiện "Thiết bị: 23"; tab Thiết bị liệt kê các nhóm kèm số lượng, mặc định thu gọn

Given đang ở danh sách gói
When bấm "Xem lại báo cáo quét"
Then báo cáo của phiên hiện tại hiện ra; bấm Quay lại thì về danh sách gói, các dấu tick vẫn giữ nguyên

Given admin mở Lịch sử quét và chọn một lần quét của máy PC-01 ngày hôm qua
When bấm Xem
Then báo cáo đầy đủ của lần quét đó hiện ở chế độ chỉ đọc

Given thư mục gói có icon.png
When admin thêm gói
Then danh sách gói hiện đúng icon đó

Given người dùng tick 3 gói được phép, trong đó 1 gói cần hạ cấp
When bấm "Cài các mục đã chọn (3)"
Then app hỏi xác nhận đúng một lần, sau đó cài lần lượt và hiện "Gói 1/3", "Gói 2/3", "Gói 3/3"

Given đang cài gói 2/3
When bấm Hủy
Then gói 2 bị hủy và gói 3 ghi "Đã bỏ qua"

Given bộ gói "Máy kế toán" gồm A, B, C, trong đó B bị chặn
When người dùng chọn bộ gói này
Then A và C được tick sẵn theo thứ tự; B hiện kèm lý do chặn

Given gói zip demo đã được cài
When quét lại phần mềm trên máy
Then gói hiện "Bằng nhau" (nhận diện được nhờ dấu cài đặt)

Given gói MSI 7-Zip đã được cài
When bấm Gỡ cài đặt và xác nhận
Then 7-Zip biến mất khỏi Programs and Features và log có bản ghi uninstall thành công

Given chỉ HKCU có UninstallString của một phần mềm
When người dùng xem gói đó
Then không có nút Gỡ

Given bản build portable
When đo dung lượng thư mục SecureInstaller\
Then tổng nhỏ hơn hoặc bằng 120 MB
```

---

## SECTION 2: PLAN

### Công nghệ
Giữ nguyên Plan 1: .NET 8 `net8.0-windows`, WPF + CommunityToolkit.Mvvm, xUnit. **Không thêm thư viện ngoài**:
- Ảnh PNG được xử lý bằng `System.Windows.Media.Imaging` có sẵn trong WPF.
- Icon từ exe được lấy bằng P/Invoke `PrivateExtractIconsW`/`DestroyIcon`, rồi chuyển sang `BitmapSource` qua `Imaging.CreateBitmapSourceFromHIcon`. Phần này chạy trong App/Packager, Core chỉ nhận về bytes PNG.

### Thay đổi dữ liệu
- `PackageEntry` thêm các trường:
  - `DocsKeyB64?` (khóa riêng cho tài liệu)
  - `BlobSize` (long)
  - `IconPngB64?` (≤ 24 KB)
  - `Uninstall?: UninstallSpec(Type: "msi"|"command"|"zip", Command?, Args?)`
  - `MarkerKey` tự sinh `HKLM\SOFTWARE\SecureInstaller\Installed\<id>`
- `Catalog` thêm `Bundles: List<Bundle(Id, Name, Description, List<BundleItem(PackageId, Version?)>)>` và `Format` (2 = Plan 1.1).
- `InstalledApp` thêm `UninstallString?`, `QuietUninstallString?`, `DisplayIcon?`, `FromHklm` (bool), `IsMarker` (bool).
- `LogEntry` thêm `ScanReportJson?`. Chỉ có ở Action `scan`. Đã mã hóa vì cả dòng log được mã hóa.
- `DeviceInfo` thêm thuộc tính tính được `Category` (enum).
- Thư mục làm việc: `%ProgramData%\SecureInstaller\work\<guid N>`. Thư mục gốc được tạo với ACL bảo vệ, owner là Administrators. Thư mục dự phòng cho log `%ProgramData%\SecureInstaller\logs` được tạo theo cùng cách.

### Hàm và interface
- `IRepositorySource`:
  - Thêm `Task<byte[]> GetPackageKeyAsync(PackageEntry, ct)` và `Task<byte[]> GetDocsKeyAsync(PackageEntry, ct)`.
  - Bản Portable: lấy từ danh mục; nếu `DocsKeyB64` null thì dùng khóa của gói.
  - Log quét được ghi bằng `WriteLog` của nguồn kho.
- `SafePath.EnsureNoReparseAncestors(string fullPath)`: duyệt từng thư mục cha đã tồn tại; gặp reparse point thì ném `UnsafePathException`.
- `UninstallService.UninstallAsync(PackageEntry, InstalledApp?, IProgress, ct) → InstallOutcome`:
  - MSI: `MsiConfigureProductExW(code, INSTALLLEVEL_DEFAULT, INSTALLSTATE_ABSENT, "REBOOT=ReallySuppress")` với giao diện im lặng.
  - command: chạy qua `ExeInstaller`.
  - zip: `PostActionRunner` xóa thư mục đích, sau đó xóa marker.
- `InstallMarker.Write(id, version)` / `Remove(id)` / `ReadAll()` trên HKLM. `InstalledAppsScanner` gộp thêm kết quả từ marker.
- `ErrorText.ForUser(Exception) → string`: hàm map lỗi sang thông báo tiếng Việt, đặt trong Core. Các chuỗi mới được thêm vào `Strings`:
  - `IoError`: "Không đọc/ghi được dữ liệu. Hãy kiểm tra USB hoặc ổ đĩa."
  - `AccessDenied`: "Không đủ quyền truy cập."
  - `UnsafePath`: "Đường dẫn đích không an toàn."
  - `WorkspaceUnsafe`: "Không tạo được thư mục làm việc an toàn."
  - `Unexpected`: "Đã xảy ra lỗi không mong muốn."
- `VaultAdmin` thêm `AddBundle` / `UpdateBundle` / `RemoveBundle`.
- Packager thêm lệnh `add-bundle <root> <bundle.json>` và `remove-bundle`.
- `VaultStore.Unlock` kiểm tra mỗi blob có tồn tại và đúng `BlobSize` (nếu khác 0).
- `QueueInstallViewModel(items, run, onBack)`: danh sách `QueueItem(Name, Status, Message)`.
- `PeripheralCategorizer.Categorize(DeviceInfo) → DeviceCategory`, theo PNPClass và tiền tố DeviceId.
- Publish: `PublishSingleFile`, `EnableCompressionInSingleFile=true`, **bỏ** `IncludeNativeLibrariesForSelfExtract` (thư viện native nằm cạnh exe), `DebugType=none`.

### Mẫu thiết kế
Giữ đúng quy ước Plan 1:
- Core không phụ thuộc WPF.
- ViewModel nhận delegate để dễ test.
- Nguồn dữ liệu Windows nằm sau interface; registry HKLM cho marker đi qua `IRegistryReader` cộng thêm một `IRegistryWriter` mới.
- Chuỗi hiển thị lấy từ `Strings`.
- Thay đổi danh mục kho đi qua `WriteCatalog`.
- **Giao diện: implementer bắt buộc nạp skill `frontend-design`** trước khi làm view.

### Kiểm thử
- **Unit test:**
  - `SafePath` với junction thật (`mklink /J` chạy được khi không có quyền admin trên máy này).
  - ACL thư mục work: test chạy ở chế độ không nâng quyền, nhánh nâng quyền ghi vào checklist.
  - `ErrorText` có bảng ánh xạ.
  - `PeripheralCategorizer`.
  - Thứ tự cài và chọn phiên bản trong bộ gói.
  - Logic hàng đợi (hủy, bỏ qua, lỗi).
  - Marker dùng registry giả.
  - Mở kho định dạng 1 và nâng lên 2.
  - Kiểm tra `BlobSize`.
- **Test ViewModel:** các ViewModel mới, gồm cả ô tick, hàng đợi, nút xem lại báo cáo, lịch sử quét, bộ gói và gỡ cài đặt.
- **Checklist E2E bổ sung:**
  - Gỡ MSI thật.
  - Thử đổi tên thư mục work khi đang cài.
  - Junction ở thư mục cha.
  - Đo dung lượng bản build.
  - Hiển thị icon.
  - Cài 3 gói một lượt.

### Bảo mật và hiệu năng
- Quyền admin vẫn xác định theo khóa ký. Phần quản lý bộ gói và lịch sử quét đều tự kiểm tra `IsAdmin`.
- Không bao giờ chạy lệnh lấy từ HKCU khi đang có quyền admin.
- Icon tối đa 24 KB mỗi gói, nên danh mục vẫn nhỏ.
- Danh sách log và lịch sử quét được tải trong `Task.Run` và phân trang.

---

## SECTION 3: TASKS

| # | Task | Phụ thuộc |
|---|---|---|
| 1 | Thư mục làm việc trong %ProgramData% có ACL bảo vệ và owner; publish không tự giải nén thư viện native, nén single-file; kiểm tra dung lượng ≤ 120 MB | — |
| 2 | `SafePath` (kiểm tra junction ở thư mục cha) cho thao tác sau cài, TargetDir của zip, log dự phòng; ACL cho thư mục log dự phòng | — |
| 3 | Khóa gói lấy qua nguồn kho: `GetPackageKeyAsync` / `GetDocsKeyAsync`, `DocsKeyB64`, log quét đi qua nguồn kho; danh mục định dạng 2 tương thích ngược | — |
| 4 | `ErrorText.ForUser` và chuỗi mới; áp dụng ở InstallService, các bộ cài, ViewModel; ghi file log khi crash ở bản Release | 3 |
| 5 | `BlobSize` được kiểm tra khi Unlock; đầu màn hình hiện phiên bản và ngày kho; thông báo chỉ đọc cho mọi người dùng | 3 |
| 6 | `InstallMarker` (HKLM) + `IRegistryWriter`; InstalledAppsScanner đọc marker và các trường gỡ cài đặt, `FromHklm` | — |
| 7 | `UninstallService` (MSI / command / zip) + `UninstallSpec` trong manifest + log `uninstall` | 2, 6 |
| 8 | Icon: `icon.png` hoặc lấy từ exe lúc thêm gói, `IconPngB64`; đọc `DisplayIcon` từ HKLM | 3 |
| 9 | `PeripheralCategorizer` + lưu `ScanReportJson` vào log quét + hàm đọc lịch sử quét | 3 |
| 10 | Giao diện báo cáo quét mới (dải tổng quan, tab, thiết bị theo nhóm, tìm kiếm, x86/x64) + nút xem lại từ danh sách gói | 5, 9 |
| 11 | Danh sách gói mới: icon, ô tick, `QueueInstallViewModel`, xác nhận hạ cấp gộp một lần, nút Gỡ | 7, 8, 10 |
| 12 | Bộ gói ở Core: model, VaultAdmin, lệnh CLI, kiểm tra tham chiếu | 3 |
| 13 | Giao diện bộ gói (người dùng chọn, admin quản lý) + Lịch sử quét trong Admin | 10, 11, 12 |
| 14 | Dọn các mục nhỏ còn lại trong backlog C + cập nhật checklist E2E + script build + README 7-Zip | tất cả |

Tiêu chí chấp nhận chi tiết của từng task nằm trong plan, sinh bằng `/writing-plans`.

**Review checkpoint:** Trước khi bắt đầu, hãy xác nhận giả định 1 (không gộp Packager vào exe, thay bằng nén). Đây là điểm duy nhất khác với yêu cầu bạn đã nêu.

---

## Assumptions to review
1. **G được làm bằng nén single-file, không gộp Packager vào app.** App bắt buộc chạy quyền admin, nên phần dòng lệnh gộp vào app sẽ không in được kết quả ra cửa sổ console. Dung lượng ước tính giảm từ 222 MB xuống khoảng 100–120 MB. — **HIGH**
   *Sửa nếu:* bạn vẫn muốn chỉ có một file exe duy nhất. Khi đó lệnh CLI phải ghi kết quả ra file thay vì ra console.
2. Một gói lỗi trong hàng đợi thì vẫn cài tiếp các gói sau. — **MEDIUM**
   *Sửa nếu:* bạn muốn dừng cả hàng đợi ngay khi có một gói lỗi.
3. Chỉ admin xem được lịch sử quét. — **MEDIUM**
4. Gỡ cài đặt dành cho mọi người dùng, giống quyền cài đặt. — **MEDIUM**
   *Sửa nếu:* chỉ admin được gỡ.
5. Gói MSI không tự lấy icon (dùng icon.png hoặc icon mặc định). — **LOW**
6. Thư mục làm việc chuyển sang `%ProgramData%\SecureInstaller\work` (khác spec Plan 1 là `%TEMP%`). — **LOW** (bắt buộc vì lý do bảo mật)
7. Lịch sử quét phân trang 200 bản ghi mỗi trang. — **LOW**

---

## Bổ sung 2026-11-12 (góp ý sau khi chạy thử bản demo 1.1 — người dùng đã đồng ý toàn bộ)

### Yêu cầu bổ sung
26. **Form "Thêm/Sửa phần mềm"** trong Quản trị, không cần tự viết manifest:
    - Chọn file cài (.msi/.exe/.zip), điền tên, mã gói, phiên bản, tham số cài ngầm, thời gian chờ.
    - Ô **phiên bản tự nhập**; app điền sẵn nếu đọc được từ file (MSI: ProductVersion/ProductName/ProductCode; EXE: thông tin phiên bản của file).
    - Tùy chọn: file đi kèm (nhiều file, đặt cạnh file cài), icon, tài liệu hướng dẫn, script trước/sau khi cài, điều kiện chặn, cách gỡ.
27. **File đi kèm** được giải nén cùng thư mục với file cài trước khi chạy.
28. **Script cấu hình** (.ps1, .cmd, .bat) chạy **trước** và/hoặc **sau** khi cài, với quyền admin:
    - Script trước lỗi → không cài, gói báo "Lỗi".
    - Script sau lỗi → gói vẫn tính là đã cài, kèm cảnh báo "Cấu hình sau cài lỗi".
    - Có giới hạn thời gian; kết quả in ra được ghi vào log.
29. **Gói chỉ có script** (không cài phần mềm): loại gói "script", nhận diện "đã cài" bằng dấu cài đặt.
30. **Từ ngữ trạng thái**: Chưa cài · Cần cập nhật · Đã cài · Máy có bản mới hơn · Đã cài (không rõ phiên bản).
31. **Chọn bộ gói** → tự tick ngay (bỏ nút "Tick các gói trong bộ"), **không tick** phần mềm đã cài / máy có bản mới hơn / đã cài không rõ phiên bản; vẫn tick/bỏ tick từng phần mềm được. Có nút **"Bỏ chọn tất cả"**.
32. **Lịch sử thiết bị lưu trữ USB**: mọi USB/ổ cứng ngoài từng cắm vào máy — tên, số serial, lần kết nối đầu, lần kết nối gần nhất (nếu Windows còn lưu).
33. **Bằng chứng kết nối internet**:
    - Mỗi mạng có thêm cột **Internet**: Có / Chỉ nội bộ / Không rõ.
    - Dòng kết luận: "Máy đã từng kết nối internet: Có — lần gần nhất …" / "Có dấu hiệu gián tiếp — …" / "Chưa thấy bằng chứng (nhật ký có thể đã bị xóa hoặc ghi đè)".
    - App chỉ khẳng định được chiều "Có"; không chứng minh được "chưa từng".

### Tiêu chí chấp nhận bổ sung
```
Given admin mở "Thêm phần mềm" và chọn một file .msi
When form hiện ra
Then tên, phiên bản, ProductCode được điền sẵn; admin sửa được phiên bản rồi Lưu → gói xuất hiện trong danh sách

Given gói có file đi kèm config.xml
When cài
Then config.xml nằm cạnh setup.exe lúc bộ cài chạy

Given gói có script trước khi cài trả về mã lỗi 1
When cài
Then bộ cài không chạy, gói báo "Lỗi", log có phần in ra của script

Given gói có script sau khi cài bị lỗi
When cài
Then gói báo "Thành công" kèm "Cấu hình sau cài lỗi"

Given phần mềm đã cài đúng phiên bản gói
When xem danh sách
Then cột trạng thái ghi "Đã cài"

Given bộ gói A, B, C và máy đã cài B
When chọn bộ gói
Then A và C được tick ngay, B không tick và có ghi chú "đã cài trên máy"

Given máy từng cắm USB Kingston ngày 10/11/2026
When quét
Then mục Lịch sử thiết bị lưu trữ có Kingston kèm serial và lần kết nối đầu 10/11/2026

Given nhật ký Windows ghi mạng "Wifi-Nha" có internet
When quét
Then cột Internet của "Wifi-Nha" là "Có" và dòng kết luận là "Máy đã từng kết nối internet: Có — lần gần nhất …"
```
