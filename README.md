# Ranvier.Anime

Native F# Anime.js port in progress. Working home: `C:/wk/ranvier-anime`.

Current full suites pass1684/1684 normal Release and Traced Debug. Public numeric and CSS animatables provide cached retargetable handles, an independent group callback clock, Ranvier ownership and property-scoped revert. Typed declarations, inline capture and real computed-style capture support scalar/unit, source colors, compound interpolation and contextual scalar retargets. Internal keyframe construction and shared playback match44 construction and106 playback traces; public numeric frames add typed target-aware callbacks and scoped revert; public CSS/row/offset keyframe authoring, modern colors, SVG-specific contexts, global composition and the wider parity inventory remain open.

Retarget an existing NumericProperty inside a Ranvier owner:

```fsharp
use targets = new Animatable(scheduler) |> Animatable.own
let position = targets.Numeric(numericProperty, duration = 250u)
position.To(100.)
position.ToPrecise(200., duration = 125.5)
targets.Revert()
```

Numeric captures and initializes one property or a nonempty array of borrowed properties. Its getter reads the first target's animation cache; external target edits do not replace that cache. Omitted retarget duration/easing retain earlier settings. Revert restores captured values in authoring order and closes the group; unrelated properties survive. Cancel, disposal and callback faults are terminal and retain the last values. Cached getters remain readable. Source property noops after revert become ObjectDisposedException from native mutating handles.

The same group accepts existing typed CSS declarations:

```fsharp
use cssTargets = new Animatable(scheduler) |> Animatable.own
let width = cssTargets.Declaration(style, Css.width(Css.px 10.), duration = 250u)
width.To(Css.width(Css.px 100.))
let cachedDeclaration = width.Value
cssTargets.Revert()
```

Declaration captures the original inline value/priority/absence and renders its explicit start immediately. CaptureInline captures existing inline values; CaptureCss takes Ranvier's ComputedStyleService and existing elements to capture actual computed values. WebCore supplies the borrowed service through ComputedStyles.forHost host. DeclarationOn and CaptureCss use each element's scalar-unit context during retarget preparation. Each has a Precise variant and single/multiple-target overloads. Revert restores individual inline properties and stylesheet fallback while preserving unrelated edits. Style-only differing units reject. Typed compound destinations require matching templates; ToComponents accepts partial numeric destinations with retained omitted endpoints. Component getters return independent snapshots. No per-frame context measurement or replacement CSS contract is introduced.

Inside a Ranvier owner, animate existing typed declarations and attach the returned timer:

```fsharp
CssAnimation.declaration scheduler style
    (Css.width (Css.px 0.)) (Css.width (Css.px 100.)) 250u
|> Timer.own
```

`declarationPrecise` accepts fractional float milliseconds. `declarationWith` and `declarationPreciseWith` accept `DeclarationAnimationOptions` for easing, component modifiers, precision, repeat/direction/speed and Timer callbacks. Priority is retained. Scalar endpoint units must match; compound templates follow Anime.js rules without contextual conversion. The legacy color/compound literal behavior is covered, rather than treating every valid CSS expression as safely interpolable.

Use the HTML element-context variant for differing scalar units, on the element and scheduler's owning host thread:

```fsharp
CssAnimation.declarationOn scheduler element
    (Css.width (Css.em 1.)) (Css.width (Css.px 100.)) 250u
|> Timer.own
```

`declarationOnPrecise`, `declarationOnWith` and `declarationOnPreciseWith` retain the same duration/options choices. Conversion measures a shallow clone once at construction and removes it before rendering; frames use captured endpoints. Separate font/layout contexts do not reuse Anime.js's global conversion cache. The source's integer width measurements and zero-width fallback remain observable. Common source angles use direct ratios; angle/length mixing is rejected.

`Ease.linear [| LinearStop.Value 0.; LinearStop.At(1.5, 0.5); LinearStop.Value 1. |]` retains overshoot and uses normalized positions. Stops keep authored order; repeated positions may produce nonfinite results as upstream does. Empty input is identity and one value is constant. `Ease.irregular 10u 1.` captures a random monotonic curve once; `Ease.irregularWith draw 10u 1.` injects finite [0,1) draws for reproducibility. Sampling never draws again. Counts0–2 yield the two endpoints. Finite signed randomness is supported.

Open `Ranvier.Anime.slnx` in Rider. It contains the library and test project and will be updated as projects are added.

