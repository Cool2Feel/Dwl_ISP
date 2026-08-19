$fontBinPath = "d:\jrx\2026\sdk\master\HM020F_SVN300\HM020F\firmware\ax32_platform_demo\resource\font\font.bin"
$fontBin = [System.IO.File]::ReadAllBytes($fontBinPath)
$fontIdxPath = "d:\jrx\2026\sdk\master\HM020F_SVN300\HM020F\firmware\ax32_platform_demo\resource\font\resfontidx.bin"
$fontIdx = [System.IO.File]::ReadAllBytes($fontIdxPath)

Write-Host "font.bin size: $($fontBin.Length)"
Write-Host "resfontidx.bin size: $($fontIdx.Length)"

$charCount = [BitConverter]::ToUInt32($fontBin, 0)
Write-Host "font.bin char count: $charCount"

Write-Host "`n--- font.bin entries for string 'English' indices ---"
$indices = @(0x45, 0x24, 0x31, 0x2D, 0x3E, 0x2F, 0x2D, 0x39, 0x31, 0x3E, 0x2D, 0x38, 0x3B, 0x43, 0x42, 0x3B, 0x38, 0x40, 0x2D, 0x33, 0x31, 0x34, 0x2D, 0x3E, 0x33, 0x31, 0x3E, 0x42, 0x3B, 0x38, 0x40, 0x2D, 0x33, 0x31, 0x35, 0x3F, 0x38, 0x3B)
foreach ($idx in $indices) {
    $offset = 4 + $idx * 8
    if ($offset + 4 -le $fontBin.Length) {
        $charCode = [BitConverter]::ToUInt32($fontBin, $offset)
        $c = [char]$charCode
        $ascii = if ($c -ge 0x20 -and $c -le 0x7E) { "'$c'" } else { "N/A" }
        Write-Host "  Index 0x$($idx.ToString('X2')) ($idx): charCode=0x$($charCode.ToString('X4')) -> $ascii"
    }
}

Write-Host "`n--- Build char index -> char code mapping from font.bin ---"
$mapping = @{}
for ($i = 0; $i -lt $charCount; $i++) {
    $offset = 4 + $i * 8
    if ($offset + 4 -le $fontBin.Length) {
        $charCode = [BitConverter]::ToUInt32($fontBin, $offset)
        $mapping[$i] = $charCode
    }
}

Write-Host "`n--- Decode string #0 using font.bin mapping ---"
$strTableOffset = 0x11C0
$entryOffset = $strTableOffset
$width = [BitConverter]::ToUInt16($fontIdx, $entryOffset)
$height = [BitConverter]::ToUInt16($fontIdx, $entryOffset + 2)
$number = [BitConverter]::ToUInt16($fontIdx, $entryOffset + 4)
$dataOffset = [BitConverter]::ToUInt16($fontIdx, $entryOffset + 6)
Write-Host "String #0: Width=$width, Height=$height, Number=$number, DataOffset=0x$($dataOffset.ToString('X4'))"

$absOffset = $strTableOffset + $dataOffset
Write-Host "Using strTableOffset + dataOffset = 0x$($absOffset.ToString('X4'))"
$result1 = ""
for ($i = 0; $i -lt $number; $i++) {
    $idx = [BitConverter]::ToUInt16($fontIdx, $absOffset + $i * 2)
    if ($idx -eq 0) { $result1 += "|" }
    elseif ($mapping.ContainsKey($idx)) {
        $cc = $mapping[$idx]
        $c = [char]$cc
        if ($c -ge 0x20 -and $c -le 0x7E) { $result1 += $c }
        elseif ($cc -ge 0x4E00 -and $cc -le 0x9FFF) { $result1 += $c }
        elseif ($cc -ge 0x8140 -and $cc -le 0xFEFE) {
            $gbkBytes = @([byte]($cc -shr 8), [byte]($cc -band 0xFF))
            $result1 += [System.Text.Encoding]::GetEncoding("GBK").GetString($gbkBytes)
        }
        else { $result1 += "?" }
    }
    else { $result1 += "?" }
}
Write-Host "Result (strTableOffset base): '$result1'"

$val2 = [BitConverter]::ToUInt32($fontIdx, 8)
$dataBase = $val2
Write-Host "`nUsing val2 as base = 0x$($dataBase.ToString('X4'))"
$absOffset2 = $dataBase + $dataOffset
Write-Host "Using val2 + dataOffset = 0x$($absOffset2.ToString('X4'))"
if ($absOffset2 + $number * 2 -le $fontIdx.Length) {
    $result2 = ""
    for ($i = 0; $i -lt $number; $i++) {
        $idx = [BitConverter]::ToUInt16($fontIdx, $absOffset2 + $i * 2)
        if ($idx -eq 0) { $result2 += "|" }
        elseif ($mapping.ContainsKey($idx)) {
            $cc = $mapping[$idx]
            $c = [char]$cc
            if ($c -ge 0x20 -and $c -le 0x7E) { $result2 += $c }
            elseif ($cc -ge 0x4E00 -and $cc -le 0x9FFF) { $result2 += $c }
            elseif ($cc -ge 0x8140 -and $cc -le 0xFEFE) {
                $gbkBytes = @([byte]($cc -shr 8), [byte]($cc -band 0xFF))
                $result2 += [System.Text.Encoding]::GetEncoding("GBK").GetString($gbkBytes)
            }
            else { $result2 += "?" }
        }
        else { $result2 += "?" }
    }
    Write-Host "Result (val2 base): '$result2'"
} else {
    Write-Host "Offset out of range"
}

Write-Host "`n--- Check area between language table and string table for char codes ---"
$langTableEnd = 4 + 14 * 8
$strTableStart = 0x11C0
Write-Host "Language table ends at: 0x$($langTableEnd.ToString('X4')) ($langTableEnd)"
Write-Host "String table starts at: 0x$($strTableStart.ToString('X4')) ($strTableStart)"
Write-Host "Gap size: $($strTableStart - $langTableEnd) bytes"

Write-Host "`nFirst 40 uint32 values in gap area:"
for ($i = 0; $i -lt 40; $i++) {
    $offset = $langTableEnd + $i * 4
    if ($offset + 4 -le $fontIdx.Length) {
        $val = [BitConverter]::ToUInt32($fontIdx, $offset)
        $c = [char]$val
        $ascii = if ($c -ge 0x20 -and $c -le 0x7E) { "'$c'" } else { "" }
        Write-Host "  Offset 0x$($offset.ToString('X4')): 0x$($val.ToString('X8')) ($val) $ascii"
    }
}

Write-Host "`nFirst 80 uint16 values in gap area:"
for ($i = 0; $i -lt 80; $i++) {
    $offset = $langTableEnd + $i * 2
    if ($offset + 2 -le $fontIdx.Length) {
        $val = [BitConverter]::ToUInt16($fontIdx, $offset)
        $c = [char]$val
        $ascii = if ($c -ge 0x20 -and $c -le 0x7E) { "'$c'" } else { "" }
        Write-Host "  [$i] Offset 0x$($offset.ToString('X4')): 0x$($val.ToString('X4')) ($val) $ascii"
    }
}