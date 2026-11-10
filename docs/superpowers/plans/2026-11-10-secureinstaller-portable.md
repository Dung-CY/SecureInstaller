# SecureInstaller — Plan 1: Core + Chế độ Portable

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Một thư mục `SecureInstaller\` chạy được từ USB, đĩa hoặc ổ máy. Người dùng đăng nhập, quét máy (phần cứng, lịch sử mạng, bản quyền, phiên bản), rồi cài ngầm các gói từ kho mã hóa. Admin quản lý kho trong app hoặc bằng CLI.

**Architecture:** `Installer.Core` chứa toàn bộ logic (crypto, vault, scanning, licensing, versioning, rules, install, logging). Mọi nguồn dữ liệu Windows (WMI, Registry, service, file) nằm sau interface để test bằng fake. `Installer.App` (WPF, MVVM) và `Installer.Packager` (CLI) chỉ là lớp vỏ mỏng gọi Core. Nguồn kho được trừu tượng hóa bằng `IRepositorySource`; Plan 2 (máy chủ LAN) sẽ thêm `ServerRepositorySource` mà không phải sửa Core.

**Tech Stack:** .NET 8 (`net8.0-windows`), WPF, CommunityToolkit.Mvvm 8.2.2, Konscious.Security.Cryptography.Argon2 1.3.1, System.Management 8.0.0, System.ServiceProcess.ServiceController 8.0.1, xUnit 2.9.2 + xunit.runner.visualstudio 2.8.2 + Microsoft.NET.Test.Sdk 17.11.1.

**Spec:** [docs/spec.md](../../spec.md). Plan này bao phủ các yêu cầu 1–9 và 14–26. Yêu cầu 10–13 (máy chủ LAN) thuộc Plan 2.

## Global Constraints

- Chỉ hỗ trợ Windows 10/11 x64. Mọi project dùng `<TargetFramework>net8.0-windows</TargetFramework>`, `<Nullable>enable</Nullable>`, `<ImplicitUsings>enable</ImplicitUsings>`.
- Không có thư viện ngoài nào ngoài danh sách trong Tech Stack.
- JSON dùng `System.Text.Json` với `JsonNamingPolicy.SnakeCaseLower`, enum ghi dạng chuỗi snake_case (`Json.Options` trong Task 4). Mọi file JSON đều là UTF-8.
- Crypto: AES-256-GCM (nonce 12 byte, tag 16 byte); Argon2id với memory 65536 KiB, iterations 3, parallelism 2, output 32 byte, salt 16 byte; chữ ký ECDSA P-256 với SHA-256.
- Cấu trúc thư mục kho đúng như spec: `vault\vault.meta`, `vault\catalog.json.enc`, `vault\catalog.sig`, `vault\packages\<id>-<ver>.pkg.enc`, `vault\docs\<id>-<ver>\<tên>.enc`, `logs\*.log.enc`.
- Khóa tài khoản sau **5** lần sai liên tiếp, trong **15 phút**.
- Đường dẫn có thể chứa ký tự tiếng Việt và khoảng trắng (thư mục dự án là `Dự án 1`). Không bao giờ ghép đường dẫn vào chuỗi lệnh mà không đặt trong dấu nháy.
- File cài đã giải mã chỉ tồn tại trong `TempWorkspace` và bị xóa trong `finally`.
- Chuỗi hiển thị (giữ nguyên văn):
  - `Strings.VaultTampered` = `"Kho bị sửa đổi, không thể sử dụng."`
  - `Strings.BadCredentials` = `"Sai tên đăng nhập hoặc mật khẩu."`
  - `Strings.AccountLocked` = `"Tài khoản bị khóa đến {0:HH:mm}."`
  - `Strings.PackageCorrupt` = `"Gói bị hỏng hoặc không đầy đủ."`
  - `Strings.VaultNotFound` = `"Không tìm thấy kho (thư mục vault) cạnh SecureInstaller.exe."`
  - Lý do chặn: `"Windows có dấu hiệu crack"`, `"Office có dấu hiệu crack"`, `"Đã cài phiên bản {0} (bằng hoặc mới hơn)"`, `"Phát hiện {0} file chứa từ khóa"`, `"Yêu cầu kết nối internet"`
  - Verdict: `Genuine`→`"Chính hãng"`, `Suspicious`→`"Nghi vấn"`, `CrackSuspected`→`"Có dấu hiệu crack"`, `Unknown`→`"Không xác định"`, `NotInstalled`→`"Không cài"`
  - VersionStatus: `NotInstalled`→`"Chưa cài"`, `Older`→`"Cũ hơn"`, `Equal`→`"Bằng nhau"`, `Newer`→`"Mới hơn"`, `Incomparable`→`"Không so sánh được"`

## Review Focus

1. **Bộ cài lớn (vài GB) và ranh giới chunk:** giải mã phải chạy theo luồng, không nạp cả file vào RAM. Kích thước 0, 1, đúng chunk, chunk ± 1 đều phải đúng. Test nằm ở Task 2.
2. **Gói bị copy dở hoặc cụt** (rút USB khi đang copy): giải mã phải báo `PackageCorruptException` và không được cài phần dữ liệu đã đọc. Test cụt chunk cuối nằm ở Task 2, test luồng cài nằm ở Task 16.
3. **Đường dẫn tiếng Việt và khoảng trắng** (`D:\Claude Code\Dự án 1\...`) khi truyền cho msiexec/exe và khi mở kho. Test nằm ở Task 14 và Task 4.
4. **USB bị rút hoặc ổ chỉ đọc khi ghi log:** ghi log không được làm hỏng luồng cài, mà phải chuyển sang `%ProgramData%`. Test nằm ở Task 15.
5. **Registry có giá trị sai kiểu hoặc thiếu** (DisplayVersion là DWORD, DateLastConnected thiếu, ProductKeyChannel null): các scanner phải bỏ qua mục đó, không ném exception. Test nằm ở Task 8, 9, 10.

---

## File Structure

```
SecureInstaller.sln
Directory.Build.props                         TargetFramework, Nullable, ImplicitUsings chung
src/Installer.Core/
  Strings.cs                                  chuỗi hiển thị (Global Constraints)
  Crypto/PackageCrypto.cs                     AES-GCM theo chunk, stream
  Crypto/KeyDerivation.cs                     Argon2id
  Crypto/KeyWrap.cs                           bọc khóa bằng AES-GCM
  Crypto/CatalogSigner.cs                     ECDSA P-256
  Model/Catalog.cs                            Catalog, PackageEntry, DetectRule, PostAction, RuleSpec, KeywordScanConfig, CrackSignature
  Model/Json.cs                               JsonSerializerOptions dùng chung
  Vault/VaultMeta.cs                          bản ghi vault.meta, UserEntry
  Vault/VaultStore.cs                         Create / Open / Unlock / IsReadOnly
  Vault/VaultSession.cs
  Vault/VaultAdmin.cs                         thao tác của admin (gói, user, settings)
  Vault/PackageBuilder.cs                     thư mục nguồn → .pkg.enc + docs
  Vault/Exceptions.cs
  Platform/IWmi.cs, Platform/Wmi.cs
  Platform/IRegistryReader.cs, Platform/RegistryReader.cs
  Platform/ISystemInfo.cs, Platform/SystemInfo.cs   services, file, hosts, domain, hostname
  Platform/IConnectivityProbe.cs, Platform/ConnectivityProbe.cs
  Scanning/HardwareScanner.cs
  Scanning/NetworkHistoryScanner.cs
  Scanning/KeywordFileScanner.cs
  Scanning/MachineScanner.cs                  gom thành ScanReport
  Scanning/ScanReport.cs
  Licensing/LicenseInfo.cs, Licensing/LicenseScanner.cs, Licensing/CrackDetector.cs
  Versioning/InstalledAppsScanner.cs, Versioning/VersionComparer.cs
  Rules/RuleEvaluator.cs
  Install/ExitCodeMapper.cs, Install/InstallTypes.cs
  Install/ExeInstaller.cs, Install/ZipInstaller.cs, Install/MsiInstaller.cs, Install/MsiNative.cs
  Install/PostActionRunner.cs, Install/TempWorkspace.cs
  Install/InstallService.cs                   điều phối: giải mã → cài → post-actions → log → dọn
  Repository/IRepositorySource.cs, Repository/PortableRepositorySource.cs
  Logging/LogEntry.cs, Logging/EncryptedLog.cs, Logging/CsvExporter.cs
