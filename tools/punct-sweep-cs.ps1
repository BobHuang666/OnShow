# Safe sweep: convert half-width , ; ! ? ( ) : to full-width ONLY inside single-line C# string
# literals that contain CJK, have NO braces (interpolation), and no URLs.
$ErrorActionPreference = 'Stop'
$root = 'C:\Users\22956\Desktop\SYS\OnShow\src'
$files = Get-ChildItem -Path $root -Recurse -Include *.cs
$total = 0

foreach ($f in $files) {
    $text = [IO.File]::ReadAllText($f.FullName)
    $orig = $text

    $text = [regex]::Replace($text, '"([^"\r\n\\][^"\r\n]*)"', {
        param($m)
        $v = $m.Groups[1].Value
        if ($v -notmatch '[\u4e00-\u9fff]') { return $m.Value }   # only CJK strings
        if ($v -match '\{') { return $m.Value }                   # skip interpolated strings
        if ($v -match '://') { return $m.Value }                  # skip URLs
        $before = $v
        $v = $v.Replace(',', [char]0xFF0C).Replace(';', [char]0xFF1B).Replace('!', [char]0xFF01).Replace('?', [char]0xFF1F).Replace('(', [char]0xFF08).Replace(')', [char]0xFF09).Replace(':', [char]0xFF1A)
        if ($v -ne $before) { $script:total++ }
        return '"' + $v + '"'
    })

    if ($text -ne $orig) {
        [IO.File]::WriteAllText($f.FullName, $text, (New-Object System.Text.UTF8Encoding($false)))
        Write-Host ('updated: ' + $f.Name)
    }
}
Write-Host ("safe replacements: " + $total)
