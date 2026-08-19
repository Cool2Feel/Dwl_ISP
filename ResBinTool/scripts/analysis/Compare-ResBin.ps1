# 比较 DestBin.bin 中的 RES.BIN 与原始 RES.BIN

$destBinPath = "ax32_platform_demo\output\DestBin.bin"
$resBinPath = "ax32_platform_demo\resource\RES.BIN"

$destBytes = [System.IO.File]::ReadAllBytes($destBinPath)
$resBytes = [System.IO.File]::ReadAllBytes($resBinPath)

Write-Host "DestBin.bin size: $($destBytes.Length) bytes"
Write-Host "RES.BIN size: $($resBytes.Length) bytes"
Write-Host ""

# 尝试不同的偏移量
$offsets = @(0x9C000, 0x9EE53, 0x9DC00)

foreach ($offset in $offsets) {
    Write-Host "=== Testing offset 0x$($offset.ToString('X')) ===" -ForegroundColor Cyan
    
    if ($offset + $resBytes.Length -le $destBytes.Length) {
        # 提取数据
        $extracted = New-Object byte[] $resBytes.Length
        [Array]::Copy($destBytes, $offset, $extracted, 0, $resBytes.Length)
        
        # 比较前 64 字节
        $match = $true
        for ($i = 0; $i -lt 64; $i++) {
            if ($extracted[$i] -ne $resBytes[$i]) {
                $match = $false
                break
            }
        }
        
        if ($match) {
            Write-Host "✓ First 64 bytes MATCH!" -ForegroundColor Green
            
            # 检查整个文件
            $fullMatch = $true
            for ($i = 0; $i -lt $resBytes.Length; $i++) {
                if ($extracted[$i] -ne $resBytes[$i]) {
                    $fullMatch = $false
                    Write-Host "✗ Mismatch at byte $i (0x$($i.ToString('X'))): extracted=0x$($extracted[$i].ToString('X2')), original=0x$($resBytes[$i].ToString('X2'))" -ForegroundColor Red
                    break
                }
            }
            
            if ($fullMatch) {
                Write-Host "✓✓✓ FULL MATCH! This is the correct offset!" -ForegroundColor Green
                Write-Host "Correct offset: 0x$($offset.ToString('X')) ($offset bytes)" -ForegroundColor Green
                break
            } else {
                Write-Host "✗ Full comparison failed" -ForegroundColor Yellow
            }
        } else {
            Write-Host "✗ First 64 bytes DO NOT match" -ForegroundColor Red
            
            # 显示差异
            Write-Host "Original RES.BIN first 16 bytes:" -ForegroundColor Gray
            for ($i = 0; $i -lt 16; $i++) {
                Write-Host ("{0:X2} " -f $resBytes[$i]) -NoNewline
            }
            Write-Host ""
            
            Write-Host "Extracted from DestBin first 16 bytes:" -ForegroundColor Gray
            for ($i = 0; $i -lt 16; $i++) {
                Write-Host ("{0:X2} " -f $extracted[$i]) -NoNewline
            }
            Write-Host ""
        }
    } else {
        Write-Host "✗ Offset beyond file size" -ForegroundColor Red
    }
    
    Write-Host ""
}
