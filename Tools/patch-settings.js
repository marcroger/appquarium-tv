// patch-settings.js — Descarga settings.json de R2, cambia DisableCatalogUpdateOnStart a true, sube
// Uso: node Tools/patch-settings.js
const {execSync, spawnSync} = require('child_process');
const fs = require('fs');
const path = require('path');

const R2_ENDPOINT = 'https://2aa2b7914f4ce7ce81e38d694b6219dc.r2.cloudflarestorage.com';
const R2_KEY = 's3://appquarium-tv/StreamingAssets/aa/settings.json';
const TMP = path.join(require('os').tmpdir(), 'settings_patch_clean.json');

const env = Object.assign({}, process.env, {
  AWS_REQUEST_CHECKSUM_CALCULATION: 'when_required',
  AWS_RESPONSE_CHECKSUM_VALIDATION: 'when_supported',
});

// 1. Download
console.log('Downloading settings.json from R2...');
const dl = spawnSync('aws', [
  's3', 'cp', R2_KEY, '-',
  '--profile', 'r2',
  '--endpoint-url', R2_ENDPOINT,
], {env, encoding: 'buffer'});

if (dl.status !== 0) {
  console.error('Download failed:', dl.stderr.toString());
  process.exit(1);
}

// 2. Parse (strip potential BOM)
let rawStr = dl.stdout.toString('utf8').replace(/^﻿/, '');
const data = JSON.parse(rawStr);
console.log('Before:', 'm_DisableCatalogUpdateOnStart =', data.m_DisableCatalogUpdateOnStart);

// 3. Patch
data.m_DisableCatalogUpdateOnStart = true;
console.log('After: ', 'm_DisableCatalogUpdateOnStart =', data.m_DisableCatalogUpdateOnStart);

// 4. Write as pure UTF-8 without BOM
const outStr = JSON.stringify(data);
fs.writeFileSync(TMP, Buffer.from(outStr, 'utf8'));
console.log('Written to', TMP, '—', fs.statSync(TMP).size, 'bytes');

// Verify no BOM
const firstBytes = fs.readFileSync(TMP).slice(0, 3);
const hasBOM = firstBytes[0] === 0xEF && firstBytes[1] === 0xBB && firstBytes[2] === 0xBF;
console.log('Has BOM:', hasBOM, '(should be false)');
if (hasBOM) { console.error('BOM detected — aborting'); process.exit(1); }

// 5. Upload
console.log('Uploading...');
const ul = spawnSync('aws', [
  's3', 'cp', TMP, R2_KEY,
  '--profile', 'r2',
  '--endpoint-url', R2_ENDPOINT,
  '--cache-control', 'public, max-age=60',
], {env, stdio: 'inherit'});

console.log('Upload exit code:', ul.status);
if (ul.status === 0) {
  console.log('SUCCESS: settings.json updated on R2');
} else {
  console.error('Upload failed');
  process.exit(1);
}