`Ranvier.Anime.Native.slnx` contains the independent native host consumer and isolated typed-CSS AOT probe. The timer/numeric/timeline/declaration/context-unit, public numeric/CSS animatable and public numeric keyframe source passes normal and Traced JIT and strict NativeAOT PE execution against the fully public Core/DOM alpha.56, CSS alpha.2, View45 and NativeP7 CPU cohort. Nine managed package identities and all24 native members are verified against independent package audits. This qualifies the Windows headless CPU smoke; hardware acceleration and broader native feature coverage remain open. See [current public cohort evidence](docs/evidence/native-public45-p7-public-numeric-keyframes.json) and [native integration scope](docs/native-integration.md).

Project operations use Partas.Build 0.8.2 from the public Cloudsmith feed configured in `nuget.config`:

```powershell
rtk proxy dotnet fsi build.fsx -- restore
rtk proxy dotnet fsi build.fsx -- build
rtk proxy dotnet fsi build.fsx -- test
```

For repeated operations, load `.SageFs/init.fsx` into a SageFs bare session rooted in this directory. Its text loads `build.defs.fsx` and binds:

```fsharp
build ["test"; "--explain"]
build ["test"]
```

The command tree is shared with the CLI and returns a structured `RunResult`. Reload the definitions and rebind `build` after editing them. Load the definitions rather than the CLI shim, whose final line exits the process.

The foundation supports numeric scheduling, easing, springs, staggering, nested timelines, callback tracks, scalar replace composition and sets, playback controls, stretching, cached numeric/CSS animatables, cancellation, Ranvier ownership and reactive drive. Typed CSS custom-property animations accept existing numeric values with matching units. All 1684 tests pass in normal Release and Traced Debug; XML documentation audit reports zero findings. Pinned source fixtures cover easing curves, scalar/color/compound interpolation, contextual units, springs, stagger distributions, timeline/property composition, shared playback, keyframe construction/playback, target-aware numeric inputs and animatable callbacks/revert. Full Anime.js parity and broader native coverage remain in progress. Direct dependencies reuse Ranvier, Ranvier.Dom, Ranvier.Css and Ranvier.Css.Dom; no replacement CSS or scheduler contract is introduced.

`Keyframes.Numeric(scheduler, property, Keyframe.values [|0.;100.;50.|], duration=1000u)` accepts an existing `NumericProperty` and starts playback. Use `NumericPrecise` and `WithDurationPrecise` for fractional milliseconds. `Keyframe.toBy` and `WithFromBy` receive a typed target/index/count/previous-cache context; `WithDurationBy` accepts unsigned callback results. Pass `owner=Api.getOwner()` inside a Ranvier root to attach before property reads and callbacks. The returned `KeyframeAnimation.Playback` is the shared Timer; `Revert()` restores pre-construction snapshots while preserving unrelated properties. Explicit cancellation through either controller closes restoration; normal completion preserves it. Public CSS, sparse rows and offsets remain in progress.

The playback timing kernel matches 55 source timer traces and 929 samples exactly, including repeat gaps, reversal/alternation, signed delay, skipped/forced samples and zero/fractional/infinite/overflow cases. Standalone controls match 51 traces, and scheduled timers match 34 admitted-frame traces. Numeric animations share that clock and match 52 source traces of property values, callbacks and state, including mutable stretching, generated schedules and signed-gap/int32 boundaries. Additional state, scheduler/lifetime and interruption tests cover cancellation and validation.

Public timelines use the shared clock for root and owned-timer sampling. Sixty source traces now verify property/set writes, root/call events, owned timer callbacks and self-state, and borrowed timer/animation/nested callbacks and state. Fifty-two root-clock comparisons replay recorded child completion aggregates, with six phase/interruption cases. Sixteen lifetime/authoring tests and five review regressions cover cancellation, owner disposal, reentrant controls, geometry resizing and fault cleanup. Cases include signed delay/gaps, speed before rounding, low FPS, uint-sized/fractional/zero/forever durations and reverse-loop boundaries. Plain property-track lifecycle callbacks and the remaining timeline operations are still incomplete. Three resumed-borrowed source cases verify setter pause callbacks. Both important final-review findings were reproduced and fixed. New strict NativeAOT receipts bind the reviewed source; full Anime.js parity remains incomplete.

Numeric animations accept `PlaybackOptions` through `new NumericAnimation(..., duration, options = options)` or `Animation.animateWithOptions options ease scheduler read write target duration`. The constructor accepts uint or precise float milliseconds and captures the start value while paused; `Reset` renders initialization. Factories initialize and resume automatically. Repeat/direction/speed/FPS controls and seven callback phases reuse the timer. The 12 ms source resume lead now applies to numeric animation, reactive retargeting and typed CSS too. `CssAnimation.customPropertyWithOptions` adds the same options to existing typed CSS endpoints while retaining units and inline priority.