src/Installer.Packager/Program.cs, Args.cs
src/Installer.App/                            App.xaml, app.manifest, ViewModels/*, Views/*
tests/Installer.Core.Tests/                   một file test cho mỗi file Core, Fakes/*
tests/Installer.App.Tests/                    test ViewModel
build/build-portable.ps1
docs/e2e-checklist.md
```

---

### Task 1: Khung solution

**Files:**
- Create: `SecureInstaller.sln`, `Directory.Build.props`, `src/Installer.Core/Installer.Core.csproj`, `src/Installer.App/Installer.App.csproj` (WPF, `<UseWPF>true</UseWPF>`, `<OutputType>WinExe</OutputType>`, `<AssemblyName>SecureInstaller</AssemblyName>`, `<ApplicationManifest>app.manifest</ApplicationManifest>`), `src/Installer.App/app.manifest`, `src/Installer.Packager/Installer.Packager.csproj` (Exe, `<AssemblyName>SecureInstaller.Packager</AssemblyName>`), `tests/Installer.Core.Tests/Installer.Core.Tests.csproj`, `tests/Installer.App.Tests/Installer.App.Tests.csproj`, `tests/Installer.Core.Tests/SmokeTest.cs`

**Interfaces:**
- Produces: các project trên. App và Packager tham chiếu Core, các project test tham chiếu project tương ứng. `app.manifest` có `<requestedExecutionLevel level="requireAdministrator" uiAccess="false"/>`.

- [ ] **Step 1:** Kiểm tra SDK: chạy `dotnet --version`, kết quả phải bắt đầu bằng `8.`. Nếu không, dừng lại và báo người dùng cài `winget install Microsoft.DotNet.SDK.8`.
- [ ] **Step 2:** Tạo solution và các project bằng `dotnet new sln`, `classlib`, `wpf`, `console`, `xunit`, rồi `dotnet sln add` và `dotnet add reference`. Thêm các package đúng phiên bản như Tech Stack: Core dùng Argon2, System.Management, ServiceController; App dùng CommunityToolkit.Mvvm.
- [ ] **Step 3:** Viết `SmokeTest.Core_assembly_loads`, kiểm tra `typeof(Installer.Core.Strings).Assembly` khác null.
- [ ] **Step 4:** Chạy `dotnet build` và `dotnet test`. Kết quả mong đợi: build không lỗi, `Passed: 1`.
- [ ] **Step 5:** Commit với message `chore: scaffold SecureInstaller solution`.

---

### Task 2: PackageCrypto (AES-GCM theo chunk)

**Files:**
- Create: `src/Installer.Core/Crypto/PackageCrypto.cs`, `src/Installer.Core/Vault/Exceptions.cs`
- Test: `tests/Installer.Core.Tests/Crypto/PackageCryptoTests.cs`

**Interfaces:**
- Produces:
  - `static class PackageCrypto { const int ChunkSize = 1 << 20; static void Encrypt(Stream plain, Stream output, byte[] key32); static void Decrypt(Stream cipher, Stream output, byte[] key32); static byte[] EncryptBytes(byte[] plain, byte[] key32); static byte[] DecryptBytes(byte[] cipher, byte[] key32); }`
  - `class PackageCorruptException : Exception`. Trong `Exceptions.cs` còn có: `VaultNotFoundException`, `VaultTamperedException`, `BadCredentialsException`, `AccountLockedException(DateTimeOffset Until)`, `NotAdminException`.
- Định dạng file: header `"SIPK"` (4 byte ASCII) + version `1` (1 byte) + base nonce (8 byte ngẫu nhiên). Theo sau là các chunk: `int32 LE độ dài ciphertext` + ciphertext + tag 16 byte. Nonce của chunk i = base nonce (8 byte) + `uint32 BE i`. AAD = 1 byte, `1` nếu là chunk cuối, `0` nếu không. Luôn có ít nhất một chunk, kể cả khi dữ liệu rỗng (chunk rỗng mang cờ cuối).
- Mọi lỗi (sai magic, sai tag, hết dữ liệu trước chunk cuối) đều ném `PackageCorruptException`.

- [ ] **Step 1: Viết test lỗi**
```csharp
[Theory] [InlineData(0)] [InlineData(1)] [InlineData(PackageCrypto.ChunkSize - 1)]
[InlineData(PackageCrypto.ChunkSize)] [InlineData(PackageCrypto.ChunkSize + 1)] [InlineData(3 * PackageCrypto.ChunkSize + 7)]
void Roundtrip_any_size(int size) // random bytes → Encrypt → Decrypt → Assert.Equal(original, result)
[Fact] void Wrong_key_throws_PackageCorrupt()
[Fact] void Flipped_byte_throws_PackageCorrupt()          // đổi 1 byte ở offset 100
[Fact] void Truncated_last_chunk_throws_PackageCorrupt()   // 2.5 chunk, cắt bỏ chunk cuối cùng theo đúng ranh giới chunk
[Fact] void Bad_magic_throws_PackageCorrupt()
[Fact] void Decrypt_streams_without_buffering_whole_file()  // 64 MiB: GC.GetTotalAllocatedBytes tăng < 16 MiB khi đọc đầu ra theo từng khối
```
- [ ] **Step 2:** Chạy `dotnet test --filter PackageCryptoTests`. Kết quả mong đợi: FAIL (chưa có kiểu `PackageCrypto`).
- [ ] **Step 3:** Cài đặt `PackageCrypto` dùng `System.Security.Cryptography.AesGcm(key, 16)`, buffer 1 chunk tái sử dụng, nonce lấy từ `RandomNumberGenerator`.
- [ ] **Step 4:** Chạy lại lệnh ở Step 2. Kết quả mong đợi: PASS hết 11 test.
- [ ] **Step 5:** Commit với message `feat(core): chunked AES-GCM package encryption`.

---

### Task 3: KeyDerivation, KeyWrap, CatalogSigner

**Files:**
- Create: `src/Installer.Core/Crypto/KeyDerivation.cs`, `KeyWrap.cs`, `CatalogSigner.cs`
- Test: `tests/Installer.Core.Tests/Crypto/KeyTests.cs`

**Interfaces:**
- Produces:
  - `static byte[] KeyDerivation.DeriveKek(string password, byte[] salt16)`: Argon2id theo đúng tham số ở Global Constraints. Password mã hóa UTF-8.
  - `static byte[] KeyDerivation.NewSalt()` (16 byte), `static byte[] KeyDerivation.NewKey()` (32 byte).
  - `static byte[] KeyWrap.Wrap(byte[] secret, byte[] kek)`: kết quả là `nonce(12) || ciphertext || tag(16)`. `static byte[] KeyWrap.Unwrap(byte[] wrapped, byte[] kek)`: sai khóa thì ném `BadCredentialsException`.
  - `static (byte[] PrivatePkcs8, byte[] PublicSpki) CatalogSigner.NewKeyPair()`, `static byte[] CatalogSigner.Sign(byte[] data, byte[] privatePkcs8)`, `static bool CatalogSigner.Verify(byte[] data, byte[] signature, byte[] publicSpki)`. Dùng `ECDsa.Create(ECCurve.NamedCurves.nistP256)`, SHA-256, định dạng IEEE P1363.

- [ ] **Step 1: Viết test lỗi**
```csharp
[Fact] void DeriveKek_is_deterministic_and_32_bytes()      // cùng password và salt → bằng nhau, Length == 32
[Fact] void DeriveKek_differs_by_salt_and_password()
[Fact] void DeriveKek_handles_vietnamese_password()        // "Mật-khẩu ưu tiên 1" → 32 byte, không ném exception
[Fact] void Wrap_unwrap_roundtrip()
[Fact] void Unwrap_wrong_kek_throws_BadCredentials()
[Fact] void Sign_verify_true_and_false_after_tamper()      // đổi 1 byte trong data → Verify == false
[Fact] void Verify_with_other_public_key_is_false()
```
- [ ] **Step 2:** Chạy `dotnet test --filter KeyTests`. Kết quả mong đợi: FAIL.
- [ ] **Step 3:** Cài đặt ba lớp theo đúng chữ ký ở trên. Argon2 dùng `Konscious.Security.Cryptography.Argon2id` với `MemorySize = 65536`, `Iterations = 3`, `DegreeOfParallelism = 2`.
- [ ] **Step 4:** Chạy lại lệnh ở Step 2. Kết quả mong đợi: PASS 7/7.
- [ ] **Step 5:** Commit với message `feat(core): key derivation, wrapping, catalog signing`.

---

### Task 4: Model + VaultStore (Create / Open / Unlock)

**Files:**
- Create: `src/Installer.Core/Strings.cs`, `Model/Catalog.cs`, `Model/Json.cs`, `Vault/VaultMeta.cs`, `Vault/VaultStore.cs`, `Vault/VaultSession.cs`
- Test: `tests/Installer.Core.Tests/Vault/VaultStoreTests.cs`

**Interfaces:**
- Consumes: `PackageCrypto.EncryptBytes/DecryptBytes`, `KeyDerivation.*`, `KeyWrap.*`, `CatalogSigner.*` từ Task 2–3.
- Produces:
```csharp
enum InstallerType { Msi, Exe, Zip }
enum UserRole { User, Admin }
record DetectRule(string? ProductCode, string? DisplayNameRegex);
record PostAction(string Type /* "copy" | "delete" */, string? Src, string Dst);
record RuleSpec(string Type);   // block_if_windows_cracked | block_if_office_cracked | block_if_installed_version_gte | block_if_keyword_file | require_internet
record KeywordScanConfig(List<string> Keywords, List<string> Paths, int MaxSeconds);
record CrackSignature(string Kind /* kms_host | service | file | hosts_block */, string Pattern);
record PackageEntry(string Id, string Name, string Version, InstallerType InstallerType, string InstallerFile,
    string SilentArgs, string? TargetDir, int TimeoutSec, DetectRule Detect, List<string> Docs,
    List<PostAction> PostActions, List<RuleSpec> Rules, string BlobFile, string Sha256, string KeyB64);
record Catalog(int Version, DateTimeOffset UpdatedAt, List<PackageEntry> Packages,
    KeywordScanConfig KeywordScan, List<CrackSignature> CrackSignatures);
static class Json { static JsonSerializerOptions Options; }
record UserEntry(string Username, UserRole Role, string SaltB64, string WrappedVkB64, string? WrappedSigningKeyB64,
    int FailedAttempts, DateTimeOffset? LockedUntil);
