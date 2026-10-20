#!/usr/bin/env node
// create-module — scaffold, wire, publish and install Module Platform modules.
// Zero dependencies (Node >= 18). Run from anywhere inside module_as_plugin/.
// Guide: docs/module-developer-guide.md
import { spawnSync } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import readline from 'node:readline/promises';
import { fileURLToPath } from 'node:url';

const TOOL_DIR = path.dirname(fileURLToPath(import.meta.url));
const TEMPLATES = path.join(TOOL_DIR, 'templates');
const CLI = 'node tools/create-module/create-module.mjs';
const RESERVED = new Set(['admin', 'api', 'platform', 'auth', 'gateway', 't', 'health', 'metrics', 'docs', 'login', 'static']);
const DIGITS = ['ZERO', 'ONE', 'TWO', 'THREE', 'FOUR', 'FIVE', 'SIX', 'SEVEN', 'EIGHT', 'NINE'];
const BASE_PORTS = [5100, 5121, 5122, 5173, 5174, 5175, 5176, 5177, 5201, 5202, 5203, 5432, 5433, 5434];
const API_PORT_START = 5204;
const WEB_PORT_START = 5178;

const HELP = `create-module — Module Platform module scaffolder

Usage: ${CLI} <command> [options]

Commands:
  new <name>           Scaffold API + federation remote + manifest + parity test, then wire it in
    --display-name <text>  default: Title Case of name
    --description <text>   default: "<Display Name> module."
    --owner <email>        default: platform-team@company.com
    --category <slug>      default: ops
    --icon <name>          default: box
    --api-port <n>         default: next free port >= ${API_PORT_START}
    --web-port <n>         default: next free port >= ${WEB_PORT_START}
    --perm-prefix <CODE>   default: derived from name (sample3 -> SAMPLE_THREE)
    --menu-order <n>       default: 50
    --no-wire              only generate files (no sln/AppHost/compose/test csproj edits)
    --skip-install         skip npm install in modules/<name>/web
    --dry-run              print the plan, write nothing
    -y, --yes              no interactive prompts
  list                 List modules with their API/web ports
  publish <name>       Publish manifest to the registry (POST /api/v1/platform/modules)
    --gateway <url>        default http://localhost:5100 (env MODULE_GATEWAY)
    --token <jwt>          platform Admin JWT (env PLATFORM_TOKEN), or:
    --email <email>        default admin@local, with
    --password <pw>        (env PLATFORM_PASSWORD) to log in
    --api-url <url>        default http://localhost:<module API port>
  install <name> --tenant <id> [--version x.y.z]   Install for a tenant (same auth options)
  remove <name> [-y]   Delete the module's generated folders and undo the wiring
  pack [--out <file>]  Zip this starter kit for distribution (default dist/create-module-kit.zip)
  help                 Show this help
`;

// ---------- helpers ----------
const log = (msg = '') => console.log(msg);
const fail = (msg) => { console.error(`error: ${msg}`); process.exit(1); };
const exists = (p) => fs.existsSync(p);
const read = (p) => fs.readFileSync(p, 'utf8');
const rel = (root, p) => path.relative(root, p) || '.';
const camel = (s) => s.replace(/-([a-z])/g, (_, c) => c.toUpperCase());

function parseArgs(argv) {
  const opts = { _: [] };
  const flags = new Set(['no-wire', 'skip-install', 'dry-run', 'yes', 'help']);
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i];
    if (a === '-y') opts.yes = true;
    else if (a === '-h') opts.help = true;
    else if (a.startsWith('--')) {
      const [k, inline] = a.slice(2).split(/=(.*)/s, 2);
      if (flags.has(k)) opts[camel(k)] = true;
      else {
        const v = inline ?? argv[++i];
        if (v === undefined) fail(`--${k} needs a value`);
        opts[camel(k)] = v;
      }
    } else opts._.push(a);
  }
  return opts;
}

function findRoot() {
  for (const start of [process.cwd(), TOOL_DIR]) {
    let dir = path.resolve(start);
    while (true) {
      if (exists(path.join(dir, 'ModulePlatform.sln'))) return dir;
      const parent = path.dirname(dir);
      if (parent === dir) break;
      dir = parent;
    }
  }
  fail('ModulePlatform.sln not found. Run inside module_as_plugin/.');
}

