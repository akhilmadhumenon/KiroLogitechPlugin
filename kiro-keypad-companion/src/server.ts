import * as http from 'http';

export type CompanionHandlers = {
  focus: (sessionId: string | undefined, title: string | undefined) => Promise<{ ok: boolean; used?: string }>;
  stop: (sessionId: string | undefined) => Promise<{ ok: boolean; used?: string }>;
  kill: (sessionId: string | undefined) => Promise<{ ok: boolean; used?: string }>;
  prompt: (text: string, submit: boolean) => Promise<{ ok: boolean; used?: string }>;
  newChat: () => Promise<{ ok: boolean; used?: string }>;
  command: (command: string) => Promise<{ ok: boolean; used?: string }>;
  open: (which: string) => Promise<{ ok: boolean; used?: string }>;
  steer: () => Promise<{ ok: boolean; used?: string }>;
  spec: () => Promise<Record<string, unknown>>;
  context: () => Promise<Record<string, unknown>>;
  sessions: () => Promise<Record<string, unknown>>;
};

export type ServerHandle = {
  port: number;
  close: () => Promise<void>;
};

export function startServer(
  port: number,
  token: string,
  handlers: CompanionHandlers,
): Promise<ServerHandle> {
  return new Promise((resolve, reject) => {
    const server = http.createServer(async (req, res) => {
      try {
        if (!authorize(req, token)) {
          send(res, 401, { ok: false, error: 'unauthorized' });
          return;
        }
        const url = new URL(req.url ?? '/', 'http://127.0.0.1');
        if (req.method === 'GET' && url.pathname === '/v1/ping') {
          send(res, 200, { ok: true });
          return;
        }
        if (req.method === 'GET' && url.pathname === '/v1/spec') {
          send(res, 200, await handlers.spec());
          return;
        }
        if (req.method === 'GET' && url.pathname === '/v1/context') {
          send(res, 200, await handlers.context());
          return;
        }
        if (req.method === 'GET' && url.pathname === '/v1/sessions') {
          send(res, 200, await handlers.sessions());
          return;
        }
        if (req.method !== 'POST') {
          send(res, 404, { ok: false, error: 'not_found' });
          return;
        }
        const body = await readJson(req);
        const result = await route(url.pathname, body, handlers);
        send(res, result ? 200 : 404, result ?? { ok: false, error: 'not_found' });
      } catch (err) {
        const message = err instanceof Error ? err.message : String(err);
        send(res, 500, { ok: false, error: message });
      }
    });
    server.once('error', reject);
    server.listen(port, '127.0.0.1', () => {
      const address = server.address();
      const bound = typeof address === 'object' && address ? address.port : port;
      resolve({
        port: bound,
        close: () =>
          new Promise((resClose, rejClose) => {
            server.close((err) => (err ? rejClose(err) : resClose()));
          }),
      });
    });
  });
}

async function route(
  pathname: string,
  body: Record<string, unknown>,
  handlers: CompanionHandlers,
): Promise<Record<string, unknown> | undefined> {
  const sessionId = typeof body.sessionId === 'string' ? body.sessionId : undefined;
  switch (pathname) {
    case '/v1/focus':
      return handlers.focus(sessionId, typeof body.title === 'string' ? body.title : undefined);
    case '/v1/stop':
      return handlers.stop(sessionId);
    case '/v1/kill':
      return handlers.kill(sessionId);
    case '/v1/prompt':
      return handlers.prompt(String(body.text ?? ''), body.submit === true);
    case '/v1/new-chat':
      return handlers.newChat();
    case '/v1/command':
      return handlers.command(String(body.command ?? ''));
    case '/v1/open':
      return handlers.open(String(body.which ?? 'tasks'));
    case '/v1/steer':
      return handlers.steer();
    default:
      return undefined;
  }
}

function authorize(req: http.IncomingMessage, token: string): boolean {
  const header = req.headers.authorization ?? '';
  return header === `Bearer ${token}`;
}

function readJson(req: http.IncomingMessage): Promise<Record<string, unknown>> {
  return new Promise((resolve, reject) => {
    const chunks: Buffer[] = [];
    req.on('data', (chunk) => chunks.push(Buffer.from(chunk)));
    req.on('end', () => {
      const raw = Buffer.concat(chunks).toString('utf8').trim();
      if (!raw) {
        resolve({});
        return;
      }
      try {
        resolve(JSON.parse(raw) as Record<string, unknown>);
      } catch (err) {
        reject(err);
      }
    });
    req.on('error', reject);
  });
}

function send(res: http.ServerResponse, status: number, body: unknown): void {
  const json = JSON.stringify(body);
  res.writeHead(status, { 'Content-Type': 'application/json' });
  res.end(json);
}