record VaultMeta(int Format /* = 1 */, Guid VaultId, string SigningPublicKeyB64, List<UserEntry> Users);
sealed class VaultStore {
  string Root { get; }  bool IsReadOnly { get; }  VaultMeta Meta { get; }
  static VaultStore Create(string root, string adminUser, string adminPassword); // ném IOException nếu vault\ đã tồn tại
  static VaultStore Open(string root);              // ném VaultNotFoundException
  VaultSession Unlock(string username, string password, DateTimeOffset now);
  internal void SaveMeta(VaultMeta meta);
}
sealed class VaultSession { string Username; bool IsAdmin; byte[] VaultKey; byte[]? SigningPrivateKey; Catalog Catalog; VaultStore Store; }
static Catalog Catalog.Empty() // Version 0, KeywordScan(rỗng, rỗng, 60), CrackSignatures = CrackSignatureDefaults (Task 9 điền danh sách; ở task này để rỗng)
```
- Hành vi của `Unlock`:
  - Không có user, hoặc unwrap thất bại: ném `BadCredentialsException`, tăng `FailedAttempts`. Khi số lần sai ≥ 5 thì đặt `LockedUntil = now + 15 phút` và lưu meta (bỏ qua bước lưu nếu `IsReadOnly`).
  - Đang trong thời gian khóa: ném `AccountLockedException`, không kiểm tra mật khẩu.
  - Thành công: reset bộ đếm, giải mã catalog, rồi `Verify(catalog.json.enc bytes, catalog.sig)`. Nếu sai thì ném `VaultTamperedException`.
  - `IsAdmin` = có `WrappedSigningKeyB64` và unwrap được, **không tin trường `Role`** trong meta.
- `IsReadOnly`: thử tạo rồi xóa file `vault\.probe-<guid>`. Nếu gặp `UnauthorizedAccessException` hoặc `IOException` thì là true.

- [ ] **Step 1: Viết test lỗi** (thư mục tạm, cần có ít nhất một test đặt tên thư mục là `"Dự án 1"`)
```csharp
[Fact] void Create_then_Unlock_admin_IsAdmin_true_catalog_version_0()
[Fact] void Open_missing_vault_throws_VaultNotFound()
[Fact] void Wrong_password_throws_BadCredentials_and_increments_counter()
[Fact] void Fifth_failure_locks_for_15_minutes()          // lần thứ 6 với mật khẩu đúng, now + 1 phút → AccountLocked, Until == t5 + 15 phút
[Fact] void Lock_expires_after_15_minutes()               // now + 16 phút → mở khóa thành công, bộ đếm = 0
[Fact] void Tampered_catalog_throws_VaultTampered()        // đổi 1 byte trong catalog.json.enc
[Fact] void Editing_role_in_meta_does_not_grant_admin()    // user có Role sửa thành Admin trong vault.meta → IsAdmin == false
[Fact] void Vault_copied_to_vietnamese_path_still_unlocks() // copy thư mục sang "...\Dự án 1\copy"
[Fact] void Json_uses_snake_case()                         // serialize PackageEntry có chứa "silent_args", "installer_type":"msi"
```
- [ ] **Step 2:** Chạy `dotnet test --filter VaultStoreTests`. Kết quả mong đợi: FAIL.
- [ ] **Step 3:** Cài đặt các kiểu và `VaultStore`. Admin đầu tiên nhận một VK mới, một cặp khóa ký mới, và `WrappedSigningKeyB64 = Wrap(privateKey, kek)`.
- [ ] **Step 4:** Chạy lại lệnh ở Step 2. Kết quả mong đợi: PASS 9/9.
- [ ] **Step 5:** Commit với message `feat(core): portable vault create/open/unlock`.

---

### Task 5: VaultAdmin + PackageBuilder

**Files:**
- Create: `src/Installer.Core/Vault/VaultAdmin.cs`, `Vault/PackageBuilder.cs`
- Test: `tests/Installer.Core.Tests/Vault/VaultAdminTests.cs`, `tests/Installer.Core.Tests/TestData/SamplePackage.cs` (helper tạo thư mục nguồn mẫu)

**Interfaces:**
- Consumes: `VaultSession`, `VaultStore.SaveMeta`, crypto (Task 2–4).
- Thư mục nguồn của một gói: `manifest.json` (các trường của `PackageEntry` **trừ** `BlobFile`, `Sha256`, `KeyB64`), `installer\<InstallerFile>`, `docs\*` (tùy chọn), `extras\*` (tùy chọn).
- Produces:
```csharp
sealed class VaultAdmin {
  VaultAdmin(VaultSession session);                        // ném NotAdminException nếu !IsAdmin
  PackageEntry AddPackage(string sourceFolder);             // ném InvalidOperationException nếu đã có cặp (id, version) trùng
  PackageEntry UpdatePackage(string id, string sourceFolder, bool keepOld); // keepOld=false → xóa mọi phiên bản cũ của id
  void RemovePackage(string id, string? version);           // version null → xóa mọi phiên bản
  void AddUser(string username, string password, UserRole role);
  void RemoveUser(string username);                          // không cho xóa admin cuối cùng (InvalidOperationException)
  void SetPassword(string username, string newPassword);
  void UpdateSettings(KeywordScanConfig keywordScan, List<CrackSignature> signatures);
}
static class PackageBuilder { static PackageEntry Build(string sourceFolder, string vaultRoot); }
```
- `Build`: đọc và kiểm tra manifest (thiếu `id`, `name`, `version`, `installer_type` hoặc `installer_file`, hoặc file cài không tồn tại → `InvalidDataException` có tên trường). Zip `installer\` + `extras\` thành stream, mã hóa bằng khóa riêng mới của gói vào `packages\<id>-<ver>.pkg.enc`. `Sha256` = hex chữ thường của file `.enc`. Mỗi doc được mã hóa bằng chính khóa đó vào `docs\<id>-<ver>\<tên>.enc`.
- Mỗi thao tác thay đổi catalog đều: `Version++`, `UpdatedAt = UtcNow`, mã hóa lại `catalog.json.enc` (ghi ra file `.tmp` rồi `File.Move` với overwrite), và ký lại `catalog.sig`.
- `AddUser` với role Admin: bọc cả VK lẫn khóa ký bằng KEK của user mới. Với role User: chỉ bọc VK.

- [ ] **Step 1: Viết test lỗi**
```csharp
[Fact] void AddPackage_writes_blob_docs_and_bumps_version() // catalog.Version == 1, File.Exists(blob), Sha256 khớp, mở lại kho thì thấy gói
[Fact] void AddPackage_missing_installer_file_throws_with_field_name()
[Fact] void AddPackage_duplicate_id_version_throws()
[Fact] void UpdatePackage_keepOld_false_removes_old_blob()
[Fact] void UpdatePackage_keepOld_true_keeps_both_versions()
[Fact] void RemovePackage_deletes_blob_and_docs()
[Fact] void User_role_cannot_construct_VaultAdmin()          // NotAdminException
[Fact] void Added_user_can_unlock_and_is_not_admin()
[Fact] void Added_admin_can_sign()                           // admin thứ hai AddPackage, rồi user mở kho không gặp VaultTampered
[Fact] void Cannot_remove_last_admin()
```
- [ ] **Step 2:** Chạy `dotnet test --filter VaultAdminTests`. Kết quả mong đợi: FAIL.
- [ ] **Step 3:** Cài đặt `PackageBuilder` và `VaultAdmin`. Dùng `System.IO.Compression.ZipArchive` ghi vào `FileStream` tạm trong `%TEMP%`, rồi xóa file tạm sau khi mã hóa.
- [ ] **Step 4:** Chạy lại lệnh ở Step 2. Kết quả mong đợi: PASS 10/10.
- [ ] **Step 5:** Commit với message `feat(core): vault admin operations and package builder`.

---

### Task 6: Packager CLI

**Files:**
- Create: `src/Installer.Packager/Program.cs`, `src/Installer.Packager/Args.cs`
- Test: `tests/Installer.Core.Tests/Packager/ArgsTests.cs` (thêm tham chiếu tới project Packager)

**Interfaces:**
- Consumes: `VaultStore`, `VaultAdmin`, `EncryptedLog.ReadAll` và `CsvExporter` (Task 15). Lệnh `export-logs` được nối vào ở Task 15; ở task này lệnh trả về exit code 2 kèm thông báo "chưa hỗ trợ".
- Produces (exit code 0 là OK, 1 là lỗi nghiệp vụ, 2 là sai cú pháp):
```
SecureInstaller.Packager init <root> --user <admin> [--password <pw>]
SecureInstaller.Packager add-package <root> <folder> --user <u> [--password <pw>]
SecureInstaller.Packager update-package <root> <id> <folder> [--keep-old] --user <u> [--password <pw>]
SecureInstaller.Packager remove-package <root> <id> [--version <v>] --user <u> [--password <pw>]
SecureInstaller.Packager add-user <root> <name> --role user|admin --new-password <pw> --user <u> [--password <pw>]
SecureInstaller.Packager remove-user <root> <name> --user <u> [--password <pw>]
SecureInstaller.Packager export-logs <root> <out.csv> --user <u> [--password <pw>]
```
- `record ParsedArgs(string Command, List<string> Positionals, Dictionary<string,string?> Options)`, `static ParsedArgs Args.Parse(string[] argv)`. Ném `ArgumentException` khi không có lệnh hoặc lệnh lạ. Thiếu `--password` thì hỏi từ console mà không hiện ký tự.
- Mọi exception của Core được in ra stderr bằng `ex.Message` và trả về 1. Không in stack trace.

- [ ] **Step 1: Viết test lỗi**
```csharp
[Fact] void Parse_add_package_positionals_and_options()     // ["add-package","D:\\Dự án 1\\usb","pkg","--user","admin"]
[Fact] void Parse_flag_without_value()                       // --keep-old → Options["keep-old"] == null, ContainsKey == true
[Fact] void Parse_unknown_command_throws()
[Fact] void Run_init_then_add_package_returns_0()            // Program.Run(string[]) → int, trên thư mục tạm
```
- [ ] **Step 2:** Chạy `dotnet test --filter ArgsTests`. Kết quả mong đợi: FAIL.
- [ ] **Step 3:** Cài đặt `Args.Parse` và `public static int Program.Run(string[] argv)`, có `Main` gọi `Run`.
- [ ] **Step 4:** Chạy lại lệnh ở Step 2. Kết quả mong đợi: PASS 4/4.
- [ ] **Step 5:** Commit với message `feat(packager): admin CLI`.

---

### Task 7: Platform abstractions + HardwareScanner

**Files:**
- Create: `src/Installer.Core/Platform/IWmi.cs`, `Wmi.cs`, `IRegistryReader.cs`, `RegistryReader.cs`, `ISystemInfo.cs`, `SystemInfo.cs`, `Scanning/HardwareScanner.cs`
- Test: `tests/Installer.Core.Tests/Fakes/FakeWmi.cs`, `FakeRegistry.cs`, `FakeSystemInfo.cs`, `tests/Installer.Core.Tests/Scanning/HardwareScannerTests.cs`

**Interfaces:**
- Produces:
```csharp
interface IWmi { IReadOnlyList<IReadOnlyDictionary<string, object?>> Query(string wql, string scope = @"root\cimv2"); }
interface IRegistryReader {
  IReadOnlyList<string> GetSubKeyNames(RegistryHive hive, RegistryView view, string path); // key không tồn tại → rỗng
  object? GetValue(RegistryHive hive, RegistryView view, string path, string name);        // không tồn tại → null
}
interface ISystemInfo { string Hostname { get; } bool IsDomainJoined { get; } IReadOnlyList<string> ServiceNames();
  bool PathExists(string expandedPath); string? ReadHostsFile(); }
