const https = require('https');
const fs = require('fs');
const path = require('path');

const url = 'https://github.com/znlgis/opengis-skills/archive/refs/heads/main.zip';
const outPath = 'D:/jrx/zl/isptool/opengis-skills.zip';

const file = fs.createWriteStream(outPath);
https.get(url, (res) => {
  res.pipe(file);
  file.on('finish', () => {
    file.close();
    console.log('Downloaded to ' + outPath + ' (' + fs.statSync(outPath).size + ' bytes)');
  });
}).on('error', (err) => {
  fs.unlinkSync(outPath);
  console.error('Download error:', err.message);
});
