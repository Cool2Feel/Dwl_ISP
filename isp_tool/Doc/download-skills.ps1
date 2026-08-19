$url = 'https://github.com/znlgis/opengis-skills/archive/refs/heads/main.zip'
$out = 'D:\jrx\zl\isptool\opengis-skills.zip'
$dest = 'D:\jrx\zl\isptool\opengis-skills'

Write-Host "Downloading from $url ..."
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$wc = New-Object System.Net.WebClient
$wc.DownloadFile($url, $out)
Write-Host "Downloaded zip: $(Get-Item $out).Length bytes"

Write-Host "Extracting..."
Expand-Archive -Path $out -DestinationPath $dest -Force
Remove-Item $out -Force
Write-Host "Extracted to $dest"

# List top-level content
Get-ChildItem $dest -Depth 1