record DiskInfo(string Model, string? Serial, string? InterfaceType);
record NicInfo(string Name, string Mac);
record DeviceInfo(string Name, string? PnpClass, string DeviceId);
record HardwareInfo(string Hostname, string? Manufacturer, string? Model, string? BiosSerial,
   List<DiskInfo> Disks, List<NicInfo> Nics, List<DeviceInfo> Peripherals, List<string> Errors);
sealed class HardwareScanner { HardwareScanner(IWmi wmi, ISystemInfo sys); HardwareInfo Scan(); }
```
- Các câu WQL:
  - `SELECT Manufacturer, Model FROM Win32_ComputerSystem`
  - `SELECT SerialNumber FROM Win32_BIOS`
  - `SELECT Model, SerialNumber, InterfaceType FROM Win32_DiskDrive` (serial được `Trim()`)
  - `SELECT Name, MACAddress FROM Win32_NetworkAdapter WHERE PhysicalAdapter = TRUE AND MACAddress IS NOT NULL`
  - `SELECT Name, PNPClass, PNPDeviceID FROM Win32_PnPEntity WHERE Present = TRUE`
- Peripherals là những thiết bị có `PNPDeviceID` bắt đầu bằng `USB\`, `HID\`, `BTHENUM\` hoặc `SWD\PRINTENUM\`, hoặc có `PNPClass` thuộc {`Printer`, `Camera`, `Image`, `Bluetooth`, `Keyboard`, `Mouse`}. Bỏ các bản trùng `DeviceId`.
- Mỗi query được bọc try/catch riêng. Lỗi thì ghi `"<Win32_Class>: <message>"` vào `Errors`, trường tương ứng để null hoặc rỗng.

- [ ] **Step 1: Viết test lỗi**
```csharp
[Fact] void Scan_maps_all_fields_from_fake_wmi()
[Fact] void Disk_serial_is_trimmed()                     // "  S4EVNX0N  " → "S4EVNX0N"
[Fact] void Peripherals_keep_usb_hid_printer_drop_pci()  // PCI\VEN_8086 bị loại
[Fact] void Failing_bios_query_records_error_and_keeps_others() // FakeWmi ném exception với Win32_BIOS → BiosSerial null, Errors[0] bắt đầu bằng "Win32_BIOS", Nics vẫn có dữ liệu
```
- [ ] **Step 2:** Chạy `dotnet test --filter HardwareScannerTests`. Kết quả mong đợi: FAIL.
- [ ] **Step 3:** Cài đặt các interface, bản thật (`Wmi` dùng `ManagementObjectSearcher`, `RegistryReader` dùng `RegistryKey.OpenBaseKey`, `SystemInfo` dùng `ServiceController.GetServices`, `IPGlobalProperties.GetIPGlobalProperties().DomainName != ""` và `%SystemRoot%\System32\drivers\etc\hosts`) và `HardwareScanner`.
- [ ] **Step 4:** Chạy lại lệnh ở Step 2. Kết quả mong đợi: PASS 4/4.
- [ ] **Step 5:** Commit với message `feat(core): platform abstractions and hardware scanner`.

---

### Task 8: NetworkHistoryScanner

**Files:**
- Create: `src/Installer.Core/Platform/IConnectivityProbe.cs`, `ConnectivityProbe.cs`, `Scanning/NetworkHistoryScanner.cs`
- Test: `tests/Installer.Core.Tests/Scanning/NetworkHistoryScannerTests.cs`

**Interfaces:**
- Consumes: `IRegistryReader` (Task 7).
- Produces:
```csharp
enum NetworkKind { Wireless, Wired, Vpn, Other }
record NetworkProfile(string Name, NetworkKind Kind, DateTime? FirstConnected, DateTime? LastConnected);
record NetworkHistory(List<NetworkProfile> Profiles, bool InternetNow);
interface IConnectivityProbe { Task<bool> HasInternetAsync(CancellationToken ct); }
sealed class NetworkHistoryScanner { NetworkHistoryScanner(IRegistryReader reg, IConnectivityProbe probe);
  Task<NetworkHistory> ScanAsync(CancellationToken ct); static DateTime? ParseSystemTime(object? value); }
```
- Đọc từ `HKLM` (Registry64) `SOFTWARE\Microsoft\Windows NT\CurrentVersion\NetworkList\Profiles\<guid>` các giá trị `ProfileName` (string), `NameType` (DWORD: 71 là Wireless, 6 là Wired, 23 là Vpn, còn lại là Other), `DateCreated`, `DateLastConnected` (REG_BINARY 16 byte theo cấu trúc SYSTEMTIME: 8 số uint16 little-endian lần lượt là year, month, dayOfWeek, day, hour, minute, second, ms; tạo `DateTime` với `DateTimeKind.Local`).
- `ParseSystemTime` trả null nếu không phải `byte[]` dài 16 byte, hoặc ngày không hợp lệ.
- Danh sách sắp xếp giảm dần theo `LastConnected` (null xếp cuối). Profile thiếu `ProfileName` thì bỏ qua.
- `ConnectivityProbe`: GET `http://www.msftconnecttest.com/connecttest.txt` với timeout 3 giây, trả true khi body bằng `"Microsoft Connect Test"`. Mọi exception đều trả false.

- [ ] **Step 1: Viết test lỗi**
```csharp
[Fact] void ParseSystemTime_decodes_bytes()     // {0xEA,0x07, 0x0B,0, 2,0, 0x0A,0, 0x0E,0, 0x1E,0, 0,0, 0,0} → 2026-11-10 14:30:00
[Fact] void ParseSystemTime_wrong_length_or_type_returns_null()  // byte[8], int 5, null
[Fact] void Scan_maps_kinds_and_sorts_by_last_connected()        // 3 profile: Wi-Fi(71), LAN(6), VPN(23)
[Fact] void Profile_with_missing_dates_is_kept_with_nulls()
[Fact] void Empty_registry_returns_empty_list_and_probe_value()  // không ném exception, InternetNow == giá trị probe trả về
```
- [ ] **Step 2:** Chạy `dotnet test --filter NetworkHistoryScannerTests`. Kết quả mong đợi: FAIL.
- [ ] **Step 3:** Cài đặt theo các quy tắc ở trên.
- [ ] **Step 4:** Chạy lại lệnh ở Step 2. Kết quả mong đợi: PASS 5/5.
- [ ] **Step 5:** Commit với message `feat(core): network connection history scanner`.

---

### Task 9: LicenseScanner + CrackDetector

**Files:**
- Create: `src/Installer.Core/Licensing/LicenseInfo.cs`, `LicenseScanner.cs`, `CrackDetector.cs`, `CrackSignatureDefaults.cs`
- Modify: `src/Installer.Core/Model/Catalog.cs` (`Catalog.Empty()` dùng `CrackSignatureDefaults.All`)
- Test: `tests/Installer.Core.Tests/Licensing/CrackDetectorTests.cs`, `LicenseScannerTests.cs`

**Interfaces:**
- Consumes: `IWmi`, `IRegistryReader`, `ISystemInfo` (Task 7), `CrackSignature` (Task 4).
- Produces:
```csharp
enum LicenseVerdict { Genuine, Suspicious, CrackSuspected, Unknown, NotInstalled }
enum LicenseChannel { Retail, Oem, VolumeMak, VolumeKms, Unknown }
record LicenseInfo(string Product, string? Edition, LicenseChannel Channel, bool? Activated,
   string? PartialKey, string? KmsHost, LicenseVerdict Verdict, List<string> Evidence);
record LicenseRaw(string Product, string? Edition, string? ChannelText, string? Description,
   int? LicenseStatus, string? PartialKey, string? KmsHost);
static class CrackDetector { static LicenseInfo Evaluate(LicenseRaw? raw, ISystemInfo sys, IReadOnlyList<CrackSignature> sigs, bool isOffice); }
sealed class LicenseScanner { LicenseScanner(IWmi wmi, IRegistryReader reg, ISystemInfo sys);
   LicenseInfo ScanWindows(IReadOnlyList<CrackSignature> sigs); LicenseInfo ScanOffice(IReadOnlyList<CrackSignature> sigs); }
static class CrackSignatureDefaults { static IReadOnlyList<CrackSignature> All; }
```
- WQL: `SELECT Name, Description, LicenseStatus, PartialProductKey, ProductKeyChannel, KeyManagementServiceMachine, DiscoveredKeyManagementServiceMachineName FROM SoftwareLicensingProduct WHERE ApplicationID='<appId>' AND PartialProductKey IS NOT NULL`. Windows dùng `55c92734-d682-4d71-983e-d6ec3f16059f`, Office dùng `0ff1ce15-a989-479d-af46-f275c6370663`. KmsHost = `KeyManagementServiceMachine`, rỗng thì lấy `Discovered...`.
- Office: nếu WMI không trả dòng nào, đọc `HKLM\SOFTWARE\Microsoft\Office\ClickToRun\Configuration` (`ProductReleaseIds`, `VersionToReport`). Có key này thì tạo `LicenseRaw` với Product = ProductReleaseIds, LicenseStatus = null. Không có thì `raw = null`.
- Ánh xạ channel, theo `ChannelText` trước rồi đến `Description`, không phân biệt hoa thường: chứa `OEM` → Oem, `Retail` → Retail, `MAK` → VolumeMak, `KMS` hoặc `GVLK` → VolumeKms, còn lại là Unknown. `Activated = LicenseStatus == 1`, và là null nếu LicenseStatus null.
- Thứ tự quyết định của `Evaluate`:
  1. `raw == null`: nếu là Office thì `NotInstalled`, nếu là Windows thì `Unknown` với evidence `"Không đọc được thông tin bản quyền"`.
  2. Một chữ ký bất kỳ khớp thì `CrackSuspected`. Mỗi chữ ký khớp thêm một evidence: `kms_host` (regex trên KmsHost) → `"KMS host = {host}"`; `service` (regex trên tên service) → `"Service: {name}"`; `file` (`Environment.ExpandEnvironmentVariables` + `PathExists`) → `"File: {path}"`; `hosts_block` (regex trên nội dung hosts) → `"File hosts chặn máy chủ kích hoạt"`.
  3. `Activated == false`: `Suspicious`, evidence `"Chưa kích hoạt"`.
  4. `Channel == VolumeKms && !IsDomainJoined`: `Suspicious`, evidence `"Key KMS trên máy không thuộc domain"`.
  5. `Activated == true`: `Genuine`.
  6. Còn lại: `Unknown`.
