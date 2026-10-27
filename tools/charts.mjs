// Renders the benchmark JSON written by tools/compare.mjs --save into SVG bar charts.
// Plain SVG, no dependencies, no fonts to install: GitHub renders these inline from the README.
//
// Usage:
//   node tools/compare.mjs --save tools/bench-results.json
//   node tools/charts.mjs tools/bench-results.json docs
import { readFileSync, mkdirSync, writeFileSync } from "node:fs";
import { dirname, join } from "node:path";

const input = process.argv[2] ?? "tools/bench-results.json";
const outDir = process.argv[3] ?? "docs";

const data = JSON.parse(readFileSync(input, "utf8"));
const servers = data.servers.filter((s) => !s.failed);

mkdirSync(outDir, { recursive: true });

const W = 760;
const LABEL_W = 260;
const BAR_H = 30;
const GAP = 16;
const PAD = 24;
const HEADER = 58;
const FOOTER = 34;

const escape = (text) =>
  String(text).replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;");

// Short labels: the full package names do not fit beside the bars, and the README table spells them out.
const SHORT = {
  "es-mcp": "es-mcp (this repo)",
  mamertofabian: "mamertofabian (Python)",
  elis132: "elis132 (Python)",
};
const shortLabel = (server) => SHORT[server.name] ?? server.name;

function chart({ title, subtitle, rows, unit, suffix = "", better = "less" }) {
  const height = HEADER + rows.length * (BAR_H + GAP) + FOOTER;
  const max = Math.max(...rows.map((r) => r.value), 1);
  const barMax = W - LABEL_W - PAD - 90;

  const bars = rows
    .map((row, index) => {
      const y = HEADER + index * (BAR_H + GAP);
      const width = Math.max(3, Math.round((row.value / max) * barMax));
      const color = row.ours ? "#1a7f37" : "#8c959f";
      const value = `${row.value.toLocaleString("en-US")}${suffix}`;
      return [
        `<text x="${PAD}" y="${y + BAR_H / 2 + 5}" font-size="14" fill="#24292f">${escape(row.label)}</text>`,
        `<rect x="${LABEL_W}" y="${y}" width="${barMax}" height="${BAR_H}" rx="4" fill="#eef1f4"/>`,
        `<rect x="${LABEL_W}" y="${y}" width="${width}" height="${BAR_H}" rx="4" fill="${color}"/>`,
        `<text x="${LABEL_W + width + 8}" y="${y + BAR_H / 2 + 5}" font-size="13" font-weight="600" fill="#24292f">${value}</text>`,
      ].join("");
    })
    .join("");

  return `<svg xmlns="http://www.w3.org/2000/svg" width="${W}" height="${height}" viewBox="0 0 ${W} ${height}" role="img" aria-label="${escape(title)}">
<rect width="${W}" height="${height}" fill="#ffffff"/>
<text x="${PAD}" y="30" font-size="17" font-weight="600" fill="#111827">${escape(title)}</text>
<text x="${PAD}" y="48" font-size="12.5" fill="#57606a">${escape(subtitle)}</text>
${bars}
<text x="${PAD}" y="${height - 12}" font-size="11.5" fill="#57606a">${escape(`median of ${data.rounds} runs x ${data.iterations} calls, ${data.queries.length} queries, limit ${data.limit} - shorter bars are better`)}</text>
</svg>
`;
}

const tokenRows = servers
  .filter((s) => typeof s.tokensPerCall === "number")
  .sort((a, b) => a.tokensPerCall - b.tokensPerCall)
  .map((s) => ({ label: shortLabel(s), value: s.tokensPerCall, ours: s.name === "es-mcp" }));

const firstResultRows = servers
  .slice()
  .sort((a, b) => a.firstResult - b.firstResult)
  .map((s) => ({ label: shortLabel(s), value: s.firstResult, ours: s.name === "es-mcp" }));

for (const [name, svg] of [
  [
    "bench-tokens.svg",
    chart({
      title: "Tokens per search reply",
      subtitle: "What one 30-row reply puts into the model's context, default settings",
      rows: tokenRows,
      suffix: "",
    }),
  ],
  [
    "bench-latency.svg",
    chart({
      title: "Time to first result",
      subtitle: "Process start, MCP handshake, then the first search reply",
      rows: firstResultRows,
      suffix: " ms",
    }),
  ],
]) {
  writeFileSync(join(outDir, name), svg);
  process.stderr.write(`wrote ${join(outDir, name)}\n`);
}

const summary = {
  generatedAt: data.generatedAt,
  machine: data.machine,
  servers: servers.map((s) => ({
    label: s.label,
    tokensPerCall: s.tokensPerCall,
    coldStart: s.coldStart,
    firstResult: s.firstResult,
    p50: s.p50,
    p95: s.p95,
  })),
};

writeFileSync(join(outDir, "bench-summary.json"), JSON.stringify(summary, null, 2) + "\n");
process.stderr.write(`wrote ${join(outDir, "bench-summary.json")}\n`);
