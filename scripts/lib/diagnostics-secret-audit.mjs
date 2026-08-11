#!/usr/bin/env node

import fs from 'node:fs';
import path from 'node:path';

const root = process.argv[2];
if (!root || !path.isAbsolute(root)) {
  console.error('Usage: diagnostics-secret-audit.mjs <absolute-directory>');
  process.exit(64);
}

// This deliberately matches only values which should have been redacted. Names
// of headers and fields are safe to include in diagnostics and support bundles.
const unredacted = /(?:authorization["\s:=]+bearer\s+(?!\[REDACTED\])[^,\s"']+|bearer\s*[:=]\s*(?!\[REDACTED\])[^,\s"']+|(?:operator|reconnect)[_ -]?token["\s:=]+(?!\[REDACTED\])[^,\s"']+)/i;

function* files(directory) {
  for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
    const full = path.join(directory, entry.name);
    if (entry.isDirectory()) yield* files(full);
    else if (entry.isFile()) yield full;
  }
}

for (const file of files(root)) {
  const text = fs.readFileSync(file, 'utf8');
  if (unredacted.test(text)) {
    console.error(`Unredacted diagnostic secret in ${path.relative(root, file)}`);
    process.exit(1);
  }
}
