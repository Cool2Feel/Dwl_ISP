const fs = require('fs');
const path = require('path');
const { execSync } = require('child_process');

// Try using PowerShell to extract
const cmd = `powershell -ExecutionPolicy Bypass -Command "Expand-Archive -Path 'D:\\Tool\\2026\\202606\\opengis-skills-main.zip' -DestinationPath 'D:\\Tool\\2026\\202606\\opengis-skills' -Force"`;
console.log('Running:', cmd);
try {
  execSync(cmd, { stdio: 'pipe', shell: true });
  console.log('Extraction done');
} catch(e) {
  console.error('Error:', e.message);
}

// Check if files exist
const destDir = 'D:\\Tool\\2026\\202606\\opengis-skills';
if (fs.existsSync(destDir)) {
  const files = fs.readdirSync(destDir, { withFileTypes: true, recursive: true });
  files.forEach(f => console.log(f.name));
} else {
  console.log('Directory not found:', destDir);
}
