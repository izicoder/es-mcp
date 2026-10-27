// Renders the benchmark JSON written by tools/compare.mjs --save into SVG bar charts.
// Plain SVG, no dependencies: GitHub renders these inline from the README.
// Two themes per chart, because GitHub picks one by the reader's colour scheme:
// docs/bench-tokens.svg (light) and docs/bench-tokens-dark.svg (dark).
//
// Usage:
//   node tools/compare.mjs --save tools/bench-results.json
//   node tools/charts.mjs tools/bench-results.json docs
import { readFileSync, mkdirSync, writeFileSync } from "node:fs";
import { join } from "node:path";

const input = process.argv[2] ?? "tools/bench-results.json";
const outDir = process.argv[3] ?? "docs";

const data = JSON.parse(readFileSync(input, "utf8"));
const servers = data.servers.filter((s) => !s.failed);

mkdirSync(outDir, { recursive: true });

const W = 760;
const PAD = 26;
const LABEL_W = 190;
const BAR_X = PAD + LABEL_W + 18;
const BAR_W = 384;
const BAR_H = 32;
const GAP = 18;
const TOP = 86;
const FOOTER = 42;
const FONT = "ui-sans-serif, -apple-system, 'Segoe UI', Helvetica, Arial, sans-serif";

const THEMES = {
  light: {
    bg: "#ffffff",
    border: "#d0d7de",
    title: "#111827",
    subtitle: "#57606a",
    label: "#24292f",
    value: "#24292f",
    track: "#eef1f4",
    ours: "#1a7f37",
    other: "#8c959f",
  },
  dark: {
    bg: "#0d1117",
    border: "#30363d",
    title: "#e6edf3",
    subtitle: "#8b949e",
    label: "#c9d1d9",
    value: "#e6edf3",
    track: "#21262d",
    ours: "#3fb950",
    other: "#6e7681",
  },
};

const escape = (text) =>
  String(text).replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;");

// Short labels: full package names do not fit beside the bars, and the README table spells them out.
const SHORT = {
  "es-mcp": "es-mcp (this repo)",
  mamertofabian: "mamertofabian (Python)",
  elis132: "elis132 (Python)",
};
const shortLabel = (server) => SHORT[server.name] ?? server.name;

function chart({ title, subtitle, rows, suffix = "", theme }) {
  const height = TOP + rows.length * (BAR_H + GAP) - GAP + FOOTER;
  const max = Math.max(...rows.map((r) => r.value), 1);

  const bars = rows
    .map((row, index) => {
      const y = TOP + index * (BAR_H + GAP);
      const width = Math.max(4, Math.round((row.value / max) * BAR_W));
      const color = row.ours ? theme.ours : theme.other;
      const weight = row.ours ? 600 : 500;
      const value = `${row.value.toLocaleString("en-US")}${suffix}`;
      const mid = y + BAR_H / 2 + 5;
      return [
        `<text x="${PAD}" y="${mid}" font-family="${FONT}" font-size="14" fill="${theme.label}">${escape(row.label)}</text>`,
        `<rect x="${BAR_X}" y="${y}" width="${BAR_W}" height="${BAR_H}" rx="6" fill="${theme.track}"/>`,
        `<rect x="${BAR_X}" y="${y}" width="${width}" height="${BAR_H}" rx="6" fill="${color}"/>`,
        `<text x="${BAR_X + BAR_W + 12}" y="${mid}" font-family="${FONT}" font-size="14" font-weight="${weight}" fill="${theme.value}">${value}</text>`,
      ].join("");
    })
    .join("");

  const note = `median of ${data.rounds} runs x ${data.iterations} calls  ·  ${data.queries.length} queries  ·  limit ${data.limit}  ·  shorter is better`;

  return `<svg xmlns="http://www.w3.org/2000/svg" width="${W}" height="${height}" viewBox="0 0 ${W} ${height}" role="img" aria-label="${escape(title)}: ${escape(rows.map((r) => `${r.label} ${r.value}${suffix}`).join(", "))}">
<rect x="0.5" y="0.5" width="${W - 1}" height="${height - 1}" rx="10" fill="${theme.bg}" stroke="${theme.border}"/>
<text x="${PAD}" y="38" font-family="${FONT}" font-size="18" font-weight="600" fill="${theme.title}">${escape(title)}</text>
<text x="${PAD}" y="61" font-family="${FONT}" font-size="13" fill="${theme.subtitle}">${escape(subtitle)}</text>
${bars}
<text x="${PAD}" y="${height - 16}" font-family="${FONT}" font-size="12" fill="${theme.subtitle}">${escape(note)}</text>
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

const charts = [
  {
    name: "bench-tokens",
    title: "Tokens per search reply",
    subtitle: "What one 30-row reply puts into the model's context, default settings",
    rows: tokenRows,
    suffix: "",
  },
  {
    name: "bench-latency",
    title: "Time to first result",
    subtitle: "Process start, MCP handshake, then the first search reply",
    rows: firstResultRows,
    suffix: " ms",
  },
];

for (const spec of charts) {
  for (const [themeName, theme] of Object.entries(THEMES)) {
    const file = join(outDir, themeName === "dark" ? `${spec.name}-dark.svg` : `${spec.name}.svg`);
    writeFileSync(file, chart({ ...spec, theme }));
    process.stderr.write(`wrote ${file}\n`);
  }
}

writeFileSync(
  join(outDir, "bench-summary.json"),
  JSON.stringify(
    {
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
    },
    null,
    2,
  ) + "\n",
);
process.stderr.write(`wrote ${join(outDir, "bench-summary.json")}\n`);
