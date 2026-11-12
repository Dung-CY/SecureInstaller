$dir = Join-Path $env:ProgramData 'SecureInstallerDemo'
New-Item -ItemType Directory -Force $dir | Out-Null
Set-Content -LiteralPath (Join-Path $dir 'script-ok.txt') -Value 'ok' -Encoding ASCII
