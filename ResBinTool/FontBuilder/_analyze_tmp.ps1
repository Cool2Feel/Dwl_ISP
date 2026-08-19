$fontDir = 'd:\jrx\2026\sdk\master\HM020F_SVN300\HM020F\firmware\ax32_platform_demo\resource\font'

# Read all 3 bin files
$fontBin = [System.IO.File]::ReadAllBytes("$fontDir\font.bin")
$resFontBin = [System.IO.File]::ReadAllBytes("$fontDir\resfont.bin")
$resFontIdx = [System.IO.File]::ReadAllBytes("$fontDir\resfontidx.bin")

$charCount = [BitConverter]::ToUInt32($fontBin, 0)
Write-Host "=== font.bin ==="
Write-Host "Size: $($fontBin.Length), charCount: $charCount"
Write-Host "Expected entries section: 4 + $charCount * 8 = $(4 + $charCount * 8)"
Write-Host ""

# Show first 5 chars in font.bin (with offset)
Write-Host "First 5 chars in font.bin:"
for ($i = 0; $i -lt 5; $i++) {
    $base = 4 + $i * 8
    $cc = [BitConverter]::ToUInt32($fontBin, $base)
    $off = [BitConverter]::ToUInt32($fontBin, $base + 4)
    $nextOff = if ($i -lt 4) { [BitConverter]::ToUInt32($fontBin, $base + 12) } else { $fontBin.Length }
    $diff = $nextOff - $off
    Write-Host "  [$i] charCode=0x$($cc.ToString('X4')), offset=$off, nextOff=$nextOff, diff=$diff"
}

Write-Host ""
Write-Host "=== resfont.bin ==="
Write-Host "Size: $($resFontBin.Length)"
$resCharCount = [BitConverter]::ToUInt32($resFontBin, 0)
Write-Host "charCount: $resCharCount"
Write-Host "Expected entries section: 4 + $resCharCount * 8 = $(4 + $resCharCount * 8)"

Write-Host ""
Write-Host "First 10 chars in resfont.bin:"
for ($i = 0; $i -lt 10; $i++) {
    $base = 4 + $i * 8
    $off = [BitConverter]::ToUInt32($resFontBin, $base)
    $w = [BitConverter]::ToUInt16($resFontBin, $base + 4)
    $h = [BitConverter]::ToUInt16($resFontBin, $base + 6)
    $nextOff = if ($i -lt 9) { [BitConverter]::ToUInt32($resFontBin, $base + 8) } else { $resFontBin.Length }
    $diff = $nextOff - $off
    $charCode = [BitConverter]::ToUInt32($fontBin, 4 + $i * 8)
    $chr = if ($charCode -ge 0x20 -and $charCode -le 0x7E) { [char]$charCode } else { '?' }
    Write-Host "  [$i] offset=$off, w=$w, h=$h, nextOff=$nextOff, diff=$diff, charCode=0x$($charCode.ToString('X4')) ('$chr')"
}

# Alignment check: For char i, find actual aligned size
Write-Host ""
Write-Host "=== Alignment check (find actual gap pattern) ==="
$gaps = @{}
for ($i = 0; $i -lt 100; $i++) {
    $base = 4 + $i * 8
    $off = [BitConverter]::ToUInt32($resFontBin, $base)
    $w = [BitConverter]::ToUInt16($resFontBin, $base + 4)
    $h = [BitConverter]::ToUInt16($resFontBin, $base + 6)
    $nextOff = if ($i -lt 99) { [BitConverter]::ToUInt32($resFontBin, $base + 8) } else { $resFontBin.Length }
    $diff = $nextOff - $off
    $bytesPerRow = [Math]::Floor(($w + 7) / 8)
    $rawSize = $bytesPerRow * $h
    if (-not $gaps.ContainsKey($diff)) { $gaps[$diff] = @() }
    $gaps[$diff] += ,@($i, $w, $h, $rawSize, $charCode)
}

Write-Host "Gap distribution (first 100 chars):"
foreach ($k in ($gaps.Keys | Sort-Object)) {
    $entries = $gaps[$k]
    $count = $entries.Count
    $first = $entries[0]
    Write-Host "  Gap=$k (count=$count): first idx=$($first[0]), w=$($first[1]), h=$($first[2]), raw=$($first[3])"
}

