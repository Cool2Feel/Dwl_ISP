const fs = require('fs');
const path = require('path');

// Write a marker file so we know the script ran
fs.writeFileSync('D:\\jrx\\zl\\isptool\\script-ran.txt', 'script started\n');

const AdmZip = require('adm-zip');
if (AdmZip) {
  const zip = new AdmZip('D:\\Tool\\2026\\202606\\opengis-skills-main.zip');
  zip.extractAllTo('D:\\Tool\\2026\\202606\\opengis-skills', true);
  fs.writeFileSync('D:\\jrx\\zl\\isptool\\script-ran.txt', 'extracted with adm-zip\n');
}
