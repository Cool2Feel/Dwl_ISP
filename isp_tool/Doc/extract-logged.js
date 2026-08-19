const fs = require('fs');
const path = require('path');
const child_process = require('child_process');

const logFile = 'D:\\jrx\\zl\\isptool\\extract-log.txt';

function log(msg) {
  fs.appendFileSync(logFile, msg + '\n');
}

// Step 1: Extract zip using PowerShell
const psCmd = `powershell -ExecutionPolicy Bypass -Command "& { Expand-Archive -Path 'D:\\Tool\\2026\\202606\\opengis-skills-main.zip' -DestinationPath 'D:\\Tool\\2026\\202606\\opengis-skills' -Force; Write-Host 'EXTRACTED' }"`;

log('Starting extraction...');
try {
  const output = child_process.execSync(psCmd, { shell: true, encoding: 'utf8', timeout: 30000 });
  log('Extract output: ' + output);
} catch(e) {
  log('Extract error: ' + e.message);
  if (e.stdout) log('stdout: ' + e.stdout);
  if (e.stderr) log('stderr: ' + e.stderr);
}

// Step 2: List files
const dir = 'D:\\Tool\\2026\\202606\\opengis-skills';
log('Checking ' + dir);
if (fs.existsSync(dir)) {
  log('Directory exists!');
  try {
    const entries = fs.readdirSync(dir, { recursive: true, withFileTypes: true });
    entries.forEach(e => {
      const fullPath = path.join(e.parentPath, e.name);
      log('  ' + fullPath);
    });
  } catch(e2) {
    log('Read error: ' + e2.message);
  }
} else {
  log('Directory not found');
  // Try checking if zip exists
  log('Zip exists: ' + fs.existsSync('D:\\Tool\\2026\\202606\\opengis-skills-main.zip'));
}
