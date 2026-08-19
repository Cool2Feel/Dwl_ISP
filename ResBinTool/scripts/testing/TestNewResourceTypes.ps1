# Test new resource type parsing functionality
# Use files from ax32_platform_demo\resource\resTable for testing

$testFiles = @(
    "ax32_platform_demo\resource\resTable\palette.bin",
    "ax32_platform_demo\resource\resTable\palette_game.bin",
    "ax32_platform_demo\resource\resTable\game_block_map.bin",
    "ax32_platform_demo\resource\resTable\game_maze_map.bin",
    "ax32_platform_demo\resource\resTable\oem2uni936.bin",
    "ax32_platform_demo\resource\resTable\uni2oem936.bin"
)

Write-Host "=== Testing New Resource Type Detection ===" -ForegroundColor Cyan
Write-Host ""

foreach ($file in $testFiles) {
    if (Test-Path $file) {
        $fileSize = (Get-Item $file).Length
        $fileName = Split-Path $file -Leaf
        
        Write-Host "File: $fileName" -ForegroundColor Yellow
        Write-Host "  Size: $fileSize bytes" -ForegroundColor Gray
        
        # Determine expected type based on file size
        if ($fileSize -eq 1024) {
            Write-Host "  Expected Type: Palette" -ForegroundColor Green
        }
        elseif ($fileSize -lt 10000) {
            Write-Host "  Expected Type: GameMap" -ForegroundColor Green
        }
        elseif ($fileSize -ge 85000 -and $fileSize -le 90000) {
            Write-Host "  Expected Type: EncodingTable" -ForegroundColor Green
        }
        else {
            Write-Host "  Expected Type: Other" -ForegroundColor Gray
        }
        
        Write-Host ""
    }
    else {
        Write-Host "File not found: $file" -ForegroundColor Red
    }
}

Write-Host "Test completed!" -ForegroundColor Cyan
