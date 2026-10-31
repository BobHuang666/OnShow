# Repair: inside C# interpolated-string braces {..}, full-width chars broke code - revert them.
$ErrorActionPreference = 'Stop'
$root = 'C:\Users\22956\Desktop\SYS\OnShow\src'
$files = Get-ChildItem -Path $root -Recurse -Include *.cs
$fixed = 0

foreach ($f in $files) {
    $text = [IO.File]::ReadAllText($f.FullName)
    $orig = $text

    $text = [regex]::Replace($text, '\{[^{}]*\}', {
        param($m)
        $v = $m.Value
        if ($v -match '[\uFF0C\uFF1B\uFF1A\uFF1F\uFF01\uFF08\uFF09]') {
            $script:fixed++
            $v = $v.Replace([char]0xFF0C, ',').Replace([char]0xFF1B, ';').Replace([char]0xFF1A, ':').Replace([char]0xFF1F, '?').Replace([char]0xFF01, '!').Replace([char]0xFF08, '(').Replace([char]0xFF09, ')')
        }
        return $v
    })

    if ($text -ne $orig) {
        [IO.File]::WriteAllText($f.FullName, $text, (New-Object System.Text.UTF8Encoding($false)))
        Write-Host ('repaired: ' + $f.Name)
    }
}
Write-Host ("brace repairs: " + $fixed)
