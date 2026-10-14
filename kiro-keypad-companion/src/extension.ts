import { execFile, execFileSync } from 'child_process';
import * as crypto from 'crypto';
import * as fs from 'fs';
import * as os from 'os';
import * as path from 'path';
import * as vscode from 'vscode';
import { startServer, type ServerHandle } from './server';

const HOME = path.join(os.homedir(), '.kiro-keypad');
const DISCOVERY = path.join(HOME, 'companion.json');
const DISCOVERED = path.join(HOME, 'discovered-commands.json');

let handle: ServerHandle | undefined;
let statusBar: vscode.StatusBarItem | undefined;
let token = '';
let commands: string[] = [];
let steerIndex = 0;

export async function activate(context: vscode.ExtensionContext): Promise<void> {
  statusBar = vscode.window.createStatusBarItem(vscode.StatusBarAlignment.Right, 90);
  statusBar.text = '$(radio-tower) Keypad…';
  statusBar.command = 'kiroKeypad.status';
  statusBar.show();
  context.subscriptions.push(statusBar);
  context.subscriptions.push(
    vscode.commands.registerCommand('kiroKeypad.restart', () => start(context)),
    vscode.commands.registerCommand('kiroKeypad.status', () => showStatus()),
  );
  await start(context);
}

export async function deactivate(): Promise<void> {
  await stop();
}

async function start(context: vscode.ExtensionContext): Promise<void> {
  await stop();
  const port = vscode.workspace.getConfiguration('kiroKeypad').get<number>('port', 47832) ?? 47832;
  token = crypto.randomBytes(24).toString('hex');
  commands = await vscode.commands.getCommands(true);
  fs.mkdirSync(HOME, { recursive: true });
  const kiro = commands.filter((name) => /kiro|chat|agent|spec/i.test(name)).sort();
  fs.writeFileSync(DISCOVERED, JSON.stringify({ capturedAt: new Date().toISOString(), commands: kiro }, null, 2));

  try {
    handle = await startServer(port, token, {
      focus: (sessionId, title) => focusSession(sessionId, title),
      stop: (sessionId) => stopSession(sessionId),
      kill: (sessionId) => killSession(sessionId),
      prompt: (text, submit) => insertPrompt(text, submit),
      newChat: () => newChat(),
      command: (command) => runCommand(command),
      open: (which) => openWhich(which),
      steer: () => steer(),
      spec: async () => specSnapshot(),
      context: async () => ({ ok: true, contextTokens: null }),
      sessions: async () => listOpenSessions(context),
    });
    fs.writeFileSync(
      DISCOVERY,
      JSON.stringify({ port: handle.port, token, pid: process.pid, startedAt: Date.now() }, null, 2),
    );
    if (statusBar) {
      statusBar.text = `$(radio-tower) Keypad:${handle.port}`;
    }
  } catch (err) {
    const message = err instanceof Error ? err.message : String(err);
    if (statusBar) {
      statusBar.text = '$(error) Keypad';
      statusBar.tooltip = message;
    }
    void vscode.window.showErrorMessage(`Kiro Keypad Companion failed: ${message}`);
  }
}

async function stop(): Promise<void> {
  if (handle) {
    await handle.close();
    handle = undefined;
  }
  fs.rmSync(DISCOVERY, { force: true });
}

async function showStatus(): Promise<void> {
  const port = handle?.port ?? 'down';
  const spec = specSnapshot();
  void vscode.window.showInformationMessage(
    `Kiro Keypad ${port}. Spec ${spec.phase} ${spec.done}/${spec.total}. ${commands.length} commands discovered.`,
  );
}

async function focusSession(sessionId: string | undefined, title?: string): Promise<{ ok: boolean; used?: string }> {
  if (sessionId && !sessionId.startsWith('pending:') && !sessionId.startsWith('local:')) {
    const viewed = await tryCommands(['kiroAgent.viewSession'], [sessionId, title ?? '']);
    if (viewed.ok) {
      return viewed;
    }

    return { ok: false, used: 'viewSession' };
  }

  const tried = await tryCommands(
    ['kiroAgent.focusChatInput'],
    [],
  );
  if (tried.ok) {
    return tried;
  }
  await activateKiro();
  return { ok: true, used: 'activate' };
}