function walk(dir, filter, out = []) {
  if (!exists(dir)) return out;
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    if (['node_modules', 'bin', 'obj', 'dist', '.git', '.DS_Store'].includes(e.name)) continue;
    const p = path.join(dir, e.name);
    if (e.isDirectory()) walk(p, filter, out);
    else if (filter(p)) out.push(p);
  }
  return out;
}

const toPascal = (name) => name.split('-').map((s) => s[0].toUpperCase() + s.slice(1)).join('');
const toTitle = (name) => name.split('-').map((s) => s[0].toUpperCase() + s.slice(1)).join(' ');
const toCamel = (name) => { const p = toPascal(name); return p[0].toLowerCase() + p.slice(1); };
const toPermPrefix = (name) => name.toUpperCase()
  .replace(/[0-9]/g, (d) => `_${DIGITS[Number(d)]}_`)
  .replace(/-/g, '_').replace(/_+/g, '_').replace(/^_|_$/g, '');

function run(cmd, args, cwd, { inherit = true } = {}) {
  const r = spawnSync(cmd, args, { cwd, stdio: inherit ? 'inherit' : 'pipe', encoding: 'utf8', shell: process.platform === 'win32' });
  return r.error ? { ok: false, error: r.error } : { ok: r.status === 0, stdout: r.stdout, stderr: r.stderr };
}

async function prompt(rl, question, def) {
  const answer = (await rl.question(`${question} (${def}): `)).trim();
  return answer || def;
}

// ---------- repository scans ----------
function usedPorts(root) {
  const ports = new Set(BASE_PORTS);
  const files = [
    ...walk(path.join(root, 'src'), (p) => p.endsWith('launchSettings.json') || p.endsWith('Program.cs')),
    ...walk(path.join(root, 'modules'), (p) => /vite\.config\.ts$|package\.json$|manifest\.yml$/.test(p)),
    ...walk(path.join(root, 'apps'), (p) => /vite\.config\.ts$|package\.json$/.test(p)),
    path.join(root, 'docker-compose.yml'),
  ].filter(exists);
  const patterns = [/localhost:(\d{4,5})/g, /Port\s*=\s*(\d{4,5})/g, /"(\d{4,5}):\d+"/g, /--port\s+(\d{4,5})/g, /port:\s*(\d{4,5})/g];
  for (const f of files) {
    const text = read(f);
    for (const re of patterns) for (const m of text.matchAll(re)) ports.add(Number(m[1]));
  }
  return ports;
}

function nextFree(used, start) {
  let p = start;
  while (used.has(p)) p++;
  return p;
}

function usedScopes(root) {
  const scopes = new Map();
  for (const f of walk(path.join(root, 'modules'), (p) => p.endsWith('manifest.yml'))) {
    const m = read(f).match(/^\s*scope:\s*(\S+)/m);
    if (m) scopes.set(m[1], rel(root, f));
  }
  return scopes;
}

function moduleApiDir(root, name) {
  const hit = walk(path.join(root, 'src', 'Modules'), (p) => p.endsWith('ModuleEndpoints.cs'))
    .find((p) => read(p).includes(`ModuleId = "${name}"`));
  if (hit) return path.dirname(path.dirname(hit));
  const csproj = walk(path.join(root, 'src', 'Modules', toPascal(name)), (p) => p.endsWith('.Api.csproj'))[0];
  return csproj ? path.dirname(csproj) : null;
}

function apiPortOf(apiDir) {
  const ls = apiDir && path.join(apiDir, 'Properties', 'launchSettings.json');
  return ls && exists(ls) ? read(ls).match(/localhost:(\d+)/)?.[1] ?? null : null;
}

// ---------- templating ----------
function render(text, tokens) {
  for (const key of Object.keys(tokens).sort((a, b) => b.length - a.length)) text = text.replaceAll(`__${key}__`, tokens[key]);
  return text;
}

