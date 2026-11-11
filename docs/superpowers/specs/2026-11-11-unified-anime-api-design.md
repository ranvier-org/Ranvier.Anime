# One Ranvier.Anime API over two engines

Status: proposed, awaiting approval. Nothing below is built yet.

## Intent

`Ranvier.Anime` (the .NET port of anime.js 4.5) and `Fable.Ranvier.Anime` (bindings over the anime.js npm package)
share a namespace and almost nothing else. Code that animates has to be written twice; `Ranvier.View.Anime/Motion.fs`
is the proof, one file holding a 550-line Fable half and a 730-line native half behind `#if FABLE_COMPILER`.

The goal is one source-compatible API: the same F# text compiles against `Ranvier.Anime` on .NET and against
`Fable.Ranvier.Anime` under Fable, and behaves the same. Where the two differ today, the native shape wins.

## Goals

- A **portable surface**: a set of fully qualified names with identical signatures in both packages.
- `Motion.fs` written once, with no engine `#if` in any part body.
- The native package gives up no performance: the portable surface is thin wrappers over what it already has.
- Both packages released from this repository by ShipIt: `Ranvier.Anime` (+ `.Traced`) and `Fable.Ranvier.Anime`
  (+ `.Traced`).

## Non-goals

- Reimplementing the whole native surface over anime.js. `NumericProperty` timelines, typed keyframe sequences,
  adapters, drag physics and the engine internals stay native-only in this release.
- Compiling the native port itself with Fable.
- Changing native behaviour or its explicit-scheduler API. Everything native has today stays.

## Anti-goals

- A lowest-common-denominator API that makes native code slower or less typed.
- Names that mean different things per engine (today `Draggable`, `ScrollSync`, `Ease`, `Reactive.drive` do).

## Design

### 1. The bindings move out of the way (breaking for Fable users)

Nineteen names collide today (`Ease`, `Timeline`, `Timer`, `Animatable`, `Draggable`, `ScrollObserver`,
`TextSplitter`, `AutoLayout`, `Scope`, `Svg`, `Text`, `Waapi`, `Utils`, `Engine`, `Keyframes`, `ScrollSync`,
`IRevertible`, `ITickable`, `Reactive.drive`). The raw anime.js bindings move from `Ranvier.Anime` to
`Ranvier.Anime.Bindings`, unchanged apart from the namespace. `Ranvier.Anime` in the Fable package then holds only the
portable surface, implemented over those bindings. Existing Fable users add `open Ranvier.Anime.Bindings`.

### 2. The portable surface

Native-shaped. Each row exists in both packages with the same signature.

| Area | Portable names | Native | Fable |
| --- | --- | --- | --- |
| Contracts | `IRevertible.Revert`, `ITickable` (`Completed`, `Cancelled`, `Pause`, `Cancel`, `Complete`, `WhenCompleted`) | as is | wrapper interface over the anime tickable |
| DOM | `Ranvier.Anime.Dom.Element`, `HTMLElement` | alias of `Ranvier.Dom` | alias of `Fable.Core.TS.Dom` |
| Host | `AnimeHost.Install`, `AnimeHost.Current` | `MotionHost` moved into the library (scheduler, computed styles, drag and viewport services) | no-op: anime has one global clock |
| Easing | `Ease.named`, `Ease.cubicBezier`, `Ease.steps`, `Ease.linear`, `Spring` — all `float -> float` | as is | anime's own easing functions |
| Animate | `PropertySpec`, `TweenStep`, `ComposedAnimationOptions`, `PlaybackOptions`, `Animate.element`, `Animate.targets`, `Animate.timeline`, `Animate.retargetable` | `Composed.*` and `Animatable` over `AnimeHost.Current` | `animate`, `createTimeline`, `createAnimatable` |
| CSS | `Animate.followCss` over `Ranvier.Css` custom properties | `Composed` text specs | `Anim.With` + animatable |
| Layout | `LayoutOptions`, `Layout.create`, `AutoLayout.Record/Animate/Revert/StartFrom` | `StartFrom` becomes public | `createLayout`, with today's Emit helpers moved in |
| Scroll | `ScrollOptions`, `ScrollThreshold`, `ScrollSync`, `ScrollEvent`, `Scroll.observe` | registry + `ObserveLinked`; `linkOf` moves in | `onScroll` + `link` |
| Drag | `DragOptions`, `DragCallbacks`, `Drag.create`, `DragHandle` (`X`, `Y`, `Freeze`, `Revert`) | `NativeDraggable` | `createDraggable` |
| SVG | `Svg.draw`, `Svg.morph` | `SvgProperties` | `svg.createDrawable`, `svg.morphTo` |
| Text | `TextSplitterOptions`, `Text.split`, `TextSplitter.Lines/Words/Chars/Freeze/Revert` | `Freeze` becomes public | `splitText` |
| Reactive | `Reactive.policy`, `MotionPolicy`, `Reactive.own` | `own` added generically over `IRevertible` | as is |

Option records are defined once per package with the same fields in the same order. Fields only one engine can honour
(native `Computed`, `Registry`; Fable `Extra`) are left out of the portable records and stay on the engine-specific
ones, so `{ X.Default with ... }` over shared fields is the portable way to build them.

A scheduler never appears in a portable signature: the portable functions read `AnimeHost.Current`. The existing
scheduler-first native functions remain for code that wants to pass one explicitly.

### 3. Keeping the two in step

A `Surface.fs` contract file, compiled into a test project for each engine, uses every portable name with its expected
signature. If one package drifts, that project stops compiling. The Fable half runs in the existing bindings suite; the
native half in `tests/Ranvier.Anime.Tests`.

### 4. Repository and release

- `src/Ranvier.Anime` gets a `PackageId`, a `<Version>`, a `CHANGELOG.md` with ShipIt front-matter, and a trimmed
  README; `build.fsx` packs it untraced and traced; `publish.yml` expects four packages.
- CI gains a `native` test group (the Expecto suite) beside `bindings` and `workflows`.
- `src/Ranvier.View.Anime`, its browser tests and the native Motion example leave this repository; the unified
  `Motion.fs` lands in ranvier-org/Ranvier.View.
- First versions (my call): `Ranvier.Anime` starts at `0.1.0-rc.1`; `Fable.Ranvier.Anime` takes a breaking prerelease
  from ShipIt after the namespace move.

### 5. Ranvier.View

In a new worktree of ranvier-org/Ranvier.View: `Ranvier.View.Anime` references `Ranvier.Anime` on .NET and
`Fable.Ranvier.Anime` under Fable, `BrowserOptions.fs` and `MotionHost.fs` are deleted (their contents now live in the
packages), and `Motion.fs` is the single-source version.

## Left to the builder

Exact module layout inside each package, how the Fable wrappers hold their anime objects, and test naming.
