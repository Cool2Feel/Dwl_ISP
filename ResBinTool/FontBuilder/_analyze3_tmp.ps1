$fontDir = 'd:\jrx\2026\sdk\master\HM020F_SVN300\HM020F\firmware\ax32_platform_demo\resource\font'
$fontBin = [System.IO.File]::ReadAllBytes("$fontDir\font.bin")
$resFontBin = [System.IO.File]::ReadAllBytes("$fontDir\resfont.bin")

$fontCharCount = [BitConverter]::ToUInt32($fontBin, 0)
$resCharCount = [BitConverter]::ToUInt32($resFontBin, 0)

Write-Host "=== font.bin last 3 chars ==="
for ($i = $fontCharCount - 3; $i -lt $fontCharCount; $i++) {
    $base = 4 + $i * 8
    $cc = [BitConverter]::ToUInt32($fontBin, $base)
    $off = [BitConverter]::ToUInt32($fontBin, $base + 4)
    Write-Host ("  [{0}] charCode=0x{1:X4}, bitmapOffset={2}" -f $i, $cc, $off)
}
Write-Host "  File size: $($fontBin.Length)"
Write-Host "  Entries end (4+N*8): $(4 + $fontCharCount * 8)"
Write-Host "  If absolute: last char end should = file_size"
$lastOff = [BitConverter]::ToUInt32($fontBin, 4 + ($fontCharCount - 1) * 8 + 4)
Write-Host "  Last char offset: $lastOff"
Write-Host "  If absolute: remaining = $($fontBin.Length - $lastOff)"
Write-Host "  If relative to 4: absolute_last = $($lastOff + 4), remaining = $($fontBin.Length - ($lastOff + 4))"

# Try interpreting font.bin offsets as relative to entries start (offset 4)
Write-Host ""
Write-Host "=== font.bin offset convention check ==="
$firstOff = [BitConverter]::ToUInt32($fontBin, 8)  # first entry's bitmapOffset
$secondOff = [BitConverter]::ToUInt32($fontBin, 16 + 4)  # second entry's bitmapOffset
Write-Host "First offset: $firstOff, expected if absolute: $(4 + $fontCharCount * 8) = 6844"
Write-Host "First offset matches: N*8 = $($fontCharCount * 8) = 6840"
Write-Host "  -> offset is RELATIVE TO OFFSET 4 (entries start)"

# Check second char's bitmap size
$size = $secondOff - $firstOff
Write-Host "First char's bitmap size (from offsets diff): $size bytes"
# Char 0 is space (0x20), small bitmap expected

# Check resfont.bin's actual last char
Write-Host ""
Write-Host "=== resfont.bin last 5 chars ==="
for ($i = $resCharCount - 5; $i -lt $resCharCount; $i++) {
    $base = 4 + $i * 8
    $off = [BitConverter]::ToUInt32($resFontBin, $base)
    $w = [BitConverter]::ToUInt16($resFontBin, $base + 4)
    $h = [BitConverter]::ToUInt16($resFontBin, $base + 6)
    $bpr = [Math]::Floor(($w + 7) / 8)
    $raw = $bpr * $h
    $al8 = ($raw + 7) -band (-8)
    $al16 = ($raw + 15) -band (-16)
    Write-Host ("  [{0}] off={1}, w={2}, h={3}, raw={4}, al8={5}, al16={6}" -f $i, $off, $w, $h, $raw, $al8, $al16)
}
Write-Host "  File size: $($resFontBin.Length)"

# Check if resfont.bin deduplicates: count unique offsets
$uniqueOffsets = @{}
for ($i = 0; $i -lt $resCharCount; $i++) {
    $base = 4 + $i * 8
    $off = [BitConverter]::ToUInt32($resFontBin, $base)
    if (-not $uniqueOffsets.ContainsKey($off)) { $uniqueOffsets[$off] = @() }
    $uniqueOffsets[$off] += $i
}
Write-Host ""
Write-Host "=== resfont.bin dedup check ==="
Write-Host "Total chars: $resCharCount, unique offsets: $($uniqueOffsets.Count)"
$dupCount = 0
foreach ($k in $uniqueOffsets.Keys) {
    if ($uniqueOffsets[$k].Count -gt 1) {
        $dupCount++
        if ($dupCount -le 5) {
            Write-Host "  Offset $k shared by indices: $($uniqueOffsets[$k] -join ',')"
        }
    }
}
Write-Host "Total shared offsets: $dupCount"

# Check the first char's bitmapOffset and compare to 4+N*8
Write-Host ""
Write-Host "=== resfont.bin first char analysis ==="
$r0_off = [BitConverter]::ToUInt32($resFontBin, 4)
$r0_w = [BitConverter]::ToUInt16($resFontBin, 8)
$r0_h = [BitConverter]::ToUInt16($resFontBin, 10)
$r1_off = [BitConverter]::ToUInt32($resFontBin, 12)
Write-Host "Char 0: off=$r0_off, w=$r0_w, h=$r0_h"
Write-Host "Char 1: off=$r1_off (shared with char 0? $($r0_off -eq $r1_off))"
Write-Host "  Expected if absolute+4gap: first_off = $(4 + $resCharCount * 8 + 4) = 6848"
Write-Host "  -> resfont.bin has 4-byte gap between entries and bitmaps"
Write-Host "  OR: resfont.bin's char 0 (space) is deduplicated to char 1's offset"

# Hypothesis: resfont.bin's first bitmap starts at 6848 (= 4 + N*8 + 4 gap)
# Let's check if the 4-byte gap is actually char 0's "real" position
# I.e., char 0 should have offset 6844 but the tool set it to 6848 (same as char 1)
# This means char 0's bitmap is EMPTY and reuses char 1's data

# Check resfontidx's first string's dataOff convention
$resFontIdx = [System.IO.File]::ReadAllBytes("$fontDir\resfontidx.bin")
$lang0BlockOff = [BitConverter]::ToUInt32($resFontIdx, 12)
$firstStrDataOff = [BitConverter]::ToUInt16($resFontIdx, $lang0BlockOff + 8 + 6)  # first entry's dataOff
Write-Host ""
Write-Host "=== resfontidx.bin first string convention ==="
Write-Host "Lang 0 block offset: $lang0BlockOff"
Write-Host "First string's dataOff: $firstStrDataOff (relative to block start)"
Write-Host "  Expected: 8 (block header) + 208*8 (entries) = $(8 + 208*8) = 1672"
Write-Host "  Match: $($firstStrDataOff -eq (8 + 208*8))"
Write-Host "  Absolute first string data pos: $($lang0BlockOff + $firstStrDataOff)"

# Check second string's dataOff
$secondStrDataOff = [BitConverter]::ToUInt16($resFontIdx, $lang0BlockOff + 8 + 8 + 6)
$firstStrNum = [BitConverter]::ToUInt16($resFontIdx, $lang0BlockOff + 8 + 4)
Write-Host "Second string's dataOff: $secondStrDataOff"
Write-Host "First string's char count: $firstStrNum"
Write-Host "  Expected: firstDataOff + firstNum*2 = $($firstStrDataOff + $firstStrNum * 2)"
Write-Host "  Match: $($secondStrDataOff -eq ($firstStrDataOff + $firstStrNum * 2))"
