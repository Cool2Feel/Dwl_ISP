$bytes = [System.IO.File]::ReadAllBytes("ax32_platform_demo\resource\RES.BIN")
Write-Host "RES.BIN size: $($bytes.Length) bytes"
Write-Host ""
Write-Host "First 64 bytes (hex):"
for ($i = 0; $i -lt 64; $i++) {
    if (($i % 16) -eq 0) {
        Write-Host ""
        Write-Host ("0x{0:X4}: " -f $i) -NoNewline
    }
    Write-Host ("{0:X2} " -f $bytes[$i]) -NoNewline
}
Write-Host ""
Write-Host ""
Write-Host "First 4 uint32 values (little-endian):"
for ($i = 0; $i -lt 4; $i++) {
    $val = [BitConverter]::ToUInt32($bytes, $i * 4)
    Write-Host ("  [{0}] 0x{1:X8} ({1})" -f $i, $val)
}
