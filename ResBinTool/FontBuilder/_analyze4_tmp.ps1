$fontDir = 'd:\jrx\2026\sdk\master\HM020F_SVN300\HM020F\firmware\ax32_platform_demo\resource\font'
$fontBin = [System.IO.File]::ReadAllBytes("$fontDir\font.bin")
$resFontBin = [System.IO.File]::ReadAllBytes("$fontDir\resfont.bin")

# Check if font.bin offsets are monotonic
$fontCharCount = [BitConverter]::ToUInt32($fontBin, 0)
Write-Host "=== font.bin offset monotonicity check ==="
$prev = -1
$nonMono = 0
$diffs = @{}
for ($i = 0; $i -lt $fontCharCount; $i++) {
    $off = [BitConverter]::ToUInt32($fontBin, 4 + $i * 8 + 4)
    if ($prev -ge 0) {
        $d = $off - $prev
        if (-not $diffs.ContainsKey($d)) { $diffs[$d] = 0 }
        $diffs[$d]++
        if ($d -le 0) { $nonMono++ }
    }
    $prev = $off
}
Write-Host "Non-monotonic count: $nonMono / $fontCharCount"
Write-Host "Diff distribution:"
foreach ($k in ($diffs.Keys | Sort-Object)) {
    Write-Host "  diff=$k : $($diffs[$k]) chars"
}

# Look at actual bytes of font.bin around offset 6840 and 6844
Write-Host ""
Write-Host "=== font.bin bytes at offsets 6836..6880 ==="
for ($i = 6836; $i -lt 6884; $i += 16) {
    $line = ""
    for ($j = 0; $j -lt 16 -and ($i + $j) -lt $fontBin.Length; $j++) {
        $line += "{0:X2} " -f $fontBin[$i + $j]
    }
    Write-Host ("  0x{0:X4}: {1}" -f $i, $line)
}

# Check what's at file offset 4 + 6840 = 6844 (if bitmapOffset is relative to offset 4)
Write-Host ""
Write-Host "=== font.bin first bitmap (assuming offset relative to 4) ==="
$bmpStart = 4 + 6840  # = 6844
for ($i = 0; $i -lt 5; $i++) {
    $off = [BitConverter]::ToUInt32($fontBin, 4 + $i * 8 + 4)
    $absOff = 4 + $off
    $nextOff = if ($i -lt 4) { 4 + [BitConverter]::ToUInt32($fontBin, 4 + ($i+1) * 8 + 4) } else { $fontBin.Length }
    $size = $nextOff - $absOff
    $line = ""
    for ($j = 0; $j -lt [Math]::Min($size, 16); $j++) {
        $line += "{0:X2} " -f $fontBin[$absOff + $j]
    }
    Write-Host ("  [{0}] absOff={1}, size={2}, first_bytes: {3}" -f $i, $absOff, $size, $line)
}

# Check resfont.bin's first few bitmaps (absolute offset)
Write-Host ""
Write-Host "=== resfont.bin first bitmaps (absolute offset) ==="
for ($i = 0; $i -lt 5; $i++) {
    $base = 4 + $i * 8
    $off = [BitConverter]::ToUInt32($resFontBin, $base)
    $w = [BitConverter]::ToUInt16($resFontBin, $base + 4)
    $h = [BitConverter]::ToUInt16($resFontBin, $base + 6)
    $nextOff = if ($i -lt 4) { [BitConverter]::ToUInt32($resFontBin, $base + 8) } else { $resFontBin.Length }
    $size = $nextOff - $off
    $line = ""
    $bytesToRead = [Math]::Min($size, 16)
    for ($j = 0; $j -lt $bytesToRead; $j++) {
        $line += "{0:X2} " -f $resFontBin[$off + $j]
    }
    Write-Host ("  [{0}] absOff={1}, w={2}, h={3}, size={4}, first_bytes: {5}" -f $i, $off, $w, $h, $size, $line)
}

# Check what's at resfont.bin offset 6844-6848 (the "4-byte gap")
Write-Host ""
Write-Host "=== resfont.bin bytes 6840..6860 (around bitmap section start) ==="
for ($i = 6840; $i -lt 6864; $i += 16) {
    $line = ""
    for ($j = 0; $j -lt 16 -and ($i + $j) -lt $resFontBin.Length; $j++) {
        $line += "{0:X2} " -f $resFontBin[$i + $j]
    }
    Write-Host ("  0x{0:X4}: {1}" -f $i, $line)
}

# Check resfont.bin's char 0 vs char 1 bitmap (should be same due to dedup)
Write-Host ""
Write-Host "=== resfont.bin char 0 (space) vs char 1 (!) bitmap ==="
$r0_off = [BitConverter]::ToUInt32($resFontBin, 4)
$r1_off = [BitConverter]::ToUInt32($resFontBin, 12)
$r1_w = [BitConverter]::ToUInt16($resFontBin, 16)
$r1_h = [BitConverter]::ToUInt16($resFontBin, 18)
$r2_off = [BitConverter]::ToUInt32($resFontBin, 20)
$r1_size = $r2_off - $r1_off
Write-Host "Char 0: off=$r0_off, Char 1: off=$r1_off (same: $($r0_off -eq $r1_off))"
Write-Host "Char 1: w=$r1_w, h=$r1_h, size=$r1_size"

# Now check if font.bin also dedups char 0
Write-Host ""
Write-Host "=== font.bin char 0 vs char 1 offsets ==="
$f0_off = [BitConverter]::ToUInt32($fontBin, 8)
$f1_off = [BitConverter]::ToUInt32($fontBin, 16)
Write-Host "Char 0: off=$f0_off, Char 1: off=$f1_off (same: $($f0_off -eq $f1_off))"

# If font.bin has different offsets for char 0 vs char 1, then font.bin does NOT dedup
# Check char 0's bitmap content (8 bytes)
$bmp = ""
for ($i = 0; $i -lt 8; $i++) {
    $bmp += "{0:X2} " -f $fontBin[4 + $f0_off + $i]
}
Write-Host "Char 0 (space) bitmap (8 bytes from 4+$f0_off=$($4 + $f0_off)): $bmp"

# Check char 1's bitmap content (8 bytes)
$bmp1 = ""
for ($i = 0; $i -lt 8; $i++) {
    $bmp1 += "{0:X2} " -f $fontBin[4 + $f1_off + $i]
}
Write-Host "Char 1 (!) bitmap (8 bytes from 4+$f1_off=$($4 + $f1_off)): $bmp1"
