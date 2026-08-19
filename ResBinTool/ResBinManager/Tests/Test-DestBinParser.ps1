# DestBinParser 快速测试脚本

Write-Host "=== DestBinParser 快速验证 ===" -ForegroundColor Cyan
Write-Host ""

# 设置路径
$destBinPath = "d:\dwl\work\2026\JT\JX_SDK\JT529X\firmware\ax32_platform_demo\output\DestBin.bin"
$resBinPath = "d:\dwl\work\2026\JT\JX_SDK\JT529X\firmware\ax32_platform_demo\output\res.bin"
$outputPath = "d:\dwl\work\2026\JT\JX_SDK\JT529X\firmware\ax32_platform_demo\output\DestBin_modified.bin"

if (-not (Test-Path $destBinPath)) {
    Write-Host "错误: 找不到 DestBin.bin" -ForegroundColor Red
    exit 1
}

Write-Host "1. 加载 DestBin.bin..." -ForegroundColor Yellow
try {
    # 使用反射加载程序集并创建实例
    $assemblyPath = "d:\dwl\work\2026\JT\JX_SDK\JT529X\firmware\tools\ResBinManager\bin\Debug\net6.0-windows\ResBinManager.dll"
    
    if (Test-Path $assemblyPath) {
        Add-Type -Path $assemblyPath
        
        $parser = New-Object ResBinManager.Core.DestBinParser
        
        if ($parser.Load($destBinPath)) {
            Write-Host "✓ 加载成功" -ForegroundColor Green
            Write-Host ""
            Write-Host $parser.GetStructureInfo()
            Write-Host ""
            
            # 提取 RES.BIN
            Write-Host "2. 提取 RES.BIN..." -ForegroundColor Yellow
            $extractedResBin = $parser.ExtractResBin()
            
            if ($null -ne $extractedResBin) {
                Write-Host "✓ 提取成功: $($extractedResBin.Length) bytes ($([math]::Round($extractedResBin.Length/1KB, 2)) KB)" -ForegroundColor Green
                Write-Host ""
                
                # 与原始文件对比
                if (Test-Path $resBinPath) {
                    Write-Host "3. 与原始 RES.BIN 对比..." -ForegroundColor Yellow
                    $originalResBin = [System.IO.File]::ReadAllBytes($resBinPath)
                    
                    if ($extractedResBin.Length -eq $originalResBin.Length) {
                        Write-Host "✓ 大小一致: $($extractedResBin.Length) bytes" -ForegroundColor Green
                        
                        # 检查前 1024 字节
                        $matchCount = 0
                        for ($i = 0; $i -lt [Math]::Min(1024, $extractedResBin.Length); $i++) {
                            if ($extractedResBin[$i] -eq $originalResBin[$i]) {
                                $matchCount++
                            }
                        }
                        
                        if ($matchCount -eq [Math]::Min(1024, $extractedResBin.Length)) {
                            Write-Host "✓ 前 1024 字节完全一致" -ForegroundColor Green
                        } else {
                            Write-Host "✗ 发现不匹配: $matchCount / $([Math]::Min(1024, $extractedResBin.Length))" -ForegroundColor Red
                        }
                    } else {
                        Write-Host "✗ 大小不一致" -ForegroundColor Red
                    }
                    Write-Host ""
                }
                
                # 替换测试
                Write-Host "4. 替换 RES.BIN 测试..." -ForegroundColor Yellow
                if (Test-Path $resBinPath) {
                    $testResBin = [System.IO.File]::ReadAllBytes($resBinPath)
                    
                    if ($parser.ReplaceResBin($testResBin, $true)) {
                        Write-Host "✓ 替换成功" -ForegroundColor Green
                    } else {
                        Write-Host "✗ 替换失败: $($parser.ErrorMessage)" -ForegroundColor Red
                    }
                    Write-Host ""
                }
                
                # 保存测试
                Write-Host "5. 保存修改后的 DestBin.bin..." -ForegroundColor Yellow
                if ($parser.Save($outputPath)) {
                    Write-Host "✓ 保存成功" -ForegroundColor Green
                    Write-Host "  输出文件: $outputPath"
                    
                    $fileInfo = Get-Item $outputPath
                    Write-Host "  文件大小: $($fileInfo.Length) bytes ($([math]::Round($fileInfo.Length/1KB, 2)) KB)"
                    Write-Host ""
                    
                    # 验证保存的文件
                    Write-Host "6. 验证保存的文件..." -ForegroundColor Yellow
                    $verifyParser = New-Object ResBinManager.Core.DestBinParser
                    
                    if ($verifyParser.Load($outputPath)) {
                        Write-Host "✓ 验证通过: 可以重新加载" -ForegroundColor Green
                        Write-Host ""
                        Write-Host $verifyParser.GetStructureInfo()
                        $verifyParser.Dispose()
                    } else {
                        Write-Host "✗ 验证失败: $($verifyParser.ErrorMessage)" -ForegroundColor Red
                    }
                } else {
                    Write-Host "✗ 保存失败: $($parser.ErrorMessage)" -ForegroundColor Red
                }
                
            } else {
                Write-Host "✗ 提取失败: $($parser.ErrorMessage)" -ForegroundColor Red
            }
            
            $parser.Dispose()
            
        } else {
            Write-Host "✗ 加载失败: $($parser.ErrorMessage)" -ForegroundColor Red
        }
        
    } else {
        Write-Host "错误: 找不到编译的程序集" -ForegroundColor Red
        Write-Host "请先运行: dotnet build" -ForegroundColor Yellow
    }
    
} catch {
    Write-Host "错误: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host $_.ScriptStackTrace -ForegroundColor DarkGray
}

Write-Host ""
Write-Host "=== 测试完成 ===" -ForegroundColor Cyan
