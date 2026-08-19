$fontBinPath = "d:\jrx\2026\sdk\master\HM020F_SVN300\HM020F\firmware\ax32_platform_demo\resource\font\font.bin"
$bytes = [System.IO.File]::ReadAllBytes($fontBinPath)
Write-Host "font.bin Size: $($bytes.Length) bytes"

Write-Host "`nFirst 128 bytes (hex):"
for ($i = 0; $i -lt 128 -and $i -lt $bytes.Length; $i += 16) {
    $line = ""
    for ($j = 0; $j -lt 16 -and ($i + $j) -lt $bytes.Length; $j++) {
        $line += "{0:X2} " -f $bytes[$i + $j]
    }
    Write-Host "  0x$($i.ToString('X4')): $line"
}

Write-Host "`nFirst 64 bytes as uint16 (LE):"
for ($i = 0; $i -lt 64 -and ($i + 1) -lt $bytes.Length; $i += 2) {
    $val = [BitConverter]::ToUInt16($bytes, $i)
    $c = [char]$val
    $ascii = if ($c -ge 0x20 -and $c -le 0x7E) { "'$c'" } else { "N/A" }
    Write-Host "  Offset 0x$($i.ToString('X4')): 0x$($val.ToString('X4')) ($val) -> $ascii"
}

$fontDataPath = "d:\jrx\2026\sdk\master\HM020F_SVN300\HM020F\firmware\ax32_platform_demo\resource\font\resfont.bin"
$fontData = [System.IO.File]::ReadAllBytes($fontDataPath)
$charCount = [BitConverter]::ToUInt32($fontData, 0)
Write-Host "`nresfont.bin char count: $charCount"

Write-Host "`n--- Looking for 'English' chars in resfont.bin entries ---"
$englishChars = [ordered]@{
    'E' = 0x45; 'n' = 0x6E; 'g' = 0x67; 'l' = 0x6C; 'i' = 0x69; 's' = 0x73; 'h' = 0x68
}

foreach ($pair in $englishChars.GetEnumerator()) {
    $targetChar = $pair.Value
    $found = $false
    for ($i = 0; $i -lt $charCount; $i++) {
        $offset = 4 + $i * 8
        $b0 = $fontData[$offset]
        $b1 = $fontData[$offset + 1]
        if ($b0 -eq $targetChar) {
            Write-Host "  Char '$($pair.Key)' (0x$($targetChar.ToString('X2'))): found at index $i, b0=0x$($b0.ToString('X2')), b1=0x$($b1.ToString('X2'))"
            $found = $true
            break
        }
    }
    if (-not $found) {
        Write-Host "  Char '$($pair.Key)' (0x$($targetChar.ToString('X2'))): NOT FOUND"
    }
}

$fontIdxPath = "d:\jrx\2026\sdk\master\HM020F_SVN300\HM020F\firmware\ax32_platform_demo\resource\font\resfontidx.bin"
$fontIdx = [System.IO.File]::ReadAllBytes($fontIdxPath)

$header = [BitConverter]::ToUInt32($fontIdx, 0)
$langCount = ($header -shr 24) -band 0xFF
Write-Host "`nLanguage count: $langCount"

$val1 = [BitConverter]::ToUInt32($fontIdx, 4)
$strCount = ($val1 -shr 8) -band 0xFF
$strOffset = $val1 -band 0xFFFF
Write-Host "Lang 0: strCount=$strCount, strTableOffset=0x$($strOffset.ToString('X4'))"

$entryOffset = $strOffset
$width = [BitConverter]::ToUInt16($fontIdx, $entryOffset)
$height = [BitConverter]::ToUInt16($fontIdx, $entryOffset + 2)
$number = [BitConverter]::ToUInt16($fontIdx, $entryOffset + 4)
$dataOffset = [BitConverter]::ToUInt16($fontIdx, $entryOffset + 6)
$absOffset = $strOffset + $dataOffset
Write-Host "`nString #0: Width=$width, Height=$height, Number=$number, DataOffset=0x$($dataOffset.ToString('X4')), AbsOffset=0x$($absOffset.ToString('X4'))"

Write-Host "`nString #0 char indices and corresponding font entries:"
for ($i = 0; $i -lt $number; $i++) {
    $idx = [BitConverter]::ToUInt16($fontIdx, $absOffset + $i * 2)
    if ($idx -eq 0) {
        Write-Host "  [$i] Index=0x$($idx.ToString('X4')) (null terminator)"
        continue
    }
    
    $charEntryOffset = 4 + $idx * 8
    if ($charEntryOffset + 7 -lt $fontData.Length) {
        $b0 = $fontData[$charEntryOffset]
        $b1 = $fontData[$charEntryOffset + 1]
        $b2 = $fontData[$charEntryOffset + 2]
        $b3 = $fontData[$charEntryOffset + 3]
        $w = [BitConverter]::ToUInt16($fontData, $charEntryOffset + 4)
        $h = [BitConverter]::ToUInt16($fontData, $charEntryOffset + 6)
        Write-Host "  [$i] Index=0x$($idx.ToString('X4')) ($idx) -> entry bytes: $($b0.ToString('X2')) $($b1.ToString('X2')) $($b2.ToString('X2')) $($b3.ToString('X2')), w=$w, h=$h"
    } else {
        Write-Host "  [$i] Index=0x$($idx.ToString('X4')) -> OUT OF RANGE"
    }
}