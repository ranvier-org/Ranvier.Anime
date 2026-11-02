# Native Anime.js for Ranvier

Status: approved for implementation by the user, 3 November 2026, with Ranvier ownership integrated from the first milestone and Ranvier.Css used directly. The matching public Native/View integration cohort remains a qualification dependency.

## Outcome

Provide Anime.js capabilities to native F# applications running Ranvier's WebCore engine. Animation logic runs on .NET, without executing Anime.js JavaScript. Typed Ranvier.Css values improve authoring and validation; WebCore remains responsible for CSS parsing, computed styles, layout and composition. Preserve recognizable Anime concepts and behavior while using F# types and native ownership.

The user requires the full capability inventory, including timelines, staggering, springs, interpolation and playback controls. A small tween library is an intermediate milestone, not completion. Partas.AnimeJs is explicitly excluded. Existing Ranvier Fable bindings and Motion documentation inform compatibility; their imported JavaScript engine is not the native implementation.

## Source and working home

Working home: `C:/wk/ranvier-anime`. The upstream research checkout is `upstream/anime`, pinned to Anime.js v4.5.0, commit `2c9cf8ea00329f6768c7d7902252ed977d75ce42`. This baseline matches the version documented by current Ranvier bindings. Future upstream changes require an explicit baseline update and renewed comparison.

Native API reconnaissance used `C:/wk/project2-view-next-release` at `54352988890d7e4484d7f0bc49b6dbc9688c90fe`. This is an inspected candidate, not a frozen or accepted integration dependency. The Project 2 coordinator has been asked for exact source and public package cohort guidance. Its reply determines the native adapter baseline before adapter implementation; the portable engine does not depend on that decision. Do not edit its checkout, protected native producers or shared tools. The existing user checkout remains untouched.

My proposed packaging choice is a standalone port repository in this working home, with small F# libraries and native integration consumers. This keeps port logic independent of ongoing Core/View release work. Exact package names are provisional and must be checked for collisions before publication; creating a public repository or publishing packages is a separate release action.

## Dependency decisions

Use `Ranvier.Css` directly for public property, value, unit and custom-property types, and reuse `Ranvier.Css.Dom` for style operations. Do not introduce another public CSS contract. Animation interpolation data remains internal. Generated `Ranvier.Css.Bindings` helpers remain available to consumers.

The native coordinator supplied View source `76022bea361b5eb2441ca2b7242f8594c58e5fcd`, public DOM alpha.55 from Core source `54f004b3baa4136777998b65e0f9941d78151224`, and Native preview.7 source `6f10bea17d7a1f22e5e97d472819c055fba3ef06`, ABI62. These supersede the earlier candidate selection. Matching Native/View public packages remain under qualification. See `C:/wk/project2/anime-port-handoff.md` for source overrides, staging-root semantics and the strict AOT caveat.

## Architecture

1. A portable calculation kernel holds playback transitions, iteration/direction rules, callback ordering, easing, spring evaluation, value interpolation, target/property composition and timeline scheduling. It has no WebCore dependency and accepts deterministic input time. Use doubles and milliseconds internally and explicit typed setters for native object targets. Do not depend on runtime reflection to enumerate arbitrary object properties; typed object accessors are the native adaptation of JavaScript object targets.
2. A shared DOM adapter handles element/selector targets, attributes, CSS/custom properties, transforms, geometry, unit conversion and snapshots. It consumes Ranvier.Dom contracts and existing style services. It may query WebCore for context-dependent values; it must not invent a second CSS/layout engine.
3. A typed CSS adapter preserves existing property/value grammar markers at authoring boundaries. Numeric values retain unit metadata. Expressions, keywords and computed strings do not become safely interpolable merely because they were valid CSS. Route engine-supported interpolation through WebCore or report unsupported interpolation precisely. Untyped native CSS input remains available with runtime validation.
4. A native host adapter supplies the owning document, scheduler, animation and timeline services, lifecycle and optional accelerated execution. All host operations run on the host's owner thread. Resources expire with their host; no callback may write through a disposed host.
5. The animation runner integrates with Ranvier core from the first working milestone: reuse core ownership and cleanup, reactive retargeting and batching, and the shared frame scheduler. Standalone use supplies an explicit lifetime; owned use registers cleanup with the current owner. Disposal cancels pending frame/subscription work and prevents later target writes. Per-frame rendering writes directly to targets, with opt-in reactive playback/progress state; it does not require writing a signal on every frame. Prove the existing core threading and batching contracts rather than inventing replacements.
6. View integration subsequently connects motion slots, enter/exit, reduced motion and layout transitions to that already-owned runner. Do not change existing browser binding behavior or require a browser app to install the native backend.

These are dependency boundaries, not a requirement to publish five packages. Keep portable logic usable without View, native libraries or a GPU. Package splitting is a builder decision after dependency and API experiments.

## Playback and composition contract

Port timer and animation state transitions before optimizing. Cover play/pause/resume/restart, seek with callback suppression, reset, reverse/alternate, finite/infinite looping and loop delay, playback speed, complete, cancel, revert, stretch and refresh. Match upstream distinctions between stopping at the current state and restoring previous styles. Preserve only owned state on restoration: unrelated concurrent property edits must survive. Define property-level snapshots, priority and absence explicitly rather than restoring an entire stale style declaration.