async function killSession(_sessionId: string | undefined): Promise<{ ok: boolean; used?: string }> {
  await tryCommands(
    ['workbench.action.chat.cancel', 'kiro.chat.cancel', 'kiro.agent.cancel'],
    [],
  );
  const closed = await tryCommands(['kiroAgent.chat.closeSessionTab'], []);
  if (closed.ok) {
    return closed;
  }

  await keystroke(
    'keystroke "c" using {command down, option down, shift down}\ndelay 0.12\nkeystroke "w" using {command down, option down, shift down}',
  );
  return { ok: true, used: 'close-tab' };
}

async function stopSession(sessionId: string | undefined): Promise<{ ok: boolean; used?: string }> {
  const tried = await tryCommands(
    ['kiro.chat.stop', 'kiro.agent.cancel', 'kiro.chat.cancel', 'workbench.action.chat.cancel'],
    sessionId ? [sessionId] : [],
  );
  if (tried.ok) {
    return tried;
  }
  await keystroke('key code 53\nkeystroke "." using command down');
  return { ok: true, used: 'escape' };
}

async function newChat(): Promise<{ ok: boolean; used?: string }> {
  const tried = await tryCommands(
    ['kiroAgent.startNewChatSession', 'kiroAgent.newSession', 'kiro.agentFocus.orchestrator.createSession'],
    [],
  );
  if (tried.ok) {
    return tried;
  }
  await keystroke('keystroke "l" using command down\ndelay 0.25\nkeystroke "t" using command down');
  return { ok: true, used: 'cmd+t' };
}

async function insertPrompt(text: string, submit: boolean): Promise<{ ok: boolean; used?: string }> {
  const clipped = text.slice(0, 4000);
  const tried = await tryCommands(
    ['kiro.chat.sendMessage', 'kiro.chat.insert', 'kiro.chat.setInput'],
    [clipped],
  );
  if (tried.ok) {
    if (submit) {
      await tryCommands(['kiro.chat.submit', 'kiro.chat.send'], []);
    }
    return tried;
  }
  await paste(clipped, submit);
  return { ok: true, used: 'paste' };
}

async function runCommand(command: string): Promise<{ ok: boolean; used?: string }> {
  if (!command) {
    return { ok: false };
  }
  if (commands.includes(command)) {
    try {
      await vscode.commands.executeCommand(command);
      return { ok: true, used: command };
    } catch {
      return { ok: false, used: command };
    }
  }
  const hint = command.split('.').pop() ?? command;
  const match = commands.find((name) => name.toLowerCase().includes(hint.toLowerCase()));
  if (!match) {
    return { ok: false, used: 'missing' };
  }
  try {
    await vscode.commands.executeCommand(match);
    return { ok: true, used: match };
  } catch {
    return { ok: false, used: match };
  }
}

async function steer(): Promise<{ ok: boolean; used?: string }> {
  const names = steeringNames();
  if (names.length === 0) {
    void vscode.window.showInformationMessage('No steering files in ~/.kiro/steering or .kiro/steering.');
    return { ok: false, used: 'none' };
  }
  const name = names[steerIndex % names.length];
  steerIndex += 1;
  return insertPrompt('/' + name + ' ', false);
}

async function openWhich(which: string): Promise<{ ok: boolean; used?: string }> {
  const spec = specSnapshot();
  const target =
    which === 'requirements' ? spec.requirementsPath
    : which === 'design' ? spec.designPath
    : which === 'hooks' ? hooksDir()
    : spec.tasksPath;
  if (!target || !fs.existsSync(target)) {
    void vscode.window.showInformationMessage(`Kiro Keypad could not find ${which}.`);
    return { ok: false, used: which };
  }
  const stat = fs.statSync(target);
  if (stat.isDirectory()) {
    const entry = fs.readdirSync(target)[0];
    if (!entry) {
      return { ok: false, used: which };
    }
    await vscode.window.showTextDocument(vscode.Uri.file(path.join(target, entry)));
  } else {
    await vscode.window.showTextDocument(vscode.Uri.file(target));
  }
  return { ok: true, used: target };
}