`new Timer(scheduler, 250u, options = { PlaybackOptions.Default with Repeat = PlaybackRepeat.Count 2u; Alternate = true })` creates a paused timer with three iterations. Use `PlaybackRepeat.Forever` for indefinite repetition or a float duration for precise fractional milliseconds. `Resume` retains direction; `Play`, `Reverse`, `Alternate`, `Seek`, `Reset`, `Restart` and `Complete` follow source control transitions with the native terminal-cancellation policy. Six optional callbacks receive the timer. `Timer.own timer` attaches it to the current Ranvier owner. Scheduling preserves the source's 12 ms resume lead before speed scaling; rates below 240 throttle callbacks on admitted host frames. Both real NativeAOT consumers verify timer completion, typed CSS repeat/alternate output and owner disposal inside an active callback.

Use `build ["test"; "-c"; "Debug"]` for the matched Traced Ranvier dependency cohort, including CSS and generated bindings. Restore uses the same configuration as build, so switching back to Release restores normal dependencies. Defaults use public CSS alpha.2 with public Bindings alpha.1 and CSS-DOM 0.1.0-alpha.1. Native commands accept `--css-candidate` to select the historical copied private CSS cohort in an isolated cache/output tree.

Use `build ["native-publish"; "-c"; "Debug"; "--public-core"]` and `build ["native-run"; "-c"; "Debug"; "--public-core"; "--aot"]` for the qualified public-Core56 replay. It uses a fresh separate cache and actual package Core/DOM references, public CSS alpha.2 and the retained private View/runtime. `core56-evidence` captures each mode before another restore changes assets; `core56-summary` records both immutable receipts. Ordinary portable tests retain DOM alpha.55 defaults. Public/private flags and exact package/source/runtime identities are recorded in [Core56 evidence](docs/evidence/native-core56-css2-hardened.json). Add `--label playback-reviewed` to `core56-evidence` and `core56-summary` for the reviewed source snapshots. Each capture label is immutable; use a new lowercase/hyphenated label for a later replay.

Restore/build/test use the owned package cache `artifacts/nuget-packages`, explicit repository feeds and a fresh HTTP lookup. They do not alter the shared global NuGet cache. Use `build ["test"; "--rebuild"]` for a nonincremental build and `build ["identity"]` to verify the Release output assemblies against restored package bytes and the coordinator's verified public DOM hash; add `-c Debug` for Traced output identity. Receipts are written under `artifacts/qualification`. These managed checks do not qualify Native/View integration.

The typed easing API exposes `Ease.create (Power 2.) InOut`, `Ease.steps 4u false`, and `Ease.cubicBezier 0.42 0. 0.58 1.`. `Ease.named` supplies Anime.js names; `Ease.linear` and `Ease.irregular` supply the piecewise and captured random curves described above. Native validation rejects unknown names, nonfinite parameters and zero steps. Finite progress can extend outside the unit interval, as signed grid axes require; steps clamp finite input as upstream does. Custom easing outputs may overshoot, including at initialization/completion; cancellation or a seek during an easing callback supersedes the interrupted render.

Use `Spring.perceivedMilliseconds 500u 0.25` for unsigned whole-millisecond duration input with bounce, or `Spring(duration = 628.5, bounce = 0.5)` for precise duration input. Physical mode accepts mass, stiffness, damping and velocity. Mutable parameter setters follow upstream recomputation rules. `Animation.animateSpring spring scheduler read write target` uses the spring's settling duration; `spring.Duration` is its perceived duration and can differ. Completion callbacks fire at the perceived threshold and can fire again after a backward sample resets completion.

Use `Stagger.distribute (StaggerValue.Step 10.) StaggerOptions.Default 5u` for numeric offsets. Typed options cover step/range values, origins, explicit 2D/3D grids, signed axes, reversal, easing, start offsets, modifiers, total overrides and index mapping. Set `Origin = StaggerOrigin.Random`, `Seed = Some 42.` and `Jitter = Some(StaggerJitter.Range(2., 9.))` for reproducible random offsets. Seeds accept finite numbers, including fractional and negative values; omitted seeds use System.Random.Shared. Jitter samples are generated before shuffling and modifiers run last. Zero maximum distance uses zero as the easing input, avoiding an undefined callback input. Range spacing still preserves upstream infinities when dividing by zero with nonzero signed distances. Bounce rejects transformed inputs below -1/11, where the upstream search cannot terminate.

