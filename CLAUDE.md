# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project status

SecureInstaller — a Windows desktop app that scans a machine and silently installs approved software from an encrypted repository. Plan 1 (core + portable) is merged to `main` (2026-11-11). Plan 1.1 (UX, uninstall, bundles, automatic installer detection; Tasks 1–27 plus user feedback rounds) is merged to `main` (2026-11-13). Next: Plan 2 (LAN server). Plan 1 decision log: `docs/superpowers/backlog-plan-1.1.md`.

- Spec (source of truth, Vietnamese): `docs/spec.md`, plus Plan 1.1 additions in `docs/spec-1.1.md`
- Plan 1 — Core + Portable mode, 20 tasks: `docs/superpowers/plans/2026-11-10-secureinstaller-portable.md`
- Plan 1.1: `docs/superpowers/plans/2026-11-11-secureinstaller-plan-1.1.md` (addenda 2 and 3 hold Tasks 20–27)
- Plan 2 — LAN server mode (spec requirements 10–13): not written yet; it must only add a `ServerRepositorySource` implementing `IRepositorySource`, not change Core.

The user communicates in Vietnamese; reply in Vietnamese. All UI strings are Vietnamese and fixed verbatim in the plan's **Global Constraints** (`Strings.*`, verdict/version labels) — copy them exactly.

## Environment

- Windows 11 LTSC (no winget). .NET SDK 8.0.428 is installed per-user at `%LOCALAPPDATA%\Microsoft\dotnet` and added to the user PATH. A runtime-only `dotnet` (from installing .NET Desktop Runtime) may come first on PATH and has no SDK — call `%LOCALAPPDATA%\Microsoft\dotnet\dotnet.exe` by full path. The build script picks a dotnet that has an SDK by itself.
- The repo lives at `D:\Claude Code\SecureInstaller` (parent folder has a space). Quote paths everywhere; several tests deliberately use Vietnamese/space paths such as `Dự án 1` because end users' paths may contain them.

## Commands

```
dotnet build
dotnet test                                              # all tests
dotnet test --filter PackageCryptoTests                  # one test class
dotnet test --filter "FullyQualifiedName~Fifth_failure"  # one test
dotnet test tests/Installer.App.Tests                    # ViewModel tests only
dotnet run --project src/Installer.App                   # triggers UAC (requireAdministrator)
powershell -ExecutionPolicy Bypass -File build/build-portable.ps1 -Out D:\temp\usb -InitVault -Admin admin [-WithDemo]
```

`-WithDemo` downloads the real installers listed in `samples/demo-installers.json` (pinned SHA-256, cached in the gitignored `samples/.cache/installers`) and adds 9 packages plus a bundle. Never execute downloaded installers in tests. Without `-InitVault` the script only republishes the exe and leaves an existing vault untouched. The 120 MB size budget covers the program only, not the vault.

Packager CLI (admin tooling): `SecureInstaller.Packager <init|add-package|update-package|remove-package|add-bundle|remove-bundle|add-user|remove-user|list|fingerprint|export-logs> <root> ... --user <admin>`.

## Architecture

- `src/Installer.Core` holds all logic; `Installer.App` (WPF, MVVM via CommunityToolkit.Mvvm) and `Installer.Packager` (CLI) are thin shells. All projects target `net8.0-windows`.
- **Every Windows data source sits behind an interface** (`IWmi`, `IRegistryReader`, `ISystemInfo`, `IConnectivityProbe`, `IInstallerRunner`) so scanners and installers are unit-tested with fakes in `tests/Installer.Core.Tests/Fakes`. Real MSI installs are verified only via `docs/e2e-checklist.md`.
- **Portable vault** (a folder next to the exe, runnable from USB, disc, or a copied folder — deliberately *not* bound to hardware):
  - One random Vault Key (VK) encrypts the catalog, logs, and nothing else directly; each package and its docs use their own key stored inside the (VK-encrypted) catalog, so Plan 2's server can hand out a single package key.
  - Key seam: `IRepositorySource.GetPackageKeyAsync` and `GetDocsKeyAsync` hand out one package's key, never VK.
  - VK is wrapped per user with Argon2id(password). Users and lockout counters live in plaintext `vault.meta` because they must be readable before login.
  - Catalog is signed with ECDSA P-256; the private key is wrapped only for admins. **Admin status = ability to unwrap the signing key**, never the `Role` field in `vault.meta`.
- Package file format (`.pkg.enc`): chunked AES-256-GCM with a last-chunk flag in AAD so truncated copies are detected — decryption must stream, never buffer whole installers.
- Install flow (`InstallService`): verify SHA-256 → decrypt into `TempWorkspace` (ACL-restricted `%ProgramData%\SecureInstaller\work\<guid>`, with no reparse-point ancestors) → unzip (cancellable per entry, zip-slip checked) → run installer → post-actions (only on Success/RebootRequired) → write the install marker → log → always delete the workspace in `finally`. The App runs several packages through a sequential queue with Cancel.
- Installer kind (`InstallerKindDetector`) is read from file bytes only, never by executing the file, and it picks the silent args. Kinds: MSI, Inno, NSIS, Burn, InstallShield, zip, script.
- Install marker: `HKLM\SOFTWARE\SecureInstaller\Installed\<id>` stores the Uninstall registry key, found by diffing the HKLM Uninstall keys before and after the install. Hidden `SystemComponent=1` entries are skipped. Detection and uninstall read HKLM only, never HKCU.
- Uninstall (`UninstallService`) tries these routes in order:
  1. explicit legacy spec;
  2. zip;
  3. msiexec by product code;
  4. `QuietUninstallString`;
  5. a command derived from `UninstallString` by installer kind.

  Script packages run their `uninstall_script` from a workspace. An uninstall counts as successful only once the targeted Uninstall key has disappeared.
- After login, `SessionSetupViewModel` asks for the unit (`ComputerName.NormalizeUnit`, e.g. `LamThao`, session only) and offers a computer rename (`SetComputerNameEx`; over 15 chars is warned, not refused). The machine's last scan is cached per vault in `%ProgramData%\SecureInstaller\last-scan-<vaultId>.enc` (VK-encrypted) and reused while the BIOS serial and host name match; "Quét lại" rescans.
- `DuLieu\<package id>` next to the exe (plain, not in the vault): when a package has `Data` (an editor checkbox), only the listed files are copied into `Data.TargetDir` after a successful install. A line is `tên trong DuLieu => tên khi chép` or a bare name; `{DonVi}` is the session unit (e.g. `{DonVi}.txt => config.txt`).
- MSI installs write a verbose log to `%ProgramData%\SecureInstaller\logs\msi` (kept only on failure); error 1603 retries once via `System32\msiexec.exe /i /qn /norestart`.
- Logging never throws; if the vault is read-only or unplugged it falls back to `%ProgramData%\SecureInstaller\logs` and syncs back on the next writable login.
- Crack detection is heuristic only: verdicts `Genuine / Suspicious / CrackSuspected / Unknown / NotInstalled`, always with evidence; read failures must yield `Unknown`, never `CrackSuspected`. Lab testing uses only `slmgr /skms 127.0.0.2`, never real crack tools.

## Workflow

The plan is executed task-by-task with TDD (failing test → implement → pass → commit). The user wants a summary plus next-step options after **each** task. Commits end with the `Co-Authored-By` line from the session's attribution reminder.