function listOpenSessions(context: vscode.ExtensionContext): Record<string, unknown> {
  try {
    const db = findWorkspaceDb(context);
    if (!db) {
      return { ok: true, sessions: [] };
    }

    const data = readMemento(db);
    const focused = typeof data['sessionPanels.focused'] === 'string' ? data['sessionPanels.focused'] : '';
    const entries = Array.isArray(data['sessionPanels.entries']) ? data['sessionPanels.entries'] : [];
    const sessions = entries.flatMap((entry) => {
      if (!entry || typeof entry !== 'object') {
        return [];
      }
      const row = entry as { id?: unknown; title?: unknown };
      if (typeof row.id !== 'string' || row.id.length === 0) {
        return [];
      }
      return [{
        id: row.id,
        title: typeof row.title === 'string' && row.title.length > 0 ? row.title : 'New',
        focused: row.id === focused,
      }];
    });
    return { ok: true, sessions };
  } catch (err) {
    const message = err instanceof Error ? err.message : String(err);
    return { ok: false, error: message, sessions: [] };
  }
}

function findWorkspaceDb(context: vscode.ExtensionContext): string | undefined {
  const storage = path.resolve(context.globalStorageUri.fsPath, '..', '..', 'workspaceStorage');
  if (!fs.existsSync(storage)) {
    return undefined;
  }

  const folders = vscode.workspace.workspaceFolders ?? [];
  let fallback: { mtime: number; db: string } | undefined;
  for (const name of fs.readdirSync(storage)) {
    const dir = path.join(storage, name);
    const db = path.join(dir, 'state.vscdb');
    const marker = path.join(dir, 'workspace.json');
    if (!fs.existsSync(db)) {
      continue;
    }

    if (fs.existsSync(marker)) {
      const text = fs.readFileSync(marker, 'utf8');
      if (folders.some((folder) => text.includes(folder.uri.toString()) || text.includes(folder.uri.fsPath))) {
        return db;
      }
    }

    const count = panelCount(db);
    const mtime = fs.statSync(db).mtimeMs;
    if (count > 0 && (!fallback || mtime > fallback.mtime)) {
      fallback = { mtime, db };
    }
  }

  return fallback?.db;
}

function panelCount(db: string): number {
  try {
    const data = readMemento(db);
    return Array.isArray(data['sessionPanels.entries']) ? data['sessionPanels.entries'].length : 0;
  } catch {
    return 0;
  }
}

function readMemento(db: string): Record<string, unknown> {
  const sqlite = process.platform === 'darwin' ? '/usr/bin/sqlite3' : 'sqlite3';
  const raw = execFileSync(
    sqlite,
    [db, "SELECT value FROM ItemTable WHERE key='kiro.kiroAgent';"],
    { encoding: 'utf8', timeout: 2000 },
  ).trim();
  if (!raw) {
    return {};
  }
  const data = JSON.parse(raw) as unknown;
  return data && typeof data === 'object' ? data as Record<string, unknown> : {};
}

