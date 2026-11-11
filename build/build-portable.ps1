<#
.SYNOPSIS
  Đóng gói SecureInstaller thành thư mục portable (chép được vào USB).
.EXAMPLE
  ./build/build-portable.ps1 -Out D:\temp\usb -InitVault -Admin admin -WithDemo
#>
param(
    [Parameter(Mandatory = $true)][string]$Out,
    [switch]$InitVault,
    [string]$Admin = 'admin',
    [switch]$WithDemo,
    [string]$Configuration = 'Release',
    # Chỉ dùng cho CI/thử nghiệm: mật khẩu truyền qua tham số sẽ hiện trong danh sách tiến trình.
    [string]$AdminPassword
)

$ErrorActionPreference = 'Stop'

if ($WithDemo -and -not $InitVault) { throw 'Tùy chọn -WithDemo cần đi kèm -InitVault.' }

$repo = Split-Path -Parent $PSScriptRoot
$dotnetCmd = Get-Command dotnet -ErrorAction SilentlyContinue
if ($dotnetCmd) { $dotnet = $dotnetCmd.Source }
else { $dotnet = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe' }
if (-not (Test-Path -LiteralPath $dotnet)) { throw 'Không tìm thấy dotnet. Hãy cài .NET SDK 8.' }

$outFull = [System.IO.Path]::GetFullPath($Out)
$appDir = Join-Path $outFull 'SecureInstaller'
$toolsDir = Join-Path $appDir 'tools'
$vaultDir = Join-Path $appDir 'vault'

if ($InitVault -and (Test-Path -LiteralPath $vaultDir)) {
    throw "Kho đã tồn tại tại $vaultDir. Script không bao giờ ghi đè kho; hãy xóa thủ công hoặc bỏ -InitVault."
}

# Lấy mật khẩu trước khi build để người dùng không phải chờ rồi mới bị hỏi.
$plain = $null
if ($InitVault) {
    if ($AdminPassword) { $plain = $AdminPassword }
    else {
        $s1 = Read-Host -AsSecureString "Mật khẩu cho quản trị viên '$Admin'"
        $s2 = Read-Host -AsSecureString 'Nhập lại mật khẩu'
        $b1 = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($s1)
        $b2 = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($s2)
        try {
            $p1 = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($b1)
            $p2 = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($b2)
            if ($p1 -ne $p2) { throw 'Hai mật khẩu không khớp.' }
            if ([string]::IsNullOrEmpty($p1)) { throw 'Mật khẩu không được để trống.' }
            $plain = $p1
        }
        finally {
            [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($b1)
            [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($b2)
            $p1 = $null; $p2 = $null
        }
    }
}

function Publish-Project([string]$proj, [string]$dest) {
    & $dotnet publish (Join-Path $repo $proj) -c $Configuration -r win-x64 --self-contained `
        -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o $dest
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish thất bại cho $proj (mã $LASTEXITCODE)" }
}

Publish-Project 'src\Installer.App' $appDir
Publish-Project 'src\Installer.Packager' $toolsDir

$packager = Join-Path $toolsDir 'SecureInstaller.Packager.exe'
if (-not (Test-Path -LiteralPath $packager)) { throw "Không thấy $packager sau khi publish." }

$fingerprint = $null
try {
    if ($InitVault) {
        & $packager init $appDir --user $Admin --password $plain
        if ($LASTEXITCODE -ne 0) { throw "Packager init thất bại (mã $LASTEXITCODE)" }

        if ($WithDemo) {
            $tmp = Join-Path ([System.IO.Path]::GetTempPath()) ('SI_demo_' + [guid]::NewGuid().ToString('N'))
            try {
                Copy-Item -LiteralPath (Join-Path $repo 'samples\demo-zip') -Destination $tmp -Recurse
                $instDir = Join-Path $tmp 'installer'
                New-Item -ItemType Directory -Path $instDir | Out-Null
                Add-Type -AssemblyName System.IO.Compression
                Add-Type -AssemblyName System.IO.Compression.FileSystem
                $zip = [System.IO.Compression.ZipFile]::Open((Join-Path $instDir 'app.zip'), 'Create')
                try {
                    $e = $zip.CreateEntry('readme.txt')
                    $w = New-Object System.IO.StreamWriter($e.Open())
                    $w.Write('SecureInstaller demo')
                    $w.Dispose()
                }
                finally { $zip.Dispose() }

                & $packager add-package $appDir $tmp --user $Admin --password $plain
                if ($LASTEXITCODE -ne 0) { throw "Packager add-package thất bại (mã $LASTEXITCODE)" }
            }
            finally { Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue }

            & $packager add-user $appDir nguoidung --role user --new-password $plain --user $Admin --password $plain
            if ($LASTEXITCODE -ne 0) { throw "Packager add-user thất bại (mã $LASTEXITCODE)" }
        }

        $fingerprint = (& $packager fingerprint $appDir | Out-String).Trim()
        if ($LASTEXITCODE -ne 0) { throw "Packager fingerprint thất bại (mã $LASTEXITCODE)" }
    }
}
finally { $plain = $null }

$sizeMb = [math]::Round(((Get-ChildItem -LiteralPath $appDir -Recurse -File | Measure-Object Length -Sum).Sum) / 1MB)
Write-Host ('Dung lượng: {0:N0} MB' -f $sizeMb)
if ($sizeMb -gt 120) { throw ('Bản build vượt 120 MB ({0} MB)' -f $sizeMb) }

Write-Host ''
Write-Host "Hoàn tất. Thư mục portable: $appDir"
if ($fingerprint) { Write-Host "Mã kho (so với màn hình đăng nhập): $fingerprint" }
if ($WithDemo) {
    Write-Host "Lưu ý: tài khoản 'nguoidung' dùng cùng mật khẩu với '$Admin', CHỈ để thử nghiệm. Hãy xóa trước khi phát hành:"
    Write-Host "  tools\SecureInstaller.Packager.exe remove-user . nguoidung --user $Admin"
}
Write-Host 'Bước tiếp theo:'
Write-Host '  1. Chép nguyên thư mục SecureInstaller vào USB (hoặc nơi khác) và chạy SecureInstaller.exe (cần quyền quản trị).'
if (-not $InitVault) {
    Write-Host '  2. Chưa có kho: chạy lại với -InitVault, hoặc tools\SecureInstaller.Packager.exe init . --user <admin>'
}
else {
    Write-Host '  2. Thêm gói: tools\SecureInstaller.Packager.exe add-package . <thư mục gói> --user <admin>'
}
Write-Host '  3. Làm theo docs\e2e-checklist.md để kiểm thử trên máy ảo.'
