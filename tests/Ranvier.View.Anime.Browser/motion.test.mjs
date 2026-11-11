// anime evaluates `isBrowser` and captures `Date.now` when it is first imported, so the DOM globals, the stubs jsdom
// lacks and the mocked clock are in place before animejs and the compiled checks are imported.
import test, { mock } from "node:test";
import { JSDOM } from "jsdom";

// Scripts run, so a check can tell that a refused script element never executed.
const dom = new JSDOM("<!doctype html><body></body>", { runScripts: "dangerously", resources: "usable" });
const globals = ["Node", "Element", "HTMLElement", "SVGElement", "NodeList", "HTMLCollection", "getComputedStyle", "Event", "FocusEvent", "MutationObserver"];
globalThis.window = dom.window;
globalThis.document = dom.window.document;
for (const name of globals) globalThis[name] = dom.window[name];
// jsdom has no layout: the frame callback, DOMPoint, DOMMatrix, ResizeObserver and path geometry are stubbed.
globalThis.requestAnimationFrame = dom.window.requestAnimationFrame = () => 0;
globalThis.cancelAnimationFrame = dom.window.cancelAnimationFrame = () => {};
class DOMPoint { constructor(x = 0, y = 0) { this.x = x; this.y = y; } matrixTransform() { return new DOMPoint(this.x, this.y); } }
class DOMMatrix {
  constructor() { Object.assign(this, { a: 1, b: 0, c: 0, d: 1, e: 0, f: 0 }); }
  inverse() { return new DOMMatrix(); }
  preMultiplySelf() { return this; }
}
// The observers observing at least one target.
globalThis.motionObservers = new Set();
class ResizeObserver {
  observe() { globalThis.motionObservers.add(this); }
  unobserve() {}
  disconnect() { globalThis.motionObservers.delete(this); }
}
for (const [name, value] of Object.entries({ DOMPoint, DOMMatrix, ResizeObserver })) globalThis[name] = dom.window[name] = value;
dom.window.document.fonts = { status: "loaded", ready: Promise.resolve() };
globalThis.matchMedia = dom.window.matchMedia = () => ({ matches: false, addEventListener() {}, removeEventListener() {} });
// A path's length is 10 and its point at a length sits at that x on the path's first y.
for (const type of [dom.window.HTMLElement, dom.window.SVGElement]) {
  type.prototype.getTotalLength = () => 10;
  type.prototype.getPointAtLength = function (length) {
    const numbers = (this.getAttribute("d") || "").match(/-?\d+(\.\d+)?/g)?.map(Number) ?? [0, 0];
    return { x: length, y: numbers[1] ?? 0 };
  };
}
// An element's box is its `motionRect` when set; the n-th child of a `data-rects` parent sits at x = 200 n.
const boxOf = dom.window.Element.prototype.getBoundingClientRect;
dom.window.Element.prototype.getBoundingClientRect = function () {
  const placed = this.motionRect ?? (this.parentElement?.dataset.rects === undefined ? null
    : { left: 200 * Array.prototype.indexOf.call(this.parentElement.children, this), top: 0, width: 100, height: 40 });
  return placed ? { ...placed, x: placed.left, y: placed.top, right: placed.left + placed.width, bottom: placed.top + placed.height } : boxOf.call(this);
};
mock.timers.enable({ apis: ["Date", "setTimeout"], now: 1000 });

// The checks read the layout calls, the latest draggable and its position writes, and the latest scroll observer.
const anime = await import("animejs");
globalThis.motionLog = [];
globalThis.motionEngine = anime.engine;
const children = layout => Array.isArray(layout.children) ? layout.children.map(child => child.textContent).join(" ") : String(layout.children);
for (const method of ["record", "animate"]) {
  const original = anime.AutoLayout.prototype[method];
  anime.AutoLayout.prototype[method] = function (...args) {
    globalThis.motionLog.push(`${method} ${children(this)}${this.properties.has("opacity") ? " fade" : ""}`);
    return original.apply(this, args);
  };
}
globalThis.motionSets = 0;
const enable = anime.Draggable.prototype.enable;
anime.Draggable.prototype.enable = function (...args) {
  globalThis.motionDraggable = this;
  return enable.apply(this, args);
};
for (const method of ["setX", "setY"]) {
  const original = anime.Draggable.prototype[method];
  anime.Draggable.prototype[method] = function (...args) {
    globalThis.motionSets++;
    return original.apply(this, args);
  };
}
const link = anime.ScrollObserver.prototype.link;
anime.ScrollObserver.prototype.link = function (...args) {
  globalThis.motionObserver = this;
  return link.apply(this, args);
};

const { cases, prepare } = await import("./generated/MotionChecks.js");
prepare();
const clock = ms => mock.timers.tick(ms);
for (const [name, check] of cases) test(name, () => check(clock));
