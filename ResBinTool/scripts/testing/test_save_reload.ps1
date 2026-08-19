# 完整测试流程：打开 DestBin.bin -> 替换资源 -> 保存 -> 重新打开

Write-Host "=== Step 1: Load original DestBin.bin ===" -ForegroundColor Cyan
$originalPath = "D:\dwl\work\2026\JT\JX_SDK\JT529X\firmware\ax32_platform_demo\output\DestBin.bin"
$outputPath = "D:\dwl\work\2026\JT\JX_SDK\JT529X\firmware\ax32_platform_demo\output\DestBin_Modified.bin"

# 加载 DestBinParser
Add-Type -Path "D:\dwl\work\2026\JT\JX_SDK\JT529X\firmware\tools\ResBinManager\bin\Debug\net6.0-windows\ResBinManager.dll"

$parser = New-Object ResBinManager.Core.DestBinParser

if ($parser.Load($originalPath)) {
    Write-Host "✓ Original file loaded successfully" -ForegroundColor Green
    Write-Host $parser.GetStructureInfo()
    
    # 提取 RES.BIN
    $resBinData = $parser.GetResBinData()
    Write-Host "`nRES.BIN size: $($resBinData.Length) bytes"
    
    # 检查第一个资源的类型
    if ($resBinData.Length -gt 16) {
        $header = $resBinData[0..15]
        Write-Host "First resource header: $(($header | ForEach-Object { '{0:X2}' -f $_ }) -join ' ')"
        
        if ($header[0] -eq 0xFF -and $header[1] -eq 0xD8 -and $header[2] -eq 0xFF) {
            Write-Host "First resource type: JPEG ✓" -ForegroundColor Green
        } else {
            Write-Host "First resource type: Other" -ForegroundColor Yellow
        }
    }
    
    Write-Host "`n=== Step 2: Save modified file ===" -ForegroundColor Cyan
    
    # 直接保存（不做任何修改）
    if ($parser.Save($outputPath)) {
        Write-Host "✓ File saved to: $outputPath" -ForegroundColor Green
        
        $fileInfo = Get-Item $outputPath
        Write-Host "Saved file size: $($fileInfo.Length) bytes"
        
        Write-Host "`n=== Step 3: Reload saved file ===" -ForegroundColor Cyan
        
        $parser2 = New-Object ResBinManager.Core.DestBinParser
        
        if ($parser2.Load($outputPath)) {
            Write-Host "✓ Modified file loaded successfully" -ForegroundColor Green
            Write-Host $parser2.GetStructureInfo()
            
            # 检查 RES.BIN
            $resBinData2 = $parser2.GetResBinData()
            Write-Host "`nRES.BIN size: $($resBinData2.Length) bytes"
            
            if ($resBinData2.Length -gt 16) {
                $header2 = $resBinData2[0..15]
                Write-Host "First resource header: $(($header2 | ForEach-Object { '{0:X2}' -f $_ }) -join ' ')"
                
                if ($header2[0] -eq 0xFF -and $header2[1] -eq 0xD8 -and $header2[2] -eq 0xFF) {
                    Write-Host "First resource type: JPEG ✓✓✓" -ForegroundColor Green
                } else {
                    Write-Host "First resource type: NOT JPEG ✗✗✗" -ForegroundColor Red
                    Write-Host "ERROR: First resource is not recognized as JPEG!" -ForegroundColor Red
                }
            }
            
            $parser2.Dispose()
        } else {
            Write-Host "✗ Failed to load modified file: $($parser2.ErrorMessage)" -ForegroundColor Red
        }
        
        $parser.Dispose()
    } else {
        Write-Host "✗ Failed to save: $($parser.ErrorMessage)" -ForegroundColor Red
    }
} else {
    Write-Host "✗ Failed to load original file: $($parser.ErrorMessage)" -ForegroundColor Red
}
