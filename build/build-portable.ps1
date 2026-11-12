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
    [string]$AdminPassword,
    # Thư mục chứa bộ cài demo đã tải (tải về đây nếu thiếu).
    [string]$DemoCache
)

$ErrorActionPreference = 'Stop'

if ($WithDemo -and -not $InitVault) { throw 'Tùy chọn -WithDemo cần đi kèm -InitVault.' }

$repo = Split-Path -Parent $PSScriptRoot
if (-not $DemoCache) { $DemoCache = Join-Path $repo 'samples\.cache\installers' }
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

function Get-MsiProductCode([string]$msi) {
    # Chỉ đọc bảng Property của MSI, không chạy bộ cài.
    $wi = New-Object -ComObject WindowsInstaller.Installer
    $db = $wi.GetType().InvokeMember('OpenDatabase', 'InvokeMethod', $null, $wi, @($msi, 0))
    $view = $db.GetType().InvokeMember('OpenView', 'InvokeMethod', $null, $db, @("SELECT Value FROM Property WHERE Property='ProductCode'"))
    $view.GetType().InvokeMember('Execute', 'InvokeMethod', $null, $view, $null) | Out-Null
    $rec = $view.GetType().InvokeMember('Fetch', 'InvokeMethod', $null, $view, $null)
    $rec.GetType().InvokeMember('StringData', 'GetProperty', $null, $rec, @(1))
}

function Add-DemoInstaller($i, [string]$file) {
    $kind = [string]$i.kind
    $m = [ordered]@{
        id = $i.id; name = $i.name; version = $i.version; installer_file = $i.file
        installer_kind = $kind.ToLowerInvariant(); detect = @{ display_name_regex = $i.display_regex }
    }
    if ($kind -eq 'Msi') { $m.installer_type = 'msi'; $m.detect.product_code = Get-MsiProductCode $file }
    else {
        $m.installer_type = 'exe'
        $m.silent_args = switch ($kind) { 'Inno' { '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART' } 'Nsis' { '/S' } 'Burn' { '/quiet /norestart' } }
    }
    $t = Join-Path ([System.IO.Path]::GetTempPath()) ('SI_demo_' + [guid]::NewGuid().ToString('N'))
    try {
        New-Item -ItemType Directory -Path (Join-Path $t 'installer') | Out-Null
        Copy-Item -LiteralPath $file -Destination (Join-Path $t 'installer')
        [System.IO.File]::WriteAllText((Join-Path $t 'manifest.json'), ($m | ConvertTo-Json -Depth 5), (New-Object System.Text.UTF8Encoding $false))
        & $packager add-package $appDir $t --user $Admin --password $plain
        if ($LASTEXITCODE -ne 0) { throw "Packager add-package thất bại cho $($i.id) (mã $LASTEXITCODE)" }
    }
    finally { Remove-Item -LiteralPath $t -Recurse -Force -ErrorAction SilentlyContinue }
}

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

            & $packager add-package $appDir (Join-Path $repo 'samples\demo-script') --user $Admin --password $plain
            if ($LASTEXITCODE -ne 0) { throw "Packager add-package thất bại cho demo-script (mã $LASTEXITCODE)" }

            # Bộ cài thật từ nhà phát hành; không có mạng thì chỉ giữ các gói offline ở trên.
            $online = $true
            try { & (Join-Path $PSScriptRoot 'fetch-demo-installers.ps1') -Out $DemoCache }
            catch { $online = $false; Write-Warning "Không tải được bộ cài demo ($($_.Exception.Message)). Chỉ thêm các phần mềm offline." }
            if ($online) {
                $list = Get-Content (Join-Path $repo 'samples\demo-installers.json') -Raw -Encoding UTF8 | ConvertFrom-Json
                foreach ($i in $list) { Add-DemoInstaller $i (Join-Path $DemoCache $i.file) }
                & $packager add-bundle $appDir (Join-Path $repo 'samples\demo-bundle.json') --user $Admin --password $plain
                if ($LASTEXITCODE -ne 0) { throw "Packager add-bundle thất bại (mã $LASTEXITCODE)" }
            }

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
if (-not $WithDemo -and $sizeMb -gt 120) { throw ('Bản build vượt 120 MB ({0} MB)' -f $sizeMb) }

Write-Host ''
Write-Host "Hoàn tất. Thư mục portable: $appDir"
if ($fingerprint) { Write-Host "Mã kho (hiện trong trang Quản trị): $fingerprint" }
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
