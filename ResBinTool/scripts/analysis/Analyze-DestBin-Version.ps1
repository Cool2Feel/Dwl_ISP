# 分析 DestBin.bin 文件头结构，查找版本信息

$destBinPath = "ax32_platform_demo\output\DestBin.bin"

if (-not (Test-Path $destBinPath)) {
    Write-Host "Error: File not found: $destBinPath" -ForegroundColor Red
    exit 1
}

$bytes = [System.IO.File]::ReadAllBytes($destBinPath)
Write-Host "DestBin.bin size: $($bytes.Length) bytes ($([math]::Round($bytes.Length / 1024.0, 2)) KB)" -ForegroundColor Cyan
Write-Host ""

# 显示前 256 字节的详细信息
Write-Host "=== First 256 bytes (hex dump) ===" -ForegroundColor Yellow
for ($i = 0; $i -lt 256; $i++) {
    if (($i % 16) -eq 0) {
        Write-Host ""
        Write-Host ("0x{0:X4}: " -f $i) -NoNewline -ForegroundColor Gray
    }
    Write-Host ("{0:X2} " -f $bytes[$i]) -NoNewline
    
    # 高亮显示 BLDR 签名
    if ($i -ge 4 -and $i -le 7) {
        $sig = [System.Text.Encoding]::ASCII.GetString($bytes, 4, 4)
        if ($sig -eq "BLDR") {
            Write-Host " <- BLDR" -ForegroundColor Green -NoNewline
        }
    }
}
Write-Host ""
Write-Host ""

# 解析可能的版本信息位置
Write-Host "=== Potential Version Information ===" -ForegroundColor Yellow
Write-Host ""

# 偏移 0x00-0x03: 可能是版本号或标志
Write-Host "Offset 0x00-0x03:" -ForegroundColor Cyan
$val = [BitConverter]::ToUInt32($bytes, 0)
Write-Host "  UInt32: 0x$($val.ToString('X8')) ($val)"
Write-Host "  Bytes: $($bytes[0].ToString('X2')) $($bytes[1].ToString('X2')) $($bytes[2].ToString('X2')) $($bytes[3].ToString('X2'))"
Write-Host ""

# 偏移 0x04-0x07: BLDR 签名
Write-Host "Offset 0x04-0x07:" -ForegroundColor Cyan
$sig = [System.Text.Encoding]::ASCII.GetString($bytes, 4, 4)
Write-Host "  ASCII: '$sig'"
Write-Host "  Bytes: $($bytes[4].ToString('X2')) $($bytes[5].ToString('X2')) $($bytes[6].ToString('X2')) $($bytes[7].ToString('X2'))"
Write-Host ""

# 偏移 0x08-0x0B: 可能是版本号或其他信息
Write-Host "Offset 0x08-0x0B:" -ForegroundColor Cyan
$val = [BitConverter]::ToUInt32($bytes, 8)
Write-Host "  UInt32: 0x$($val.ToString('X8')) ($val)"
Write-Host "  Bytes: $($bytes[8].ToString('X2')) $($bytes[9].ToString('X2')) $($bytes[10].ToString('X2')) $($bytes[11].ToString('X2'))"
Write-Host ""

# 偏移 0x0C-0x0F
Write-Host "Offset 0x0C-0x0F:" -ForegroundColor Cyan
$val = [BitConverter]::ToUInt32($bytes, 12)
Write-Host "  UInt32: 0x$($val.ToString('X8')) ($val)"
Write-Host "  Bytes: $($bytes[12].ToString('X2')) $($bytes[13].ToString('X2')) $($bytes[14].ToString('X2')) $($bytes[15].ToString('X2'))"
Write-Host ""

# 尝试解析为字符串（前 64 字节）
Write-Host "=== ASCII String Analysis (First 64 bytes) ===" -ForegroundColor Yellow
$str = ""
for ($i = 0; $i -lt 64; $i++) {
    if ($bytes[$i] -ge 32 -and $bytes[$i] -le 126) {
        $str += [char]$bytes[$i]
    } else {
        $str += "."
    }
}
Write-Host "String: $str"
Write-Host ""

# 搜索常见的版本字符串模式
Write-Host "=== Searching for Version Strings ===" -ForegroundColor Yellow
$patterns = @("V\d+\.\d+", "v\d+\.\d+", "VER", "ver", "Version", "version")
foreach ($pattern in $patterns) {
    $content = [System.Text.Encoding]::ASCII.GetString($bytes, 0, [Math]::Min(1024, $bytes.Length))
    $matches = [regex]::Matches($content, $pattern)
    if ($matches.Count -gt 0) {
        foreach ($match in $matches) {
            Write-Host "Found '$pattern' at offset $($match.Index): '$($match.Value)'" -ForegroundColor Green
        }
    }
}
Write-Host ""

# 检查 RES.BIN 头部是否有版本信息
$resBinOffset = 0x9DC00
if ($resBinOffset + 16 -le $bytes.Length) {
    Write-Host "=== RES.BIN Header (at offset 0x$($resBinOffset.ToString('X'))) ===" -ForegroundColor Yellow
    Write-Host ""
    
    Write-Host "Offset 0x00-0x03 (Magic/Version):" -ForegroundColor Cyan
    $val = [BitConverter]::ToUInt32($bytes, $resBinOffset)
    Write-Host "  UInt32: 0x$($val.ToString('X8')) ($val)"
    Write-Host ""
    
    Write-Host "Offset 0x04-0x07 (Version?):" -ForegroundColor Cyan
    $val = [BitConverter]::ToUInt32($bytes, $resBinOffset + 4)
    Write-Host "  UInt32: 0x$($val.ToString('X8')) ($val)"
    Write-Host "  Possible version: $($val)"
    Write-Host ""
}

Write-Host "Analysis complete!" -ForegroundColor Green
