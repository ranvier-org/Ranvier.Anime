---
title: Testing and costs
order: 3
description: Deterministic animation tests on a manual anime clock under jsdom and Playwright, and the bundle cost of the bindings.
---

## A manual clock under jsdom

Install DOM globals and mock `Date` **before importing anime**: it captures `Date.now`,
`requestAnimationFrame` and browser detection at import time. Then load the compiled F# checks:

```js
import test, { mock } from "node:test";
import { JSDOM } from "jsdom";

const dom = new JSDOM("<!doctype html><body></body>");
globalThis.window = dom.window;
globalThis.document = dom.window.document;
for (const name of ["Node", "Element", "HTMLElement", "SVGElement", "NodeList", "HTMLCollection", "getComputedStyle"])
  globalThis[name] = dom.window[name];
globalThis.requestAnimationFrame = dom.window.requestAnimationFrame = () => 0;
mock.timers.enable({ apis: ["Date"], now: 1000 });

const { checks } = await import("./generated/Checks.js");
for (const [name, check] of checks) test(name, () => check(ms => mock.timers.tick(ms)));
```

Each F# check advances mocked time, ticks the engine and asserts the resulting style:

```fsharp
open Fable.Core
open Fable.Ranvier.Anime

[<Emit("document.body.appendChild(document.createElement($0))")>]
let createElement (tag: string) : TS.Dom.Element = jsNative

[<Emit("$0.style.opacity")>]
let opacityOf (element: TS.Dom.Element) : string = jsNative

/// Advances the mocked clock by the given milliseconds.
type Clock = float -> unit

let linearFade (advance: Clock) =
    Testing.manual ()
    let card = createElement "div"
    Anime.animate (card, Anim (opacity = Value.fromTo (0, 1), duration = 1000, ease = Ease.linear)) |> ignore

    let samples =
        [
            for step in 0..4 do
                advance (if step = 0 then 0. else 250.)
                Testing.step ()
                opacityOf card
        ]

    if samples <> [ "0"; "0.262"; "0.512"; "0.762"; "1" ] then
        failwithf "Unexpected opacities %A" samples

let checks = [| "a linear fade reads the same values every run", linearFade |]
```

- `Testing.manual ()` sets `Engine.settings.useDefaultMainLoop` and `pauseOnDocumentHidden` to
  false, which leaves every tick to `Testing.step ()`.
- `Testing.step ()` advances every running tickable to the current mocked time.
- Each read runs 12 ms ahead of the mocked time: anime starts a resumed tickable 12 ms early to force
  a frame on the next tick, so the fade above reads 0.262 after 250 ms, every run.
- `Reactive.policy ()` is `Full` under jsdom, which has no `matchMedia`; a test writes the signal to
  check the `Reduced` paths.

jsdom has no layout and no Web Animations. `animate`, `Utils.set`, `revert`, animatables, timelines
and a layout's `record ()` run as in a browser. `splitText`, `onScroll` and `createDraggable` need
stubs of `ResizeObserver`, `DOMPoint` and `DOMMatrix`, and a FLIP layout's transforms, which run
through the Web Animations API, do not move.
The [bindings suite](https://github.com/ranvier-org/Ranvier.Anime/blob/main/tests/Fable.Ranvier.Anime.Tests/anime.test.mjs)
holds a complete set of globals for these checks.

## Playwright

In a browser, `page.clock` controls the same two inputs. Install the clock, paused, before the page's
scripts run, so anime captures the controlled `Date` and frame callback:

```js
const start = new Date("2026-10-21T09:00:00Z").getTime();
await page.clock.install({ time: start });
await page.clock.pauseAt(start + 1);
await page.goto("/kanban/");
await page.clock.runFor(120);   // samples an animation 120 ms in
```

`page.clock.runFor (ms)` moves every anime animation by `ms`. The Web Animations API runs on the
browser's own timeline, outside the page clock: a FLIP test asserts that `element.getAnimations ()`
holds the layout's animation, calls `finish ()` on it, then asserts the final boxes.

## Bundle costs

Measured with rolldown (minified) and gzip level 9 on anime 4.5.0, importing from `animejs`. anime's
root imports tree-shake, so a bundle holds the features it calls:

| Entry | gzip |
| --- | --: |
| `animate` | 12.2 kB |
| `createTimeline` | 13.4 kB |
| `createAnimatable` | 12.6 kB |
| `createLayout` | 20.6 kB |
| `createDraggable` | 19.5 kB |
| `onScroll` + `animate` | 16.5 kB |
| `splitText` | 3.3 kB |
| `waapi.animate` | 4.5 kB |
| `spring` + `animate` | 13.1 kB |
| everything | 41.0 kB |

What the bindings add, from the committed measurement
([`bindings-bundle.json`](https://github.com/ranvier-org/Ranvier.Anime/blob/main/docs/research/motion/bindings-bundle.json)),
checked for drift in CI:

| Entry | Against | Δ gzip | Budget |
| --- | --- | --: | --: |
| `Anime.animate (el, Anim (...))` | the same call written in JavaScript | 0 B | 64 B |

- The emitted `Fable.Ranvier.Anime` modules hold no `curry`, `uncurry`, `partialApply` or `createObj`.
- The bundle cost of the `Ranvier.View.Anime` Motion parts is documented with
  [Ranvier.View](https://ranvier-org.github.io/Ranvier.View/view/motion-testing/#bundle-costs).