Use `StaggerGrid.Automatic` with existing `Ranvier.Dom.DOMPointInit option array` values for measured object positions. Coordinates must be finite; missing positions cause flat index fallback. Automatic distances normalize by the smallest nonzero absolute distance, preserving uneven spacing. `Stagger.autoElements value options elements` reads existing element bounding-box centers on the host's owning thread, respecting total overrides and retaining no host resources. Compiled recording-interface tests verify measurement behavior; actual Native/View integration is still pending.

`CssStagger.step Css.px (Css.px 10.) StaggerOptions.Default 5u` returns generated length values; `CssStagger.range Css.px (Css.px 10.) (Css.px 100.) options 5u` preserves matching endpoint units. The constructor validates each output, retaining its concrete generated type and grammar. Nonnumeric/compound inputs, mismatched units and nonfinite results are rejected. Start, jitter and modifiers operate in magnitudes of the chosen unit. This typed API deliberately rejects the upstream string API's mixed-unit suffix ambiguity; native computed-value conversion remains separate work.

`new Timeline(scheduler, options = { PlaybackOptions.Default with Repeat = PlaybackRepeat.Count 2u; Alternate = true })` starts paused with three iterations. `Resume` retains direction; `Play`, `Reverse`, `Alternate`, `Pause`, `Seek`, `Reset` and `Restart` share the timer clock. Speed/FPS and seven root callbacks are available. `Complete` samples the end while preserving the existing reusable native timeline lifetime. `Sync(child, TimelinePosition.At 0.)` borrows a timer, numeric animation or nested timeline; labels and `TimelinePosition.Expression "<<+=25"` support relative positioning. Nested duration is captured and cycles are rejected. `SyncStaggered` captures the iteration end before insertion. Use `Timeline.own` for current-owner cleanup. Cancellation is terminal, retains geometry/rendered values and leaves borrowed children usable. Full property-track callbacks, additive/blend/global composition, keyframes, removal and stretch/refresh/revert/promises remain required work.

`Call((fun () -> ...), TimelinePosition.At 50.)` adds a callback track. Source comparisons cover forward/backward crossings, repeated boundaries, equal-time ordering and signed negative positions. `Seek(time, muteCallbacks = true)` suppresses direct callback tracks; Reset also clears their completion state. As upstream Sync does, borrowed nested controllers retain their own callback behavior, so parent muting/reset can still trigger their callbacks. Callback state changes before user code to prevent recursively refiring the same forward event. Ancestor cancellation guards every descendant's final write.

`let child = timeline.AddTimer(250u, options = { PlaybackOptions.Default with Repeat = PlaybackRepeat.Count 1u })` adds an owned timer and returns its controller. Float milliseconds support precise durations. Its frames come exclusively from the timeline; parent muting applies to its callbacks and parent cancellation disposes it. `timeline.Sync(existingTimer)` borrows through current-time property sampling, preserves its own callback policy and leaves it usable after parent cancellation. Explicitly cancelling an owned child makes that track inert and releases its completion gate. Callback faults close the root and all owned timers while preserving the original exception.

Create a `NumericProperty(read, write)` handle and reuse it for tracks targeting the same property. `timeline.Animate(property, 100., 100u, TimelinePosition.At 0.)` accepts unsigned whole milliseconds; use the float overload for precise durations. `timeline.Set(property, 40., TimelinePosition.At 50.)` schedules an instantaneous replacement. Scalar replace tracks within one controller support explicit from/easing/modifier options, overlaps, gaps, out-of-order insertion and backward seeks. Adding tracks pauses and resets controller time while preparing cached values without dispatching property writes. Durations use source 12-place precision; positive values that round to zero are rejected. These handles use explicit F# accessors, with no property reflection or replacement CSS type.

Reference generators start the source engine at a controlled zero clock; admitted-frame cases advance it explicitly. They print fixture SHA256 values and retain deterministic generated schedules. Animation playback traces explicitly Reset both implementations before the initial snapshot.

Upstream baseline: Anime.js v4.5.0, commit `2c9cf8ea00329f6768c7d7902252ed977d75ce42`. The research checkout is ignored under `upstream/anime`; the tracked provenance and parity inventory record it. Preserve its MIT license with derived source, tests and distributions; THIRD-PARTY-NOTICES.md records the source credits.
