# ASCII-only UI test for --apply-update self replacement (PS 5.1 safe), v2
$ErrorActionPreference = 'Stop'
$base = Join-Path $env:TEMP 'OnShowUpdateTest'
if (Test-Path $base) { Remove-Item $base -Recurse -Force }
New-Item -ItemType Directory -Path (Join-Path $base 'old') | Out-Null
New-Item -ItemType Directory -Path (Join-Path $base 'new') | Out-Null

Copy-Item 'C:\Users\22956\Desktop\SYS\OnShow\dist\OnShow.exe' (Join-Path $base 'old\OnShow.exe')
Copy-Item 'C:\Users\22956\Desktop\SYS\OnShow\dist\OnShow.exe' (Join-Path $base 'new\OnShow.exe')
$target = Join-Path $base 'old\OnShow.exe'
$t0 = (Get-Item $target).LastWriteTimeUtc

Start-Process $target
Start-Sleep -Seconds 5
$old = Get-Process OnShow -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $target }
if (-not $old) { Write-Host 'FAIL: old instance not running'; exit 1 }
Write-Host ('old instance running pid=' + $old.Id)

# start new exe as installer while old still holds the file (should enter retry loop)
$newExe = Join-Path $base 'new\OnShow.exe'
Start-Process $newExe -ArgumentList ('--apply-update "' + $target + '"')
Start-Sleep -Seconds 4
$installer = Get-Process OnShow -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $newExe }
Write-Host ('installer alive while old running: ' + [bool]$installer)

# kill ONLY the old instance (simulate graceful exit); installer must survive
$old | Stop-Process -Force
Start-Sleep -Seconds 8

$t1 = (Get-Item $target).LastWriteTimeUtc
Write-Host ('target rewritten: ' + ($t1 -gt $t0))
$p = Get-Process OnShow -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $target }
if ($p) { Write-Host ('restarted process pid=' + $p.Id + ' path=' + $p.Path) }
else { Write-Host 'FAIL: no restarted process from target dir'; exit 1 }

Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
Write-Host 'C3 TEST DONE'
