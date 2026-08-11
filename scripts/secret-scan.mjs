#!/usr/bin/env node

import fs from 'node:fs';
import path from 'node:path';

const [mode, target] = process.argv.slice(2);
const pattern = /-----BEGIN (?:[A-Z ]+)?PRIVATE KEY-----|gh[pousr]_[A-Za-z0-9_]{20,}|AKIA[0-9A-Z]{16}|Authorization:\s*Bearer\s+[A-Za-z0-9._~-]{24,}|PARTYGAME_OPERATOR_TOKEN=(?!REPLACE)[A-Za-z0-9._~-]{32,}|(?:password|secret)\s*[:=]\s*[A-Za-z0-9._~+/-]{24,}/i;
const excludedDirectoryNames = new Set(['.git', 'node_modules', 'DerivedData', 'bin', 'obj']);
const excludedRelativePaths = new Set(['scripts/scan-secrets.sh', 'scripts/secret-scan.mjs']);

function inspect(label, content) {
  if (pattern.test(content)) {
    console.error(`Potential secret found: ${label}`);
    process.exitCode = 1;
  }
}

function inspectFile(file, label = file) {
  try {
    const stat = fs.lstatSync(file);
    if (!stat.isFile() || stat.isSymbolicLink()) return;
    inspect(label, fs.readFileSync(file, 'utf8'));
  } catch (error) {
    if (error.code !== 'ENOENT') throw error;
  }
}

function inspectDirectory(directory, relative = '') {
  for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
    const childRelative = path.posix.join(relative, entry.name);
    const full = path.join(directory, entry.name);
    if (entry.isDirectory()) {
      if (!excludedDirectoryNames.has(entry.name)) inspectDirectory(full, childRelative);
    } else if (!excludedRelativePaths.has(childRelative)) {
      inspectFile(full, childRelative);
    }
  }
}

if (mode === '--files0') {
  for (const file of fs.readFileSync(0, 'utf8').split('\0').filter(Boolean)) {
    const relative = file.split(path.sep).join('/');
    if (!excludedRelativePaths.has(relative)) inspectFile(file, relative);
  }
} else if (mode === '--stdin') {
  inspect('stdin', fs.readFileSync(0, 'utf8'));
} else if (mode === '--directory' && target) {
  inspectDirectory(path.resolve(target));
} else {
  console.error('Usage: secret-scan.mjs --files0 | --stdin | --directory <path>');
  process.exit(64);
}

if (process.exitCode) process.exit(process.exitCode);
