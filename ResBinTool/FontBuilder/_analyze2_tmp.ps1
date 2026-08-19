$fontDir = 'd:\jrx\2026\sdk\master\HM020F_SVN300\HM020F\firmware\ax32_platform_demo\resource\font'
$resFontBin = [System.IO.File]::ReadAllBytes("$fontDir\resfont.bin")
$fontBin = [System.IO.File]::ReadAllBytes("$fontDir\font.bin")

$resCharCount = [BitConverter]::ToUInt32($resFontBin, 0)
$fontCharCount = [BitConverter]::ToUInt32($fontBin, 0)
Write-Host "resfont.bin: size=$($resFontBin.Length), charCount=$resCharCount"
Write-Host "font.bin: size=$($fontBin.Length), charCount=$fontCharCount"
Write-Host ""

# Expected entries end
$entriesEndRes = 4 + $resCharCount * 8
$entriesEndFont = 4 + $fontCharCount * 8
Write-Host "Expected entries end: resfont=$entriesEndRes, font=$entriesEndFont"
Write-Host ""

# Compare first 5 chars between font.bin and resfont.bin
Write-Host "=== Comparison: font.bin vs resfont.bin (first 5 chars) ==="
for ($i = 0; $i -lt 5; $i++) {
    $fbase = 4 + $i * 8
    $f_cc = [BitConverter]::ToUInt32($fontBin, $fbase)
    $f_off = [BitConverter]::ToUInt32($fontBin, $fbase + 4)

    $rbase = 4 + $i * 8
    $r_off = [BitConverter]::ToUInt32($resFontBin, $rbase)
    $r_w = [BitConverter]::ToUInt16($resFontBin, $rbase + 4)
    $r_h = [BitConverter]::ToUInt16($resFontBin, $rbase + 6)

    # In font.bin, find the next char's offset to compute bitmap size
    $f_next = if ($i -lt $fontCharCount - 1) { [BitConverter]::ToUInt32($fontBin, $fbase + 12) } else { $fontBin.Length }
    $f_size = $f_next - $f_off

    # In resfont.bin, find the next char's offset
    $r_next = if ($i -lt $resCharCount - 1) { [BitConverter]::ToUInt32($resFontBin, $rbase + 8) } else { $resFontBin.Length }
    $r_size = $r_next - $r_off

    Write-Host ("  [{0}] font.bin: cc=0x{1:X4}, off={2}, next={3}, size={4}" -f $i, $f_cc, $f_off, $f_next, $f_size)
    Write-Host ("  [{0}] resfont.bin: off={1}, w={2}, h={3}, next={4}, size={5}" -f $i, $r_off, $r_w, $r_h, $r_next, $r_size)
    Write-Host ("      Diff (resfont_off - font_off) = {0}" -f ($r_off - $f_off))
}

# Find the LAST char's offset to verify bitmap section end
Write-Host ""
Write-Host "=== Last char in each file ==="
$lastIdx = $resCharCount - 1
$rbase = 4 + $lastIdx * 8
$r_off_last = [BitConverter]::ToUInt32($resFontBin, $rbase)
$r_w_last = [BitConverter]::ToUInt16($resFontBin, $rbase + 4)
$r_h_last = [BitConverter]::ToUInt16($resFontBin, $rbase + 6)
$r_bpr = [Math]::Floor(($r_w_last + 7) / 8)
$r_raw = $r_bpr * $r_h_last
$r_aligned = ($r_raw + 15) -band (-16)
$r_end = $r_off_last + $r_aligned
Write-Host "Last char resfont.bin: off=$r_off_last, w=$r_w_last, h=$r_h_last, raw=$r_raw, aligned16=$r_aligned, end=$r_end (file_size=$($resFontBin.Length))"

$flastIdx = $fontCharCount - 1
$fbase = 4 + $flastIdx * 8
$f_off_last = [BitConverter]::ToUInt32($fontBin, $fbase)
$f_cc_last = [BitConverter]::ToUInt32($fontBin, $fbase)
# Need to look up last char's w/h from resfont
$f_end = $fontBin.Length
$f_size_last = $f_end - $f_off_last
Write-Host "Last char font.bin: cc=0x$($f_cc_last.ToString('X4')), off=$f_off_last, size=$f_size_last, end=$f_end"

# Test: compute total bitmap section size by summing aligned sizes
# For resfont.bin: aligned size per char
$totalAligned = 0
$lastOff = 0
for ($i = 0; $i -lt $resCharCount; $i++) {
    $rbase = 4 + $i * 8
    $r_w = [BitConverter]::ToUInt16($resFontBin, $rbase + 4)
    $r_h = [BitConverter]::ToUInt16($resFontBin, $rbase + 6)
    $r_bpr = [Math]::Floor(($r_w + 7) / 8)
    $r_raw = $r_bpr * $r_h
    $r_aligned = ($r_raw + 15) -band (-16)
    $totalAligned += $r_aligned
}
Write-Host ""
Write-Host "Sum of aligned16 sizes for resfont.bin = $totalAligned"
Write-Host "Expected bitmap section = $($resFontBin.Length - $entriesEndRes)"
Write-Host "Diff: $($totalAligned - ($resFontBin.Length - $entriesEndRes))"

# Test 8-byte alignment
$totalAligned8 = 0
for ($i = 0; $i -lt $resCharCount; $i++) {
    $rbase = 4 + $i * 8
    $r_w = [BitConverter]::ToUInt16($resFontBin, $rbase + 4)
    $r_h = [BitConverter]::ToUInt16($resFontBin, $rbase + 6)
    $r_bpr = [Math]::Floor(($r_w + 7) / 8)
    $r_raw = $r_bpr * $r_h
    $r_aligned = ($r_raw + 7) -band (-8)
    $totalAligned8 += $r_aligned
}
Write-Host "Sum of aligned8 sizes for resfont.bin = $totalAligned8"
Write-Host "Diff with bitmap section: $($totalAligned8 - ($resFontBin.Length - $entriesEndRes))"

# Maybe bitmapOffset is NOT aligned - check raw sizes sum
$totalRaw = 0
for ($i = 0; $i -lt $resCharCount; $i++) {
    $rbase = 4 + $i * 8
    $r_w = [BitConverter]::ToUInt16($resFontBin, $rbase + 4)
    $r_h = [BitConverter]::ToUInt16($resFontBin, $rbase + 6)
    $r_bpr = [Math]::Floor(($r_w + 7) / 8)
    $r_raw = $r_bpr * $r_h
    $totalRaw += $r_raw
}
Write-Host "Sum of raw sizes for resfont.bin = $totalRaw"

# Check first 5 chars' offsets in resfont.bin (to see if first offset = charCount*8 or 4+charCount*8)
Write-Host ""
Write-Host "=== First few offsets ==="
for ($i = 0; $i -lt 5; $i++) {
    $rbase = 4 + $i * 8
    $r_off = [BitConverter]::ToUInt32($resFontBin, $rbase)
    Write-Host "  [$i] bitmapOffset = $r_off"
}
Write-Host "charCount*8 = $($resCharCount * 8)"
Write-Host "4+charCount*8 = $(4 + $resCharCount * 8)"
