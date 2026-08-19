# Test font file detection
Write-Host "=== Testing Font File Detection ===" -ForegroundColor Cyan
Write-Host ""

$testFiles = @(
    @{Path="ax32_platform_demo\resource\resTable\resfont.bin"; ExpectedType="Font (resfont.bin)"; Description="Font data file"},
    @{Path="ax32_platform_demo\resource\resTable\resfontidx.bin"; ExpectedType="Font (resfontidx.bin)"; Description="Font index file"}
)

foreach ($test in $testFiles) {
    $file = $test.Path
    if (Test-Path $file) {
        $fileSize = (Get-Item $file).Length
        $fileName = Split-Path $file -Leaf
        
        Write-Host "File: $fileName" -ForegroundColor Yellow
        Write-Host "  Description: $($test.Description)" -ForegroundColor Gray
        Write-Host "  Size: $fileSize bytes" -ForegroundColor Gray
        Write-Host "  Expected Type: $($test.ExpectedType)" -ForegroundColor Green
        
        # Read first few bytes
        $bytes = [System.IO.File]::ReadAllBytes($file)
        
        if ($fileName -eq "resfont.bin") {
            $charCount = [System.BitConverter]::ToUInt32($bytes, 0)
            Write-Host "  First 4 bytes (char count): $charCount" -ForegroundColor Cyan
        }
        elseif ($fileName -eq "resfontidx.bin") {
            $magic = [System.BitConverter]::ToUInt16($bytes, 0)
            Write-Host "  First 2 bytes (magic): 0x$($magic.ToString('X4'))" -ForegroundColor Cyan
            if ($magic -eq 0x584D) {
                Write-Host "  ✓ Magic matches 'MX' (0x584D)" -ForegroundColor Green
            }
        }
        
        Write-Host ""
    }
    else {
        Write-Host "File not found: $file" -ForegroundColor Red
        Write-Host ""
    }
}

Write-Host "Test completed!" -ForegroundColor Cyan