- `CrackSignatureDefaults.All`: kms_host `^(127\.|localhost$|0\.0\.0\.0|::1)`; service `^(KMSELDI|Service_KMS|KMSpico|AutoKMS|KMSAuto.*)$`; file `%ProgramFiles%\KMSpico`, `%SystemRoot%\SECOH-QAD.exe`, `%SystemRoot%\System32\SppExtComObjHook.dll`; hosts_block `(?m)^\s*[\d.]+\s+(activation|validation)(-v2)?\.sls\.microsoft\.com`.

- [ ] **Step 1: Viết test lỗi**
```csharp
[Fact] void Oem_activated_is_Genuine()                 // ChannelText "OEM:DM", status 1 → Genuine, Channel Oem, Evidence rỗng
[Fact] void Kms_loopback_is_CrackSuspected_with_evidence() // KmsHost "127.0.0.2" → Evidence chứa "KMS host = 127.0.0.2"
[Fact] void Known_service_is_CrackSuspected()           // ServiceNames chứa "KMSELDI"
[Fact] void Hosts_block_is_CrackSuspected()
[Fact] void Not_activated_is_Suspicious()
[Fact] void Gvlk_not_domain_joined_is_Suspicious_but_domain_joined_is_Genuine()
[Fact] void Null_raw_windows_Unknown_office_NotInstalled()
[Fact] void Scanner_null_ProductKeyChannel_uses_Description() // Description "..., VOLUME_KMSCLIENT channel" → VolumeKms
[Fact] void Scanner_wmi_throws_returns_Unknown_never_CrackSuspected()
[Fact] void Office_c2r_only_maps_product()              // không có dòng WMI, có key C2R "O365ProPlusRetail" → Product == "O365ProPlusRetail"
```
- [ ] **Step 2:** Chạy `dotnet test --filter "CrackDetectorTests|LicenseScannerTests"`. Kết quả mong đợi: FAIL.
- [ ] **Step 3:** Cài đặt theo các quy tắc ở trên. Regex dùng `RegexOptions.IgnoreCase` với timeout 1 giây.
- [ ] **Step 4:** Chạy lại lệnh ở Step 2. Kết quả mong đợi: PASS 10/10.
- [ ] **Step 5:** Commit với message `feat(core): Windows/Office license scan and crack heuristics`.

---

### Task 10: InstalledAppsScanner + VersionComparer

**Files:**
- Create: `src/Installer.Core/Versioning/InstalledAppsScanner.cs`, `VersionComparer.cs`
- Test: `tests/Installer.Core.Tests/Versioning/VersionComparerTests.cs`, `InstalledAppsScannerTests.cs`

**Interfaces:**
- Consumes: `IRegistryReader`, `DetectRule`.
- Produces:
```csharp
record InstalledApp(string DisplayName, string? DisplayVersion, string KeyName, bool Is64Bit, bool PerUser);
sealed class InstalledAppsScanner { InstalledAppsScanner(IRegistryReader reg); IReadOnlyList<InstalledApp> Find(DetectRule rule); }
enum VersionStatus { NotInstalled, Older, Equal, Newer, Incomparable }
record VersionCheck(VersionStatus Status, string? InstalledVersion, string PackageVersion, IReadOnlyList<InstalledApp> Matches);
static class VersionComparer { static int? Compare(string a, string b);   // null nếu một trong hai không có chữ số
   static VersionCheck Check(IReadOnlyList<InstalledApp> installed, string packageVersion); }
```
- Đọc ba nhánh `SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall`: HKLM Registry64, HKLM Registry32, HKCU Registry64.
- Khớp khi tên subkey bằng `ProductCode` (không phân biệt hoa thường), hoặc `DisplayName` khớp `DisplayNameRegex`.
- `DisplayVersion` không phải string (ví dụ DWORD) thì dùng `ToString()`. Không có `DisplayName` thì bỏ qua.
- `Compare`: lấy các cụm `\d+` thành danh sách `long`, thêm 0 cho bằng độ dài, so từng phần tử.
- `Check`: không có mục nào thì `NotInstalled`. Ngược lại lấy mục có phiên bản cao nhất trong những mục so sánh được. Không mục nào so sánh được thì `Incomparable`. installed < package là `Older`, bằng là `Equal`, lớn hơn là `Newer`.

- [ ] **Step 1: Viết test lỗi**
```csharp
[Theory]
[InlineData("22.01","24.08",-1)] [InlineData("1.2","1.2.0",0)] [InlineData("10.0.19045","10.0.9",1)]
[InlineData("2021 build 16.0.14332","16.0.14332",1)] [InlineData("v3.10","3.9",1)] [InlineData("1.0.0.1","1.0",1)]
void Compare_cases(string a, string b, int expected)   // so Math.Sign
[Fact] void Compare_no_digits_returns_null()            // ("latest","1.0")
[Fact] void Check_picks_highest_of_x86_and_x64()        // 22.01 (x86) + 23.01 (x64), gói 24.08 → Older, InstalledVersion "23.01"
[Fact] void Check_empty_is_NotInstalled()
[Fact] void Check_all_incomparable_is_Incomparable()
[Fact] void Find_matches_product_code_case_insensitive_in_wow64()
[Fact] void Find_matches_regex_in_hkcu()
[Fact] void Find_dword_display_version_is_stringified() // DisplayVersion = 5 (int) → "5"
```
- [ ] **Step 2:** Chạy `dotnet test --filter "VersionComparerTests|InstalledAppsScannerTests"`. Kết quả mong đợi: FAIL.
- [ ] **Step 3:** Cài đặt theo các quy tắc ở trên.
- [ ] **Step 4:** Chạy lại lệnh ở Step 2. Kết quả mong đợi: PASS hết.
- [ ] **Step 5:** Commit với message `feat(core): installed app detection and version comparison`.

---

### Task 11: KeywordFileScanner + MachineScanner/ScanReport

**Files:**
- Create: `src/Installer.Core/Scanning/KeywordFileScanner.cs`, `ScanReport.cs`, `MachineScanner.cs`
- Test: `tests/Installer.Core.Tests/Scanning/KeywordFileScannerTests.cs`, `MachineScannerTests.cs`

**Interfaces:**
- Consumes: `HardwareScanner`, `NetworkHistoryScanner`, `LicenseScanner`, `KeywordScanConfig`, `CrackSignature`.
- Produces:
```csharp
record KeywordScanResult(List<string> MatchedFiles, bool TimedOut, bool Truncated);
sealed class KeywordFileScanner { KeywordScanResult Scan(KeywordScanConfig cfg, CancellationToken ct); const int MaxMatches = 1000; }
record ScanReport(DateTimeOffset ScannedAt, HardwareInfo Hardware, NetworkHistory Network,
   LicenseInfo Windows, LicenseInfo Office, KeywordScanResult Keywords);
sealed class MachineScanner { MachineScanner(HardwareScanner hw, NetworkHistoryScanner net, LicenseScanner lic, KeywordFileScanner kw);
   Task<ScanReport> ScanAsync(Catalog catalog, IProgress<string>? step, CancellationToken ct); }
```
- Quét từ khóa: so khớp **tên file** có chứa từ khóa (không phân biệt hoa thường). Đường dẫn được mở rộng biến môi trường. Duyệt bằng `EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = ReparsePoint }`. Dừng khi quá `MaxSeconds` (`TimedOut = true`) hoặc khi đủ 1000 kết quả (`Truncated = true`). Thư mục không tồn tại thì bỏ qua.
- `MachineScanner` báo từng bước: `"Đang quét phần cứng"`, `"Đang đọc lịch sử mạng"`, `"Đang kiểm tra bản quyền"`, `"Đang tìm file theo từ khóa"`.

- [ ] **Step 1: Viết test lỗi**
```csharp
[Fact] void Finds_files_by_name_keyword_case_insensitive()  // thư mục tạm có "Bao_Cao_MAT.docx" và "Tài liệu Mật.txt"; keywords ["mat","mật"] → tìm thấy cả 2
[Fact] void Missing_path_is_skipped()
[Fact] void Stops_at_max_seconds()                           // MaxSeconds = 0 → TimedOut == true
[Fact] void Truncates_at_1000()                              // tạo 1005 file khớp → Count == 1000, Truncated
[Fact] void MachineScanner_reports_four_steps_in_order()     // dùng fake
```
- [ ] **Step 2:** Chạy `dotnet test --filter "KeywordFileScannerTests|MachineScannerTests"`. Kết quả mong đợi: FAIL.
- [ ] **Step 3:** Cài đặt theo các quy tắc ở trên.
- [ ] **Step 4:** Chạy lại lệnh ở Step 2. Kết quả mong đợi: PASS 5/5.
- [ ] **Step 5:** Commit với message `feat(core): keyword file scan and aggregated scan report`.

---

### Task 12: RuleEvaluator

**Files:**
- Create: `src/Installer.Core/Rules/RuleEvaluator.cs`
- Test: `tests/Installer.Core.Tests/Rules/RuleEvaluatorTests.cs`

