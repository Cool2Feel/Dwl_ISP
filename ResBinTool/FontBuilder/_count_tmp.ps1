$fontDir = 'd:\jrx\2026\sdk\master\HM020F_SVN300\HM020F\firmware\ax32_platform_demo\resource\font'

$lines = Get-Content "$fontDir\fontSrc\english.txt" -Encoding UTF8
$nonEmpty = $lines | Where-Object { $_.Trim() -ne '' -and -not $_.Trim().StartsWith('//') }
Write-Host "english.txt non-empty/non-comment lines: $($nonEmpty.Count)"

$tabLines = Get-Content "$fontDir\font.tab"
Write-Host "font.tab lines: $($tabLines.Count)"

# Count R_ID_STR_ entries in user_str.h
$hLines = Get-Content "$fontDir\user_str.h"
$ridLines = $hLines | Where-Object { $_ -match '^\s*R_ID_STR_' }
Write-Host "user_str.h R_ID_STR_ entries: $($ridLines.Count)"

# File sizes
$binFiles = @('font.bin', 'resfont.bin', 'resfontidx.bin')
foreach ($f in $binFiles) {
    $p = Join-Path $fontDir $f
    if (Test-Path $p) {
        Write-Host "${f}: $((Get-Item $p).Length) bytes"
    }
}
