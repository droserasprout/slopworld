// Read a snapshot before admission; unchanged descriptors reuse their parsed result.
// Parsing stays with Python's TOML implementation. Credentials never enter argv.
import { execFile } from 'node:child_process';
import { readFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';

export function endpointProvider(env = process.env) {
  let previous;
  let parsed;
  return async () => {
    try {
      if (env.SLOPWEB_ENDPOINT_FILE) {
        const contents = await readFile(env.SLOPWEB_ENDPOINT_FILE, 'utf8');
        if (contents !== previous) {
          previous = contents;
          parsed = new Promise(resolve => {
            const child = execFile('python3',
              [fileURLToPath(new URL('../endpoint.py', import.meta.url)), '--stdin'],
              { env, timeout: 5000, maxBuffer: 65536 }, (error, stdout) => {
                if (error) {
                  if (previous === contents) previous = undefined;
                  resolve({}); return;
                }
                try { resolve(JSON.parse(stdout)); } catch { resolve({}); }
              });
            child.stdin.on('error', () => {});
            child.stdin.end(contents);
          });
        }
        return await parsed;
      }
      const token = env.SLOPWEB_TOKEN_FILE
        ? (await readFile(env.SLOPWEB_TOKEN_FILE, 'utf8')).trim() : env.SLOPD_TOKEN;
      return { endpoint: env.SLOPD_URL, token };
    } catch {
      previous = undefined;
      return {}; // Never fall back to stale credentials or replay failed mutations.
    }
  };
}
