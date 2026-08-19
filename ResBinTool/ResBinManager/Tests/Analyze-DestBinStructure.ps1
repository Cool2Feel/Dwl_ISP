# DestBin.bin 结构诊断脚本
# 用法: .\Analyze-DestBinStructure.ps1 "D:\path\to\DestBin.bin"

param(
    [Parameter(Mandatory=$true)]
    [string]$FilePath
)

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "DestBin.bin Structure Analysis Tool" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# 检查文件是否存在
if (-not (Test-Path $FilePath)) {
    Write-Host "ERROR: File not found: $FilePath" -ForegroundColor Red
    exit 1
}

# 读取文件
try {
    $fileBytes = [System.IO.File]::ReadAllBytes($FilePath)
    Write-Host "✓ File loaded successfully" -ForegroundColor Green
} catch {
    Write-Host "ERROR: Failed to read file: $_" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "=== Basic Information ===" -ForegroundColor Yellow
Write-Host "File Path: $FilePath"
Write-Host "File Size: $($fileBytes.Length) bytes ($([math]::Round($fileBytes.Length / 1KB, 2)) KB / $([math]::Round($fileBytes.Length / 1MB, 2)) MB)"
Write-Host ""

# 检查文件大小
if ($fileBytes.Length -lt 646144) {
    Write-Host "⚠ WARNING: File is smaller than expected minimum size (646,144 bytes)" -ForegroundColor Yellow
    Write-Host "  This may not be a valid DestBin.bin file" -ForegroundColor Yellow
    Write-Host ""
}

# 检查文件头
Write-Host "=== File Header Analysis ===" -ForegroundColor Yellow

# 前 16 字节（十六进制）
Write-Host "First 16 bytes (hex):" -NoNewline
for ($i = 0; $i -lt 16 -and $i -lt $fileBytes.Length; $i++) {
    Write-Host (" {0:X2}" -f $fileBytes[$i]) -NoNewline
}
Write-Host ""

# BLDR 签名检查（偏移 0x0004-0x0007）
if ($fileBytes.Length -ge 8) {
    $bldrSig = [System.Text.Encoding]::ASCII.GetString($fileBytes, 4, 4)
    Write-Host "BLDR signature at offset 0x0004: '$bldrSig'" -NoNewline
    
    if ($bldrSig -eq "BLDR") {
        Write-Host " ✓" -ForegroundColor Green
    } else {
        Write-Host " ✗ (Expected: 'BLDR')" -ForegroundColor Red
    }
} else {
    Write-Host "Cannot check BLDR signature (file too small)" -ForegroundColor Red
}

Write-Host ""

# 检查标准偏移量 0x9DC00
$resOffset = 0x9DC00
Write-Host "=== RES.BIN Location Analysis ===" -ForegroundColor Yellow
Write-Host "Standard offset: 0x$resOffset ($resOffset bytes / $([math]::Round($resOffset / 1KB, 2)) KB)"
Write-Host ""

if ($fileBytes.Length -gt $resOffset) {
    Write-Host "Checking RES.BIN at standard offset 0x$resOffset..." -ForegroundColor Cyan
    
    # 显示 RES.BIN 起始位置的 32 字节
    Write-Host "RES.BIN first 32 bytes (hex):" -NoNewline
    for ($i = 0; $i -lt 32; $i++) {
        if (($i % 16) -eq 0) {
            Write-Host ""
            Write-Host ("  0x{0:X4}: " -f $i) -NoNewline
        }
        Write-Host ("{0:X2} " -f $fileBytes[$resOffset + $i]) -NoNewline
    }
    Write-Host ""
    
    # 尝试解析为 ASCII
    try {
        $asciiStr = [System.Text.Encoding]::ASCII.GetString($fileBytes, $resOffset, 32)
        Write-Host "RES.BIN first 32 bytes (ASCII): $asciiStr" -ForegroundColor Gray
    } catch {
        Write-Host "Cannot decode as ASCII" -ForegroundColor Gray
    }
    
    # 检查 RES.BIN 魔数（应该是小端序的地址指针）
    if ($fileBytes.Length -ge $resOffset + 12) {
        $addr1 = [BitConverter]::ToUInt32($fileBytes, $resOffset)
        $addr2 = [BitConverter]::ToUInt32($fileBytes, $resOffset + 4)
        $addr3 = [BitConverter]::ToUInt32($fileBytes, $resOffset + 8)
        
        Write-Host ""
        Write-Host "First 3 address pointers:" -ForegroundColor Cyan
        Write-Host "  Address 1: 0x$addr1.ToString('X8') ($addr1)"
        Write-Host "  Address 2: 0x$addr2.ToString('X8') ($addr2)"
        Write-Host "  Address 3: 0x$addr3.ToString('X8') ($addr3)"
        
        # 验证地址合理性
        Write-Host ""
        Write-Host "Address validation:" -ForegroundColor Cyan
        
        $isValid = $true
        
        if ($addr1 -gt $resOffset) {
            Write-Host "  ✓ Address 1 > offset" -ForegroundColor Green
        } else {
            Write-Host "  ✗ Address 1 <= offset (invalid)" -ForegroundColor Red
            $isValid = $false
        }
        
        if ($addr2 -gt $addr1) {
            Write-Host "  ✓ Address 2 > Address 1" -ForegroundColor Green
        } else {
            Write-Host "  ✗ Address 2 <= Address 1 (invalid)" -ForegroundColor Red
            $isValid = $false
        }
        
        if ($addr3 -gt $addr2) {
            Write-Host "  ✓ Address 3 > Address 2" -ForegroundColor Green
        } else {
            Write-Host "  ✗ Address 3 <= Address 2 (invalid)" -ForegroundColor Red
            $isValid = $false
        }
        
        if ($addr3 -lt $fileBytes.Length) {
            Write-Host "  ✓ Address 3 < file size" -ForegroundColor Green
        } else {
            Write-Host "  ✗ Address 3 >= file size (invalid)" -ForegroundColor Red
            $isValid = $false
        }
        
        Write-Host ""
        if ($isValid) {
            Write-Host "✓ RES.BIN structure appears VALID at offset 0x$resOffset" -ForegroundColor Green
            Write-Host ""
            Write-Host "Estimated RES.BIN size: $($fileBytes.Length - $resOffset) bytes ($([math]::Round(($fileBytes.Length - $resOffset) / 1KB, 2)) KB)" -ForegroundColor Green
        } else {
            Write-Host "✗ RES.BIN structure appears INVALID at offset 0x$resOffset" -ForegroundColor Red
            Write-Host ""
            Write-Host "Possible reasons:" -ForegroundColor Yellow
            Write-Host "  1. Different SDK version with different offset" -ForegroundColor Yellow
            Write-Host "  2. Custom firmware build" -ForegroundColor Yellow
            Write-Host "  3. File corruption" -ForegroundColor Yellow
            Write-Host ""
            Write-Host "Suggested action:" -ForegroundColor Cyan
            Write-Host "  Try scanning for RES.BIN at other offsets..." -ForegroundColor Cyan
        }
    }
} else {
    Write-Host "✗ File is too small to contain RES.BIN at offset 0x$resOffset" -ForegroundColor Red
    Write-Host "  File size: $($fileBytes.Length) bytes" -ForegroundColor Red
    Write-Host "  Required minimum: $($resOffset + 1024) bytes" -ForegroundColor Red
}

Write-Host ""
Write-Host "=== Candidate Offset Scanning ===" -ForegroundColor Yellow

$candidateOffsets = @(
    @{Offset=0x80000; Name="512 KB"},
    @{Offset=0x90000; Name="576 KB"},
    @{Offset=0x9C000; Name="624 KB"},
    @{Offset=0xA0000; Name="640 KB"},
    @{Offset=0xB0000; Name="704 KB"}
)

foreach ($candidate in $candidateOffsets) {
    $offset = $candidate.Offset
    
    if ($offset + 12 -le $fileBytes.Length) {
        $addr1 = [BitConverter]::ToUInt32($fileBytes, $offset)
        $addr2 = [BitConverter]::ToUInt32($fileBytes, $offset + 4)
        $addr3 = [BitConverter]::ToUInt32($fileBytes, $offset + 8)
        
        $isValid = ($addr1 -gt $offset) -and ($addr2 -gt $addr1) -and ($addr3 -gt $addr2) -and ($addr3 -lt $fileBytes.Length)
        
        Write-Host ("Offset 0x{0:X6} ({1}): " -f $offset, $candidate.Name) -NoNewline
        
        if ($isValid) {
            Write-Host "✓ POSSIBLE MATCH" -ForegroundColor Green
            Write-Host ("  Addresses: 0x{0:X8}, 0x{1:X8}, 0x{2:X8}" -f $addr1, $addr2, $addr3) -ForegroundColor Gray
        } else {
            Write-Host "✗ Invalid" -ForegroundColor Red
        }
    } else {
        Write-Host ("Offset 0x{0:X6} ({1}): Skipped (beyond file size)" -f $offset, $candidate.Name) -ForegroundColor Gray
    }
}

Write-Host ""
Write-Host "=== Summary ===" -ForegroundColor Yellow
Write-Host ""

if ($fileBytes.Length -ge 8) {
    $bldrSig = [System.Text.Encoding]::ASCII.GetString($fileBytes, 4, 4)
    if ($bldrSig -eq "BLDR") {
        Write-Host "✓ File has valid BLDR signature" -ForegroundColor Green
    } else {
        Write-Host "✗ File does NOT have BLDR signature" -ForegroundColor Red
        Write-Host "  This may not be a DestBin.bin file" -ForegroundColor Red
    }
}

if ($fileBytes.Length -gt $resOffset) {
    $addr1 = [BitConverter]::ToUInt32($fileBytes, $resOffset)
    $addr2 = [BitConverter]::ToUInt32($fileBytes, $resOffset + 4)
    $addr3 = [BitConverter]::ToUInt32($fileBytes, $resOffset + 8)
    
    $isValid = ($addr1 -gt $resOffset) -and ($addr2 -gt $addr1) -and ($addr3 -gt $addr2) -and ($addr3 -lt $fileBytes.Length)
    
    if ($isValid) {
        Write-Host "✓ RES.BIN found at standard offset 0x$resOffset" -ForegroundColor Green
        Write-Host ""
        Write-Host "Next steps:" -ForegroundColor Cyan
        Write-Host "  1. The file should load successfully in ResBinManager" -ForegroundColor Cyan
        Write-Host "  2. If it still fails, check the Debug output for more details" -ForegroundColor Cyan
    } else {
        Write-Host "✗ RES.BIN NOT found at standard offset" -ForegroundColor Red
        Write-Host ""
        Write-Host "Next steps:" -ForegroundColor Cyan
        Write-Host "  1. Check if any candidate offsets above show 'POSSIBLE MATCH'" -ForegroundColor Cyan
        Write-Host "  2. If found, you may need to update DestBinParser.cs with the correct offset" -ForegroundColor Cyan
        Write-Host "  3. Or use the original Res.bin file directly" -ForegroundColor Cyan
    }
} else {
    Write-Host "✗ File is too small" -ForegroundColor Red
    Write-Host ""
    Write-Host "Next steps:" -ForegroundColor Cyan
    Write-Host "  1. Verify this is the correct DestBin.bin file" -ForegroundColor Cyan
    Write-Host "  2. Rebuild the firmware using MakeSPIBin.exe" -ForegroundColor Cyan
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Analysis Complete" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
