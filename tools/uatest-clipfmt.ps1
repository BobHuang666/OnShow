# ASCII-only: feedback image flow via UIA + clipboard format introspection (PS 5.1, UTF-8 BOM needed)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$root = [System.Windows.Automation.AutomationElement]::RootElement
Stop-Process -Name OnShow -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 1

function FindByName($parent, $name) {
    $c = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $name)
    $parent.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $c)
}

# 0. fresh config with consent
$cfgObj = [ordered]@{
    Version = 1
    Window = [ordered]@{ Mode = 'DesktopTop'; X = 120; Y = 120; Visible = $true }
    AgreementAcceptedVersion = 1
    AutoCheckUpdate = $false
}
$cfgObj | ConvertTo-Json -Depth 5 | Out-File "$env:APPDATA\OnShow\config.json" -Encoding utf8 -Force

# 1. test png
$png = Join-Path $env:TEMP 'uatest-shot.png'
$bmp = New-Object System.Drawing.Bitmap(300, 180)
$g = [System.Drawing.Graphics]::FromImage($bmp); $g.Clear([System.Drawing.Color]::SteelBlue)
$g.FillRectangle([System.Drawing.Brushes]::Orange, 20, 20, 120, 60); $g.Dispose()
$bmp.Save($png, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()

# 2. launch app, open settings -> about -> feedback
Start-Process 'C:\Users\22956\Desktop\SYS\OnShow\dist\OnShow.exe' -ArgumentList '--settings'
Start-Sleep -Seconds 6
$settings = FindByName $root 'OnShow 设置'
if (-not $settings) { Write-Host 'FAIL: no settings'; exit 1 }
$nav = FindByName $settings ([char]0x2139 + '  关于')
if (-not $nav) { Write-Host 'FAIL: no nav item'; exit 1 }
$nav.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
Start-Sleep -Seconds 2
(FindByName $settings '反馈问题').GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Start-Sleep -Seconds 3
$fb = FindByName $root '反馈 OnShow'
if (-not $fb) { Write-Host 'FAIL: no feedback window'; exit 1 }
Write-Host 'feedback window open'

# 3. add screenshot: click add button, type path into filename box, click open
(FindByName $fb ([char]0xFF0B + ' 添加截图')).GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Start-Sleep -Seconds 3
$dlg = FindByName $root '选择截图'
if (-not $dlg) { Write-Host 'FAIL: no file dialog'; exit 1 }
$clsCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ClassNameProperty, 'ComboBoxEx32')
$combo = $dlg.FindFirst([System.Windows.Automation.TreeScope]::Children, $clsCond)
$editCls = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ClassNameProperty, 'Edit')
$edit = $combo.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $editCls)
try { $edit.SetFocus() } catch { Write-Host 'SetFocus failed, continuing (edit has default focus)' }
Start-Sleep -Milliseconds 300
[System.Windows.Forms.SendKeys]::SendWait('^a')
Start-Sleep -Milliseconds 150
[System.Windows.Forms.SendKeys]::SendWait($png)
Start-Sleep -Milliseconds 400
[System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
Start-Sleep -Seconds 2
if (FindByName $root '选择截图') {
    [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
    Start-Sleep -Seconds 2
    if (FindByName $root '选择截图') { Write-Host 'FAIL: file dialog still open'; exit 1 }
}
Write-Host 'screenshot added'

# 4. type text and submit
$editBox = $fb.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
    (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Edit)))
$editBox.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('clipboard format test, ignore')

$oldClipText = [System.Windows.Forms.Clipboard]::GetText()
(FindByName $fb '提交到 GitHub').GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Start-Sleep -Seconds 5

# 5. verify clipboard formats
$dataObj = [System.Windows.Forms.Clipboard]::GetDataObject()
$hasText = $dataObj.GetDataPresent([System.Windows.Forms.DataFormats]::UnicodeText) -and $dataObj.GetText().Contains('clipboard format test')
$hasPng = $dataObj.GetDataPresent('PNG')
$hasBitmap = $dataObj.GetDataPresent([System.Windows.Forms.DataFormats]::Bitmap)
$hasFiles = $dataObj.GetDataPresent([System.Windows.Forms.DataFormats]::FileDrop)
Write-Host ('clip text: ' + $hasText + ' | PNG stream: ' + $hasPng + ' | DIB bitmap: ' + $hasBitmap + ' | FileDrop(should be False): ' + $hasFiles)

if ($hasPng) {
    $pngData = $dataObj.GetData('PNG')
    $ms = New-Object System.IO.MemoryStream
    if ($pngData -is [System.IO.Stream]) { $pngData.CopyTo($ms); $bytes = $ms.ToArray() } else { $bytes = $pngData }
    $sig = [System.Text.Encoding]::ASCII.GetString($bytes[0..3])
    Write-Host ('PNG signature: ' + $sig + ' (expect ~PNG)')
}
if (-not ($hasText -and $hasPng -and $hasBitmap -and -not $hasFiles)) { Write-Host 'CLIP-FMT FAIL'; exit 1 }

# restore user clipboard text
if ($oldClipText) { [System.Windows.Forms.Clipboard]::SetText($oldClipText) }

# 6. dismiss info dialog, close app
Start-Sleep -Seconds 1
$ok = FindByName $root '反馈'
if ($ok) {
    $k = FindByName $ok '知道了'
    if ($k) { $k.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() }
}
Start-Sleep -Seconds 1
Stop-Process -Name OnShow -Force -ErrorAction SilentlyContinue
Write-Host 'CLIP-FMT TEST PASSED'