// *.template files are stored with a suffix so IDEs don't restore/build them in place.
function plan(root, t) {
  const targets = [
    ['api', path.join(root, 'src', 'Modules', t.PASCAL, `${t.PASCAL}.Api`)],
    ['module', path.join(root, 'modules', t.MODULE_NAME)],
    ['test', path.join(root, 'tests', 'SharedKernel.Tests', 'Modules')],
  ];
  return targets.flatMap(([src, dest]) => walk(path.join(TEMPLATES, src), () => true).map((f) => ({
    from: f,
    to: path.join(dest, render(path.relative(path.join(TEMPLATES, src), f), t).replace(/\.template$/, '')),
  })));
}

// ---------- wiring (idempotent, marker-delimited where possible) ----------
const marker = (name, c = '//') => [`${c} >>> module:${name} (create-module)`, `${c} <<< module:${name}`];

function insertBefore(text, anchor, block, file) {
  const i = text.lastIndexOf(anchor);
  if (i < 0) throw new Error(`anchor "${anchor.trim()}" not found in ${file}`);
  return text.slice(0, i) + block + text.slice(i);
}

function addProjectReference(file, include) {
  let text = read(file);
  if (text.includes(include)) return;
  const refs = [...text.matchAll(/^([ \t]*)<ProjectReference Include="[^"]*" \/>\r?\n/gm)];
  if (!refs.length) throw new Error(`no ProjectReference found in ${file}`);
  const last = refs[refs.length - 1];
  const at = last.index + last[0].length;
  text = text.slice(0, at) + `${last[1]}<ProjectReference Include="${include}" />\n` + text.slice(at);
  fs.writeFileSync(file, text);
}

function removeLine(file, needle) {
  if (!exists(file)) return;
  const text = read(file);
  const next = text.split('\n').filter((l) => !l.includes(needle)).join('\n');
  if (next !== text) fs.writeFileSync(file, next);
}

function removeBlock(file, name, c) {
  if (!exists(file)) return;
  const [start, end] = marker(name, c);
  const text = read(file);
  const s = text.indexOf(start);
  const e = text.indexOf(end);
  if (s < 0 || e < 0) return;
  const lineStart = text.lastIndexOf('\n', s) + 1;
  const lineEnd = text.indexOf('\n', e) + 1 || text.length;
  fs.writeFileSync(file, text.slice(0, lineStart) + text.slice(lineEnd).replace(/^\r?\n/, ''));
}

function wire(root, t) {
  const steps = [];
  const csprojRel = path.join('src', 'Modules', t.PASCAL, `${t.PASCAL}.Api`, `${t.PASCAL}.Api.csproj`);

  // 1. solution
  const sln = run('dotnet', ['sln', 'ModulePlatform.sln', 'add', csprojRel], root, { inherit: false });
  steps.push(sln.ok ? 'ModulePlatform.sln: project added'
    : `ModulePlatform.sln: SKIPPED (${sln.error ? 'dotnet not found' : (sln.stderr || sln.stdout || '').trim()}) -> run: dotnet sln ModulePlatform.sln add ${csprojRel}`);

  // 2. Aspire AppHost
  const appHostDir = path.join(root, 'src', 'Aspire', 'ModulePlatform.AppHost');
  addProjectReference(path.join(appHostDir, 'ModulePlatform.AppHost.csproj'), `..\\..\\Modules\\${t.PASCAL}\\${t.PASCAL}.Api\\${t.PASCAL}.Api.csproj`);
  const programPath = path.join(appHostDir, 'Program.cs');
  const program = read(programPath);
  const [ps, pe] = marker(t.MODULE_NAME);
  if (!program.includes(ps)) {
    fs.writeFileSync(programPath, insertBefore(program, 'builder.Build().Run();', `${ps}
var ${t.CAMEL} = builder.AddProject<Projects.${t.PASCAL}_Api>("${t.MODULE_NAME}")
    .WithEndpoint("http", e => { e.Port = ${t.API_PORT}; e.IsProxied = false; })
    .WithEnvironment("ASPNETCORE_URLS", "http://localhost:${t.API_PORT}")
    .WithReference(platform)
    .WithReference(auth)
    .WithEnvironment("Platform__BaseUrl", "http://localhost:5121")
    .WithEnvironment("Auth__BaseUrl", "http://localhost:5122");

// Federation remotes must be built (vite preview) — remoteEntry.js is not served by \`vite dev\`.
var ${t.CAMEL}Web = builder.AddNpmApp("${t.MODULE_NAME}-web", "../../../modules/${t.MODULE_NAME}/web", "serve")
    .WithEndpoint("http", e => { e.Port = ${t.WEB_PORT}; e.IsProxied = false; })
    .WithExternalHttpEndpoints();
${pe}

`, programPath));
  }
  steps.push('Aspire AppHost: ProjectReference + resources added');

  // 3. docker-compose
  const composePath = path.join(root, 'docker-compose.yml');
  const compose = read(composePath);
  const [cs, ce] = marker(t.MODULE_NAME, '#');
  if (!compose.includes(cs)) {
    fs.writeFileSync(composePath, insertBefore(compose, '\nvolumes:', `
  ${cs}
  ${t.MODULE_NAME}:
    build:
      context: .
      dockerfile: src/Modules/${t.PASCAL}/${t.PASCAL}.Api/Dockerfile
    ports:
      - "${t.API_PORT}:8080"
    environment:
      ASPNETCORE_ENVIRONMENT: Production
      Jwt__SecretKey: \${JWT_SECRET:?set JWT_SECRET (>=32 chars, see .env.example)}
      Platform__BaseUrl: http://platform:8080
      Auth__BaseUrl: http://auth:8080
    depends_on:
      - platform
      - auth

  ${t.MODULE_NAME}-web:
    build:
      context: .
      dockerfile: modules/${t.MODULE_NAME}/web/Dockerfile
    ports:
      - "${t.WEB_PORT}:80"
  ${ce}
`, composePath));
  }
  steps.push('docker-compose.yml: services added');

  // 4. tests
  addProjectReference(path.join(root, 'tests', 'SharedKernel.Tests', 'SharedKernel.Tests.csproj'),
    `..\\..\\src\\Modules\\${t.PASCAL}\\${t.PASCAL}.Api\\${t.PASCAL}.Api.csproj`);
  steps.push('SharedKernel.Tests.csproj: ProjectReference added');
  return steps;
}

// ---------- commands ----------
async function cmdNew(root, opts) {
  const name = opts._[1];
  if (!name) fail('usage: new <name>');
  if (!/^[a-z][a-z0-9-]{2,49}$/.test(name) || name.endsWith('-') || name.includes('--'))
    fail(`invalid name "${name}": lowercase letters, digits and single hyphens, 3-50 chars, starting with a letter`);
  if (RESERVED.has(name)) fail(`"${name}" is a reserved name`);

  const pascal = toPascal(name);
  if (exists(path.join(root, 'modules', name))) fail(`modules/${name} already exists`);
  if (exists(path.join(root, 'src', 'Modules', pascal))) fail(`src/Modules/${pascal} already exists`);

  let displayName = opts.displayName ?? toTitle(name);
  let description = opts.description;
  let owner = opts.owner ?? 'platform-team@company.com';
  if (!opts.yes && process.stdin.isTTY && !opts.dryRun) {
    const rl = readline.createInterface({ input: process.stdin, output: process.stdout });
    if (!opts.displayName) displayName = await prompt(rl, 'Display name', displayName);
    if (!opts.description) description = await prompt(rl, 'Description', `${displayName} module.`);
    if (!opts.owner) owner = await prompt(rl, 'Owner email', owner);
    rl.close();
  }
  description ??= `${displayName} module.`;

  if (!/^[A-Za-z0-9][A-Za-z0-9 ._()-]{2,99}$/.test(displayName))
    fail('display name must be 3-100 chars: letters, digits, space . _ ( ) -');
  if (/[\r\n]/.test(description) || description.length > 300) fail('description must be one line, <= 300 chars');
  if (!/^[^\s@]+@[^\s@]+$/.test(owner)) fail(`invalid owner email "${owner}"`);
  const category = opts.category ?? 'ops';
  const icon = opts.icon ?? 'box';
  for (const [k, v] of [['category', category], ['icon', icon]]) if (!/^[a-z0-9-]+$/.test(v)) fail(`--${k} must match ^[a-z0-9-]+$`);

  const perm = opts.permPrefix ?? toPermPrefix(name);
  if (!/^[A-Z]+(_[A-Z]+)*$/.test(perm)) fail(`perm prefix "${perm}" must be UPPER_SNAKE letters only (no digits)`);

  const scope = `${name.replace(/-/g, '_')}_remote`;
  const scopes = usedScopes(root);
  if (scopes.has(scope)) fail(`federation scope "${scope}" already used by ${scopes.get(scope)}`);

  const used = usedPorts(root);
  const port = (flag, start) => {
    if (opts[flag] === undefined) return nextFree(used, start);
    const p = Number(opts[flag]);
    const label = flag.replace(/[A-Z]/g, (c) => `-${c.toLowerCase()}`);
    if (!Number.isInteger(p) || p < 1024 || p > 65535) fail(`--${label} must be 1024-65535`);
    if (used.has(p)) fail(`port ${p} is already used in this repo`);
    return p;
  };
  const apiPort = port('apiPort', API_PORT_START);
  used.add(apiPort);
  const webPort = port('webPort', WEB_PORT_START);
  const menuOrder = Number(opts.menuOrder ?? 50);
  if (!Number.isInteger(menuOrder)) fail('--menu-order must be an integer');

  const tokens = {
    MODULE_NAME: name, PASCAL: pascal, CAMEL: toCamel(name), DISPLAY_NAME: displayName,
    DESCRIPTION: description, DESCRIPTION_YAML: JSON.stringify(description), OWNER: owner,
    CATEGORY: category, ICON: icon, API_PORT: String(apiPort), WEB_PORT: String(webPort),
    SCOPE: scope, PERM: perm, CSS: `mod-${name}`, MENU_ORDER: String(menuOrder),
  };
  const files = plan(root, tokens);
  const clash = files.find((f) => exists(f.to));
  if (clash) fail(`${rel(root, clash.to)} already exists`);

  log(`Module      : ${name} (${displayName})`);
  log(`Project     : ${pascal}.Api   scope: ${scope}   permissions: ${perm}_DATA_VIEW, ${perm}_DATA_PROTECTED`);
  log(`Ports       : API ${apiPort}, remote ${webPort}`);
  log(`Files (${files.length}):`);
  for (const f of files) log(`  + ${rel(root, f.to)}`);
  if (opts.dryRun) {
    log(opts.noWire ? '\n(dry run, no wiring)' : '\nWould wire: ModulePlatform.sln, AppHost csproj + Program.cs, docker-compose.yml, SharedKernel.Tests.csproj');
    return;
  }

  for (const f of files) {
    fs.mkdirSync(path.dirname(f.to), { recursive: true });
    fs.writeFileSync(f.to, render(read(f.from), tokens));
  }
  log('\nFiles written.');

  if (!opts.noWire) {
    try { for (const s of wire(root, tokens)) log(`  ~ ${s}`); }
    catch (e) { log(`  ! wiring stopped: ${e.message} (finish manually, see docs/module-developer-guide.md)`); }
  }

  if (!opts.skipInstall) {
    log(`\nnpm install in modules/${name}/web ...`);
    if (!run('npm', ['install', '--no-audit', '--no-fund'], path.join(root, 'modules', name, 'web')).ok)
      log('  ! npm install failed — run it manually.');
  }

  log(`
Done. Next steps (from module_as_plugin/):
  dotnet build ModulePlatform.sln && dotnet test ModulePlatform.sln
  dotnet run --project src/Aspire/ModulePlatform.AppHost          # or standalone:
  dotnet run --project src/Modules/${pascal}/${pascal}.Api            # :${apiPort}
  (cd modules/${name}/web && npm run serve)                          # :${webPort}
  ${CLI} publish ${name} --password <pw>
  ${CLI} install ${name} --tenant <TENANT_ID> --password <pw>
Then open Tenant Admin http://localhost:5175 -> Home -> ${displayName}.`);
}

function cmdList(root) {
  const modulesDir = path.join(root, 'modules');
  const dirs = exists(modulesDir) ? fs.readdirSync(modulesDir, { withFileTypes: true }).filter((d) => d.isDirectory()) : [];
  const rows = [['NAME', 'VERSION', 'SCOPE', 'API', 'REMOTE']];
  for (const d of dirs) {
    const mf = path.join(modulesDir, d.name, 'manifest.yml');
    if (!exists(mf)) continue;
    const m = read(mf);
    const name = m.match(/^\s*name:\s*(\S+)/m)?.[1] ?? d.name;
    rows.push([
      name, m.match(/^\s*version:\s*(\S+)/m)?.[1] ?? '-', m.match(/^\s*scope:\s*(\S+)/m)?.[1] ?? '-',
      apiPortOf(moduleApiDir(root, name)) ?? '-', m.match(/remoteUrl:\s*\S+?:(\d+)\//)?.[1] ?? '-',
    ]);
  }
  const w = rows[0].map((_, i) => Math.max(...rows.map((r) => String(r[i]).length)));
  for (const r of rows) log(r.map((c, i) => String(c).padEnd(w[i])).join('  '));
}

async function api(method, url, body, token) {
  let res;
  try {
    res = await fetch(url, {
      method, headers: { 'Content-Type': 'application/json', ...(token ? { Authorization: `Bearer ${token}` } : {}) },
      body: body === undefined ? undefined : JSON.stringify(body),
    });
  } catch (e) { fail(`cannot reach ${url} (${e.cause?.code ?? e.message}). Is the gateway running?`); }
  const text = await res.text();
  let json; try { json = JSON.parse(text); } catch { json = text; }
  return { status: res.status, ok: res.ok, json };
}

async function token(gateway, opts) {
  const t = opts.token ?? process.env.PLATFORM_TOKEN;
  if (t) return t;
  const password = opts.password ?? process.env.PLATFORM_PASSWORD;
  if (!password) fail('provide --token (or PLATFORM_TOKEN) or --password (or PLATFORM_PASSWORD) for a platform Admin');
  const r = await api('POST', `${gateway}/api/v1/platform/auth/login`, { email: opts.email ?? 'admin@local', password });
  if (!r.ok) fail(`login failed (${r.status}): ${JSON.stringify(r.json)}`);
  return r.json.data.accessToken;
}

function report(label, r) {
  log(`${label}: HTTP ${r.status}`);
  log(typeof r.json === 'string' ? r.json : JSON.stringify(r.json, null, 2));
  if (!r.ok) process.exit(1);
}

const gatewayUrl = (opts) => (opts.gateway ?? process.env.MODULE_GATEWAY ?? 'http://localhost:5100').replace(/\/$/, '');

async function cmdPublish(root, opts) {
  const name = opts._[1];
  if (!name) fail('usage: publish <name>');
  const mf = path.join(root, 'modules', name, 'manifest.yml');
  if (!exists(mf)) fail(`modules/${name}/manifest.yml not found`);
  const port = apiPortOf(moduleApiDir(root, name));
  const apiUrl = opts.apiUrl ?? (port && `http://localhost:${port}`);
  if (!apiUrl) fail('cannot find the module API port; pass --api-url');
  const gateway = gatewayUrl(opts);
  const jwt = await token(gateway, opts);
  report('publish', await api('POST', `${gateway}/api/v1/platform/modules`, { manifestYaml: read(mf), artifactApiUrl: apiUrl }, jwt));
}

async function cmdInstall(root, opts) {
  const name = opts._[1];
  if (!name || !opts.tenant) fail('usage: install <name> --tenant <id> [--version x.y.z]');
  const gateway = gatewayUrl(opts);
  const jwt = await token(gateway, opts);
  report('install', await api('POST',
    `${gateway}/api/v1/platform/tenants/${encodeURIComponent(opts.tenant)}/modules/${encodeURIComponent(name)}/install`,
    opts.version ? { version: opts.version } : {}, jwt));
}

async function cmdRemove(root, opts) {
  const name = opts._[1];
  if (!name || !/^[a-z][a-z0-9-]{2,49}$/.test(name)) fail('usage: remove <name>');
  const pascal = toPascal(name);
  const targets = [
    path.join(root, 'modules', name),
    path.join(root, 'src', 'Modules', pascal),
    path.join(root, 'tests', 'SharedKernel.Tests', 'Modules', `${pascal}ModuleTests.cs`),
  ].filter(exists);
  log(`This deletes:\n${targets.map((t) => `  - ${rel(root, t)}`).join('\n') || '  (no files)'}\nand removes ${name} from the solution, AppHost, docker-compose.yml and SharedKernel.Tests.csproj.`);
  if (!opts.yes) {
    if (!process.stdin.isTTY) fail('pass -y to confirm in non-interactive mode');
    const rl = readline.createInterface({ input: process.stdin, output: process.stdout });
    const ok = (await rl.question(`Type the module name (${name}) to confirm: `)).trim() === name;
    rl.close();
    if (!ok) fail('aborted');
  }
  const csprojRel = path.join('src', 'Modules', pascal, `${pascal}.Api`, `${pascal}.Api.csproj`);
  run('dotnet', ['sln', 'ModulePlatform.sln', 'remove', csprojRel], root, { inherit: false });
  removeSolutionFolder(path.join(root, 'ModulePlatform.sln'), pascal);
  const appHost = path.join(root, 'src', 'Aspire', 'ModulePlatform.AppHost');
  removeLine(path.join(appHost, 'ModulePlatform.AppHost.csproj'), `\\Modules\\${pascal}\\${pascal}.Api\\`);
  removeBlock(path.join(appHost, 'Program.cs'), name, '//');
  removeBlock(path.join(root, 'docker-compose.yml'), name, '#');
  removeLine(path.join(root, 'tests', 'SharedKernel.Tests', 'SharedKernel.Tests.csproj'), `\\Modules\\${pascal}\\${pascal}.Api\\`);
  for (const t of targets) fs.rmSync(t, { recursive: true, force: true });
  log(`Removed ${name}. If it was published/installed, uninstall it from tenants via the platform.`);
}

// `dotnet sln remove` leaves the empty solution folder behind; drop it if it has no children.
function removeSolutionFolder(slnPath, folder) {
  let sln = read(slnPath);
  const re = new RegExp(`Project\\("\\{2150E333-8FDC-42A3-9474-1A3956D46DE8\\}"\\) = "${folder}", "${folder}", "\\{([0-9A-Fa-f-]+)\\}"\\r?\\nEndProject\\r?\\n`);
  const m = sln.match(re);
  if (!m) return;
  const guid = m[1];
  if (new RegExp(`\\{[0-9A-Fa-f-]+\\} = \\{${guid}\\}`).test(sln)) return;
  sln = sln.replace(re, '').split('\n').filter((l) => !(l.includes(`{${guid}}`) && l.includes(' = '))).join('\n');
  fs.writeFileSync(slnPath, sln);
}

function cmdPack(root, opts) {
  const out = path.resolve(opts.out ?? path.join(root, 'dist', 'create-module-kit.zip'));
  fs.mkdirSync(path.dirname(out), { recursive: true });
  if (exists(out)) fs.rmSync(out);
  const parent = path.dirname(TOOL_DIR);
  const base = path.basename(TOOL_DIR);
  const r = process.platform === 'win32'
    ? run('tar', ['-a', '-c', '-f', out, '--exclude=node_modules', base], parent)
    : run('zip', ['-rq', out, base, '-x', '*/node_modules/*', '*.DS_Store', '*/obj/*', '*/bin/*'], parent);
  if (!r.ok) fail('zip failed (needs `zip` on macOS/Linux or `tar` on Windows)');
  log(`Packed ${rel(process.cwd(), out)}`);
  log(`Unzip into module_as_plugin/tools/ of a platform checkout, then run: ${CLI} help`);
}

// ---------- main ----------
const opts = parseArgs(process.argv.slice(2));
const command = opts._[0];
if (!command || command === 'help' || opts.help) { log(HELP); process.exit(0); }
if (Number(process.versions.node.split('.')[0]) < 18) fail('Node.js >= 18 is required');
const root = findRoot();
const commands = { new: cmdNew, list: cmdList, publish: cmdPublish, install: cmdInstall, remove: cmdRemove, pack: cmdPack };
if (!commands[command]) fail(`unknown command "${command}". Run with "help".`);
await commands[command](root, opts);
