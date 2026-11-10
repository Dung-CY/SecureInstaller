# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project status

SecureInstaller — a Windows desktop app that scans a machine and silently installs approved software from an encrypted repository. Plan 1 is in progress on branch `feat/portable`; progress ledger lives in `.superpowers/sdd/2026-11-10-secureinstaller-portable/progress.md` (git-ignored).

- Spec (source of truth, Vietnamese): `docs/spec.md`
- Plan 1 — Core + Portable mode, 20 tasks: `docs/superpowers/plans/2026-11-10-secureinstaller-portable.md`
- Plan 2 — LAN server mode (spec requirements 10–13): not written yet; it must only add a `ServerRepositorySource` implementing `IRepositorySource`, not change Core.

The user communicates in Vietnamese; reply in Vietnamese. All UI strings are Vietnamese and fixed verbatim in the plan's **Global Constraints** (`Strings.*`, verdict/version labels) — copy them exactly.

## Environment

- Windows 11 LTSC (no winget). .NET SDK 8.0.428 is installed per-user at `%LOCALAPPDATA%\Microsoft\dotnet` and added to the user PATH; a shell started before the install may not see `dotnet` — call it by full path if needed.
- The repo lives at `D:\Claude Code\SecureInstaller` (parent folder has a space). Quote paths everywhere; several tests deliberately use Vietnamese/space paths such as `Dự án 1` because end users' paths may contain them.

## Commands (as defined by Plan 1)

```
dotnet build
dotnet test                                              # all tests
dotnet test --filter PackageCryptoTests                  # one test class
dotnet test --filter "FullyQualifiedName~Fifth_failure"  # one test
dotnet test tests/Installer.App.Tests                    # ViewModel tests only
dotnet run --project src/Installer.App                   # triggers UAC (requireAdministrator)
./build/build-portable.ps1 -Out D:\temp\usb -InitVault -Admin admin
```

Packager CLI (admin tooling): `SecureInstaller.Packager <init|add-package|update-package|remove-package|add-user|remove-user|export-logs> <root> ... --user <admin>`.

## Architecture

- `src/Installer.Core` holds all logic; `Installer.App` (WPF, MVVM via CommunityToolkit.Mvvm) and `Installer.Packager` (CLI) are thin shells. All projects target `net8.0-windows`.
- **Every Windows data source sits behind an interface** (`IWmi`, `IRegistryReader`, `ISystemInfo`, `IConnectivityProbe`, `IInstallerRunner`) so scanners and installers are unit-tested with fakes in `tests/Installer.Core.Tests/Fakes`. Real MSI installs are verified only via `docs/e2e-checklist.md`.
- **Portable vault** (a folder next to the exe, runnable from USB, disc, or a copied folder — deliberately *not* bound to hardware):
  - One random Vault Key (VK) encrypts the catalog, logs, and nothing else directly; each package and its docs use their own key stored inside the (VK-encrypted) catalog, so Plan 2's server can hand out a single package key.
  - VK is wrapped per user with Argon2id(password). Users and lockout counters live in plaintext `vault.meta` because they must be readable before login.
  - Catalog is signed with ECDSA P-256; the private key is wrapped only for admins. **Admin status = ability to unwrap the signing key**, never the `Role` field in `vault.meta`.
- Package file format (`.pkg.enc`): chunked AES-256-GCM with a last-chunk flag in AAD so truncated copies are detected — decryption must stream, never buffer whole installers.
- Install flow (`InstallService`): verify SHA-256 → decrypt into `TempWorkspace` (ACL-restricted `%TEMP%\SI_<guid>`) → unzip → run installer → post-actions (only on Success/RebootRequired) → log → always delete the workspace in `finally`.
- Logging never throws; if the vault is read-only or unplugged it falls back to `%ProgramData%\SecureInstaller\logs` and syncs back on the next writable login.
- Crack detection is heuristic only: verdicts `Genuine / Suspicious / CrackSuspected / Unknown / NotInstalled`, always with evidence; read failures must yield `Unknown`, never `CrackSuspected`. Lab testing uses only `slmgr /skms 127.0.0.2`, never real crack tools.

## Workflow

The plan is executed task-by-task with TDD (failing test → implement → pass → commit). The user wants a summary plus next-step options after **each** task. Commits end with the `Co-Authored-By` line from the session's attribution reminder.
