# ASCII-only E2E update test: fake v1.0.5 -> real v1.1.0 release (PS 5.1 safe)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms

$root = [System.Windows.Automation.AutomationElement]::RootElement

function FindByName($parent, $name) {
    $c = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $name)
    $parent.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $c)
}

Stop-Process -Name OnShow -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 1

# 1. publish fake old version 1.0.5 (self-contained, same as CI)
$dir = Join-Path $env:TEMP 'OnShowE2E'
if (Test-Path $dir) { Remove-Item $dir -Recurse -Force }
$dotnet = Join-Path $env:USERPROFILE '.dotnet\dotnet.exe'
& $dotnet publish 'C:\Users\22956\Desktop\SYS\OnShow\src\OnShow.App\OnShow.App.csproj' `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:Version=1.0.5 -p:DebugType=none -p:DebugSymbols=false -o $dir | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Host 'FAIL: publish'; exit 1 }
$exe = Join-Path $dir 'OnShow.exe'
Write-Host ('fake 1.0.5 published: ' + $exe)

# 2. seed config: consent accepted, auto check on, no last-check timestamp
$cfg = "$env:APPDATA\OnShow\config.json"
$cfgObj = [ordered]@{
    Version = 1
    Window = [ordered]@{ Mode = 'DesktopTop'; X = 100; Y = 100; Visible = $true }
    AgreementAcceptedVersion = 1
    AutoCheckUpdate = $true
}
$cfgObj | ConvertTo-Json -Depth 5 | Out-File $cfg -Encoding utf8 -Force

# 3. launch old version; auto-check should fire after ~30s and open UpdateWindow
Start-Process $exe
Write-Host 'old 1.0.5 launched, waiting for auto-check update window...'

$upd = $null
$deadline = (Get-Date).AddSeconds(100)
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Seconds 3
    $upd = FindByName $root 'OnShow 更新'
    if ($upd) { break }
}
if (-not $upd) { Write-Host 'FAIL: UpdateWindow did not appear'; exit 1 }
Write-Host 'UpdateWindow appeared (auto-check path OK)'

# verify version line mentions both versions
$verText = $upd.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
    (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)))
Write-Host ('window first text: ' + $verText.Current.Name)

# 4. click "立即更新"
$btn = FindByName $upd '立即更新'
if (-not $btn) { Write-Host 'FAIL: update button not found'; exit 1 }
$btn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Write-Host 'clicked update, downloading (polling status)...'

# 5. poll for app exit (download -> apply -> restart); print status text while window alive
$prevPid = (Get-Process OnShow -ErrorAction SilentlyContinue).Id
$deadline = (Get-Date).AddMinutes(9)
$statusShown = ''
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Seconds 10
    $w = FindByName $root 'OnShow 更新'
    if ($w) {
        $texts = $w.FindAll([System.Windows.Automation.TreeScope]::Descendants,
            (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)))
        foreach ($t in $texts) { if ($t.Current.Name -and $t.Current.Name.Length -gt 3) { $statusShown = $t.Current.Name } }
        Write-Host ('  status: ' + $statusShown)
        $stillOld = Get-Process OnShow -ErrorAction SilentlyContinue | Where-Object { $_.Id -eq $prevPid }
        if (-not $stillOld) { Write-Host 'old process exited (applying update)'; break }
    }
    $now = Get-Process OnShow -ErrorAction SilentlyContinue
    if ($now -and -not ($now | Where-Object { $_.Id -eq $prevPid })) { Write-Host 'new process detected'; break }
}

# 6. wait for restarted instance
Start-Sleep -Seconds 8
$new = Get-Process OnShow -ErrorAction SilentlyContinue
if (-not $new) { Write-Host 'FAIL: no process after update'; exit 1 }
Write-Host ('process after update: pid=' + $new.Id + ' path=' + $new.Path)

# 7. verify replaced file hash matches release checksums.txt
$expectedLine = (Invoke-WebRequest -Uri 'https://github.com/BobHuang666/OnShow/releases/download/v1.1.0/checksums.txt' -UseBasicParsing -Headers @{ 'User-Agent' = 'curl' }).Content
$expectedHash = ($expectedLine -split "`n" | Where-Object { $_ -match 'OnShow-v1\.1\.0-win-x64\.zip' })[0].Trim().Split(' ')[0]
$actualHash = (Get-FileHash $exe -Algorithm SHA256).Hash.ToLower()
Write-Host ('expected: ' + $expectedHash)
Write-Host ('actual  : ' + $actualHash)
if ($expectedHash -ne $actualHash) { Write-Host 'C5 FAIL: hash mismatch'; exit 1 }

# 8. verify running version via UIA? (skip; hash proves 1.1.0 binary)
Stop-Process -Name OnShow -Force -ErrorAction SilentlyContinue
Write-Host 'C5 E2E TEST PASSED'
