# Backlog cho Plan 1.1 và Plan 2

> Tổng hợp từ review toàn nhánh Plan 1 (2026-11-11) và góp ý của người dùng sau khi chạy thử bản demo.

## A. Phải làm đầu tiên trong Plan 1.1 (bảo mật / thiết kế)
1. **Workspace ra khỏi %TEMP% của user**: tạo dưới `%ProgramData%\SecureInstaller\work` với ACL bảo vệ (Admins+SYSTEM, owner = Administrators), kiểm tra không phải reparse point. Đặt `DOTNET_BUNDLE_EXTRACT_BASE_DIR` (hoặc bỏ self-extract native DLL) để tránh DLL planting trong `%TEMP%\.net`.
2. **Post-action / zip TargetDir đi theo junction ở thư mục cha**: duyệt các ancestor đã tồn tại của Dst/TargetDir, từ chối nếu có reparse point.
3. **Seam cho Plan 2**: thêm `Task<byte[]> GetPackageKeyAsync(pkg, ct)` (và khóa riêng cho docs) vào `IRepositorySource`; docs dùng `DocsKeyB64` riêng; scan log đi qua `IRepositorySource.WriteLog`.
4. **Map lỗi tiếng Việt thống nhất**: IOException / UnauthorizedAccess / PackageCorrupt / CryptographicException → `Strings.*`; `ex.Message` chỉ vào log Detail.
5. **Spec req 5 + đĩa chỉ đọc**: hiện số phiên bản & ngày cập nhật kho; thông báo cho user khi kho chỉ đọc.

## B. Tính năng người dùng yêu cầu
- Sắp xếp lại giao diện báo cáo quét: dải Tổng quan; tab Máy tính tách khỏi **Thiết bị ngoại vi** (nhóm theo loại, thu gọn, tìm kiếm); hiện cả x86/x64 khi có.
- Xem lại báo cáo quét: nút trong danh sách gói + **Lịch sử quét** (lưu báo cáo đầy đủ vào log mã hóa).
- **Icon phần mềm**: `icon.png` trong gói hoặc tự trích từ exe/msi lúc thêm gói; icon phần mềm đã cài từ `DisplayIcon`.
- **Chọn nhiều phần mềm cài một lúc** (hàng đợi trong ViewModel, hủy được cả hàng đợi).
- **C. Bộ gói theo vai trò** (cài lần lượt một lần bấm).
- **D. Gỡ cài đặt** (lưu `UninstallString`/`QuietUninstallString`; gói zip có detect rule tổng hợp qua marker).
- **G. Gộp Packager vào SecureInstaller.exe** (giảm ~66 MB).
- **H. Plan 2 — máy chủ LAN** (sau Plan 1.1).

## C. Nên sửa sớm (từ triage)
- Reparse-point skip trong quét từ khóa bỏ sót thư mục OneDrive → chỉ bỏ junction/symlink.
- Hủy giữa lúc giải mã/giải nén (Decrypt(ct), giải nén từng entry).
- Nhật ký crash khi chạy Release (handler hiện chỉ Debug.WriteLine).
- Phản hồi "Đang hủy…"; LoadLogs xóa Error khi thành công; tải log Admin trong Task.Run.
- Test Busy kiểm tra trong PropertyChanged (không thể fail) → viết lại.
- build-portable.ps1: từ chối mật khẩu chứa `"`; checklist dùng chữ hiển thị thật ("Kho bị sửa đổi, không thể sử dụng.", "KMS host = 127.0.0.2").
- Gói zip bị hủy để lại file dở trong TargetDir.
- README 7-Zip: JSON backslash đơn `C:\App\a.ini` → `C:\App\a.ini`.
- Kiểm tra silent_args MSI nên hiểu dấu ngoặc kép (`INSTALLDIR="C:\My -x"` bị chặn nhầm).
- Blob bị thay thế chỉ phát hiện lúc cài; lưu kích thước blob trong catalog và kiểm tra khi mở kho.
- Thư mục log dự phòng `%ProgramData%\SecureInstaller\logs` nên có ACL bảo vệ và kiểm tra reparse point.

## D. Ghi nhận cho Plan 2
- `EncryptedLog` cần VK — máy trạm chế độ máy chủ không có VK; bộ đệm log offline cần khóa riêng (vd. khóa phiên bọc bằng public key của máy chủ).
- Tách interface `SubmitScanAsync` / `AwaitApprovalAsync` riêng để `PortableRepositorySource` không phải giả lập.
- Cần lớp phiên ở App (không phụ thuộc trực tiếp `VaultSession`).

