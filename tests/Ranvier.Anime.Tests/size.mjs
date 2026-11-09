// Bundle-size check of the bindings: the Fixtures.fs `sized` entry against a hand-written twin calling anime directly,
// both bundled with rolldown (minified) and compressed with gzip level 9. Exits 1 when the gzip delta exceeds the
// budget. Writes docs/research/motion/bindings-bundle.json.
import { rolldown } from "rolldown";
import { mkdirSync, writeFileSync } from "node:fs";
import { gzipSync } from "node:zlib";

const here = new URL("./", import.meta.url);
const output = new URL("../../docs/research/motion/bindings-bundle.json", here);
const entries = {
  bindings: 'export { sized } from "../generated/Fixtures.js";\n',
  handWritten: 'import { animate } from "animejs";\nexport function sized(el) { return animate(el, { x: 100, opacity: [0, 1], duration: 400, ease: "outQuart" }); }\n'
};
const result = {};
mkdirSync(new URL("generated-size/", here), { recursive: true });
for (const [name, source] of Object.entries(entries)) {
  const input = new URL(`generated-size/${name}.mjs`, here);
  writeFileSync(input, source);
  const bundle = await rolldown({ input: input.pathname.replace(/^\/([A-Za-z]:)/, "$1"), logLevel: "silent" });
  const { output: [chunk] } = await bundle.generate({ format: "esm", minify: true });
  await bundle.close();
  result[name] = { raw: chunk.code.length, gzip: gzipSync(chunk.code, { level: 9 }).length };
}
result.deltaGzip = result.bindings.gzip - result.handWritten.gzip;
result.budgetGzip = 64;
result.withinBudget = result.deltaGzip <= result.budgetGzip;
if (!result.withinBudget) process.exitCode = 1;
mkdirSync(new URL("./", output), { recursive: true });
writeFileSync(output, JSON.stringify(result, null, 2) + "\n");
console.log(JSON.stringify(result, null, 2));
