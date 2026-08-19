$zip = "D:\Tool\2026\202606\opengis-skills-main.zip"
$dest = "D:\Tool\2026\202606\opengis-skills"

Write-Host "Extracting $zip to $dest"
Expand-Archive -Path $zip -DestinationPath $dest -Force

Write-Host "Contents:"
Get-ChildItem $dest -Recurse -Depth 3 | Select-Object FullName, Length
