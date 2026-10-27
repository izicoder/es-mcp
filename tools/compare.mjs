// Compares MCP file-search servers on the same machine, same Everything index.
//
// Measures, per server:
//   cold start   - spawn -> initialize response
//   first result - spawn -> first tools/call result (what an agent actually waits for)
//   warm p50/p95 - repeated tools/call round trip on identical queries
//
// Each round starts a fresh process; the reported number is the median across rounds.
// No dependencies, read-only queries.
//
// Usage:
//   node tools/compare.mjs                    every server that is present
//   node tools/compare.mjs --rounds 5 --iterations 40
//   node tools/compare.mjs --only es-mcp
//   node tools/compare.mjs --inspect [--schema]
//
// Environment:
//   ES_EXE                  path to es.exe (used by es.exe based servers)
//   EVERYTHING_SDK_PATH     path to Everything64.dll (used by SDK based servers)
import { spawn } from "node:child_process";
import { existsSync, writeFileSync } from "node:fs";
import { createInterface } from "node:readline";
import { cpus, totalmem } from "node:os";

const argv = process.argv.slice(2);
const flag = (name, fallback) => {
  const i = argv.indexOf("--" + name);
  return i === -1 ? fallback : argv[i + 1];
};
const iterations = Number(flag("iterations", 20));
const rounds = Number(flag("rounds", 3));
const only = flag("only", null);
const savePath = flag("save", null);
const inspect = argv.includes("--inspect");

const COMPARE = "C:\\Users\\obolo\\AppData\\Local\\Temp\\opencode\\mcpbench";
const PY = COMPARE + "\\venv\\Scripts\\python.exe";
const ES_EXE = process.env.ES_EXE ?? COMPARE + "\\es.exe";
const SDK = process.env.EVERYTHING_SDK_PATH ?? "";
const LIMIT = 30;

// Same query text for every server. Everything syntax; es.exe understands it too.
const QUERIES = [
  { label: "name fragment", query: "README" },
  { label: "extension filter", query: "ext:cs" },
  { label: "wildcard", query: "*.sln" },
  { label: "path filter", query: "path:node_modules ext:json" },
  { label: "size filter", query: "size:>1gb" },
  { label: "date filter", query: "dm:today" },
];

const SERVERS = [
  {
    name: "es-mcp",
    label: "es-mcp *(this repo)*",
    command: "out\\es-mcp\\es-mcp.exe",
    args: [],
    env: {},
    call: (query) => ({ name: "search_files", arguments: { query, limit: LIMIT } }),
  },
  {
    name: "mamertofabian",
    label: "mamertofabian/mcp-everything-search *(Python, ctypes SDK)*",
    command: PY,
    args: ["-m", "mcp_server_everything_search"],
    env: { EVERYTHING_SDK_PATH: SDK },
    call: (query) => ({ name: "search", arguments: { base: { query, max_results: LIMIT } } }),
  },
  {
    name: "elis132",
    label: "elis132/everything-mcp *(Python, es.exe per call)*",
    command: PY,
    args: ["-m", "everything_mcp"],
    env: { ES_PATH: ES_EXE, PATH: COMPARE + ";" + process.env.PATH },
    call: (query) => ({ name: "everything_search", arguments: { params: { query, max_results: LIMIT } } }),
  },
];

function connect(server) {
  const started = performance.now();
  const child = spawn(server.command, server.args, {
    stdio: ["pipe", "pipe", "pipe"],
    env: { ...process.env, ...server.env },
    windowsHide: true,
  });
  const pending = new Map();
  let nextId = 1;
  let stderr = "";
  let exited = false;

  child.on("error", (error) => {
    exited = true;
    stderr += String(error);
    for (const [, entry] of pending) entry.reject(new Error("spawn failed: " + error.message));
    pending.clear();
  });
  child.on("exit", () => {
    exited = true;
    for (const [, entry] of pending) entry.reject(new Error("server exited: " + stderr.trim().slice(-300)));
    pending.clear();
  });
  child.stderr.on("data", (chunk) => {
    stderr += chunk.toString();
  });
  createInterface({ input: child.stdout }).on("line", (line) => {
    let message;
    try {
      message = JSON.parse(line);
    } catch {
      return;
    }
    const entry = pending.get(message.id);
    if (!entry) return;
    pending.delete(message.id);
    if (message.error) entry.reject(new Error(message.error.message));
    else entry.resolve(message);
  });

  const send = (method, params, timeoutMs = 60000) => {
    const id = nextId++;
    const sent = performance.now();
    return new Promise((resolve, reject) => {
      const timer = setTimeout(() => {
        pending.delete(id);
        reject(new Error(`${method} timed out after ${timeoutMs}ms`));
      }, timeoutMs);
      pending.set(id, {
        resolve: (m) => {
          clearTimeout(timer);
          resolve({ message: m, ms: performance.now() - sent });
        },
        reject: (e) => {
          clearTimeout(timer);
          reject(e);
        },
      });
      if (exited) return reject(new Error("server exited before " + method));
      child.stdin.write(JSON.stringify({ jsonrpc: "2.0", id, method, params }) + "\n");
    });
  };

  return { child, send, started };
}

