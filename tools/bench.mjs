// Measures MCP round-trip latency against a running Everything index.
// Usage: node tools/bench.mjs [path-to-es-mcp.exe] [iterations]
// Prints a markdown table; no dependencies, no network, read-only queries.
import { spawn } from "node:child_process";
import { existsSync } from "node:fs";
import { createInterface } from "node:readline";

const exe = process.argv[2] ?? "out/es-mcp/es-mcp.exe";
const iterations = Number(process.argv[3] ?? 20);

if (!existsSync(exe)) {
  console.error(`es-mcp.exe not found at ${exe}. Run: dotnet publish src/EsMcp -c Release -o out/es-mcp`);
  process.exit(1);
}

const queries = [
  { label: "file name fragment", args: { query: "README", limit: 30 } },
  { label: "extension filter", args: { query: "ext:cs", limit: 30 } },
  { label: "wildcard", args: { query: "*.sln", limit: 30 } },
  { label: "full page", args: { query: "ext:exe", limit: 1000 } },
  { label: "size filter", args: { query: "size:>1gb", limit: 30 } },
  { label: "date filter", args: { query: "dm:today", limit: 30 } },
  { label: "regex", args: { query: "re_chunk.*\\.pak$", regex: true, limit: 30 } },
  { label: "large result set, first page", args: { query: "*", sort: "size_desc", limit: 10 } },
];

const child = spawn(exe, [], { stdio: ["pipe", "pipe", "inherit"] });
const rl = createInterface({ input: child.stdout });
const pending = new Map();
let nextId = 1;

rl.on("line", (line) => {
  let message;
  try { message = JSON.parse(line); } catch { return; }
  const resolve = pending.get(message.id);
  if (resolve) { pending.delete(message.id); resolve(message); }
});

function send(method, params) {
  const id = nextId++;
  const payload = { jsonrpc: "2.0", id, method, params };
  const started = performance.now();
  return new Promise((resolve, reject) => {
    pending.set(id, (message) => {
      if (message.error) return reject(new Error(message.error.message));
      resolve({ message, ms: performance.now() - started });
    });
    child.stdin.write(JSON.stringify(payload) + "\n");
  });
}

const total = (message) => {
  const structured = message?.result?.structuredContent;
  if (typeof structured?.total === "number") return structured.total;

  const text = message?.result?.content?.[0]?.text ?? "";
  const match = text.match(/^(\d+) total/);
  return match ? Number(match[1]) : 0;
};

await send("initialize", {});
const warmup = await send("tools/call", { name: "search_files", arguments: queries[0].args });
const indexedTotal = total(await send("tools/call", { name: "search_files", arguments: { query: "*", limit: 1 } }));

const rows = [];
for (const { label, args } of queries) {
  const samples = [];
  let matches = 0;
  for (let i = 0; i < iterations; i++) {
    const { message, ms } = await send("tools/call", { name: "search_files", arguments: args });
    samples.push(ms);
    matches = total(message);
  }
  samples.sort((a, b) => a - b);
  const at = (p) => samples[Math.min(samples.length - 1, Math.floor(p * samples.length))];
  rows.push({
    query: args.regex ? "`regex: " + args.query + "`" : "`" + args.query + "`",
    matches,
    p50: at(0.5),
    p95: at(0.95),
  });
}

child.kill();

const fmt = (ms) => (ms < 10 ? ms.toFixed(1) : Math.round(ms).toString()) + " ms";
console.log("Total indexed by Everything: **" + indexedTotal.toLocaleString("en-US") + "** objects\n");
console.log("| Query | Matches | p50 | p95 |");
console.log("| --- | --- | --- | --- |");
for (const row of rows) {
  console.log(`| ${row.query} | ${row.matches.toLocaleString("en-US")} | ${fmt(row.p50)} | ${fmt(row.p95)} |`);
}
console.log("\n" + iterations + " calls per query, MCP stdio round-trip, warm index. First call after server start: " + fmt(warmup.ms) + ".");
