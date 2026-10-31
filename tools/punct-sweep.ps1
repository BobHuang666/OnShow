# Convert half-width punctuation to full-width inside CJK-containing UI strings (XAML attrs, C# literals).
# Skips URLs / markup-extension-like values. PS 5.1; needs UTF-8 BOM for Chinese output only.
$ErrorActionPreference = 'Stop'
$root = 'C:\Users\22956\Desktop\SYS\OnShow\src'
$files = Get-ChildItem -Path $root -Recurse -Include *.xaml, *.cs
$total = 0

foreach ($f in $files) {
    $text = [IO.File]::ReadAllText($f.FullName)
    $orig = $text

    # XAML attribute values containing CJK (no < > inside)
    $text = [regex]::Replace($text, '"([^"<>]*[\u4e00-\u9fff][^"<>]*)"', {
        param($m)
        $v = $m.Groups[1].Value
        if ($v -match '://') { return $m.Value }
        $before = $v
        $v = $v.Replace(',', [char]0xFF0C).Replace(';', [char]0xFF1B).Replace('!', [char]0xFF01).Replace('?', [char]0xFF1F).Replace('(', [char]0xFF08).Replace(')', [char]0xFF09)
        $v = $v.Replace(':', [char]0xFF1A)
        if ($v -ne $before) { $script:total++ }
        return '"' + $v + '"'
    })

    # C# string literals containing CJK (single line, no URL)
    $text = [regex]::Replace($text, '"([^"\n][^"\n]*[\u4e00-\u9fff][^"\n]*)"', {
        param($m)
        $v = $m.Groups[1].Value
        if ($v -match '://') { return $m.Value }
        $before = $v
        $v = $v.Replace(',', [char]0xFF0C).Replace(';', [char]0xFF1B).Replace('!', [char]0xFF01).Replace('?', [char]0xFF1F).Replace('(', [char]0xFF08).Replace(')', [char]0xFF09)
        if ($v -ne $before) { $script:total++ }
        return '"' + $v + '"'
    })

    if ($text -ne $orig) {
        [IO.File]::WriteAllText($f.FullName, $text, (New-Object System.Text.UTF8Encoding($false)))
        Write-Host ('updated: ' + $f.Name)
    }
}
Write-Host ("string-level replacements: " + $total)
