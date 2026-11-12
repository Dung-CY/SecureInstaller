# SecureInstaller — Plan 1.1: Hardening + UX

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close the remaining same-user→elevated escalation gaps, prepare the key seam for Plan 2, and rebuild the UX. The UX work covers a scan-report redesign with re-view and history, package icons, multi-select and bundle installs, uninstall, and a smaller build.

**Architecture:** Extends Plan 1 without changing its layering. Core stays WPF-free; the App ViewModels take delegates; Windows sources sit behind interfaces. New data is added as **trailing optional record parameters**, so Plan 1 JSON (catalog format 1, logs) still deserializes. `InstallViewModel` is replaced by `QueueInstallViewModel`; a single install is a queue of one.

**Tech Stack:** Unchanged — .NET 8 `net8.0-windows`, WPF, CommunityToolkit.Mvvm 8.2.2, Konscious Argon2 1.3.1, System.Management 8.0.0, ServiceController 8.0.1, xUnit 2.9.2. No new packages: the PNG encoder is hand-written over `System.IO.Compression.ZLibStream`.

**Spec:** [docs/spec-1.1.md](../../spec-1.1.md). All assumptions in it were approved by the user on 2026-11-11. Also read [docs/spec.md](../../spec.md) and the Plan 1 decision log in [docs/superpowers/backlog-plan-1.1.md](../backlog-plan-1.1.md), section E.

## Global Constraints

- Everything in Plan 1's Global Constraints still holds. That includes the existing `Strings.*` values, the verdict/version labels, the crypto parameters, and "quote all paths; Vietnamese/space paths must work".
- Record additions must be **trailing parameters with defaults**. Never reorder existing positional parameters, because old vault JSON must still load.
- New `Strings` constants (verbatim):
  - `IoError` = `"Không đọc/ghi được dữ liệu. Hãy kiểm tra USB hoặc ổ đĩa."`
  - `AccessDenied` = `"Không đủ quyền truy cập."`
  - `UnsafePath` = `"Đường dẫn đích không an toàn."`
  - `WorkspaceUnsafe` = `"Không tạo được thư mục làm việc an toàn."`
  - `Unexpected` = `"Đã xảy ra lỗi không mong muốn."`
  - `ReadOnlyNotice` = `"Kho chỉ đọc — nhật ký được lưu tạm trên máy này"`
  - `VaultInfo` = `"Kho phiên bản {0} · cập nhật {1:dd/MM/yyyy}"`
  - `NoUninstall` = `"Gói này không hỗ trợ gỡ tự động"`
  - `NotInVault` = `"Không có trong kho"`
- Queue status texts (verbatim):
  - `"Đang chờ"`
  - `"Đang cài"`
  - `"Đang gỡ"`
  - `"Thành công"`
  - `"Cần khởi động lại"`
  - `"Lỗi"`
  - `"Đã hủy"`
  - `"Đã bỏ qua"`
- Queue header: `"Gói {i}/{n}"`. Multi-install button: `"Cài các mục đã chọn ({n})"`.
- Peripheral category labels (verbatim): `KeyboardMouse`→`"Bàn phím & chuột"`, `UsbStorage`→`"Lưu trữ USB"`, `Printer`→`"Máy in"`, `Camera`→`"Camera"`, `Bluetooth`→`"Bluetooth"`, `Audio`→`"Âm thanh"`, `Other`→`"Khác"`.
- Workspace base: `%ProgramData%\SecureInstaller\work`. Fallback log dir stays `%ProgramData%\SecureInstaller\logs`. Install marker key: `HKLM\SOFTWARE\SecureInstaller\Installed\<id>` with values `Version` (string) and `InstalledAt` (ISO-8601 string), Registry64.
- **Never execute or load anything sourced from HKCU** (UninstallString, QuietUninstallString, DisplayIcon) while elevated.
- Icon: PNG, at most `24 * 1024` bytes, rendered 48×48.
- Build output: `SecureInstaller\` total ≤ 120 MB; publish with `-p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:DebugType=none --self-contained -r win-x64`, and **without** `IncludeNativeLibrariesForSelfExtract`.
- UI tasks (10, 11, 13): the implementer **must load the `frontend-design` skill** before writing XAML.

## Review Focus

1. **A Plan 1 vault (catalog format 1, no DocsKeyB64/BlobSize/Bundles, logs without ScanReportJson) must open, install and show docs unchanged.** Covered by tests in Task 3 and Task 5.
2. **Elevated vs non-elevated behaviour diverges.** Tests run non-elevated; production is elevated. The elevation decision must be injectable (`Func<bool> isElevated`) so both branches are unit-tested. Covered in Task 1.
3. **Queue edge cases:** cancel mid-item, a failed item followed by more items, and an empty selection. Covered in Task 11.
4. **Junction checks with not-yet-existing ancestors:** only existing ancestors are checked, and a missing directory chain must still be creatable. Covered in Task 2.
5. **HKCU-only uninstall/icon data is never used.** Covered in Tasks 6, 7 and 8.

---

## File Structure (new or changed)

```
src/Installer.Core/
  Strings.cs                              + new constants
  Errors/ErrorText.cs                     ForUser(Exception) → string
  Errors/Exceptions.cs                    UnsafePathException, WorkspaceUnsafeException
  Platform/SafePath.cs                    EnsureNoReparseAncestors
  Platform/SecureDirectory.cs             CreateProtected(path, isElevated) — shared by workspace + fallback logs
  Platform/IRegistryWriter.cs, RegistryWriter.cs
  Platform/IconExtractor.cs               PrivateExtractIconsW → 48×48 PNG bytes (hand-written PNG encoder)
  Install/TempWorkspace.cs                base moves to ProgramData; isElevated injectable
  Install/InstallMarker.cs                Write/Remove/ReadAll over registry
  Install/UninstallService.cs, Install/CommandLineSplitter.cs
  Install/MsiNative.cs                    + MsiConfigureProductExW
  Install/InstallService.cs               uses repo key fns, ErrorText, marker
  Repository/IRepositorySource.cs         + GetPackageKeyAsync / GetDocsKeyAsync
  Model/Catalog.cs                        + Bundle, BundleItem, UninstallSpec, new trailing params
  Model/BundleResolver.cs
  Scanning/PeripheralCategorizer.cs, Scanning/ScanHistory.cs
  Versioning/InstalledAppsScanner.cs      + uninstall/icon fields, FindForPackage
  Vault/PackageBuilder.cs, VaultAdmin.cs, VaultStore.cs
  Logging/LogEntry.cs                     + ScanReportJson
