# ASCII-only UIA test: feedback window image pipeline (PS 5.1 safe)
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

# 0. fresh config with consent accepted (avoid EULA)
$cfg = "$env:APPDATA\OnShow\config.json"
$cfgObj = [ordered]@{
    Version = 1
    Window = [ordered]@{ Mode = 'DesktopTop'; X = 100; Y = 100; Visible = $true }
    AgreementAcceptedVersion = 1
    AutoCheckUpdate = $false
}
$cfgObj | ConvertTo-Json -Depth 5 | Out-File $cfg -Encoding utf8 -Force

# 1. test png
$png = Join-Path $env:TEMP 'uatest-shot.png'
$bmp = New-Object System.Drawing.Bitmap(200, 120)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.Clear([System.Drawing.Color]::SteelBlue)
$g.Dispose(); $bmp.Save($png, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()

# 2. launch app, open settings
Start-Process 'C:\Users\22956\Desktop\SYS\OnShow\dist\OnShow.exe' -ArgumentList '--settings'
Start-Sleep -Seconds 6
$settings = FindByName $root 'OnShow 设置'
if (-not $settings) { Write-Host 'FAIL: no settings'; exit 1 }

# 3. nav to about page, open feedback
$nav = FindByName $settings ([char]0x2139 + '  关于')
if (-not $nav) { Write-Host 'FAIL: no nav item'; exit 1 }
$nav.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
Start-Sleep -Seconds 2
(FindByName $settings '反馈问题').GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Start-Sleep -Seconds 3
$fb = FindByName $root '反馈 OnShow'
if (-not $fb) { Write-Host 'FAIL: no feedback window'; exit 1 }
Write-Host 'feedback window open'

# 4. add screenshot via file dialog
(FindByName $fb ([char]0xFF0B + ' 添加截图')).GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Start-Sleep -Seconds 3
$dlg = FindByName $root '选择截图'
Start-Sleep -Milliseconds 800
$clsCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ClassNameProperty, 'ComboBoxEx32')
$combo = $dlg.FindFirst([System.Windows.Automation.TreeScope]::Children, $clsCond)
if (-not $combo) { Write-Host 'FAIL: ComboBoxEx32 not found'; exit 1 }
$editCls = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ClassNameProperty, 'Edit')
$edit = $combo.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $editCls)
if (-not $edit) { Write-Host 'FAIL: filename edit not found'; exit 1 }
$edit.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($png)
Start-Sleep -Milliseconds 500
$openBtn = FindByName $dlg '打开(O)'
if (-not $openBtn) { Write-Host 'FAIL: open button not found'; exit 1 }
$openBtn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Start-Sleep -Seconds 2
if (FindByName $root '选择截图') { Write-Host 'FAIL: file dialog still open'; exit 1 }
Write-Host 'screenshot added'

# 5. type text and submit
$editBox = $fb.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
    (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Edit)))
$editBox.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('image pipeline test, ignore')

$oldClipText = [System.Windows.Forms.Clipboard]::GetText()
(FindByName $fb '提交到 GitHub').GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Start-Sleep -Seconds 5

# 6. verify clipboard: text + bitmap + file drop list
$dataObj = [System.Windows.Forms.Clipboard]::GetDataObject()
$hasText = $dataObj.GetDataPresent([System.Windows.Forms.DataFormats]::UnicodeText) -and $dataObj.GetText().Contains('image pipeline test')
$hasImage = $dataObj.GetDataPresent([System.Windows.Forms.DataFormats]::Bitmap)
$hasFiles = $dataObj.GetDataPresent([System.Windows.Forms.DataFormats]::FileDrop)
$files = @()
if ($hasFiles) { $files = $dataObj.GetData([System.Windows.Forms.DataFormats]::FileDrop) }
Write-Host ('clip text: ' + $hasText + ' | clip bitmap: ' + $hasImage + ' | clip files: ' + $hasFiles + ' -> ' + ($files -join ','))
if (-not ($hasText -and $hasImage -and $hasFiles)) { Write-Host 'C6-IMG FAIL'; exit 1 }

# restore user clipboard text
if ($oldClipText) { [System.Windows.Forms.Clipboard]::SetText($oldClipText) }

# 7. dismiss the info dialog and close app
Start-Sleep -Seconds 1
$ok = FindByName $root '反馈'
if ($ok) {
    $k = FindByName $ok '知道了'
    if ($k) { $k.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() }
}
Start-Sleep -Seconds 1
Stop-Process -Name OnShow -Force -ErrorAction SilentlyContinue
Write-Host 'C6-IMG TEST DONE'
