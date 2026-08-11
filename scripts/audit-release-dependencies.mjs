#!/usr/bin/env node

import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { execFileSync } from 'node:child_process';

let packagePath = '';
for (let index = 2; index < process.argv.length; index += 2) {
  if (process.argv[index] === '--package') packagePath = process.argv[index + 1] ?? '';
  else {
    console.error('Usage: audit-release-dependencies.mjs --package <partygame.tar.gz>');
    process.exit(64);
  }
}
if (!packagePath || !fs.existsSync(packagePath)) {
  console.error('--package must name an existing archive');
  process.exit(64);
}

// The transferable shell scripts intentionally use only POSIX/BSD system tools
// plus this explicit vocabulary of non-system developer/runtime tools. Keeping
// this list finite makes the audit useful without pretending to be a shell
// parser; a newly introduced external command must be classified here first.
const candidateTools = [
  'cargo', 'curl', 'dotnet', 'git', 'go', 'java', 'jq', 'node', 'npm', 'npx',
  'openssl', 'perl', 'pip', 'pip3', 'python', 'python3', 'rg', 'ripgrep',
  'ruby', 'shasum', 'sqlite3', 'tar', 'unzip'
];
const standardSystemTools = new Set(['openssl']);

const temporary = fs.mkdtempSync(path.join(os.tmpdir(), 'partygame-release-deps-'));
try {
  execFileSync('tar', ['-xzf', packagePath, '-C', temporary], { stdio: 'pipe' });
  const roots = fs.readdirSync(temporary, { withFileTypes: true }).filter(entry => entry.isDirectory()).map(entry => path.join(temporary, entry.name));
  if (roots.length !== 1) throw new Error('package must contain exactly one root directory');
  const root = roots[0];
  const manifest = JSON.parse(fs.readFileSync(path.join(root, 'package-manifest.json'), 'utf8'));
  const requiredTools = new Set(manifest.requiredTools ?? []);
  const unexpected = new Map();

  function walk(directory) {
    for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
      const full = path.join(directory, entry.name);
      if (entry.isDirectory()) walk(full);
      else if (entry.isFile() && entry.name.endsWith('.sh')) {
        const source = fs.readFileSync(full, 'utf8');
        for (const tool of candidateTools) {
          if ((requiredTools.has(tool) || standardSystemTools.has(tool)) || !new RegExp(`\\b${tool}\\b`).test(source)) continue;
          if (!unexpected.has(tool)) unexpected.set(tool, []);
          unexpected.get(tool).push(path.relative(root, full));
        }
      }
    }
  }
  walk(path.join(root, 'scripts'));

  if (unexpected.size) {
    for (const [tool, files] of [...unexpected].sort(([left], [right]) => left.localeCompare(right))) {
      console.error(`Undeclared release command: ${tool} (${files.join(', ')})`);
    }
    process.exit(1);
  }
  console.log(`RELEASE_DEPENDENCY_AUDIT_PASS version=${manifest.version}`);
} catch (error) {
  console.error(`RELEASE_DEPENDENCY_AUDIT_FAIL: ${error.message}`);
  process.exit(1);
} finally {
  fs.rmSync(temporary, { recursive: true, force: true });
}