## E. Nhật ký quyết định (Rulings) của Plan 1
- Ruling: work on branch feat/portable in-place instead of a separate worktree — repo is new, single developer, nothing else in flight — cost if wrong: none, branch can be moved to a worktree later.
- Ruling: pause after EVERY task for the user (user instruction + CLAUDE.md override the skill's continuous-execution rule).
- | T16 | Truncated_blob test vs signed real vault | Ruling: test uses a fake IRepositorySource wrapping the real truncated blob with matching Sha256 — signed catalog can't be edited — cost: none |
- Ruling: user chose to run Tasks 2–6 continuously, then report — per user message 'C'.
- Ruling: signing public key in plaintext vault.meta can be swapped by a non-admin who holds VK to forge a catalog — accepted risk per spec assumption "bảo mật vừa phải đổi lấy sự đơn giản"; mitigation for Task 17/19: show the vault's signing-key fingerprint on the login/admin screen so admins can spot a swap — cost if wrong: a determined insider can alter the package list; surface to user.
- Ruling (T4): expired lock resets FailedAttempts before password check — otherwise one wrong try after expiry would re-lock immediately — cost if wrong: weaker brute-force throttle (5 tries per 15 min, as spec says)
- Ruling (T5): VaultSession.Catalog becomes internal-settable so admin edits are visible to the session — cost: small mutability in a session object
- Ruling (T5): id/version restricted to ^[A-Za-z0-9._-]+$ because they become file names — cost: admins can't use spaces/Vietnamese in package ids (Name is free text)
- Ruling (T5): manifest docs omitted → take all files in docs\ ; default timeout_sec 1800 — plan silent on defaults
- Ruling (T5): VaultAdmin refuses read-only vault with "Kho đang ở chế độ chỉ đọc" (string from Task 19)
- Ruling (T5 fix): blob file name = `<id>-<ver>-<8 random hex>.pkg.enc`, docs dir = same stem under vault\docs\ — catalog already stores BlobFile so names need not be derivable; removes in-place overwrite (C1) and collisions (I1); old files deleted only after catalog commit — cost: deviates from spec's `<id>-<ver>.pkg.enc` naming (cosmetic)
- Ruling (T5 fix): WriteCatalog writes catalog.json.enc.tmp + catalog.sig.tmp, then moves .enc then .sig; VaultStore.Open rolls forward if both .tmp files exist and the pair verifies against meta's public key, otherwise deletes the leftovers — cost: small extra logic in Open
- Ruling (T5 fix r2): Open repair rule, in order: (1) live enc+sig verify → delete leftover .tmp files; (2) else sig.tmp verifies against live enc → move sig.tmp to sig; (3) else both .tmp verify → move both; (4) else leave as is (Unlock reports tampered). WriteCatalog: failure before .enc move → delete its .tmp files and rethrow (caller cleanup is then correct); failure after .enc move → throw CatalogCommitPendingException and callers must NOT delete the new files (Open rule 2 completes it). Build opens blob/doc files with FileMode.CreateNew — cost: a crash leaves orphan blobs (harmless disk use)
- Ruling: user chose A — run Tasks 7–11 continuously, then report.
- Ruling (T7 minors, load-bearing): RegistryReader catch widen to IOException/ArgumentException (Review Focus 5) and FakeWmi rows OrdinalIgnoreCase (fake fidelity for T8–T11) — CARRIED into Task 8 dispatch
- Ruling (T9): Office WMI read failure + no C2R key → Unknown (not NotInstalled) — read failures must never be presented as a fact
- Ruling (T9): SystemInfo.IsDomainJoined switched to NetGetJoinInformation (status 3) — fixes T7 minor that would make GVLK heuristic wrong on workgroup PCs with a DNS suffix
- Ruling (T9): invalid admin-supplied regex signatures are skipped silently; unknown kinds ignored
- Ruling (T11): keyword match normalizes file name and keyword to NFC — Vietnamese names may be stored decomposed (NFD) and would silently miss
- Ruling (T11): cancellation returns partial result with TimedOut=true instead of throwing; overlapping paths de-duplicated
- Ruling (T11 fix): MachineScanner isolates each phase — on exception (not OperationCanceled) fallback: HardwareInfo with Errors entry, NetworkHistory([], false), LicenseInfo Unknown + "Không đọc được thông tin bản quyền", empty KeywordScanResult; caller cancellation still propagates (whole scan cancelled) — cost: none
- Ruling (T11 fix): deadline/ct checked on every enumerated file via Stopwatch; one blocked MoveNext on a dead share can't be interrupted — documented limitation
- Ruling: user chose A — run Tasks 12–16 continuously, then report.
- Ruling (T13): workspace created with ACL in one call (no permissive window); protected delete roots extended with %SystemRoot%\System32 and %ProgramData% beyond the plan's list — cost: an admin can't delete those two exact folders via post-action (never legitimate)
- Ruling (T13 fix): Delete/Copy Dst rejects paths starting with \ (covers \?\, \.\, UNC); for existing targets expand 8.3 via GetLongPathNameW before comparing; do NOT resolve junction targets (deleting a junction removes only the link, so resolving would over-block) — cost: admins can't target network shares in post-actions
- Ruling (T13 fix): TempWorkspace exposes public bool Deleted; leftovers after retries are scheduled with MoveFileEx(DELAY_UNTIL_REBOOT) (best effort, needs admin); add static SweepStale(TimeSpan olderThan) removing %TEMP%\SI_* older than the age — InstallService/App call it at startup (T16/T17); attribute pass skips reparse points; Dst bare root → Dst error
- Ruling (T13 fix r2): grant current-user SID only when the process is NOT elevated (dev/test runs); elevated runs get Administrators + SYSTEM only — cost: none for production (app is requireAdministrator)
- Ruling (T13 fix r2): skip any SI_* / workspace root that is a reparse point in SweepStale and TryDelete; reject ':' anywhere after index 1 in Dst
- Ruling (T14): MSI timeout/cancel cancels via external UI handler returning IDCANCEL (rollback) and waits for the install thread — the plan said "let msiexec finish", but then InstallService would delete the workspace while MSI still reads the package — cost: a timed-out MSI is rolled back rather than left running
- Ruling (T14): MSI progress algorithm extracted into pure MsiProgressParser so it is unit-testable; MSI installs serialized with a static semaphore (external UI is process-global)
- Ruling (T14): zip entries all validated before extracting anything (no partial extraction on zip-slip)
- Ruling (T14 fix): filter = INSTALLLOGMODE_PROGRESS 0x400 | INSTALLLOGMODE_ACTIONSTART 0x100 (msi.h: 1 << (INSTALLMESSAGE_x >> 24)); plan text corrected by this ruling
- Ruling (T14 fix): parser resets its last-reported percent on every reset (type 0) message, so the bar may restart at 0 for the execution phase — cost: bar can visibly go back once
- Ruling (T15): export-logs requires admin; CSV gets Vietnamese headers + Excel formula-injection guard (' prefix) — plan silent on both
- Ruling: user chose A — run Tasks 17–19 continuously, then report.
- Ruling (T17): ScanReportViewModel takes a scan delegate instead of MachineScanner (sealed concrete deps; App.Tests can't see Core.Tests fakes) — supersedes plan signature
- Ruling (T17): login footer shows vault fingerprint "Mã kho: XXXX-XXXX-XXXX-XXXX" (SHA-256 of signing SPKI, first 8 bytes) — mitigation promised in T3 ruling
- Ruling (T17): startup calls TempWorkspace.SweepStale(1 day); exit calls InstallService.CleanupDocs (carried from T13/T16)
- Ruling (T18): PackageListViewModel takes delegates (findInstalled/install/extractDoc/confirm/openFile/navigate) instead of concrete Core services — supersedes plan ctor; only one install at a time; InstallViewModel has Cancel
- Ruling (T18 minor, load-bearing): window-closing guard during install (leftover plaintext installer if killed) — CARRIED into Task 19 (touches MainWindow); SweepStale on next start is the backstop
- Ruling (T19): AdminViewModel ctor (session, logsDir, fallbackDir, pickFolder, pickSavePath, confirm, onBack); admin entry via PackageListViewModel.OpenAdminCommand (admins only); back rebuilds the package list from the updated catalog; CatalogCommitPending → "Thay đổi đã ghi một phần; hãy mở lại ứng dụng để hoàn tất." (carried T5 minor); window close blocked during install (carried T18)
- Ruling: user chose A — Task 20, then user tries the app.
- Ruling (T20): build script adds -WithDemo (zip demo package "Gói thử nghiệm" + test user nguoidung) so the user can try the app; Packager gains `fingerprint <root>` (no login) for checklist item 17; never overwrites an existing vault
- Ruling (final): fix wave before merge = Critical 1 + PackageBuilder rejects MSI silent_args starting with / or - + README backslashes + Argon2 known-answer test + Flush(true) on vault.meta/catalog writes + SweepStale only SI_<32hex> and SI_docs + PackageCrypto buffers sized to input + delete pkg.enc/pkg.zip after use + remove dead MainViewModel.IsBusyInstalling — all small, no format change
- Ruling (final): Important 1–5 (workspace to %ProgramData% protected + bundle extract dir; junction-ancestor checks; key-retrieval seam + separate doc key; Vietnamese error mapping; vault version/date + read-only notice) become the FIRST tasks of Plan 1.1 — design changes better specced with the redesign; nothing has been distributed yet — cost if wrong: main carries known escalation gaps until Plan 1.1 task 1 lands