**Interfaces:**
- Consumes: `ScanReport`, `PackageEntry`, `VersionCheck`, `Strings`.
- Produces: `record RuleResult(bool Allowed, List<string> Reasons, bool NeedsDowngradeConfirm);` và `static RuleResult RuleEvaluator.Evaluate(ScanReport scan, PackageEntry pkg, VersionCheck version);`
- Các rule:
  - `block_if_windows_cracked`: chặn khi `Windows.Verdict == CrackSuspected`.
  - `block_if_office_cracked`: chặn khi `Office.Verdict == CrackSuspected`.
  - `block_if_installed_version_gte`: chặn khi status là `Equal` hoặc `Newer`, lý do dùng `InstalledVersion`.
  - `block_if_keyword_file`: chặn khi `MatchedFiles.Count > 0`.
  - `require_internet`: chặn khi `!Network.InternetNow`.
  - Loại rule không biết thì bỏ qua.
- `NeedsDowngradeConfirm = Allowed && status == Newer`. Lý do lấy đúng nguyên văn từ Global Constraints, theo thứ tự rule trong gói.

- [ ] **Step 1: Viết test lỗi:** mỗi rule có một test bị chặn (kiểm tra đúng chuỗi lý do) và một test được phép. Thêm:
```csharp
[Fact] void Multiple_reasons_in_rule_order()
[Fact] void Newer_without_gte_rule_needs_downgrade_confirm()
[Fact] void Unknown_rule_type_is_ignored()
```
- [ ] **Step 2:** Chạy `dotnet test --filter RuleEvaluatorTests`. Kết quả mong đợi: FAIL.
- [ ] **Step 3:** Cài đặt `RuleEvaluator`.
- [ ] **Step 4:** Chạy lại lệnh ở Step 2. Kết quả mong đợi: PASS 13/13.
- [ ] **Step 5:** Commit với message `feat(core): install rule evaluation`.

---

### Task 13: TempWorkspace + PostActionRunner

**Files:**
- Create: `src/Installer.Core/Install/TempWorkspace.cs`, `PostActionRunner.cs`
- Test: `tests/Installer.Core.Tests/Install/TempWorkspaceTests.cs`, `PostActionRunnerTests.cs`

**Interfaces:**
- Produces:
```csharp
sealed class TempWorkspace : IDisposable { static TempWorkspace Create(); string Path { get; } }
record PostActionResult(PostAction Action, bool Ok, string? Error);
static class PostActionRunner { static List<PostActionResult> Run(IEnumerable<PostAction> actions, string extrasRoot); }
```
- `Create`: tạo `%TEMP%\SI_<guid:N>`. Tắt kế thừa quyền, chỉ cấp FullControl cho `BUILTIN\Administrators`, `NT AUTHORITY\SYSTEM` và SID của người dùng hiện tại (`DirectorySecurity.SetAccessRuleProtection(true,false)`).
- `Dispose`: xóa đệ quy (bỏ thuộc tính ReadOnly trước), thử lại 3 lần cách nhau 500ms. Không ném exception ra ngoài.
- `copy`: `Src` là đường dẫn tương đối nằm dưới `extrasRoot`. Từ chối nếu `Src` là đường dẫn tuyệt đối, hoặc sau khi `Path.GetFullPath` thì không còn nằm dưới `extrasRoot`. `Dst` được mở rộng biến môi trường, phải là đường dẫn tuyệt đối. Tự tạo thư mục cha, ghi đè file cũ.
- `delete`: `Dst` là đường dẫn tuyệt đối, xóa file hoặc thư mục đệ quy. Không tồn tại thì vẫn tính là Ok. Từ chối nếu là gốc ổ đĩa, `%SystemRoot%`, `%ProgramFiles%`, `%ProgramFiles(x86)%` hoặc `%UserProfile%`.
- Một action lỗi không làm dừng các action sau.

- [ ] **Step 1: Viết test lỗi**
```csharp
[Fact] void Workspace_deleted_on_dispose_even_after_exception() // using + throw → !Directory.Exists
[Fact] void Workspace_acl_is_protected_and_has_three_rules()
[Fact] void Copy_creates_dirs_and_matches_hash()                 // Dst trong thư mục tạm "Dự án 1\out\a.ini"
[Fact] void Copy_rejects_parent_traversal()                      // Src "..\..\secret.txt" → Ok false
[Fact] void Delete_missing_is_ok()
[Fact] void Delete_refuses_protected_roots()                     // "C:\\", "%SystemRoot%" → Ok false
[Fact] void Failure_does_not_stop_following_actions()
```
- [ ] **Step 2:** Chạy `dotnet test --filter "TempWorkspaceTests|PostActionRunnerTests"`. Kết quả mong đợi: FAIL.
- [ ] **Step 3:** Cài đặt theo các quy tắc ở trên.
- [ ] **Step 4:** Chạy lại lệnh ở Step 2. Kết quả mong đợi: PASS 7/7.
- [ ] **Step 5:** Commit với message `feat(core): temp workspace and post-install actions`.

---

### Task 14: Bộ cài MSI / EXE / ZIP

**Files:**
- Create: `src/Installer.Core/Install/InstallTypes.cs`, `ExitCodeMapper.cs`, `ExeInstaller.cs`, `ZipInstaller.cs`, `MsiInstaller.cs`, `MsiNative.cs`
- Test: `tests/Installer.Core.Tests/Install/ExitCodeMapperTests.cs`, `ExeInstallerTests.cs`, `ZipInstallerTests.cs`, `tests/Installer.Core.Tests/TestData/fake-setup.cmd`

**Interfaces:**
- Produces:
```csharp
enum InstallStatus { Success, RebootRequired, Failed, Timeout, Blocked }
record InstallProgress(int? Percent, string Step);
record InstallRequest(InstallerType Type, string InstallerPath, string SilentArgs, string? TargetDir, TimeSpan Timeout);
record InstallOutcome(InstallStatus Status, int? ExitCode, string Message);
interface IInstallerRunner { Task<InstallOutcome> RunAsync(InstallRequest req, IProgress<InstallProgress> progress, CancellationToken ct); }
static class ExitCodeMapper { static InstallOutcome Map(int exitCode); }
sealed class InstallerRunnerFactory { IInstallerRunner For(InstallerType t); }   // trả về Msi/Exe/ZipInstaller
```
- `ExitCodeMapper`: 0 → Success `"Cài đặt thành công"`; 3010, 1641 → RebootRequired `"Cần khởi động lại máy"`; 1602 → Failed `"Người dùng hủy cài đặt"`; 1618 → Failed `"Đang có một bộ cài khác chạy"`; mã khác → Failed `"Lỗi cài đặt (mã {code})"`.
- `ExeInstaller`: `ProcessStartInfo(InstallerPath)` với `Arguments = SilentArgs`, `UseShellExecute = false`, `CreateNoWindow = true`, `WorkingDirectory = thư mục chứa file cài`. Báo các bước `(null,"Đang khởi động bộ cài")` → `(null,"Đang cài đặt")` → `(100,"Hoàn tất")`. Quá timeout thì `Kill(entireProcessTree: true)` và trả `Timeout` với `"Quá thời gian cài đặt"`. Bị hủy qua `ct` thì cũng kill.
- `ZipInstaller`: `TargetDir` (đã mở rộng biến môi trường) là bắt buộc. Giải nén từng entry, báo `Percent = i*100/n` với Step `"Đang giải nén"`. Entry nào thoát ra ngoài TargetDir thì trả Failed `"Gói zip không hợp lệ"`.
- `MsiInstaller`: P/Invoke `msi.dll` với `MsiSetInternalUI(INSTALLUILEVEL_NONE=2)`, `MsiSetExternalUIW(handler, INSTALLLOGMODE_PROGRESS|ACTIONSTART=0x400|0x10000)` và `MsiInstallProductW(path, "ACTION=INSTALL REBOOT=ReallySuppress " + SilentArgs)` trên một luồng nền. Cách đọc tiến độ từ chuỗi message PROGRESS (`"1: a 2: b 3: c 4: d"`):
```
field1==0 (reset): total=b; forward=(c==0); position = forward?0:total; scriptInProgress=(d==1)
field1==1 (action info): if c==1 then stepPerTick=b (dùng cho message field1==2 sau đó)
field1==2 (report): position += forward ? b : -b
field1==3 (add): total += b
percent = total>0 ? clamp(position*100/total, 0, 100) : null ; chỉ báo khi percent tăng
```
  ACTIONSTART thì cập nhật Step bằng tên action. Kết quả trả về được đưa qua `ExitCodeMapper.Map`. Timeout trên MSI chỉ báo `Timeout` và để msiexec tự kết thúc. `SilentArgs` mặc định của gói `.msi` là chuỗi rỗng, tham số chỉ là thuộc tính (properties).

- [ ] **Step 1: Viết test lỗi**
```csharp
[Theory] [InlineData(0,InstallStatus.Success)] [InlineData(3010,InstallStatus.RebootRequired)]
[InlineData(1641,InstallStatus.RebootRequired)] [InlineData(1603,InstallStatus.Failed)] void Map_codes(int c, InstallStatus s)
[Fact] void Map_unknown_message_contains_code()          // 1603 → "Lỗi cài đặt (mã 1603)"
[Fact] void Exe_passes_args_with_vietnamese_path()       // fake-setup.cmd nằm trong "Dự án 1\setup x" ghi %* ra file → nội dung == "/S /D=C:\\X"
[Fact] void Exe_nonzero_exit_maps_failed()               // fake-setup.cmd exit /b 1603
[Fact] void Exe_timeout_kills_and_returns_Timeout()      // "timeout /t 30", Timeout = 1s → Timeout trong < 5s
[Fact] void Zip_extracts_and_reports_100()
[Fact] void Zip_slip_entry_fails()                       // entry "..\evil.txt"
```
  Test MSI thật nằm trong checklist E2E (Task 20), không nằm trong unit test.
