#!/usr/bin/env node
import { createHash } from 'node:crypto';
import { promises as fs } from 'node:fs';
import path from 'node:path';

const [command, ...args] = process.argv.slice(2);

if (command === 'config') {
  const [target, apiBaseUrl, publicAppUrl, version] = args;
  if (!target || !version) usage();
  await fs.writeFile(target, `${JSON.stringify({
    apiBaseUrl: apiBaseUrl ?? '',
    signalRHubUrl: '/hubs/game',
    publicBaseUrl: publicAppUrl ?? '',
    applicationVersion: version,
    // Legacy names remain available for artifacts generated before the LAN contract.
    signalRBaseUrl: apiBaseUrl ?? '',
    publicAppUrl: publicAppUrl ?? '',
    buildVersion: version,
  }, null, 2)}\n`);
} else if (command === 'manifest') {
  const [releaseRoot, version, commitHash, timestamp, dotnetVersion, nodeVersion, npmVersion] = args;
  if (![releaseRoot, version, commitHash, timestamp, dotnetVersion, nodeVersion, npmVersion].every(Boolean)) usage();
  const relativeFiles = await files(releaseRoot);
  const included = relativeFiles.filter(file => !['manifest.json', 'checksums.sha256', 'BUILD_INFO.txt'].includes(file));
  const checksumLines = [];
  for (const file of included) checksumLines.push(`${await sha256(path.join(releaseRoot, file))}  ${file}`);
  checksumLines.sort();
  await fs.writeFile(path.join(releaseRoot, 'checksums.sha256'), `${checksumLines.join('\n')}\n`);
  const checksums = Object.fromEntries(checksumLines.map(line => {
    const [hash, file] = line.split('  ');
    return [file, hash];
  }));
  const artifacts = ['api', 'display', 'admin'].map(name => ({ name, files: included.filter(file => file.startsWith(`${name}/`)).length }));
  const manifest = {
    version,
    commitHash,
    buildTimestampUtc: timestamp,
    tools: { dotnet: dotnetVersion, node: nodeVersion, npm: npmVersion },
    artifacts,
    checksums,
    testSummary: { backend: 'dotnet test', display: 'vitest/lint/build', admin: 'vitest/lint/build', ios: 'Release build-for-testing' },
  };
  await fs.writeFile(path.join(releaseRoot, 'manifest.json'), `${JSON.stringify(manifest, null, 2)}\n`);
  await fs.writeFile(path.join(releaseRoot, 'BUILD_INFO.txt'), [
    `PartyGame release ${version}`,
    `Commit: ${commitHash}`,
    `Built at: ${timestamp}`,
    `dotnet: ${dotnetVersion}`,
    `node: ${nodeVersion}`,
    `npm: ${npmVersion}`,
  ].join('\n') + '\n');
} else if (command === 'version') {
  const [manifestPath] = args;
  if (!manifestPath) usage();
  const manifest = JSON.parse(await fs.readFile(manifestPath, 'utf8'));
  process.stdout.write(`${manifest.version}\n`);
} else {
  usage();
}

async function files(root, prefix = '') {
  const entries = await fs.readdir(path.join(root, prefix), { withFileTypes: true });
  const result = [];
  for (const entry of entries) {
    const relative = path.join(prefix, entry.name);
    if (entry.isDirectory()) result.push(...await files(root, relative));
    else if (entry.isFile()) result.push(relative.split(path.sep).join('/'));
  }
  return result;
}

async function sha256(file) {
  return createHash('sha256').update(await fs.readFile(file)).digest('hex');
}

function usage() {
  process.stderr.write('Usage: release-assets.mjs <config|manifest|version> ...\n');
  process.exit(64);
}
