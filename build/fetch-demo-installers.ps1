# Downloads the official demo installers listed in samples\demo-installers.json and verifies their SHA-256.
# The files are only ever read as bytes by tests; nothing here executes them.
param([Parameter(Mandatory)][string]$Out, [string]$ListFile = (Join-Path $PSScriptRoot '..\samples\demo-installers.json'))
$ErrorActionPreference = 'Stop'
$list = Get-Content $ListFile -Raw -Encoding UTF8 | ConvertFrom-Json
New-Item -ItemType Directory -Force $Out | Out-Null
$total = 0
foreach ($i in $list) {
    $dest = Join-Path $Out $i.file
    $ok = (Test-Path $dest) -and ((Get-FileHash $dest -Algorithm SHA256).Hash -ieq $i.sha256)
    if (-not $ok) {
        if (Test-Path $dest) { Remove-Item $dest -Force }
        Write-Host "Tai $($i.name) $($i.version)..."
        Invoke-WebRequest -Uri $i.url -OutFile $dest -UseBasicParsing
        $h = (Get-FileHash $dest -Algorithm SHA256).Hash
        if ($h -ine $i.sha256) { Remove-Item $dest -Force; throw "SHA-256 khong khop cho $($i.file): $h" }
    }
    $total += (Get-Item $dest).Length
    Write-Host ("{0,-40} {1,10:N0} bytes  OK" -f $i.file, (Get-Item $dest).Length)
}
Write-Host ("Tong: {0:N1} MB" -f ($total / 1MB))
