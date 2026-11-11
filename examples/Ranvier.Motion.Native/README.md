# Ranvier.Motion.Native

The Motion example pages of Ranvier.View (`examples/Ranvier.Motion`: kanban, dashboard, story, physics, graphs and
routes) as one native app. An SDL3 window shows a WebCore page, and the `Ranvier.View.Anime` Motion parts run on
Ranvier.Anime in place of anime.js.

```sh
dotnet run --project examples/Ranvier.Motion.Native            # open the app
dotnet fsi build.fsx motion-example                            # build it and run its page checks in a hidden window
```

## What carries over

Each page is the browser page's source with its templates, state, Elmish loops and words unchanged. Only the engine
calls are rewritten:

| Browser | Native |
|---|---|
| `Anime.animate (el, Anim (...))` | `Native.animate el (Native.timing duration ease) [| PropertySpec... |]` |
| `Anime.timeline(...).add(...)` | `Native.timeline autoplay` with `ComposedTimeline.Add` |
| `Value.stagger`, `Grid.size`, `StaggerFrom.center` | `Native.stagger` and `Native.spread` over `Stagger.distribute` |
| `Ease.spring (stiffness, damping)` on a plain point | `Native.springPoint` over an `AnimatedObject` |
| `Reactive.track (Reactive.own (animate ...))` | `Native.clock`, a progress signal owned by the current scope |
| `Reactive.scrub` | `Native.scrub`, which seeks a paused timeline |
| `setInterval` / `setTimeout` | `Native.timers`, run by the window loop |
| the parameter records (`LayoutParams`, `AnimatableParams`, ...) | the shared records (`LayoutOptions`, `ComposedAnimationOptions`, ...) |

`Host.fs` installs `AnimeHost` with the host's frames, computed styles and the drag and scroll services before it
mounts. `Shell.fs` replaces the separate documents with one frame: the site header switches pages, each page mounts
in an arm that owns its state and timers, and opening a page loads its stylesheets (`styles/`, copied unchanged) and
scrolls to the top, as a page load does.

On Windows x64 the app uses the GPU WebCore runtime (`Ranvier.WebCore.Native.Gpu.win-x64`) in place of the
aggregate's Slim one, and opens its window with SDL's `direct3d11` renderer. The page is then presented as shared
D3D11 textures at display scale 1; other display scales and other platforms paint on the CPU.

## Differences from the browser pages

- **Routes** has no location hash, so the tabs dispatch the route they name. The tab indicator follows in pixels of its
  measured width, where the browser uses `x` in `%`.
- **Graphs** listens for mouse events (the native host sends no pointer events) and reads the viewBox from its
  attribute. The tooltip eases to percentage text through `animateOn`, because the native `follow` has no `Unit`.
- **Story** is mounted, not served and hydrated.
- **Physics** restores the full motion policy when the page leaves, because the graph outlives the page.

## Checks

`Smoke.fs` opens every page and drives it through the host's mouse, wheel and frame loop, 72 checks in all. They
cover presence, FLIP, dragging between lanes and springing home on the board; the stream, gauges, ring, counter and
timers on the dashboard; split, scroll progress and the extras on the story; dragging, the spring, scrubbing and the
reduced policy in physics; zoom, wheel, inspection and the leaderboard in graphs; and routes, page loops and the
indicator in routes. On Windows x64 it also requires the runtime's `presentation-d3d11` capability. The run fails
on any mount or commit error.