Timelines include absolute and relative positions, labels, previous-child start/end positions, nested timelines, timers, calls, sets, synchronization and removal. Evaluate children and overlapping property owners in upstream order, including backward seeks and gaps between children. Callback tests cover equal timestamps, seek crossings, zero durations, loop boundaries, reentrant cancellation and completion. Completion subscriptions must settle once under cancellation and disposal, with documented native equivalents for JavaScript promises.

Animations include scalar and keyframe values, per-property timing/easing, function-based values, relative operators, replace/add/blend composition and retargetable animatables. Pure interpolation covers numbers, compatible units, colors and compound strings. DOM-dependent conversion, transform ordering, SVG values and custom-property behavior require real engine tests. Native adaptations must be individually recorded rather than silently replacing behavior.

## Acceleration policy

The managed engine is the behavioral reference. Eligible keyframe animations may use WebCore's native animation effects and compositor. Eligibility depends on property, easing, composition, callback and synchronization requirements, and actual backend capability. A custom function or coupled timeline may require managed sampling. Avoid per-frame signal writes and unnecessary interop crossings; batch property application where the API supports it.

Do not claim that all CSS properties are GPU accelerated, or that native WebCore implies GPU execution. Verify transform/opacity promotion and actual GPU presentation using the runtime's supported evidence. Managed fallback is explicit and testable; an unsupported requested accelerated mode must be reported. Arbitrary GPU computation, WebGPU/WebGL and a new renderer are not prerequisites for this port.

## Complete feature inventory

Track every upstream export and behavior family with its implementation, tests, native adaptation and runtime dependencies. Initial families are:

- Engine, clocks, timers, animations, controls, callbacks, completion and memory/lifecycle behavior.
- Easings: named families, custom powers, elastic/back variants, cubic Bezier, linear stops, steps, irregular easing and springs, including spring-derived duration and mutable spring parameters.
- Stagger: numeric/unit steps and ranges, origins, reversal, easing, seeded jitter, index mapping, 2D/3D and automatic grids, axis selection and timeline positions.
- Timelines, nested composition, labels, calls, sets, synchronization and retargetable animatables.
- Targets, DOM attributes, CSS values, units, colors, transforms, keyframes, custom properties, object accessors and utility functions.
- SVG drawing, path following and morphing; scroll observation and synchronization; draggable input, constraints and inertia; text splitting/scrambling; scopes, media conditions and cleanup; automatic layout transitions.
- Native equivalents of the WAAPI facade and adapter extensibility. The optional three.js adapter is an interoperability difference: three.js objects are JavaScript-specific. Offer typed native object/property adapters rather than claiming three.js compatibility. Record this distinction explicitly in the parity inventory; do not count an unimplemented native equivalent as done.

No family is marked implemented merely because the underlying DOM interface exists. Unsupported or missing runtime operations become concrete coordinator requests with reproductions. Implement independent pure logic while native packaging proceeds.

## Verification and completion

Generate reference cases from the pinned, unmodified upstream engine, recording baseline commit, inputs, time steps, property outputs, state and callback traces. Compare the native implementation on deterministic time sequences. Numeric tolerances must be justified per operation; discrete events and callback order match exactly. Include boundary cases, dense samples and generated cases, not just endpoints. Preserve MIT attribution and the upstream license for derived source and tests.

Use SageFs for pure F# type/logic experiments and red/green checks. Persist passing logic and verify persisted assemblies; run the port's full Release build and unfiltered tests as final gates. Source API reconnaissance is not a passing workspace build. The attempted native project semantic check timed out with verdict unknown; metadata inspection succeeded but does not prove runtime behavior.

Native tests exercise style reads/writes, custom properties and unit conversion, playback and cancellation, host disposal, callback reentrancy, View lifecycle and representative SVG/scroll/input/text/layout behavior. Run native CPU and supported GPU presentation modes, with normal/traced builds and a real native application AOT consumer where supported. Platform coverage must distinguish verified platforms from unavailable environments; do not infer platform parity from one Windows run.

Final integration restores an exact matched public Core/Native/View/CSS dependency cohort into a clean consumer, without producer project references or loose DLL substitution. Record package/source versions and payload identity. Source-based tests can advance before this cohort ships, but cannot satisfy package integration acceptance.

Completion requires the export/behavior inventory to account for the full baseline, with passing behavioral comparisons and native integration evidence for every claimed capability; documented deliberate F# adaptations; measured acceleration evidence; examples using typed CSS, timelines, stagger, springs and ownership; package/license documentation; and no unresolved required runtime gaps. A known missing feature remains incomplete even when documented.

## Implementation order and builder discretion

First pin references and establish the behavior oracle and portable calculation tests. In the first working milestone, connect a minimal runner to the shared manual frame scheduler and actual Ranvier core ownership: a reactive source retargets a numeric property, owner disposal cancels it, and subsequent frames cannot write. The user approved this early integration and the direct Ranvier.Css dependency. Then port the remaining easing/spring/stagger/interpolation and playback/composition behavior, followed by timelines and animatables. Add typed CSS/DOM and native adapters, then complete the remaining SVG, scroll, drag, text, scope and layout families and View integration. Build acceleration only after behavioral equivalence can be measured. Finish with the matched public cohort and complete audit.

The builder may choose internal data structures, exact F# method/module spelling, test framework, project splitting and batching strategies. Preserve the described behavior, typed authoring and native runtime boundary. Benchmark allocations, interop calls and frame work before choosing SIMD, pooling or a more complex backend. Do not promise speedups without measurements.