- [ ] **Step 2:** Chạy `dotnet test --filter "ExitCodeMapperTests|ExeInstallerTests|ZipInstallerTests"`. Kết quả mong đợi: FAIL.
- [ ] **Step 3:** Cài đặt các lớp. `MsiNative` khai báo `[DllImport("msi.dll", CharSet = CharSet.Unicode)]` và giữ delegate handler trong một field để GC không thu hồi.
- [ ] **Step 4:** Chạy lại lệnh ở Step 2. Kết quả mong đợi: PASS hết.
- [ ] **Step 5:** Commit với message `feat(core): silent MSI/EXE/ZIP installers with progress`.

---

### Task 15: Log mã hóa + xuất CSV

**Files:**
- Create: `src/Installer.Core/Logging/LogEntry.cs`, `EncryptedLog.cs`, `CsvExporter.cs`
- Modify: `src/Installer.Packager/Program.cs` (nối lệnh `export-logs`)
- Test: `tests/Installer.Core.Tests/Logging/EncryptedLogTests.cs`

**Interfaces:**
- Consumes: `PackageCrypto.EncryptBytes/DecryptBytes`, `VaultStore` (`Root`, `IsReadOnly`, `Meta.VaultId`).
- Produces:
```csharp
record LogEntry(DateTimeOffset Ts, string Username, string Hostname, string? BiosSerial, string Action /* "scan" | "install" */,
   string? PackageId, string? PackageVersion, string Status, int? ExitCode, string? Detail);
sealed class EncryptedLog {
  EncryptedLog(string vaultRoot, Guid vaultId, byte[] vaultKey, bool vaultReadOnly, string? fallbackDir = null);
  string ActiveDir { get; }                 // <root>\logs, hoặc fallback (mặc định %ProgramData%\SecureInstaller\logs)
  void Append(LogEntry e);                  // không bao giờ ném exception; lỗi ghi ở ActiveDir thì chuyển sang fallback rồi thử lại một lần
  int SyncFallbackIntoVault();              // chuyển các file fallback có cùng vaultId vào <root>\logs; trả về số file
  static IEnumerable<LogEntry> ReadAll(string dir, Guid vaultId, byte[] vaultKey); // bỏ qua dòng hỏng
}
static class CsvExporter { static void Export(IEnumerable<LogEntry> entries, string path); } // UTF-8 có BOM để Excel đọc được tiếng Việt
```
- Tên file: `<vaultId:N>-<yyyyMMdd>-<hostname>.log.enc`. Mỗi dòng là `Base64(PackageCrypto.EncryptBytes(json UTF-8, vk))` + `\n`, ghi bằng `FileMode.Append`.

- [ ] **Step 1: Viết test lỗi**
```csharp
[Fact] void Append_100_then_ReadAll_returns_100_in_order()
[Fact] void Readonly_vault_writes_to_fallback()
[Fact] void Write_failure_switches_to_fallback_and_does_not_throw() // xóa <root>\logs rồi tạo file trùng tên "logs" để chặn ghi
[Fact] void Sync_moves_only_matching_vault_files()
[Fact] void Corrupt_line_is_skipped()
[Fact] void Csv_has_bom_header_and_escapes_commas_and_vietnamese()  // Detail "a, \"b\" ư" → được trích dẫn đúng
```
- [ ] **Step 2:** Chạy `dotnet test --filter EncryptedLogTests`. Kết quả mong đợi: FAIL.
- [ ] **Step 3:** Cài đặt các lớp, rồi nối lệnh `export-logs` trong Packager: đọc cả `<root>\logs` lẫn fallback, sau đó gọi `CsvExporter.Export`.
- [ ] **Step 4:** Chạy lại lệnh ở Step 2. Kết quả mong đợi: PASS 6/6.
- [ ] **Step 5:** Commit với message `feat(core): encrypted append-only logs with fallback and CSV export`.

---

### Task 16: IRepositorySource + InstallService

**Files:**
- Create: `src/Installer.Core/Repository/IRepositorySource.cs`, `PortableRepositorySource.cs`, `src/Installer.Core/Install/InstallService.cs`
- Test: `tests/Installer.Core.Tests/Install/InstallServiceTests.cs`

