const fs = require('fs');
const path = require('path');
const child_process = require('child_process');

// Step 1: Extract zip using PowerShell
const psCmd = `powershell -ExecutionPolicy Bypass -Command "& { Expand-Archive -Path 'D:\\Tool\\2026\\202606\\opengis-skills-main.zip' -DestinationPath 'D:\\Tool\\2026\\202606\\opengis-skills' -Force; Write-Host 'EXTRACTED' }"`;

console.log('Extracting...');
const output = child_process.execSync(psCmd, { shell: true, encoding: 'utf8' });
console.log('Output:', output);

// Step 2: List files
const dir = 'D:\\Tool\\2026\\202606\\opengis-skills';
console.log('Files:');
if (fs.existsSync(dir)) {
  const entries = fs.readdirSync(dir, { recursive: true, withFileTypes: true });
  entries.forEach(e => {
    const fullPath = path.join(e.parentPath, e.name);
    console.log('  ' + fullPath);
  });
} else {
  console.log('  Directory not found');
}
