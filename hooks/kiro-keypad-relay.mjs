#!/usr/bin/env node
// Forwards a Kiro hook payload to the keypad plugin and waits when the tool is gated.
import { request } from 'node:http';

const raw = await new Promise((resolve, reject) => {
  const chunks = [];
  process.stdin.on('data', (chunk) => chunks.push(chunk));
  process.stdin.on('end', () => resolve(Buffer.concat(chunks).toString('utf8')));
  process.stdin.on('error', reject);
});

let event = {};
try {
  event = raw.trim() ? JSON.parse(raw) : {};
} catch {
  event = { parseError: true, raw };
}

if (event && typeof event === 'object' && !event.user_prompt && process.env.USER_PROMPT) {
  event.user_prompt = process.env.USER_PROMPT;
}

const body = JSON.stringify(event);
const response = await new Promise((resolve) => {
  const req = request(
    {
      hostname: '127.0.0.1',
      port: 47831,
      path: '/hook',
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'Content-Length': Buffer.byteLength(body),
      },
      timeout: 16 * 60 * 1000,
    },
    (res) => {
      const chunks = [];
      res.on('data', (chunk) => chunks.push(chunk));
      res.on('end', () => {
        const text = Buffer.concat(chunks).toString('utf8');
        try {
          resolve(JSON.parse(text));
        } catch {
          resolve({ permission: 'allow', ok: false });
        }
      });
    },
  );
  req.on('error', () => resolve({ permission: 'allow', unreachable: true }));
  req.on('timeout', () => {
    req.destroy();
    resolve({ permission: 'deny', reason: 'Keypad gate timed out' });
  });
  req.end(body);
});

if (response.permission === 'deny') {
  console.error(response.reason || 'Denied from the MX Keypad');
  process.exit(2);
}

process.exit(0);