**Interfaces:**
- Consumes: `VaultSession`, `PackageCrypto`, `TempWorkspace`, `InstallerRunnerFactory`, `PostActionRunner`, `EncryptedLog`, `LogEntry`.
- Produces (Plan 2 sẽ cài đặt lại đúng interface này):
```csharp
interface IRepositorySource {
  Catalog Catalog { get; }
  Task<Stream> OpenPackageBlobAsync(PackageEntry pkg, CancellationToken ct);
  Task<Stream> OpenDocBlobAsync(PackageEntry pkg, string docName, CancellationToken ct);
  void WriteLog(LogEntry entry);
}
sealed class PortableRepositorySource : IRepositorySource { PortableRepositorySource(VaultSession session, EncryptedLog log); }
sealed class InstallService {
  InstallService(IRepositorySource repo, InstallerRunnerFactory runners, Func<TempWorkspace> workspaceFactory);
  Task<InstallOutcome> InstallAsync(PackageEntry pkg, string username, HardwareInfo hw, IProgress<InstallProgress> progress, CancellationToken ct);
  Task<string> ExtractDocAsync(PackageEntry pkg, string docName, CancellationToken ct); // trả về đường dẫn file đã giải mã trong %TEMP%\SI_docs\
}
```
- Các bước của `InstallAsync`:
  1. Báo `(0,"Đang kiểm tra gói")`. Copy blob vào `ws\pkg.enc`, đồng thời tính SHA-256. Hash khác `pkg.Sha256` thì trả `Failed` với `Strings.PackageCorrupt`.
  2. Báo `(null,"Đang giải mã")`. Giải mã ra `ws\pkg.zip`. Gặp `PackageCorruptException` thì trả `Failed` với `Strings.PackageCorrupt`.
  3. Giải nén vào `ws\pkg\`.
  4. Chạy runner với `ws\pkg\installer\<InstallerFile>`.
  5. Nếu kết quả là `Success` hoặc `RebootRequired` thì chạy `PostActionRunner.Run(pkg.PostActions, ws\pkg\extras)`. Action lỗi được ghi vào `Message`, nhưng không đổi Status.
  6. `WriteLog` với Action = `"install"`.
  7. Gọi `ws.Dispose()` trong `finally`.
- `ExtractDocAsync` xóa thư mục `%TEMP%\SI_docs` cũ trước khi ghi file mới. App gọi xóa thư mục này khi thoát.

- [ ] **Step 1: Viết test lỗi:** tạo kho thật bằng `VaultAdmin` với một gói zip, và dùng `FakeRunner` ghi lại `InstallRequest`.
```csharp
[Fact] void Install_zip_package_success_runs_post_actions_logs_and_cleans_workspace()
[Fact] void Hash_mismatch_returns_PackageCorrupt_and_runner_not_called()
[Fact] void Truncated_blob_returns_PackageCorrupt()          // cắt file .pkg.enc rồi cập nhật Sha256 trong fake catalog cho khớp
[Fact] void Failed_install_skips_post_actions_and_logs_failed()
[Fact] void Runner_throws_workspace_still_deleted()
[Fact] void ExtractDoc_returns_readable_file()
```
- [ ] **Step 2:** Chạy `dotnet test --filter InstallServiceTests`. Kết quả mong đợi: FAIL.
- [ ] **Step 3:** Cài đặt các lớp. `InstallerRunnerFactory` được thêm constructor nhận `Func<InstallerType, IInstallerRunner>?` để test thay runner.
- [ ] **Step 4:** Chạy lại lệnh ở Step 2, rồi chạy toàn bộ `dotnet test`. Kết quả mong đợi: PASS hết.
- [ ] **Step 5:** Commit với message `feat(core): repository abstraction and install orchestration`.

---

### Task 17: App WPF: khởi động, đăng nhập, báo cáo quét

**Files:**
- Create: `src/Installer.App/App.xaml(.cs)`, `AppServices.cs` (tạo các đối tượng thật), `ViewModels/MainViewModel.cs`, `LoginViewModel.cs`, `ScanReportViewModel.cs`, `Views/MainWindow.xaml`, `LoginView.xaml`, `ScanReportView.xaml`, `Converters/DisplayText.cs`
- Test: `tests/Installer.App.Tests/LoginViewModelTests.cs`, `ScanReportViewModelTests.cs`

**Interfaces:**
- Consumes: `VaultStore`, `MachineScanner`, `Strings`, enum Verdict và VersionStatus.
- Produces:
  - `MainViewModel.Current : ObservableObject` (trang đang hiển thị). `NavigateTo(object vm)`.
  - `LoginViewModel(Func<VaultStore> openVault, Action<VaultSession> onSuccess, TimeProvider time)` với các thuộc tính `Username`, `Password`, `Error`, `IsBusy` và `LoginCommand` (async, chạy Argon2 trên `Task.Run`). Gặp `VaultNotFoundException` thì `Error = Strings.VaultNotFound`.
  - `ScanReportViewModel(MachineScanner scanner, Catalog catalog)` với `ScanCommand`, `CurrentStep`, `Report`, và các danh sách hiển thị: `HardwareRows`, `NetworkRows`, `WindowsLicenseText`, `OfficeLicenseText`, `KeywordFiles`, `ContinueCommand`.
  - `static string DisplayText.Of(LicenseVerdict | VersionStatus)` trả đúng chuỗi ở Global Constraints.
- Khi khởi động: kho = `Path.Combine(AppContext.BaseDirectory, "vault")`. Sau khi đăng nhập thì gọi `EncryptedLog.SyncFallbackIntoVault()`, sang trang quét, và ghi log `scan` khi quét xong.
- Giao diện báo cáo có 4 tab: **Phần cứng**, **Lịch sử mạng** (cột Tên, Loại, Lần đầu, Lần cuối; ngày theo định dạng `dd/MM/yyyy HH:mm`), **Bản quyền** (Windows và Office, mỗi bên ghi Sản phẩm, Kênh, Kích hoạt, Kết luận, danh sách Bằng chứng; `CrackSuspected` hiện chữ đỏ), **File từ khóa**.

- [ ] **Step 1: Viết test lỗi**
```csharp
[Fact] void Login_success_invokes_onSuccess()
[Fact] void Login_bad_password_sets_error_text()        // Error == Strings.BadCredentials
[Fact] void Login_locked_shows_until_time()             // Error == string.Format(Strings.AccountLocked, until)
[Fact] void Login_missing_vault_shows_VaultNotFound()
[Fact] void Scan_populates_tabs_and_crack_flag()        // fake scanner trả Windows CrackSuspected → WindowsLicenseText chứa "Có dấu hiệu crack"
[Fact] void DisplayText_maps_all_enum_values()
```
- [ ] **Step 2:** Chạy `dotnet test tests/Installer.App.Tests`. Kết quả mong đợi: FAIL.
- [ ] **Step 3:** Cài đặt các ViewModel, rồi làm View bằng XAML. Gọi skill `frontend-design` để chọn bố cục và màu sắc. Không có code-behind ngoài `InitializeComponent` và việc nối `PasswordBox` vào thuộc tính.
- [ ] **Step 4:** Chạy lại lệnh ở Step 2, sau đó chạy app bằng `dotnet run --project src/Installer.App` trên một thư mục có kho mẫu tạo bằng Packager. Kết quả mong đợi: test PASS, app hiện UAC, đăng nhập được, có đủ 4 tab dữ liệu.
- [ ] **Step 5:** Commit với message `feat(app): startup, login and scan report screens`.

---

### Task 18: App WPF: danh sách gói, cài đặt, hướng dẫn

**Files:**
- Create: `src/Installer.App/ViewModels/PackageListViewModel.cs`, `PackageRowViewModel.cs`, `InstallViewModel.cs`, `Views/PackageListView.xaml`, `InstallView.xaml`
- Test: `tests/Installer.App.Tests/PackageListViewModelTests.cs`, `InstallViewModelTests.cs`

**Interfaces:**
- Consumes: `InstalledAppsScanner`, `VersionComparer.Check`, `RuleEvaluator.Evaluate`, `InstallService`, `IRepositorySource`, `ScanReport`.
- Produces:
  - `PackageRowViewModel` với `Name`, `PackageVersion`, `InstalledVersion`, `StatusText` (theo `DisplayText`), `Allowed`, `ReasonsText` (nối bằng `"; "`), `NeedsDowngradeConfirm`, `HasDocs`, `InstallCommand`, `OpenDocCommand(string doc)`.
  - `PackageListViewModel(IRepositorySource repo, ScanReport scan, InstalledAppsScanner apps, InstallService installer, Func<string,bool> confirm, Action<string> openFile)`.
  - `InstallViewModel` với `Percent` (null thì thanh tiến độ ở chế độ indeterminate), `Step`, `Result`, `IsDone`, `BackCommand`.
- Mỗi gói hiện một dòng, mỗi phiên bản là một dòng riêng. Nút Cài bị disable khi `!Allowed` và hiện tooltip là `ReasonsText`.
- Khi `NeedsDowngradeConfirm` thì hỏi `confirm($"Máy đang có phiên bản mới hơn ({installed}). Vẫn cài {pkg}?")`.
- Khi kết quả là `RebootRequired` thì hiện thêm dòng `"Hãy khởi động lại máy để hoàn tất."`.
- Hướng dẫn: gọi `ExtractDocAsync` rồi `openFile(path)` (bản thật dùng `Process.Start(new ProcessStartInfo(path){UseShellExecute=true})`).
- Sau khi cài xong thì quét lại phiên bản của dòng đó.

- [ ] **Step 1: Viết test lỗi**
```csharp
[Fact] void Rows_show_older_status_and_versions()           // 22.01 so với 24.08 → StatusText "Cũ hơn"
[Fact] void Blocked_row_disables_install_and_lists_reasons()
[Fact] void Downgrade_requires_confirm_and_cancel_skips_install()
[Fact] void Install_progress_updates_percent_and_step()
[Fact] void Reboot_required_shows_reboot_text()
[Fact] void OpenDoc_calls_openFile_with_extracted_path()
[Fact] void After_install_row_rechecked()                   // fake apps trả 24.08 sau khi cài → "Bằng nhau"
```
- [ ] **Step 2:** Chạy `dotnet test tests/Installer.App.Tests`. Kết quả mong đợi: FAIL các test mới.
- [ ] **Step 3:** Cài đặt các ViewModel và View.
- [ ] **Step 4:** Chạy lại lệnh ở Step 2, rồi cài thử một gói zip mẫu bằng app thật. Kết quả mong đợi: PASS, file có mặt ở `TargetDir`, `%TEMP%` không còn `SI_*`.
- [ ] **Step 5:** Commit với message `feat(app): package list, install progress and docs`.

---

### Task 19: App WPF: màn hình Admin

**Files:**
- Create: `src/Installer.App/ViewModels/AdminViewModel.cs`, `Views/AdminView.xaml`
- Test: `tests/Installer.App.Tests/AdminViewModelTests.cs`

**Interfaces:**
- Consumes: `VaultAdmin`, `EncryptedLog.ReadAll`, `CsvExporter`.
- Produces: `AdminViewModel(VaultSession session, EncryptedLog log, Func<string?> pickFolder, Func<string?> pickSavePath)` gồm các tab:
  - **Gói:** danh sách, `AddPackageCommand`, `UpdatePackageCommand(keepOld)`, `RemovePackageCommand`.
  - **Người dùng:** `AddUserCommand`, `RemoveUserCommand`, `ResetPasswordCommand`.
  - **Cấu hình quét:** từ khóa, đường dẫn, số giây tối đa, danh sách dấu hiệu crack, `SaveSettingsCommand`.
  - **Log:** lọc theo ngày, user, máy; `ExportCsvCommand`.
- Nút Admin trên `MainWindow` chỉ hiện khi `session.IsAdmin`. Mọi lỗi từ `VaultAdmin` hiện ở `Error`, không làm app crash.
- Nếu kho chỉ đọc (`IsReadOnly`) thì disable mọi lệnh sửa và hiện `"Kho đang ở chế độ chỉ đọc"`.

- [ ] **Step 1: Viết test lỗi**
```csharp
[Fact] void Add_package_from_picked_folder_appears_in_list_and_version_bumped()
[Fact] void Cancel_picker_does_nothing()
[Fact] void Remove_last_admin_shows_error()
[Fact] void Save_settings_persists_after_reopen()
[Fact] void Readonly_vault_disables_mutations()
[Fact] void Export_csv_writes_file()
```
- [ ] **Step 2:** Chạy `dotnet test tests/Installer.App.Tests`. Kết quả mong đợi: FAIL các test mới.
- [ ] **Step 3:** Cài đặt `AdminViewModel` và View. Trong View thật, `pickFolder` và `pickSavePath` dùng `Microsoft.Win32.OpenFolderDialog` và `SaveFileDialog`.
- [ ] **Step 4:** Chạy lại lệnh ở Step 2. Kết quả mong đợi: PASS.
- [ ] **Step 5:** Commit với message `feat(app): admin screens`.

---

### Task 20: Đóng gói portable + checklist E2E

**Files:**
- Create: `build/build-portable.ps1`, `docs/e2e-checklist.md`, `samples/7zip/manifest.json` (mẫu, không commit file cài)

**Interfaces:**
- `build-portable.ps1 -Out <dir> [-InitVault -Admin <name>]`:
  - `dotnet publish src/Installer.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o <Out>\SecureInstaller`
  - Publish Packager tương tự vào `<Out>\SecureInstaller\tools`.
  - Nếu có `-InitVault` thì chạy `tools\SecureInstaller.Packager.exe init <Out>\SecureInstaller --user <Admin>`.

- [ ] **Step 1:** Viết script, chạy `./build/build-portable.ps1 -Out D:\temp\usb -InitVault -Admin admin`. Kết quả mong đợi: có `D:\temp\usb\SecureInstaller\SecureInstaller.exe`, `tools\SecureInstaller.Packager.exe` và `vault\vault.meta`.
- [ ] **Step 2:** Viết `docs/e2e-checklist.md`, mỗi mục ghi rõ "Làm" và "Kết quả mong đợi" (dựa trên các Acceptance Criteria của spec):
  1. Chạy từ USB thật.
  2. Copy thư mục ra `C:\` rồi chạy.
  3. Thử mở `.pkg.enc` bằng 7-Zip, không được.
  4. Sửa 1 byte trong catalog, app báo `Strings.VaultTampered`.
  5. Cài `.msi` 7-Zip trên máy ảo sạch, tiến độ chạy 0 → 100% và có trong Programs and Features.
  6. Cài một `.exe` ngầm.
  7. Có bước sau cài copy file.
  8. Lab KMS loopback, **chỉ** dùng `slmgr /skms 127.0.0.2`, không dùng tool crack → app báo "Có dấu hiệu crack".
  9. Máy không cài Office → "Không cài".
  10. Rút USB khi đang cài → bộ cài chạy tiếp, log nằm ở `%ProgramData%`.
  11. Chạy từ ổ chỉ đọc (ISO được mount).
  12. Sau mọi lần cài, `%TEMP%` không còn `SI_*`.
- [ ] **Step 3:** Commit với message `build: portable publish script and E2E checklist`.

---

## Self-Review (đã chạy)

- **Bao phủ spec:** yêu cầu 1 → T17; 2 → T4, T19; 3 → T2, T5; 4 → T4; 5 → T5, T17; 6–8 → T4, T5, T20; 9 → T4, T15; 14 → T7; 15 → T8; 16–18 → T9; 19 → T11; 20 → T10; 21 → T14; 22 → T12; 23 → T13; 24 → T16, T18; 25 → T13, T16; 26 → T15, T17. Yêu cầu 10–13 thuộc Plan 2.
- **Nhất quán kiểu:** tên `IRepositorySource`, `InstallOutcome`, `VersionCheck`, `LicenseVerdict`, `PackageEntry` khớp nhau giữa các task.
- **Review Focus:** cả 5 mục đều đã có test ở task sở hữu.