# Check resfontidx.bin structure
Write-Host ""
Write-Host "=== resfontidx.bin ==="
Write-Host "Size: $($resFontIdx.Length)"
$hdr = [BitConverter]::ToUInt32($resFontIdx, 0)
$magic = $hdr -band 0xFFFF
$invW = ($hdr -shr 16) -band 0xFF
$langCount = ($hdr -shr 24) -band 0xFF
Write-Host "Header: magic=0x$($magic.ToString('X4')), invalidW=$invW, langCount=$langCount"

# Check offset 4
$u32at4 = [BitConverter]::ToUInt32($resFontIdx, 4)
Write-Host "u32@4 = 0x$($u32at4.ToString('X8')) = $u32at4"

# Read lang table
Write-Host ""
Write-Host "Language table:"
for ($li = 0; $li -lt $langCount; $li++) {
    $base = 8 + $li * 8
    $langId = [BitConverter]::ToUInt32($resFontIdx, $base)
    $blockOff = [BitConverter]::ToUInt32($resFontIdx, $base + 4)
    Write-Host "  [$li] langId=0x$($langId.ToString('X4')), blockOffset=$blockOff"
}

# Show first block structure
Write-Host ""
Write-Host "First language block:"
$lang0BlockOff = [BitConverter]::ToUInt32($resFontIdx, 12)
$u16_0 = [BitConverter]::ToUInt16($resFontIdx, $lang0BlockOff)
$u16_1 = [BitConverter]::ToUInt16($resFontIdx, $lang0BlockOff + 2)
$u32_at4 = [BitConverter]::ToUInt32($resFontIdx, $lang0BlockOff + 4)
Write-Host ("  Block @ {0}: u16_0=0x{1:X4}, u16_1=0x{2:X4} (blockSize?), u32_at4=0x{3:X8}" -f $lang0BlockOff, $u16_0, $u16_1, $u32_at4)

# Show first 3 string entries
$entriesStart = $lang0BlockOff + 8
for ($si = 0; $si -lt 3; $si++) {
    $base = $entriesStart + $si * 8
    $w = [BitConverter]::ToUInt16($resFontIdx, $base)
    $h = [BitConverter]::ToUInt16($resFontIdx, $base + 2)
    $n = [BitConverter]::ToUInt16($resFontIdx, $base + 4)
    $do = [BitConverter]::ToUInt16($resFontIdx, $base + 6)
    $abs = $lang0BlockOff + $do
    Write-Host "  String[$si]: w=$w, h=$h, n=$n, dataOff=0x$($do.ToString('X4')) (abs=$abs)"
}

# Find total file size vs declared
Write-Host ""
Write-Host "Total file size: $($resFontIdx.Length)"
$lastLangOff = [BitConverter]::ToUInt32($resFontIdx, 8 + ($langCount - 1) * 8 + 4)
$lastBlockU16_1 = [BitConverter]::ToUInt16($resFontIdx, $lastLangOff + 2)
Write-Host "Last block @$lastLangOff, blockSize=$lastBlockU16_1"
Write-Host "Last block end: $($lastLangOff + $lastBlockU16_1)"

# Calculate expected size: 8 (header) + langCount*8 (langTable) + sum(blockSizes)
$totalSize = 8 + $langCount * 8
for ($li = 0; $li -lt $langCount; $li++) {
    $base = 8 + $li * 8
    $blockOff = [BitConverter]::ToUInt32($resFontIdx, $base + 4)
    $bs = [BitConverter]::ToUInt16($resFontIdx, $blockOff + 2)
    $totalSize += $bs
}
Write-Host "Expected total = $totalSize (header + langTable + sum(blockSizes))"

# String count per lang
$firstLangBlockOff = [BitConverter]::ToUInt32($resFontIdx, 12)
$firstStrEntryOff = $firstLangBlockOff + 8
$firstDO = [BitConverter]::ToUInt16($resFontIdx, $firstStrEntryOff + 6)
Write-Host ""
Write-Host "First string's dataOff = $firstDO (= $($firstLangBlockOff + $firstDO) absolute)"
# String count = firstDO / 8 (since each entry is 8 bytes)
$strCount = $firstDO / 8 - 1  # subtract 1 because relOffset starts from after block header (8)
# Actually: if dataOff = 8 + N*8, then strCount = N (entries fill from offset 8 to offset 8+N*8)
Write-Host "firstDO / 8 = $($firstDO / 8) - 1 = $($firstDO / 8 - 1) (possible strCount?)"
