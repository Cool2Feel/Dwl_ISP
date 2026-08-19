var fs = require('fs');
var path = require('path');

// Mark as started
var logPath = 'D:\\Tool\\2026\\202606\\extraction_done.txt';
fs.writeFileSync(logPath, 'EXTRACTING\n');

// Try native Windows extract via child_process
var cp = require('child_process');
try {
  cp.execSync(
    'powershell -ExecutionPolicy Bypass -Command "Expand-Archive -Path \'D:\\Tool\\2026\\202606\\opengis-skills-main.zip\' -DestinationPath \'D:\\Tool\\2026\\202606\\opengis-skills\' -Force"',
    { shell: true, timeout: 60000 }
  );
  fs.writeFileSync(logPath, 'EXTRACTED_OK\n');
} catch(e) {
  fs.writeFileSync(logPath, 'EXTRACT_FAILED: ' + e.message + '\n');
}

// Now list the extracted dir
var dir = 'D:\\Tool\\2026\\202606\\opengis-skills';
if (fs.existsSync(dir)) {
  fs.writeFileSync(logPath, 'DIR_EXISTS\n');
  try {
    var entries = cp.execSync('dir ' + dir + ' /s /b', { shell: true, encoding: 'utf8', timeout: 10000 });
    fs.writeFileSync('D:\\Tool\\2026\\202606\\dir_list.txt', entries);
  } catch(e2) {
    fs.appendFileSync(logPath, 'DIR_ERROR: ' + e2.message + '\n');
  }
} else {
  fs.appendFileSync(logPath, 'DIR_NOT_FOUND\n');
  // Check zip exists
  fs.appendFileSync(logPath, 'ZIP_EXISTS: ' + fs.existsSync('D:\\Tool\\2026\\202606\\opengis-skills-main.zip') + '\n');
}