async function inspectServer(server) {
  const session = connect(server);
  try {
    await session.send("initialize", {
      protocolVersion: "2025-06-18",
      capabilities: {},
      clientInfo: { name: "compare", version: "0" },
    });
    const { message } = await session.send("tools/list", {});
    const tools = message.result.tools;
    if (argv.includes("--schema")) return tools.map((t) => ({ name: t.name, schema: t.inputSchema }));
    return tools;
  } finally {
    session.child.kill();
  }
}

async function round(server) {
  const session = connect(server);
  try {
    await session.send("initialize", {
      protocolVersion: "2025-06-18",
      capabilities: {},
      clientInfo: { name: "compare", version: "0" },
    });
    const coldStart = performance.now() - session.started;
    await session.send("tools/list", {});

    await session.send("tools/call", server.call(QUERIES[0].query));
    const firstResult = performance.now() - session.started;

    const perQuery = [];
    for (const { label, query } of QUERIES) {
      const samples = [];
      for (let i = 0; i < iterations; i++) {
        const { ms } = await session.send("tools/call", server.call(query));
        samples.push(ms);
      }
      samples.sort((a, b) => a - b);
      const at = (p) => samples[Math.min(samples.length - 1, Math.floor(p * samples.length))];
      perQuery.push({ label, query, p50: at(0.5), p95: at(0.95) });
    }
    return { coldStart, firstResult, perQuery };
  } finally {
    session.child.kill();
  }
}

async function measure(server) {
  const runs = [];
  for (let i = 0; i < rounds; i++) runs.push(await round(server));
  const nil = (values) => values.filter((v) => typeof v === "number").sort((a, b) => a - b);
  const median = (values) => {
    const sorted = nil(values);
    return sorted.length ? sorted[Math.floor(sorted.length / 2)] : null;
  };
  return {
    coldStart: median(runs.map((r) => r.coldStart)),
    firstResult: median(runs.map((r) => r.firstResult)),
    queries: QUERIES.map((q, i) => ({
      label: q.label,
      query: q.query,
      p50: median(runs.map((r) => r.perQuery[i].p50)),
      p95: median(runs.map((r) => r.perQuery[i].p95)),
    })),
    spread: {
      coldStart: Math.max(...runs.map((r) => r.coldStart)) - Math.min(...runs.map((r) => r.coldStart)),
    },
  };
}

const present = SERVERS.filter((s) => (only ? s.name === only : true) && existsSync(s.command));

if (!present.length) {
  console.error("No servers found. Publish es-mcp first: dotnet publish src/EsMcp -c Release -o out/es-mcp");
  process.exit(1);
}

if (inspect) {
  for (const server of present) {
    try {
      const tools = await inspectServer(server);
      console.log("\n=== " + server.label.replace(/\*/g, ""));
      if (argv.includes("--schema")) {
        console.log(JSON.stringify(tools, null, 1));
        continue;
      }
      for (const tool of tools) {
        const props = Object.keys(tool.inputSchema?.properties ?? {});
        console.log(`  ${tool.name}(${props.join(", ")}) - ${(tool.description ?? "").slice(0, 80)}`);
      }
    } catch (error) {
      console.log("\n=== " + server.label.replace(/\*/g, "") + " FAILED: " + error.message);
    }
  }
  process.exit(0);
}

// Payload size, the other half of the cost: what each call puts into the model's context.
// Tokens are estimated as characters / 4 and labelled as such in the output.
async function measureTokens(server, queries) {
  const session = connect(server);
  try {
    await session.send("initialize", {
      protocolVersion: "2025-06-18",
      capabilities: {},
      clientInfo: { name: "compare", version: "0" },
    });
    await session.send("tools/list", {});
    const rows = [];
    for (const { label, query } of queries) {
      const { message } = await session.send("tools/call", server.call(query));
      const text = JSON.stringify(message.result?.content ?? "");
      const structured = JSON.stringify(message.result?.structuredContent ?? "");
      rows.push({
        label,
        query,
        textChars: text.length,
        structuredChars: message.result?.structuredContent ? structured.length : 0,
        totalChars: text.length + (message.result?.structuredContent ? structured.length : 0),
      });
    }
    return rows;
  } finally {
    session.child.kill();
  }
}

