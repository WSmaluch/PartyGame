#!/usr/bin/env node
import net from 'node:net';

const server = net.createServer();
server.listen(0, '127.0.0.1', () => {
  const address = server.address();
  if (typeof address === 'object' && address) process.stdout.write(`${address.port}\n`);
  server.close();
});
server.on('error', error => { process.stderr.write(`${error.message}\n`); process.exit(1); });