src/Installer.App/
  ViewModels/ScanReportViewModel.cs       overview, groups, search, read-only mode
  ViewModels/PackageListViewModel.cs, PackageRowViewModel.cs   icons, selection, bundles, uninstall
  ViewModels/QueueInstallViewModel.cs     (replaces InstallViewModel)
  ViewModels/AdminViewModel.cs            + bundles tab, scan-history tab, async log load
  ViewModels/MainViewModel.cs             + VaultInfo, ReadOnlyNotice
  Converters/IconConverter.cs             byte[]/base64 → BitmapImage
  Views/*.xaml                            redesigned
  App.xaml.cs                             crash log file
src/Installer.Packager/Program.cs         + add-bundle, remove-bundle; icon extractor wiring
build/build-portable.ps1, docs/e2e-checklist.md, samples/7zip/README.md
```

---

### Task 1: Protected workspace in ProgramData + leaner publish

**Files:**
- Create: `src/Installer.Core/Platform/SecureDirectory.cs` and `src/Installer.Core/Errors/Exceptions.cs`
- Modify: `src/Installer.Core/Install/TempWorkspace.cs` and `build/build-portable.ps1`
- Test: `tests/Installer.Core.Tests/Install/TempWorkspaceTests.cs`

**Interfaces:**
- Produces:
  - `public sealed class WorkspaceUnsafeException : Exception` and `public sealed class UnsafePathException : Exception`, both in namespace `Installer.Core.Errors`, both with a `(string message)` ctor.
  - `public static class SecureDirectory`:
    - `static void CreateProtected(string path, bool elevated)`. When elevated, it applies a protected DACL (Administrators and SYSTEM, FullControl, CI|OI) and sets the owner to BUILTIN\Administrators. When not elevated, it also grants the current user FullControl (dev and test runs). It throws `WorkspaceUnsafeException(Strings.WorkspaceUnsafe)` if `path` exists as a reparse point, or if any existing ancestor between `%ProgramData%` and `path` is a reparse point.
    - `static bool IsElevated()`, which uses WindowsPrincipal with the Administrator role.
  - `TempWorkspace.Create(string? baseDir = null, Func<bool>? isElevated = null)`. The base defaults to `%ProgramData%\SecureInstaller\work`. The base is (re)secured through `SecureDirectory.CreateProtected` on every call, then the child `<guid N>` is created with the same ACL in one call.
  - `TempWorkspace.SweepStale(TimeSpan olderThan, string? baseDir = null)` sweeps the new base **and** the legacy `%TEMP%` `SI_<32hex>` folders.

- [ ] **Step 1: Write failing tests**
```csharp
[Fact] void Workspace_is_created_under_given_base()                  // base = temp test dir; ws.Path starts with base + "\\"
[Fact] void Elevated_branch_acl_has_admins_and_system_only_and_owner_admins() // isElevated: () => true; rules SetEquals {BA, SY}; owner == BA (where SetOwner fails non-elevated, assert the DirectorySecurity built by an internal BuildSecurity(true) instead)
[Fact] void Non_elevated_branch_includes_current_user()
[Fact] void Base_that_is_a_junction_throws_WorkspaceUnsafe()        // mklink /J base → elsewhere
[Fact] void SweepStale_cleans_new_base_and_legacy_temp()
```
- [ ] **Step 2:** Run `dotnet test --filter TempWorkspaceTests`. Expect FAIL.
- [ ] **Step 3:** Implement. Expose `internal static DirectorySecurity BuildSecurity(bool elevated)` so the elevated ACL is testable without elevation. Remove the old `%TEMP%` base from `Create`.
- [ ] **Step 4:** In `build/build-portable.ps1`, change the publish flags to exactly the Global Constraints value. After publishing, compute the total size of `<Out>\SecureInstaller` and print `Dung lượng: {MB} MB`. Fail with a clear message if it is over 120.
- [ ] **Step 5:** Run `dotnet test`, then run the script with `-Out <scratchpad>\usb -InitVault -WithDemo -AdminPassword "Test@12345"`. Expect all tests to pass and the printed size to be ≤ 120 MB. Do not launch the exe. Delete the scratch output afterwards.
- [ ] **Step 6:** Commit with the message `feat(core): protected ProgramData workspace; compressed publish without native self-extract`.

---

### Task 2: Junction-ancestor checks + hardened fallback log dir

**Files:**
- Create: `src/Installer.Core/Platform/SafePath.cs`
- Modify: `Install/PostActionRunner.cs`, `Install/ZipInstaller.cs`, `Logging/EncryptedLog.cs`
- Test: `tests/Installer.Core.Tests/Platform/SafePathTests.cs`, plus additions to `PostActionRunnerTests` and `ZipInstallerTests`

**Interfaces:**
- Consumes: `UnsafePathException` and `SecureDirectory.CreateProtected` from Task 1.
- Produces: `public static class SafePath { static void EnsureNoReparseAncestors(string fullPath); }`. It walks from the drive root down to `fullPath`. For each component that **exists**, it throws `UnsafePathException(Strings.UnsafePath)` when that component has `FileAttributes.ReparsePoint`. Components that do not exist are skipped. A UNC or `\\?\` input throws.
- PostActionRunner (copy and delete) and ZipInstaller (TargetDir) call it after expansion and before any IO. Any `UnsafePathException` maps to result error `Strings.UnsafePath`; ZipInstaller returns Failed with that message.
- `EncryptedLog` creates the default fallback dir through `SecureDirectory.CreateProtected(dir, SecureDirectory.IsElevated())`. On `WorkspaceUnsafeException` it treats the fallback as unavailable: Append swallows, as it already does.

- [ ] **Step 1: Write failing tests**
```csharp
[Fact] void Junction_parent_is_rejected()                 // tmp\real, tmp\link → junction to real; EnsureNoReparseAncestors(tmp\link\a\b) throws
[Fact] void Missing_tail_components_are_allowed()          // tmp\x\y\z where only tmp exists → no throw
[Fact] void Plain_path_passes()
[Fact] void Delete_under_junction_parent_is_refused_and_target_survives()   // PostActionRunner; file inside real dir still exists
[Fact] void Zip_target_under_junction_fails_with_UnsafePath()
```
- [ ] **Step 2:** Run `dotnet test --filter "SafePathTests|PostActionRunnerTests|ZipInstallerTests"`. Expect FAIL.
- [ ] **Step 3:** Implement and wire.
- [ ] **Step 4:** Run the same filter, then the full suite. Expect PASS.
- [ ] **Step 5:** Commit with the message `feat(core): refuse reparse-point ancestors for post-actions, zip target and fallback logs`.

---

### Task 3: Key seam via the repository, separate docs key, catalog format 2

**Files:**
- Modify: `Repository/IRepositorySource.cs`, `Repository/PortableRepositorySource.cs`, `Install/InstallService.cs`, `Model/Catalog.cs`, `Vault/PackageBuilder.cs`, and `src/Installer.App/AppServices.cs` (scan log through the repo)
- Test: `tests/Installer.Core.Tests/Install/InstallServiceTests.cs` and `tests/Installer.Core.Tests/Vault/FormatCompatTests.cs`

**Interfaces:**
- Produces:
  - `IRepositorySource` adds `Task<byte[]> GetPackageKeyAsync(PackageEntry pkg, CancellationToken ct)` and `Task<byte[]> GetDocsKeyAsync(PackageEntry pkg, CancellationToken ct)`.
  - Portable implementation: `Convert.FromBase64String(pkg.KeyB64)` and `pkg.DocsKeyB64 ?? pkg.KeyB64`. A `FormatException` becomes `PackageCorruptException`.
  - `PackageEntry` gets these trailing parameters: `long BlobSize = 0, string? DocsKeyB64 = null, string? IconPngB64 = null, UninstallSpec? Uninstall = null`.
  - `record UninstallSpec(string Type, string? Command = null, string? Args = null)`. Type is `"msi" | "command" | "zip"`.
  - `Catalog` gets the trailing parameters `List<Bundle>? Bundles = null, int Format = 1`.
  - `record Bundle(string Id, string Name, string Description, List<BundleItem> Items)` and `record BundleItem(string PackageId, string? Version = null)`.
  - PackageBuilder writes `BlobSize` = length of the .enc file, generates a separate random `DocsKeyB64` for docs, and every catalog write sets `Format = 2`.
  - InstallService no longer reads `pkg.KeyB64`. It uses only `repo.GetPackageKeyAsync` and `GetDocsKeyAsync`.
  - AppServices writes the scan log through the `IRepositorySource` instance (PortableRepositorySource), not through `EncryptedLog` directly.
- Manifest: optional `"uninstall": {"type": "...", "command": "...", "args": "..."}` is parsed into `Uninstall`. Validation: type must be one of the three; `command` is required when type is `command`.

- [ ] **Step 1: Write failing tests**
```csharp
[Fact] void Format1_catalog_json_without_new_fields_deserializes_with_defaults()   // literal Plan-1 JSON → BlobSize 0, DocsKeyB64 null, Bundles null, Format 1
[Fact] void New_package_has_separate_docs_key_and_blob_size()
[Fact] void InstallService_uses_repository_key_not_catalog()     // fake repo returns key; pkg.KeyB64 = "garbage" → install still succeeds
[Fact] void Legacy_docs_without_docs_key_still_open()            // DocsKeyB64 null → docs decrypt with package key
[Fact] void Manifest_uninstall_command_requires_command()
```
- [ ] **Step 2:** Run `dotnet test --filter "InstallServiceTests|FormatCompatTests|VaultAdminTests"`. Expect FAIL.
- [ ] **Step 3:** Implement. Update every existing `IRepositorySource` fake.
- [ ] **Step 4:** Run the full suite. Expect PASS.
- [ ] **Step 5:** Commit with the message `feat(core): repository key retrieval, separate docs key, catalog format 2`.

---

### Task 4: User-facing Vietnamese errors + Release crash log

**Files:**
- Create: `src/Installer.Core/Errors/ErrorText.cs`
- Modify: `Strings.cs`, `Install/InstallService.cs`, `Install/ZipInstaller.cs`, `Install/MsiInstaller.cs`, `Install/ExeInstaller.cs`, `src/Installer.App/ViewModels/{LoginViewModel,AdminViewModel,PackageListViewModel}.cs`, `src/Installer.App/App.xaml.cs`
- Test: `tests/Installer.Core.Tests/Errors/ErrorTextTests.cs`

**Interfaces:**
- Produces: `public static class ErrorText { static string ForUser(Exception ex); }`. Rules are checked in order:

| Exception | Returned text |
|---|---|
| UnsafePathException, WorkspaceUnsafeException, InvalidDataException, NotAdminException, BadCredentialsException, AccountLockedException | `ex.Message` (we author these messages) |
| PackageCorruptException | `Strings.PackageCorrupt` |
| VaultTamperedException, CryptographicException | `Strings.VaultTampered` |
| UnauthorizedAccessException | `Strings.AccessDenied` |
| IOException (including subclasses) | `Strings.IoError` |
| OperationCanceledException | `"Người dùng hủy cài đặt"` |
| InvalidOperationException whose message contains at least one non-ASCII character (ours are Vietnamese) | `ex.Message` |
| anything else | `Strings.Unexpected` |

- Every place that previously put `ex.Message` into a user-visible outcome, Error or Status now calls `ErrorText.ForUser`. The raw `ex.ToString()` goes only to the log `Detail` or the crash log.
- `App.xaml.cs` handlers (Dispatcher, AppDomain, TaskScheduler) append `"{ISO ts} {ex}"` to `%LOCALAPPDATA%\SecureInstaller\crash.log`, best effort, then show `ErrorText.ForUser(ex)` in the existing MessageBox format.

- [ ] **Step 1: Write failing tests.** Add one `[Theory]` row per mapping row in the table. Also add `[Fact] void Install_io_failure_shows_IoError_and_no_path()`: a fake repo throws `IOException("D:\\secret\\vault\\x")`, and the test asserts `outcome.Message == Strings.IoError` and that the logged `Detail` contains the path.
- [ ] **Step 2:** Run `dotnet test --filter "ErrorTextTests|InstallServiceTests"`. Expect FAIL.
- [ ] **Step 3:** Implement and apply everywhere listed above.
- [ ] **Step 4:** Run the full suite. Fix any App tests that asserted raw messages.
- [ ] **Step 5:** Commit with the message `feat: map exceptions to Vietnamese user messages; Release crash log`.

---

### Task 5: Blob integrity at open + vault info / read-only notice

**Files:**
- Modify: `Vault/VaultStore.cs`, `src/Installer.App/ViewModels/MainViewModel.cs`, `Views/MainWindow.xaml`, `AppServices.cs`
- Test: `tests/Installer.Core.Tests/Vault/VaultStoreTests.cs` and `tests/Installer.App.Tests/MainViewModelTests.cs`

**Interfaces:**
- `VaultStore.Unlock`: after the catalog verifies, for each package, if `vault\packages\<BlobFile>` is missing, or if `BlobSize > 0` and the file length differs, it throws `VaultTamperedException(Strings.VaultTampered)`.
- `MainViewModel` adds `string? VaultInfo` (= `string.Format(Strings.VaultInfo, catalog.Version, catalog.UpdatedAt.ToLocalTime())`) and `string? ReadOnlyNotice` (= `Strings.ReadOnlyNotice` when `store.IsReadOnly`). AppServices sets both after login. MainWindow shows them in a slim header bar above `Current` on every screen after login.

- [ ] **Step 1: Write failing tests**
```csharp
[Fact] void Replaced_blob_with_different_size_throws_VaultTampered_on_unlock()
[Fact] void Missing_blob_throws_VaultTampered_on_unlock()
[Fact] void Legacy_entry_with_BlobSize_0_skips_size_check()
[Fact] void VaultInfo_and_ReadOnlyNotice_text()      // App test: "Kho phiên bản 7 · cập nhật 11/11/2026"
```
- [ ] **Step 2:** Run the RED check. Expect FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Make the tests pass, then run the full suite.
- [ ] **Step 5:** Commit with the message `feat: detect replaced blobs at open; show vault version/date and read-only notice`.

---

### Task 6: Install marker + richer installed-app data

**Files:**
- Create: `Platform/IRegistryWriter.cs`, `Platform/RegistryWriter.cs`, `Install/InstallMarker.cs`
- Modify: `Versioning/InstalledAppsScanner.cs`, `Install/InstallService.cs`
- Test: `tests/Installer.Core.Tests/Install/InstallMarkerTests.cs`, `InstalledAppsScannerTests.cs`, and `tests/Installer.Core.Tests/Fakes/FakeRegistry.cs` (implement the writer too)

**Interfaces:**
- Produces:
  - `interface IRegistryWriter { void SetString(RegistryHive hive, RegistryView view, string path, string name, string value); void DeleteKey(RegistryHive hive, RegistryView view, string path); }`. The real implementation uses `CreateSubKey`/`DeleteSubKeyTree(throwOnMissing:false)`.
  - `sealed class InstallMarker(IRegistryReader reg, IRegistryWriter writer)`:
    - `void Write(string packageId, string version, DateTimeOffset at)` writes to path `SOFTWARE\SecureInstaller\Installed\<id>`, HKLM, Registry64.
    - `void Remove(string packageId)`.
    - `IReadOnlyDictionary<string,string> ReadAll()` returns id → version.
  - `InstalledApp` gets these trailing parameters: `string? UninstallString = null, string? QuietUninstallString = null, string? DisplayIcon = null, bool IsMarker = false`. They are filled from the Uninstall key values of the same names, as strings only.
  - `InstalledAppsScanner(IRegistryReader reg, InstallMarker? marker = null)` adds `IReadOnlyList<InstalledApp> FindForPackage(PackageEntry pkg)`. This returns `Find(pkg.Detect)`, plus, if the marker holds `pkg.Id`, `new InstalledApp(pkg.Name, markerVersion, "marker:" + pkg.Id, true, false, IsMarker: true)`.
  - `InstallService` takes an optional `InstallMarker? marker` ctor parameter. When the outcome is Success or RebootRequired, it calls `marker.Write(pkg.Id, pkg.Version, now)`, best effort, with failures added to Detail.

- [ ] **Step 1: Write failing tests**
```csharp
[Fact] void Marker_roundtrip_write_read_remove()
[Fact] void FindForPackage_includes_marker_entry()
[Fact] void Uninstall_fields_are_read_as_strings_and_dword_ignored()
[Fact] void Successful_install_writes_marker_failed_does_not()
```
- [ ] **Step 2:** Run `dotnet test --filter "InstallMarkerTests|InstalledAppsScannerTests|InstallServiceTests"`. Expect FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Make the tests pass, then run the full suite.
- [ ] **Step 5:** Commit with the message `feat(core): HKLM install marker and uninstall/icon fields for installed apps`.

---

### Task 7: UninstallService

**Files:**
- Create: `Install/UninstallService.cs`, `Install/CommandLineSplitter.cs`
- Modify: `Install/MsiNative.cs`, `Install/MsiInstaller.cs` (extract a shared `RunMsiAsync(Func<uint> call, …)`)
- Test: `tests/Installer.Core.Tests/Install/UninstallServiceTests.cs`, `CommandLineSplitterTests.cs`

**Interfaces:**
- Consumes:
  - `InstallMarker`, `InstalledApp` (Task 6)
  - `SafePath` / PostActionRunner delete (Task 2)
  - `ExeInstaller`, `ExitCodeMapper`
  - `IRepositorySource.WriteLog`
- Produces:
  - `static class CommandLineSplitter { static (string Exe, string Args) Split(string commandLine); }`. If the line starts with `"`, the exe is the text up to the next `"`. Otherwise it is the text up to and including the first `.exe` (case-insensitive). The rest, trimmed, is the args. It throws `InvalidDataException` when there is no exe.
  - `sealed class UninstallService(IRepositorySource repo, InstallMarker marker, Func<InstallerType, IInstallerRunner> exeRunner)` with:
    - `bool CanUninstall(PackageEntry pkg, IReadOnlyList<InstalledApp> installed)`
    - `Task<InstallOutcome> UninstallAsync(PackageEntry pkg, IReadOnlyList<InstalledApp> installed, string username, HardwareInfo hw, IProgress<InstallProgress> progress, CancellationToken ct)`
  - Strategy: pick the first one that applies.
    1. `pkg.Uninstall?.Type == "zip"` or `pkg.InstallerType == Zip` → delete `pkg.TargetDir` through `PostActionRunner.Run([new PostAction("delete", null, targetDir)], "")`. Ok → Success.
    2. `pkg.Uninstall?.Type == "msi"`, or `pkg.InstallerType == Msi`, with a ProductCode taken from `pkg.Detect.ProductCode`, or else from an installed match that is `!PerUser` and whose `KeyName` is a GUID → `MsiConfigureProductExW(code, 0, 2 /*INSTALLSTATE_ABSENT*/, "REBOOT=ReallySuppress")`, using the same silent UI, progress and cancel machinery as MsiInstaller.
    3. `pkg.Uninstall?.Type == "command"` → expand env vars in `Command`. It must be fully qualified, pass `SafePath`, and must not start with `\\`. Run ExeInstaller with `Args`.
    4. Otherwise, the first installed match with `!PerUser` and a non-empty `QuietUninstallString` → `CommandLineSplitter.Split`, then ExeInstaller. **Matches with `PerUser == true` are never used.**
    5. Otherwise, `CanUninstall` is false.
  - On Success or RebootRequired, call `marker.Remove(pkg.Id)`.
  - Log with Action `"uninstall"`. Use the same Detail rules as install (Task 4 ErrorText for Message).
  - Timeout = `pkg.TimeoutSec`.

- [ ] **Step 1: Write failing tests**
```csharp
[Theory] [InlineData("\"C:\\P F\\u.exe\" /S", "C:\\P F\\u.exe", "/S")] [InlineData("C:\\Tools\\un.exe -q", "C:\\Tools\\un.exe", "-q")] void Split_cases(...)
[Fact] void Zip_uninstall_deletes_target_and_marker()
[Fact] void Command_from_hkcu_only_is_never_used()          // only PerUser match has QuietUninstallString → CanUninstall false
[Fact] void Hklm_quiet_uninstall_string_runs_through_runner() // FakeRunner records path/args
[Fact] void Manifest_command_must_be_fully_qualified()
[Fact] void Uninstall_logs_action_uninstall()
```
- [ ] **Step 2:** Run `dotnet test --filter "UninstallServiceTests|CommandLineSplitterTests"`. Expect FAIL.
- [ ] **Step 3:** Implement. The MSI path is verified in E2E only.
- [ ] **Step 4:** Make the tests pass, then run the full suite.
- [ ] **Step 5:** Commit with the message `feat(core): uninstall service (zip, msi, manifest command, HKLM quiet string)`.

---

### Task 8: Package icons

**Files:**
- Create: `Platform/IconExtractor.cs`
- Modify: `Vault/PackageBuilder.cs`, `Vault/VaultAdmin.cs` (pass the extractor), `src/Installer.Packager/Program.cs`, `src/Installer.App/AppServices.cs`
- Test: `tests/Installer.Core.Tests/Platform/IconExtractorTests.cs` and `VaultAdminTests`

**Interfaces:**
- Produces:
  - `public static class IconExtractor`:
    - `static byte[]? ExtractPng(string filePath, int index = 0, int size = 48)`. Uses `PrivateExtractIconsW`, then `GetIconInfo` + `GetDIBits` into a 32-bpp BGRA buffer, then `DestroyIcon`/`DeleteObject`, then the PNG encoder. Returns null on any failure.
    - `static byte[] EncodePng(int w, int h, byte[] rgba)`. Writes the signature, IHDR (8-bit RGBA), and IDAT via `ZLibStream` with filter byte 0 per row, then IEND. CRC32 is a small table implementation.
    - `static (string Path, int Index) ParseDisplayIcon(string value)`: optional quotes, optional trailing `,index`.
  - `VaultAdmin(VaultSession session, Func<string, byte[]?>? iconFromExe = null)`.
  - `PackageBuilder.Build` decides the icon in this order:
    1. `icon.png` in the source folder. It must start with the PNG signature and be ≤ 24 KB, else InvalidDataException `"icon.png phải là ảnh PNG ≤ 24 KB"`.
    2. Otherwise, for an `exe` installer, `iconFromExe(installerPath)`, but only if the result is ≤ 24 KB.
    3. Otherwise null.

    The result is stored in `IconPngB64`.
  - App and Packager pass `p => IconExtractor.ExtractPng(p)`.
  - `PackageRowViewModel` gets the installed-app icon from `IconExtractor.ExtractPng(path, index)` for the first `!PerUser` match with a `DisplayIcon` (cache per path).

- [ ] **Step 1: Write failing tests**
```csharp
[Fact] void EncodePng_produces_valid_signature_and_chunks()  // starts 89 50 4E 47; contains "IHDR","IDAT","IEND"
[Fact] void Extract_from_notepad_returns_png()                // %SystemRoot%\System32\notepad.exe (real file; read-only)
[Fact] void Extract_from_missing_file_returns_null()
[Theory] [InlineData("\"C:\\a b\\x.exe\",3","C:\\a b\\x.exe",3)] [InlineData("C:\\x.ico","C:\\x.ico",0)] void ParseDisplayIcon_cases(...)
[Fact] void Icon_png_in_folder_is_used_and_oversize_rejected()
```
- [ ] **Step 2:** Run `dotnet test --filter "IconExtractorTests|VaultAdminTests"`. Expect FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Make the tests pass, then run the full suite.
- [ ] **Step 5:** Commit with the message `feat: package icons from icon.png or exe; installed-app icons from HKLM DisplayIcon`.

---

### Task 9: Peripheral categories + persisted scan history

**Files:**
- Create: `Scanning/PeripheralCategorizer.cs`, `Scanning/ScanHistory.cs`
- Modify: `Scanning/HardwareScanner.cs` (add the `USBSTOR\` prefix and the `MEDIA` class), `Logging/LogEntry.cs`, `src/Installer.App/AppServices.cs` (scan log carries the JSON)
- Test: `tests/Installer.Core.Tests/Scanning/PeripheralCategorizerTests.cs`, `ScanHistoryTests.cs`

**Interfaces:**
- Produces:
  - `enum DeviceCategory { KeyboardMouse, UsbStorage, Printer, Camera, Bluetooth, Audio, Other }`.
  - `static class PeripheralCategorizer { static DeviceCategory Categorize(DeviceInfo d); static string Label(DeviceCategory c); }`. Labels are verbatim from Global Constraints. Rules, checked in order (prefix tests are OrdinalIgnoreCase):

    | Condition | Category |
    |---|---|
    | PnpClass Keyboard or Mouse, or id starts `HID\` | KeyboardMouse |
    | id starts `USBSTOR\` or PnpClass DiskDrive or WPD | UsbStorage |
    | PnpClass Printer or PrintQueue, or id starts `SWD\PRINTENUM\` | Printer |
    | PnpClass Camera or Image | Camera |
    | PnpClass Bluetooth or id starts `BTHENUM\` | Bluetooth |
    | PnpClass MEDIA or AudioEndpoint | Audio |
    | anything else | Other |

  - `LogEntry` gets the trailing parameter `string? ScanReportJson = null`. The scan log sets it to `JsonSerializer.Serialize(report, Json.Options)`.
  - `record ScanHistoryItem(DateTimeOffset Ts, string Username, string Hostname, string? BiosSerial, ScanReport Report)`.
  - `static class ScanHistory { static List<ScanHistoryItem> From(IEnumerable<LogEntry> entries); }`. Keeps Action `"scan"` entries with non-null JSON that deserializes; skips the rest. Sorted by Ts descending.

- [ ] **Step 1: Write failing tests:** a `[Theory]` per categorizer rule, plus `ScanHistory_roundtrips_report_and_skips_bad_json` and `Old_log_entry_without_json_deserializes`.
- [ ] **Step 2:** Run `dotnet test --filter "PeripheralCategorizerTests|ScanHistoryTests|HardwareScannerTests"`. Expect FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Make the tests pass, then run the full suite.
- [ ] **Step 5:** Commit with the message `feat(core): peripheral categories and persisted scan history`.

---

### Task 10: Scan report redesign + re-view

**Files:**
- Modify: `src/Installer.App/ViewModels/ScanReportViewModel.cs`, `Views/ScanReportView.xaml`, `Themes/Theme.xaml`, `AppServices.cs`
- Test: `tests/Installer.App.Tests/ScanReportViewModelTests.cs`

**Interfaces:**
- Consumes: `PeripheralCategorizer` and `ScanHistoryItem` (Task 9).
- Produces:
  - `record OverviewItem(string Label, string Value, bool IsWarning)`. Items, in order:
    - `"Windows"` → verdict text, warning when CrackSuspected or Suspicious
    - `"Office"` → the same rule
    - `"Internet"` → `"Có"`/`"Không"`
    - `"File từ khóa"` → count, warning when > 0
    - `"Thiết bị"` → peripheral count
  - `partial class PeripheralGroup : ObservableObject { string Label; int Count; ObservableCollection<DeviceInfo> Items; [ObservableProperty] bool isExpanded /* default false */; }`. `PeripheralGroups` are sorted by category enum order and omit empty groups.
  - `[ObservableProperty] string deviceSearch`. Filtering keeps devices whose Name or DeviceId contains the text (OrdinalIgnoreCase), recomputes Count, and **expands** groups with matches when the search is non-empty.
  - `static ScanReportViewModel ForReport(ScanReport report, string title, Action onBack)` creates a read-only instance: no scan runs, the Continue button reads `"Quay lại"` and calls onBack. `Title` is `"Báo cáo quét"` normally, or the given title, e.g. `"Lịch sử quét — PC-01 — 10/11/2026 14:30"`.
- The view follows the spec layout: an overview strip of cards on top, then tabs in the order `Máy tính | Thiết bị ngoại vi | Mạng | Bản quyền | File từ khóa`. Load the `frontend-design` skill first.
- The view must stay usable at 1024×640 with no horizontal scroll. Long lists virtualize.

- [ ] **Step 1: Write failing tests**
```csharp
[Fact] void Overview_has_five_items_with_warnings()
[Fact] void Peripherals_grouped_collapsed_by_default_with_counts()
[Fact] void Device_search_filters_and_expands_matching_groups()
[Fact] void ForReport_is_read_only_and_back_calls_onBack()
```
- [ ] **Step 2:** Run `dotnet test tests/Installer.App.Tests`. Expect FAIL.
- [ ] **Step 3:** Implement the VM, then the view.
- [ ] **Step 4:** Make the tests pass. `dotnet build src/Installer.App` must give 0 warnings.
- [ ] **Step 5:** Commit with the message `feat(app): redesigned scan report with overview, grouped peripherals and read-only re-view`.

---

### Task 11: Package list redesign: icons, multi-select queue, uninstall, x86/x64

**Files:**
- Create: `ViewModels/QueueInstallViewModel.cs`, `Converters/IconConverter.cs`
- Delete: `ViewModels/InstallViewModel.cs`, `Views/InstallView.xaml(.cs)`, replaced by `Views/QueueInstallView.xaml`
- Modify: `ViewModels/PackageListViewModel.cs`, `PackageRowViewModel.cs`, `Views/PackageListView.xaml`, `MainViewModel.cs` (CloseBlockedMessage now checks QueueInstallViewModel), `AppServices.cs`
- Test: `tests/Installer.App.Tests/PackageListViewModelTests.cs` and `QueueInstallViewModelTests.cs` (replacing InstallViewModelTests)

**Interfaces:**
- Consumes:
  - `FindForPackage` (Task 6)
  - `UninstallService.CanUninstall/UninstallAsync` (Task 7)
  - `IconPngB64` (Task 8)
  - `ScanReportViewModel.ForReport` (Task 10)
- Produces:
  - `record QueueItem(PackageEntry Package)` with observable `Status` (the verbatim queue texts) and `Message`.
  - `QueueInstallViewModel(string title, IReadOnlyList<PackageEntry> items, Func<PackageEntry, IProgress<InstallProgress>, CancellationToken, Task<InstallOutcome>> run, Action onBack, string runningText = "Đang cài")`. It also has an internal overload that takes a progress factory, as before.
    - Properties: `Items`, `Header` (`"Gói {i}/{n}"`), `Percent`, `IsIndeterminate`, `Step`, `IsDone`, `CancelCommand`, `BackCommand`, `Completion`.
    - Runs items sequentially. Outcome → status:
      - Success → `"Thành công"`
      - RebootRequired → `"Cần khởi động lại"`
      - Failed or Timeout → `"Lỗi"`, with `Message = outcome.Message`
      - the cancelled item → `"Đã hủy"`
      - items after a cancel → `"Đã bỏ qua"`
      - a failed item does **not** stop the queue
    - If any item ended in RebootRequired, the done text adds `"Hãy khởi động lại máy để hoàn tất."`.
  - `PackageRowViewModel` adds:
    - `[ObservableProperty] bool isSelected` (setter ignored when !Allowed)
    - `byte[]? Icon` and `byte[]? InstalledIcon`
    - `string InstalledVersion`, which lists every comparable match, e.g. `"23.01 (x64), 22.01 (x86)"`
    - `bool CanUninstall` and `UninstallCommand`
  - `PackageListViewModel` ctor changes:
    - `findInstalled` becomes `Func<PackageEntry, IReadOnlyList<InstalledApp>>`.
    - adds `uninstall: Func<PackageEntry, IReadOnlyList<InstalledApp>, IProgress<InstallProgress>, CancellationToken, Task<InstallOutcome>>`
    - adds `canUninstall: Func<PackageEntry, IReadOnlyList<InstalledApp>, bool>`
    - adds `viewScan: Action`
  - `PackageListViewModel` adds:
    - `SelectedCount`, `InstallSelectedLabel` (`"Cài các mục đã chọn ({n})"`), `InstallSelectedCommand` (disabled when 0)
    - `ViewScanCommand`
    - the per-row Install button, which becomes a queue of 1
  - Downgrade confirms are **collected once**: before starting, for every selected row with NeedsDowngradeConfirm, one confirm text lists all of them. Rows the user declines are dropped.
  - Selection order: if a bundle was applied (Task 13), bundle order; otherwise display order.
  - After the queue's Back, all rows refresh and the selection clears.
  - Uninstall uses QueueInstallViewModel with title `"Gỡ cài đặt"`, `runningText = "Đang gỡ"`, after `confirm($"Gỡ {name} {version} khỏi máy?")`. Rows with `!CanUninstall` show a tooltip `Strings.NoUninstall`.
  - `ViewScanCommand` navigates to `ScanReportViewModel.ForReport(scan, "Báo cáo quét", () => navigate(this))`. Selection is preserved.
- View: an icon column (48 px, default icon when null), a checkbox column, and actions (Hướng dẫn, Gỡ). The top toolbar holds "Xem lại báo cáo quét", the bundle picker placeholder (filled in Task 13) and the primary install button. Load `frontend-design` first.

- [ ] **Step 1: Write failing tests**
```csharp
[Fact] void Blocked_row_cannot_be_selected()
[Fact] void Install_selected_runs_in_order_and_reports_each_status()
[Fact] void Failed_item_does_not_stop_queue()
[Fact] void Cancel_marks_current_cancelled_and_rest_skipped()
[Fact] void Downgrade_confirms_are_asked_once_and_declined_rows_dropped()
[Fact] void Installed_version_lists_x64_and_x86()
[Fact] void Uninstall_hidden_when_cannot_and_runs_queue_when_can()
[Fact] void View_scan_and_back_keeps_selection()
[Fact] void Close_blocked_while_queue_running()
```
- [ ] **Step 2:** Run `dotnet test tests/Installer.App.Tests`. Expect FAIL.
- [ ] **Step 3:** Implement the VMs, the IconConverter (base64 or byte[] → frozen BitmapImage; null → the default icon resource) and the views. Wire AppServices: real `FindForPackage`, `UninstallService` with the real ExeInstaller factory, and the marker over the real registry.
- [ ] **Step 4:** Make the tests pass. The build must give 0 warnings.
- [ ] **Step 5:** Commit with the message `feat(app): package icons, multi-select install queue, uninstall, x86/x64 versions`.

---

### Task 12: Bundles in Core + CLI

**Files:**
- Create: `Model/BundleResolver.cs`
- Modify: `Vault/VaultAdmin.cs`, `src/Installer.Packager/Program.cs`, `Args.cs`
- Test: `tests/Installer.Core.Tests/Vault/BundleTests.cs` and `Packager/ArgsTests.cs`

**Interfaces:**
- Produces:
  - `static class BundleResolver { static List<(BundleItem Item, PackageEntry? Package)> Resolve(Catalog c, Bundle b); }`. Matching is by id, OrdinalIgnoreCase. If Version is given, the exact version is used; if Version is null, the highest version via `VersionComparer.Compare` is used. Order follows `b.Items`. A missing package gives null.
  - `VaultAdmin` adds:
    - `Bundle AddBundle(Bundle b)`, `Bundle UpdateBundle(Bundle b)` (match by Id) and `void RemoveBundle(string id)`.
    - Validation: Id matches `^[A-Za-z0-9._-]+$`; Name is non-blank; Items are non-empty; every referenced id/version must resolve, else InvalidDataException `"Bộ gói tham chiếu gói không có trong kho: {id}"`.
    - A duplicate Id on Add, or a missing Id on Update or Remove, throws InvalidOperationException `"Không tìm thấy bộ gói"` or `"Bộ gói đã tồn tại"` respectively.
    - Each change goes through the existing commit path (Version++).
  - Packager:
    - `add-bundle <root> <bundle.json> --user <u> [--password]`. The bundle.json uses snake_case Bundle JSON. It creates the bundle or updates it if it exists.
    - `remove-bundle <root> <id> --user <u>`.
    - Stdout: `Đã lưu bộ gói {name}` / `Đã xóa bộ gói {id}`.
  - RemovePackage does **not** cascade into bundles; stale references are handled in the UI.

- [ ] **Step 1: Write failing tests:** `Resolve_latest_and_exact_version`, `Resolve_missing_returns_null_entry`, `AddBundle_rejects_unknown_package`, `Update_and_remove_bundle`, `Bundle_survives_reopen`, `Cli_add_bundle_returns_0`.
- [ ] **Step 2:** Run `dotnet test --filter "BundleTests|ArgsTests"`. Expect FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Make the tests pass, then run the full suite.
- [ ] **Step 5:** Commit with the message `feat: role bundles in catalog with admin API and CLI`.

---

### Task 13: Bundle UI + admin scan history + admin log polish

**Files:**
- Modify: `ViewModels/PackageListViewModel.cs`, `ViewModels/AdminViewModel.cs`, `Views/PackageListView.xaml`, `Views/AdminView.xaml`, `AppServices.cs`
- Test: `tests/Installer.App.Tests/PackageListViewModelTests.cs` and `AdminViewModelTests.cs`

**Interfaces:**
- Consumes:
  - `BundleResolver`, `VaultAdmin` bundle API (Task 12)
  - `ScanHistory` (Task 9)
  - `ScanReportViewModel.ForReport` (Task 10)
- Produces:
  - `PackageListViewModel` adds:
    - `Bundles`, the bundle list (hidden when empty)
    - `[ObservableProperty] Bundle? selectedBundle`
    - `ApplyBundleCommand`: clears the selection, then selects resolvable and Allowed rows in bundle order. `BundleNotices` (strings) lists `"{name}: {reason}"` for blocked rows and `"{id}: Không có trong kho"` for missing ones.
  - `AdminViewModel` adds a **Bộ gói** tab:
    - a list, plus an editor with `EditId`, `EditName`, `EditDescription` and `EditItems`. EditItems takes one line per item, `id` or `id@version`.
    - `SaveBundleCommand` (add or update) and `RemoveBundleCommand` (with confirm), all running through the existing async Run helper.
  - `AdminViewModel` adds a **Lịch sử quét** tab:
    - `ScanHistory.From(logs)`, filtered by `MachineFilter`, FromDate and ToDate.
    - Paged 200 per page with `PageText` `"Trang {p}/{n}"` and `NextPage`/`PrevPage`.
    - `OpenScanCommand(item)` navigates to `ScanReportViewModel.ForReport(item.Report, $"Lịch sử quét — {item.Hostname} — {item.Ts.ToLocalTime():dd/MM/yyyy HH:mm}", back to admin)`. AdminViewModel gets a `navigate` delegate for this.
  - Log loading moves into `Task.Run` (`LoadLogsAsync`, also used by the history tab), and a successful load clears `Error`. Fixes the backlog items.
  - Cancel feedback: QueueInstallViewModel's Cancel sets `Step = "Đang hủy…"` immediately.
  - Rewrite `Busy_disables_commands_during_operation`. Gate the operation with a TaskCompletionSource-based picker and assert **outside** the PropertyChanged handler while the operation is pending.

- [ ] **Step 1: Write failing tests**
```csharp
[Fact] void Apply_bundle_selects_in_bundle_order_and_lists_blocked_and_missing()
[Fact] void Admin_save_bundle_parses_lines_and_persists()
[Fact] void Admin_invalid_bundle_line_shows_error()
[Fact] void Scan_history_lists_filters_pages_and_opens_report()
[Fact] void Logs_load_async_and_clear_error_on_success()
[Fact] void Cancel_shows_dang_huy_immediately()
```
- [ ] **Step 2:** Run `dotnet test tests/Installer.App.Tests`. Expect FAIL.
- [ ] **Step 3:** Implement. Load `frontend-design` before editing XAML.
- [ ] **Step 4:** Make the tests pass. The build must give 0 warnings.
- [ ] **Step 5:** Commit with the message `feat(app): bundles UI, admin scan history, async logs`.

---

### Task 14: Backlog sweep, checklist, build, docs

**Files:**
- Modify:
  - `Scanning/KeywordFileScanner.cs`
  - `Crypto/PackageCrypto.cs` (+ `Decrypt(Stream, Stream, byte[], CancellationToken)` overload)
  - `Install/InstallService.cs` (decrypt with ct; extract per entry with ct)
  - `Install/ZipInstaller.cs` (delete files it created on cancel or failure)
  - `Vault/PackageBuilder.cs` (quote-aware MSI silent_args check)
  - `build/build-portable.ps1`
  - `docs/e2e-checklist.md`
  - `samples/7zip/README.md`
  - `CLAUDE.md`
- Test: additions to the corresponding test files

**Interfaces:** No new public API, apart from the `Decrypt` overload with a CancellationToken. The existing overload delegates to it with `CancellationToken.None`.

- [ ] **Step 1: Write failing tests**
```csharp
[Fact] void Keyword_scan_enters_cloud_placeholder_dirs_but_skips_junctions()  // junction skipped; a normal dir with ReparsePoint attr but LinkTarget == null (simulate via interface/helper `ShouldSkipDir(FileSystemInfo)`) is entered
[Fact] void Decrypt_observes_cancellation_between_chunks()
[Fact] void Zip_cancel_removes_files_it_created_but_not_preexisting()
[Fact] void Msi_silent_args_quote_aware()   // INSTALLDIR="C:\My -x" accepted; /qn rejected
```
- [ ] **Step 2:** Run the RED check. Expect FAIL.
- [ ] **Step 3:** Implement the items:
  - Keyword scan: own recursion; skip a directory only when `LinkTarget != null`.
  - Decrypt: check `ct` before each chunk.
  - InstallService: ZipArchive extraction per entry with `ct`, keeping the .NET zip-slip safety by validating that each entry's full path stays under the destination.
  - ZipInstaller: track the files it creates and delete them on failure or cancel.
  - Quote-aware tokenizer for silent_args: tokens split on whitespace outside double quotes.
  - Build script: reject passwords containing `"` with the message `"Mật khẩu không được chứa dấu ngoặc kép (\")"`.
  - README: double the backslashes.
  - Checklist:
    - use the visible texts `"Kho bị sửa đổi, không thể sử dụng."` and `"KMS host = 127.0.0.2"`;
    - add items: rename-workspace attempt (expect Access denied), junction ancestor, real MSI uninstall, 3-package queue + cancel, bundle apply, icons, build ≤ 120 MB, scan history re-open.
  - CLAUDE.md: update the status line, commands and architecture bullets for the new pieces (workspace location, key seam, marker, uninstall, queue).
- [ ] **Step 4:** Run the full suite. Run the build script into scratch with `-InitVault -WithDemo` and check the size is ≤ 120 MB. Do not launch the exe, and delete the scratch output.
- [ ] **Step 5:** Commit with the message `chore: backlog sweep (cancellable decrypt/extract, keyword scan, zip cleanup), checklist and docs`.

---

## Self-Review (done)

- **Spec coverage:**

  | Req | Task(s) |
  |---|---|
  | 1, 2 | T1 |
  | 3 | T2, T7 |
  | 4 | T3 |
  | 5 | T4 |
  | 6 | T5 |
  | 7 | T5 |
  | 8, 9, 10 | T10 |
  | 11 | T9, T13 |
  | 12 | T11 |
  | 13 | T8 |
  | 14 | T8, T11 |
  | 15–18 | T11 |
  | 19 | T12, T13 |
  | 20 | T13 |
  | 21, 22 | T7, T11 |
  | 23 | T6 |
  | 24 | T7 |
  | 25 | T1, T14 |

  Edge-case table: covered across T1, T2, T7, T11, T13 and T3/T5 (legacy vault).
- **Type consistency:** `FindForPackage`, `UninstallService.CanUninstall/UninstallAsync`, `QueueInstallViewModel`, `ScanReportViewModel.ForReport`, `BundleResolver.Resolve`, `IRepositorySource.GetPackageKeyAsync/GetDocsKeyAsync` and `ScanHistoryItem` are used with the same signatures everywhere.
- **Review Focus:** each of the 5 lines has a test in its owning task. Line 1 → T3/T5, line 2 → T1, line 3 → T11, line 4 → T2, line 5 → T7/T8.

---

## Addendum 2026-11-12: Tasks 15–19 (spec-1.1 requirements 26–33)

Execution order: 15, 16, 17, 18, 19, then Task 14 last.

### Addendum Global Constraints
- **VersionStatus labels.** These replace the Plan 1 labels:

  | VersionStatus | Label |
  |---|---|
  | NotInstalled | "Chưa cài" |
  | Older | "Cần cập nhật" |
  | Equal | "Đã cài" |
  | Newer | "Máy có bản mới hơn" |
  | Incomparable | "Đã cài (không rõ phiên bản)" |

- **New strings (verbatim):**
  - "Cấu hình sau cài lỗi"
  - "Script trước khi cài lỗi"
  - "Bỏ chọn tất cả"
  - "đã cài trên máy"
  - "Lịch sử thiết bị lưu trữ"
  - Internet column values: "Có" / "Chỉ nội bộ" / "Không rõ"
  - Conclusion texts:
    - "Máy đã từng kết nối internet: Có — lần gần nhất {0:dd/MM/yyyy HH:mm}"
    - "Có dấu hiệu gián tiếp — lần gần nhất {0:dd/MM/yyyy HH:mm}"
    - "Chưa thấy bằng chứng (nhật ký có thể đã bị xóa hoặc ghi đè)"
  - Format dates with InvariantCulture.
- **New allowed package:** `System.Diagnostics.EventLog` 8.0.1 (Microsoft), for EventLogReader. Add it to Installer.Core only.
- **Script interpreters never resolve through PATH.** Use these exact command lines:
  - PowerShell: `%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "<path>"`
  - cmd: `%SystemRoot%\System32\cmd.exe /c "<path>"`, with the path quoted.
- **Log parsing.** When parsing the event log and the setupapi log, read structured properties or fixed English log markers. Never parse localized message text.

### Task 15: Package editor form (add and edit software without hand-written manifests)

**Files:**
- Create:
  - `src/Installer.Core/Vault/PackageDraft.cs` (the record plus `PackageSource.Create(PackageDraft, string tempDir) → string folder`)
  - `src/Installer.Core/Platform/InstallerInfoReader.cs`
  - `src/Installer.App/ViewModels/PackageEditorViewModel.cs`
  - `src/Installer.App/Views/PackageEditorView.xaml`
- Modify: `AdminViewModel.cs`, `AdminView.xaml`, `AppServices.cs`.
- Tests: `tests/Installer.Core.Tests/Vault/PackageSourceTests.cs`, `Platform/InstallerInfoReaderTests.cs`, `tests/Installer.App.Tests/PackageEditorViewModelTests.cs`.

**Interfaces:**
- `record PackageDraft(string Id, string Name, string Version, InstallerType Type, string InstallerPath, string SilentArgs, string? TargetDir, int TimeoutSec, DetectRule Detect, List<string> ExtraFiles, List<string> DocFiles, string? IconPath, string? PreScriptPath, string? PostScriptPath, int ScriptTimeoutSec, List<RuleSpec> Rules, UninstallSpec? Uninstall)`.
- `PackageSource.Create` writes the existing manifest format into `tempDir`:
  - The installer and every ExtraFile go into `installer\`, file name only. Duplicate names throw InvalidDataException.
  - Docs go into `docs\`.
  - The icon is written as `icon.png`.
  - Scripts go into `scripts\`, with manifest fields `pre_script` / `post_script`. Task 16 consumes these fields; this task only writes them.
  - It returns the folder. The editor then calls the existing `VaultAdmin.AddPackage(folder)` or `UpdatePackage(id, folder, keepOld)`, and deletes the temp folder in `finally`.
- `InstallerInfoReader.Read(string path) → (string? Name, string? Version, string? ProductCode)`:
  - `.msi`: use P/Invoke `MsiOpenPackageExW(path, 1 /*IGNOREMACHINESTATE*/)`, then `MsiGetProductPropertyW` for ProductName, ProductVersion and ProductCode, then `MsiCloseHandle`.
  - `.exe`: use `FileVersionInfo` (ProductName, ProductVersion).
  - Any other file type, or any failure, returns nulls.
  - It never executes the file.
- `PackageEditorViewModel(PackageEntry? existing, Func<string?> pickFile, Func<IReadOnlyList<string>> pickFiles, Func<PackageDraft, Task> save, Action onCancel)`. Also inject `Func<string, (string?, string?, string?)> readInfo` for tests.
  - Fields bind to the draft.
  - Picking the installer pre-fills Name, Version and Id when they are empty. Id is Name lower-cased, with every character outside `[a-z0-9._-]` replaced by '-'. For an MSI it also pre-fills Detect.ProductCode.
  - Type is inferred from the file extension.
  - Save validates the following and shows errors in `Error`:
    - Name is set.
    - Id matches the regex `^[A-Za-z0-9._-]+\z`.
    - Version is non-blank.
    - An installer has been chosen.
  - Editing an existing package pre-fills from the PackageEntry. The installer must be re-picked, and the form says so.
- Admin "Gói" tab:
  - Buttons "Thêm phần mềm" and "Sửa" open the editor (navigate).
  - The old folder-based add stays, renamed "Thêm từ thư mục", for advanced use.

**Tests:**
- PackageSource writes a manifest that `PackageBuilder.Build` accepts, and the extra file lands in `installer\`.
- Duplicate extra file names are rejected.
- InstallerInfoReader returns a non-null ProductVersion for `%SystemRoot%\System32\notepad.exe` and nulls for a text file.
- Editor: pre-fill from a fake reader, validation errors, and Save calls `save` with the draft.

Load `frontend-design` before writing XAML. Render the screenshot `admin-3-editor.png` to scratchpad/ui-preview with a temporary test; do not commit the screenshot.

**Commit:** `feat: package editor form with version prefill and extra files`

### Task 16: Pre/post configuration scripts and script-only packages

**Files:**
- Create: `src/Installer.Core/Install/ScriptRunner.cs`.
- Modify:
  - `Model/Catalog.cs`: add trailing `string? PreScript = null, string? PostScript = null, int ScriptTimeoutSec = 600` to PackageEntry, and add `InstallerType.Script`.
  - `Vault/PackageBuilder.cs`:
    - add the manifest fields;
    - zip `scripts\`;
    - for the Script type, require installer_file to be a script in `scripts\`;
    - validate script extensions: .ps1, .cmd, .bat.
  - `Install/InstallService.cs`.
  - `Install/InstallTypes.cs`: the factory maps Script → ScriptRunner.
  - `Install/UninstallService.cs`: the Script type gets no plan unless it has an explicit command.
- Tests: `ScriptRunnerTests.cs`, plus additions to `InstallServiceTests` and `VaultAdminTests`.

**Interfaces:**
- `ScriptRunner : IInstallerRunner`:
  - Runs `.ps1`, `.cmd` and `.bat` with the exact command lines from the Global Constraints.
  - WorkingDirectory = the script's folder; CreateNoWindow.
  - Captures stdout and stderr, keeping the last 4000 characters.
  - Exit code 0 → Success; any other code → Failed with "Lỗi cài đặt (mã {code})".
  - On timeout, kill the process tree and return Timeout.
  - `InstallOutcome.Detail` = the captured output.
- InstallService runs the steps in this order:
  1. pre_script. If it fails, the outcome is Failed with "Script trước khi cài lỗi", and the installer does not run.
  2. The installer. For the Script type there is no separate installer: the installer_file is the script itself and runs via ScriptRunner.
  3. post_actions.
  4. post_script. If it fails, keep Success/RebootRequired, append " (Cấu hình sau cài lỗi)" to Message, and put the output into Detail.
  5. The marker.
  6. The log.
- Scripts run from the extracted package workspace (`pkg\scripts\`).

**Tests:**
- Real `.cmd` scripts in a temp dir:
  - exit 0
  - exit 3
  - output is captured
  - timeout, using `ping -n 30 127.0.0.1`
- A `.ps1` script that writes a file.
- A pre-script failure stops the runner (the FakeRunner is not called).
- A post-script failure keeps Success, with the warning.
- A Script-type package installs and writes the marker.
- The manifest rejects `.exe` as a script.

**Commit:** `feat(core): pre/post configuration scripts and script-only packages`

### Task 17: Status wording, auto-apply bundles, clear selection

**Files:**
- Modify: `src/Installer.App/Converters/DisplayText.cs`, `ViewModels/PackageListViewModel.cs`, `Views/PackageListView.xaml`, and the affected App tests.
- Update the Plan 1 label tests deliberately to the new labels.

**Interfaces:**
- Use the new labels from the Addendum Global Constraints.
- Changing `SelectedBundle` applies the bundle immediately. Remove `ApplyBundleCommand` and its button.
- Applying a bundle ticks only rows that are Allowed and have VersionStatus NotInstalled or Older.
- A row skipped because it is already installed gets the notice `"{name}: đã cài trên máy"`.
- `ClearSelectionCommand` ("Bỏ chọn tất cả") unticks every row and clears the bundle notices.
- Choosing an empty or null bundle clears bundle-order mode only.

**Tests:**
- The new labels.
- Selecting a bundle ticks only NotInstalled/Older rows and lists the installed ones.
- A manual tick is still allowed after a bundle is applied.
- ClearSelection.

Re-render `pkg-4-bundle.png`.

**Commit:** `feat(app): clearer status wording; bundles apply on selection; clear selection`

### Task 18: USB storage history

**Files:**
- Create, in Core:
  - `Platform/IEventLogReader.cs` and `Platform/EventLogReader.cs` (using System.Diagnostics.EventLog)
  - `Platform/IFileTextReader.cs` (reads text files with FileShare.ReadWrite)
  - `Scanning/UsbStorageHistoryScanner.cs`
- Modify:
  - `Scanning/ScanReport.cs`: trailing `List<UsbStorageRecord>? UsbHistory = null`.
  - `MachineScanner.cs`: add a new phase, isolated like the others.
  - App `ScanReportViewModel` and its view: add a section "Lịch sử thiết bị lưu trữ" in the Thiết bị ngoại vi tab.
  - `AppServices.cs`.
- Tests: `UsbStorageHistoryScannerTests.cs`.

**Interfaces:**
- `record EventRecordInfo(int Id, DateTime TimeCreated, IReadOnlyList<object?> Properties)`.
- `interface IEventLogReader { IReadOnlyList<EventRecordInfo> Read(string logName, int[] ids, int max = 2000); }`. A missing or disabled log returns an empty list. It never throws.
- `record UsbStorageRecord(string FriendlyName, string Serial, string DeviceId, DateTime? FirstConnected, DateTime? LastConnected)`.
- Data sources:
  - **Device list:** `HKLM\SYSTEM\CurrentControlSet\Enum\USBSTOR\<Disk&Ven..>\<serial>`. Read FriendlyName. The serial is the subkey name without its trailing `&0`.
  - **FirstConnected:** in `%SystemRoot%\INF\setupapi.dev*.log`, find the `>>>  [Device Install` lines that contain that `USBSTOR\...\<serial>`. Take the earliest `>>>  Section start yyyy/MM/dd HH:mm:ss.fff` that follows one of them.
  - **LastConnected:** the latest of these:
    - `Microsoft-Windows-Partition/Diagnostic` event 1006 matching the serial;
    - `Microsoft-Windows-Kernel-PnP/Configuration` events 400/410 whose device-instance property matches.
  - A missing value is null, and the UI shows "—".
- Before coding the parser, the implementer must dump one real event of each kind on this machine (read-only) to confirm the property positions, and record them in the report.

**Tests:**
- A fake registry, fake setupapi text and fake events produce the expected records.
- Malformed log lines are skipped.
- No events → LastConnected is null.
- A Partition event without a serial match is ignored.

Screenshot: `scan-7-usb.png`.

**Commit:** `feat: USB storage connection history in the scan report`

### Task 19: Internet connection evidence

**Files:**
- Create: `src/Installer.Core/Scanning/InternetEvidenceScanner.cs`.
- Modify:
  - `NetworkProfile`: trailing `bool? HadInternet = null`, set in a post-pass.
  - `ScanReport`: trailing `InternetEvidence? Internet = null`.
  - `MachineScanner`.
  - App `ScanReportViewModel` and the Mạng tab: add an "Internet" column and a conclusion line at the top.
  - `AppServices.cs`.
- Tests: `InternetEvidenceScannerTests.cs`.

**Interfaces:**
- `record EvidenceItem(string Source, DateTime Time, string Text)`.
- `enum InternetVerdict { Yes, Indirect, NoEvidence }`.
- `record InternetEvidence(InternetVerdict Verdict, DateTime? LastSeen, List<EvidenceItem> Items)`.
- **Strong evidence (→ Yes):**
  - A `Microsoft-Windows-NetworkProfile/Operational` event 10000 whose connectivity property has an internet flag: NLM_CONNECTIVITY IPV4_INTERNET 0x40 or IPV6_INTERNET 0x400.
  - It also sets HadInternet on the profile with that name:
    - flag seen → `true`;
    - profile seen only without the flag → `false`, shown as "Chỉ nội bộ";
    - profile never seen → null, shown as "Không rõ".
  - A `Microsoft-Windows-NCSI/Operational` capability event that reports Internet capability.
- **Indirect evidence (→ Indirect, used only when there is no strong evidence):**
  - System log `Microsoft-Windows-WindowsUpdateClient` events 19/43/44.
  - `Microsoft-Windows-Time-Service` sync events whose peer is not a private or LAN address.
- Before coding, the implementer dumps one real event per source on this machine to confirm the property positions, and records them.
- The conclusion texts are listed in the Addendum Global Constraints.

**Tests:**
- A strong internet flag → Yes, and HadInternet is set per profile name.
- Only Windows Update events → Indirect.
- Nothing → NoEvidence.
- A local-only profile → false.
- A Time-Service peer with a private IP is ignored.

Screenshot: re-render `scan-3-mang.png`.

**Commit:** `feat: internet connection evidence per network and overall verdict`