function specSnapshot(): Record<string, unknown> & {
  phase: string;
  done: number;
  total: number;
  requirementsPath?: string;
  designPath?: string;
  tasksPath?: string;
} {
  const folder = vscode.workspace.workspaceFolders?.[0];
  const empty = { ok: true, phase: 'Spec', done: 0, total: 0, currentTask: null, specDir: null };
  if (!folder) {
    return empty;
  }
  const root = path.join(folder.uri.fsPath, '.kiro', 'specs');
  if (!fs.existsSync(root)) {
    return empty;
  }
  const dirs = fs.readdirSync(root, { withFileTypes: true }).filter((d) => d.isDirectory());
  let best: { dir: string; mtime: number } | undefined;
  for (const dir of dirs) {
    const full = path.join(root, dir.name);
    const tasks = path.join(full, 'tasks.md');
    const stamp = fs.existsSync(tasks) ? fs.statSync(tasks).mtimeMs : fs.statSync(full).mtimeMs;
    if (!best || stamp > best.mtime) {
      best = { dir: full, mtime: stamp };
    }
  }
  if (!best) {
    return empty;
  }
  const requirementsPath = existing(path.join(best.dir, 'requirements.md'));
  const designPath = existing(path.join(best.dir, 'design.md'));
  const tasksPath = existing(path.join(best.dir, 'tasks.md'));
  const parsed = countTasks(tasksPath ? fs.readFileSync(tasksPath, 'utf8') : '');
  const phase = parsed.total > 0 ? 'Tasks' : designPath ? 'Design' : 'Reqs';
  return {
    ok: true,
    phase,
    done: parsed.done,
    total: parsed.total,
    currentTask: parsed.current,
    specDir: best.dir,
    requirementsPath,
    designPath,
    tasksPath,
  };
}

function countTasks(markdown: string): { done: number; total: number; current: string | null } {
  let done = 0;
  let total = 0;
  let current: string | null = null;
  for (const raw of markdown.split('\n')) {
    const line = raw.trim();
    const open = line.indexOf('- [ ]');
    const closed = line.toLowerCase().indexOf('- [x]');
    if (open < 0 && closed < 0) {
      continue;
    }
    total += 1;
    if (closed >= 0 && (open < 0 || closed < open)) {
      done += 1;
      continue;
    }
    if (!current && open >= 0) {
      current = line.slice(open + 5).trim();
    }
  }
  return { done, total, current };
}

function steeringNames(): string[] {
  const dirs = [path.join(os.homedir(), '.kiro', 'steering')];
  const folder = vscode.workspace.workspaceFolders?.[0];
  if (folder) {
    dirs.push(path.join(folder.uri.fsPath, '.kiro', 'steering'));
  }
  const names: string[] = [];
  for (const dir of dirs) {
    if (!fs.existsSync(dir)) {
      continue;
    }
    for (const file of fs.readdirSync(dir)) {
      if (file.endsWith('.md')) {
        names.push(file.slice(0, -3));
      }
    }
  }
  return names;
}

function hooksDir(): string {
  return path.join(os.homedir(), '.kiro', 'hooks');
}

function existing(file: string): string | undefined {
  return fs.existsSync(file) ? file : undefined;
}

async function tryCommands(candidates: string[], args: unknown[]): Promise<{ ok: boolean; used?: string }> {
  // Do not filter on the command list captured at startup. Kiro's chat commands
  // (kiroAgent.viewSession and the session-tab commands) are registered a moment
  // later, on a separate extension host, so they are missing from that snapshot.
  for (const command of candidates) {
    try {
      await vscode.commands.executeCommand(command, ...args);
      return { ok: true, used: command };
    } catch (err) {
      const message = err instanceof Error ? err.message : String(err);
      if (/not found/i.test(message) || args.length === 0) {
        continue;
      }
      try {
        await vscode.commands.executeCommand(command);
        return { ok: true, used: command };
      } catch {
        continue;
      }
    }
  }
  return { ok: false };
}

async function paste(text: string, submit: boolean): Promise<void> {
  await activateKiro();
  const escaped = text.replace(/\\/g, '\\\\').replace(/"/g, '\\"');
  const submitLine = submit ? 'key code 36' : '';
  await osascript(`
    set oldClip to the clipboard
    set the clipboard to "${escaped}"
    tell application "System Events"
      keystroke "v" using command down
      delay 0.12
      ${submitLine}
    end tell
    delay 0.05
    set the clipboard to oldClip
  `);
}

async function keystroke(body: string): Promise<void> {
  await activateKiro();
  await osascript(`tell application "System Events"\n${body}\nend tell`);
}

async function activateKiro(): Promise<void> {
  await osascript('tell application "Kiro" to activate');
}

function osascript(source: string): Promise<void> {
  return new Promise((resolve, reject) => {
    execFile('osascript', ['-e', source], (err) => (err ? reject(err) : resolve()));
  });
}