const tokenQueries = QUERIES.map((q) => ({ ...q, query: q.query }));
const mean = (values) => (values.length ? values.reduce((a, b) => a + b, 0) / values.length : 0);
if (argv.includes("--tokens")) {
  console.log(`\nPayload per call at limit ${LIMIT}; tokens estimated as characters / 4.\n`);
  console.log("| Server | Content chars | structuredContent chars | Total chars | ~Tokens |");
  console.log("| --- | --- | --- | --- | --- |");
  const perServer = [];
  for (const server of present) {
    try {
      const rows = await measureTokens(server, tokenQueries);
      const textChars = mean(rows.map((r) => r.textChars));
      const structuredChars = mean(rows.map((r) => r.structuredChars));
      const totalChars = mean(rows.map((r) => r.totalChars));
      perServer.push({ server, rows, textChars, structuredChars, totalChars });
      console.log(
        `| ${server.label} | ${Math.round(textChars)} | ${Math.round(structuredChars)} | ${Math.round(totalChars)} | ${Math.round(totalChars / 4)} |`,
      );
    } catch (error) {
      console.log(`| ${server.label} | failed: ${error.message.replace(/\|/g, "/")} | | | |`);
    }
  }
  for (const { server, rows } of perServer) {
    console.log(`\n### ${server.label.replace(/\*/g, "")}`);
    console.log("| Query | Content | structuredContent | ~Tokens |");
    console.log("| --- | --- | --- | --- |");
    for (const row of rows) {
      console.log(
        `| \`${row.query}\` | ${row.textChars} | ${row.structuredChars} | ${Math.round(row.totalChars / 4)} |`,
      );
    }
  }
  process.exit(0);
}

const results = [];
for (const server of present) {
  process.stderr.write(`measuring ${server.name} (${rounds} rounds x ${iterations} calls)\n`);
  try {
    results.push({ server, data: await measure(server) });
  } catch (error) {
    results.push({ server, error: error.message });
    process.stderr.write("  failed: " + error.message + "\n");
  }
}

const fmt = (ms) => (ms === null ? "n/a" : ms < 10 ? ms.toFixed(1) + " ms" : Math.round(ms) + " ms");
const avg = (values) => values.reduce((a, b) => a + b, 0) / values.length;

console.log(`\n${rounds} rounds x ${iterations} calls per query, ${QUERIES.length} queries, limit ${LIMIT}.\n`);
console.log("| Server | Cold start | First result | Warm p50 | Warm p95 |");
console.log("| --- | --- | --- | --- | --- |");
for (const { server, data, error } of results) {
  if (error) {
    console.log(`| ${server.label} | failed | | | |`);
    continue;
  }
  const p50 = avg(data.queries.map((q) => q.p50));
  const p95 = avg(data.queries.map((q) => q.p95));
  console.log(`| ${server.label} | ${fmt(data.coldStart)} | ${fmt(data.firstResult)} | ${fmt(p50)} | ${fmt(p95)} |`);
}

for (const { server, data } of results) {
  if (!data) continue;
  console.log(`\n### ${server.label.replace(/\*/g, "")}`);
  console.log("| Query | p50 | p95 |");
  console.log("| --- | --- | --- |");
  for (const row of data.queries) {
    console.log(`| \`${row.query}\` | ${fmt(row.p50)} | ${fmt(row.p95)} |`);
  }
}

if (savePath) {
  const servers = [];
  for (const { server, data, error } of results) {
    const label = server.label.replace(/\*/g, "").trim();
    if (error) {
      servers.push({ name: server.name, label, failed: error });
      continue;
    }

    // The same calls, measured by payload size instead of time.
    let tokens = null;
    try {
      const rows = await measureTokens(server, QUERIES);
      tokens = Math.round(mean(rows.map((r) => r.totalChars)) / 4);
    } catch {
      // A server that cannot answer twice is already reported above.
      tokens = null;
    }

    servers.push({
      name: server.name,
      label,
      coldStart: Math.round(data.coldStart),
      firstResult: Math.round(data.firstResult),
      p50: Number(avg(data.queries.map((q) => q.p50)).toFixed(1)),
      p95: Number(avg(data.queries.map((q) => q.p95)).toFixed(1)),
      tokensPerCall: tokens,
    });
  }

  const document = {
    generatedAt: new Date().toISOString(),
    rounds,
    iterations,
    limit: LIMIT,
    queries: QUERIES.map((q) => q.query),
    machine: {
      cpu: cpus()[0]?.model?.trim() ?? "unknown",
      memoryGb: Math.round(totalmem() / 1024 ** 3),
      os: `${process.platform} ${process.arch}`,
    },
    servers,
  };

  writeFileSync(savePath, JSON.stringify(document, null, 2) + "\n");
  process.stderr.write(`wrote ${savePath}\n`);
}
