# 深度扫描 DestBin.bin 寻找 RES.BIN 位置
# 用法: .\DeepScan-ResBinOffset.ps1 "D:\path\to\DestBin.bin"

param(
    [Parameter(Mandatory=$true)]
    [string]$FilePath
)

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Deep Scan for RES.BIN Offset" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

if (-not (Test-Path $FilePath)) {
    Write-Host "ERROR: File not found: $FilePath" -ForegroundColor Red
    exit 1
}

$fileBytes = [System.IO.File]::ReadAllBytes($FilePath)
Write-Host "File size: $($fileBytes.Length) bytes ($([math]::Round($fileBytes.Length / 1MB, 2)) MB)" -ForegroundColor Green
Write-Host ""

Write-Host "Scanning from 0x80000 to file end with 256-byte steps..." -ForegroundColor Yellow
Write-Host "This may take a few moments..." -ForegroundColor Gray
Write-Host ""

$foundOffsets = @()
$stepSize = 256
$startOffset = 0x80000
$endOffset = $fileBytes.Length - 1024

for ($i = $startOffset; $i -lt $endOffset; $i += $stepSize) {
    if ($i + 12 -le $fileBytes.Length) {
        try {
            $addr1 = [BitConverter]::ToUInt32($fileBytes, $i)
            $addr2 = [BitConverter]::ToUInt32($fileBytes, $i + 4)
            $addr3 = [BitConverter]::ToUInt32($fileBytes, $i + 8)
            
            # 验证地址合理性
            if ($addr1 -gt $i -and $addr2 -gt $addr1 -and $addr3 -gt $addr2 -and $addr3 -lt $fileBytes.Length) {
                $foundOffsets += @{
                    Offset = $i
                    Addr1 = $addr1
                    Addr2 = $addr2
                    Addr3 = $addr3
                }
                
                Write-Host ("✓ Found potential RES.BIN at offset 0x{0:X6} ({0})" -f $i) -ForegroundColor Green
                Write-Host ("  Addresses: 0x{0:X8} ({0}), 0x{1:X8} ({1}), 0x{2:X8} ({2})" -f $addr1, $addr2, $addr3) -ForegroundColor Gray
                
                # 显示更多上下文
                if ($foundOffsets.Count -ge 10) {
                    Write-Host ""
                    Write-Host "Found 10+ matches, stopping scan..." -ForegroundColor Yellow
                    break
                }
            }
        } catch {
            # 忽略转换错误
        }
    }
    
    # 进度显示（每扫描 100KB 显示一次）
    if (($i % 102400) -eq 0) {
        $progress = [math]::Round(($i - $startOffset) / ($endOffset - $startOffset) * 100, 1)
        Write-Host ("  Progress: {0}% (0x{1:X6})" -f $progress, $i) -ForegroundColor DarkGray
    }
}

Write-Host ""
Write-Host "=== Scan Results ===" -ForegroundColor Yellow
Write-Host ""

if ($foundOffsets.Count -eq 0) {
    Write-Host "✗ No valid RES.BIN offsets found" -ForegroundColor Red
    Write-Host ""
    Write-Host "Possible reasons:" -ForegroundColor Yellow
    Write-Host "  1. This is not a DestBin.bin file" -ForegroundColor Yellow
    Write-Host "  2. RES.BIN uses a different format" -ForegroundColor Yellow
    Write-Host "  3. File is corrupted" -ForegroundColor Yellow
} else {
    Write-Host ("✓ Found {0} potential RES.BIN location(s):" -f $foundOffsets.Count) -ForegroundColor Green
    Write-Host ""
    
    $index = 1
    foreach ($match in $foundOffsets) {
        Write-Host ("{0}. Offset: 0x{1:X6} ({1} bytes / {2} KB)" -f $index, $match.Offset, [math]::Round($match.Offset / 1KB, 2)) -ForegroundColor Cyan
        Write-Host ("   First address: 0x{0:X8} ({0})" -f $match.Addr1) -ForegroundColor Gray
        Write-Host ("   Second address: 0x{0:X8} ({0})" -f $match.Addr2) -ForegroundColor Gray
        Write-Host ("   Third address: 0x{0:X8} ({0})" -f $match.Addr3) -ForegroundColor Gray
        
        $resSize = $fileBytes.Length - $match.Offset
        Write-Host ("   Estimated RES.BIN size: {0} bytes ({1} KB)" -f $resSize, [math]::Round($resSize / 1KB, 2)) -ForegroundColor Gray
        Write-Host ""
        
        $index++
    }
    
    Write-Host "=== Recommendation ===" -ForegroundColor Yellow
    Write-Host ""
    
    if ($foundOffsets.Count -eq 1) {
        $bestMatch = $foundOffsets[0]
        Write-Host ("The most likely RES.BIN offset is: 0x{0:X6} ({0})" -f $bestMatch.Offset) -ForegroundColor Green
        Write-Host ""
        Write-Host "To fix DestBinParser.cs, update the PROGRAM_CODE_SIZE constant:" -ForegroundColor Cyan
        Write-Host ("  private const uint PROGRAM_CODE_SIZE = 0x{0:X6};  // {0} bytes" -f $bestMatch.Offset) -ForegroundColor White
    } else {
        Write-Host "Multiple candidates found. The first one is most likely correct:" -ForegroundColor Cyan
        $bestMatch = $foundOffsets[0]
        Write-Host ("  Offset: 0x{0:X6} ({0})" -f $bestMatch.Offset) -ForegroundColor White
        Write-Host ""
        Write-Host "Update DestBinParser.cs:" -ForegroundColor Cyan
        Write-Host ("  private const uint PROGRAM_CODE_SIZE = 0x{0:X6};  // {0} bytes" -f $bestMatch.Offset) -ForegroundColor White
    }
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Scan Complete" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
