# Extract opengis-skills
PowerShell -ExecutionPolicy Bypass -Command "& {
    \$zip = 'D:\Tool\2026\202606\opengis-skills-main.zip'
    \$dest = 'D:\Tool\2026\202606\opengis-skills'
    Write-Host 'Extracting...'
    Expand-Archive -Path \$zip -DestinationPath \$dest -Force
    Write-Host 'Done!'
    Get-ChildItem \$dest -Recurse | Select-Object FullName, Length
}"
