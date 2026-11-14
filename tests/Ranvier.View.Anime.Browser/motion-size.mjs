// Bundle-size checks of the Motion parts, over the SizeEntries.fs entries bundled with rolldown and compressed with gzip
// level 9 (spec "Bundle budget"):
// - glue: Motion.presence against its twin calling animate through li.motion and li.exit (≤ 900 B), and
//   Motion.flip against its twin calling createLayout through ul.reflow (≤ 1100 B: over the 589 B of anime-only
//   parameters, the shared LayoutOptions record and its conversion to anime layout parameters add about 320 B, and the
//   portable FlipLayout seam between Motion.flip and the engine about 160 B);
// - isolation: an unminified entry using one Motion part declares no anime feature class beyond those its own anime
//   imports bring (AutoLayout, Draggable, ScrollObserver, TextSplitter, WAAPIAnimation, Timeline);
// - helpers: the emitted Ranvier.View.Anime and Fable.Ranvier.Anime modules hold no curry, uncurry, partialApply or
//   createObj.
// Exits 1 on a breach. Writes artifacts/browser/motion-bundle.json.
import { rolldown } from "rolldown";
import { mkdirSync, readFileSync, readdirSync, writeFileSync } from "node:fs";
import { gzipSync } from "node:zlib";

const here = new URL("./", import.meta.url);
const output = new URL("../../artifacts/browser/motion-bundle.json", here);
const pathOf = url => decodeURIComponent(url.pathname).replace(/^\/([A-Za-z]:)/, "$1");
mkdirSync(new URL("generated-size/", here), { recursive: true });

const bundle = async (name, source, minify) => {
  const input = new URL(`generated-size/${name}.mjs`, here);
  writeFileSync(input, source);
  const built = await rolldown({ input: pathOf(input), logLevel: "silent" });
  const { output: [chunk] } = await built.generate({ format: "esm", minify });
  await built.close();
  return chunk.code;
};
const entry = name => `export { ${name} as run } from "../generated/SizeEntries.js";\n`;
const sizeOf = code => ({ raw: code.length, gzip: gzipSync(code, { level: 9 }).length });

const result = { glue: {}, isolation: {}, helpers: {} };
let breach = false;

for (const [part, twin, budgetGzip] of [["presence", "presenceTwin", 900], ["flip", "flipTwin", 1100]]) {
  const motion = sizeOf(await bundle(part, entry(part), true));
  const handWritten = sizeOf(await bundle(twin, entry(twin), true));
  const deltaGzip = motion.gzip - handWritten.gzip;
  const withinBudget = deltaGzip <= budgetGzip;
  breach ||= !withinBudget;
  result.glue[part] = { motion, handWritten, deltaGzip, budgetGzip, withinBudget };
}

const classes = ["AutoLayout", "Draggable", "ScrollObserver", "TextSplitter", "WAAPIAnimation", "Timeline"];
// rolldown declares a bundled class as `class Name` or `var Name = class`.
const declared = code => classes.filter(name => new RegExp(`\\b(class ${name}|${name} = class)\\b`).test(code));
// Each part with the anime functions it calls; the classes those functions bring are the part's own.
const parts = {
  on: ["animate"], appear: ["animate"], enter: ["animate"], exit: ["animate"], presence: ["animate"],
  fade: ["animate"], collapse: ["animate"], flip: ["createLayout"],
  follow: ["createAnimatable"], animateOn: ["animate"], draw: ["createDrawable", "createAnimatable"],
  morph: ["animate", "morphTo"], scroll: ["onScroll", "animate"], drag: ["createDraggable"],
  split: ["splitText", "animate"]
};
const plain = declared(await bundle("plain", entry("plain"), false));
for (const [part, imports] of Object.entries(parts)) {
  const own = declared(await bundle(`own-${part}`, `export { ${imports.join(", ")} } from "animejs";\n`, false));
  const found = declared(await bundle(`isolation-${part}`, entry(part), false));
  const foreign = found.filter(name => !own.includes(name) && !plain.includes(name));
  breach ||= foreign.length > 0;
  result.isolation[part] = { own, found, foreign };
}

const helpers = /\b(curry\d*|uncurry\d*|partialApply|createObj)\b/g;
const generated = new URL("generated/", here);
// Both projects are compiled from source, Fable.Ranvier.Anime with its Portable folder.
const modules = ["src/Ranvier.View.Anime/", "src/Fable.Ranvier.Anime/"].flatMap(folder =>
  readdirSync(new URL(folder, generated), { recursive: true })
    .filter(file => file.endsWith(".js"))
    .map(file => folder + file.replaceAll("\\", "/")));
for (const module of modules) {
  const text = readFileSync(new URL(module, generated), "utf8");
  const calls = [...text.matchAll(helpers)].map(match => match[0]);
  breach ||= calls.length !== 0;
  result.helpers[module] = calls.length;
}

result.withinBudget = !breach;
if (breach) process.exitCode = 1;
mkdirSync(new URL("./", output), { recursive: true });
writeFileSync(output, JSON.stringify(result, null, 2) + "\n");
console.log(JSON.stringify(result, null, 2));
