# Ranvier.Anime

Native F# Anime.js port in progress. Working home: `C:/wk/ranvier-anime`.

Open `Ranvier.Anime.slnx` in Rider. It contains the library and test project and will be updated as projects are added.

`Ranvier.Anime.Native.slnx` contains the independent native host consumer and isolated typed-CSS AOT probe. Normal and Traced NativeAOT smokes pass with the corrected private CSS candidate and pinned private Core/View/runtime graph. Public-cohort acceptance remains incomplete. See [native integration evidence](docs/native-integration.md) for reproducible operations and limits.

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

The foundation supports numeric scheduling, easing, springs, staggering, nested timelines, callback tracks, scalar replace composition and sets, pause/resume, seek, retargeting, cancellation, Ranvier ownership and reactive drive. Typed CSS custom-property animations accept existing numeric values with matching units. All 354 tests pass in normal Release and Traced Debug; XML documentation audit reports zero findings. Easing comparisons cover 46 upstream default names and three curves at nine sample points each. Spring fixtures cover twelve constructor cases, six sequential parameter changes and numeric runner integration. Stagger fixtures cover 67 numeric/geometry/random distributions and seven unit distributions. Timeline references cover 29 positions, three synchronized-child traces, seven staggered-position cases, six callback traces, two nested-controller traces and 56 property schedules. Full Anime.js parity and native host integration remain in progress. Direct dependencies reuse Ranvier, Ranvier.Dom, Ranvier.Css and Ranvier.Css.Dom; no replacement CSS or scheduler contract is introduced.

The internal playback timing kernel matches 55 source timer traces and 929 samples exactly, including repeat gaps, reversal/alternation, signed delay, skipped/forced samples and zero/fractional/infinite/overflow cases. It separates raw clock state from rounded public time/progress. Timer callbacks and scheduler controls, plus numeric/timeline loop-direction-speed integration, remain required work; the existing controllers do not expose these new timing capabilities yet.

Use `build ["test"; "-c"; "Debug"]` for the matched Traced Ranvier dependency cohort, including CSS and generated bindings. Restore uses the same configuration as build, so switching back to Release restores normal dependencies. Native commands accept `--css-candidate` to select the copied private CSS cohort in an isolated cache/output tree; ordinary commands retain public versions.

Restore/build/test use the owned package cache `artifacts/nuget-packages`, explicit repository feeds and a fresh HTTP lookup. They do not alter the shared global NuGet cache. Use `build ["test"; "--rebuild"]` for a nonincremental build and `build ["identity"]` to verify the Release output assemblies against restored package bytes and the coordinator's verified public DOM hash; add `-c Debug` for Traced output identity. Receipts are written under `artifacts/qualification`. These managed checks do not qualify Native/View integration.

The typed easing API exposes `Ease.create (Power 2.) InOut`, `Ease.steps 4u false`, and `Ease.cubicBezier 0.42 0. 0.58 1.`. `Ease.named` supplies Anime.js names. Native validation rejects unknown names, nonfinite parameters and zero steps. Finite progress can extend outside the unit interval, as signed grid axes require; steps clamp finite input as upstream does. Irregular easing and multi-stop linear easing remain required work. Custom easing outputs may overshoot, including at initialization/completion; cancellation or a seek during an easing callback supersedes the interrupted render.

Use `Spring.perceivedMilliseconds 500u 0.25` for unsigned whole-millisecond duration input with bounce, or `Spring(duration = 628.5, bounce = 0.5)` for precise duration input. Physical mode accepts mass, stiffness, damping and velocity. Mutable parameter setters follow upstream recomputation rules. `Animation.animateSpring spring scheduler read write target` uses the spring's settling duration; `spring.Duration` is its perceived duration and can differ. Completion callbacks fire at the perceived threshold and can fire again after a backward sample resets completion.

