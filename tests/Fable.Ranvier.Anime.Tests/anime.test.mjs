// anime evaluates `isBrowser` and captures `Date.now` when it is first imported, so the DOM globals and the mocked Date
// are in place before the compiled checks, and animejs with them, are imported.
import test, { mock } from "node:test";
import { JSDOM } from "jsdom";

const dom = new JSDOM("<!doctype html><body></body>");
const globals = ["Node", "Element", "HTMLElement", "SVGElement", "NodeList", "HTMLCollection", "getComputedStyle", "requestAnimationFrame", "cancelAnimationFrame"];
globalThis.window = dom.window;
globalThis.document = dom.window.document;
for (const name of globals) globalThis[name] = dom.window[name];
mock.timers.enable({ apis: ["Date"], now: 1000 });

const { cases, prepare } = await import("./generated/AnimeChecks.js");
prepare();
const clock = ms => mock.timers.tick(ms);
for (const [name, check] of cases) test(name, () => check(clock));
const { cases: cssCases } = await import("./generated/CssChecks.js");
for (const [name, check] of cssCases) test(name, () => check(clock));
const sugar = await import("./generated/sugar/SugarChecks.js");
test("generated CSS sugars retain numeric metadata and typed Anime endpoints under Fable", () => sugar.run());