Use `Stagger.distribute (StaggerValue.Step 10.) StaggerOptions.Default 5u` for numeric offsets. Typed options cover step/range values, origins, explicit 2D/3D grids, signed axes, reversal, easing, start offsets, modifiers, total overrides and index mapping. Set `Origin = StaggerOrigin.Random`, `Seed = Some 42.` and `Jitter = Some(StaggerJitter.Range(2., 9.))` for reproducible random offsets. Seeds accept finite numbers, including fractional and negative values; omitted seeds use System.Random.Shared. Jitter samples are generated before shuffling and modifiers run last. Zero maximum distance uses zero as the easing input, avoiding an undefined callback input. Range spacing still preserves upstream infinities when dividing by zero with nonzero signed distances. Bounce rejects transformed inputs below -1/11, where the upstream search cannot terminate.

Use `StaggerGrid.Automatic` with existing `Ranvier.Dom.DOMPointInit option array` values for measured object positions. Coordinates must be finite; missing positions cause flat index fallback. Automatic distances normalize by the smallest nonzero absolute distance, preserving uneven spacing. `Stagger.autoElements value options elements` reads existing element bounding-box centers on the host's owning thread, respecting total overrides and retaining no host resources. Compiled recording-interface tests verify measurement behavior; actual Native/View integration is still pending.

`CssStagger.step Css.px (Css.px 10.) StaggerOptions.Default 5u` returns generated length values; `CssStagger.range Css.px (Css.px 10.) (Css.px 100.) options 5u` preserves matching endpoint units. The constructor validates each output, retaining its concrete generated type and grammar. Nonnumeric/compound inputs, mismatched units and nonfinite results are rejected. Start, jitter and modifiers operate in magnitudes of the chosen unit. This typed API deliberately rejects the upstream string API's mixed-unit suffix ambiguity; native computed-value conversion remains separate work.

`new Timeline(scheduler)` starts paused. `Sync(child, TimelinePosition.At 0.)` borrows an existing numeric animation or nested timeline; labels and `TimelinePosition.Expression "<<+=25"` support relative positioning. Nested duration is captured at synchronization and cycles are rejected. `SyncStaggered(children, value, options)` captures the timeline end before adding the group and applies modifiers after resolving that base. Use Play/Pause/Seek/Reset/Restart/Complete for synchronized playback and `Timeline.own` for current-owner cleanup. Cancellation is terminal, preserves rendered values and leaves borrowed children paused and usable. Unknown plain labels append; invalid relative labels/nonfinite positions raise argument errors. Timer tracks, additive/blend/global cross-controller composition, direction/loops/speed, removal and refresh/revert/promises remain required work.

`Call((fun () -> ...), TimelinePosition.At 50.)` adds a callback track. Source comparisons cover forward/backward crossings, repeated boundaries, equal-time ordering and signed negative positions. `Seek(time, muteCallbacks = true)` suppresses direct callback tracks; Reset also clears their completion state. As upstream Sync does, borrowed nested controllers retain their own callback behavior, so parent muting/reset can still trigger their callbacks. Callback state changes before user code to prevent recursively refiring the same forward event. Ancestor cancellation guards every descendant's final write.

Create a `NumericProperty(read, write)` handle and reuse it for tracks targeting the same property. `timeline.Animate(property, 100., 100u, TimelinePosition.At 0.)` accepts unsigned whole milliseconds; use the float overload for precise durations. `timeline.Set(property, 40., TimelinePosition.At 50.)` schedules an instantaneous replacement. Scalar replace tracks within one controller support explicit from/easing/modifier options, overlaps, gaps, out-of-order insertion and backward seeks. Adding tracks pauses and resets controller time while preparing cached values without dispatching property writes. Durations use source 12-place precision; positive values that round to zero are rejected. These handles use explicit F# accessors, with no property reflection or replacement CSS type.

Reference generation fixes Date.now to zero before loading the unmodified source engine, matching the manual scheduler's zero origin. The generator prints its fixture SHA256; generated schedules are seeded and reproducible.

Upstream baseline: Anime.js v4.5.0, commit `2c9cf8ea00329f6768c7d7902252ed977d75ce42`. The research checkout is ignored under `upstream/anime`; the tracked provenance and parity inventory record it. Preserve its MIT license when deriving source or tests. This initial numeric runner is independently authored; upstream behavioral comparison remains required.
