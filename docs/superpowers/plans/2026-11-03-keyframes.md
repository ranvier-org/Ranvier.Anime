# Keyframe Animation Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans to implement this plan inline, task by task. The overall native port design and implementation are already authorized.

**Goal:** Add source-backed keyframe animation, including raw value arrays, explicit frame sequences, sparse duration rows and normalized offset rows, to native numeric and typed CSS playback.

**Architecture:** Compile authored frames once into normalized endpoint data and timing geometry, then sample them through the existing interpolation kernel and shared Timer. Keep contextual CSS conversion, function-value capture and restoration outside frame dispatch. Reuse NumericProperty, Ranvier.Css.Declaration and Ranvier ownership.

**Tech Stack:** F#, existing Ranvier Core/DOM/CSS interfaces, warm SageFs, Cloudsmith Partas.Build, pinned Anime.js 4.5 source.

**Spec:** `docs/superpowers/specs/2026-11-03-native-anime-design.md`.

## Global Constraints

- Full Anime.js capability scope remains in `parity-inventory.json`; this plan does not close unrelated families.
- Pinned behavioral source: `2c9cf8ea00329f6768c7d7902252ed977d75ce42`; preserve MIT notices and its clean checkout.
- Public whole-millisecond duration conveniences use uint, with named precise float paths. Signed delays remain supported.
- Use Math.Clamp where bounds are ordered. Use existing CSS values and DOM services rather than a replacement CSS contract.
- Every project operation runs through build.fsx/Partas.Build; use SageFs for the inner loop and final compiled normal/Traced gates.
- Current native evidence is public DOM56/CSS2/View45/NativeP7 Windows headless CPU. No GPU, successor-cohort or full-native inference.

## Review Focus

- Raw arrays of one, two and more values have different source meanings; explicit frame arrays must remain unambiguous.
- Missing properties in a duration row hold the previous raw endpoint; explicit frame durations do not redistribute the remaining default duration.
- Offset frames use the previous key's ease and may end before normalized position one; duplicate offsets and an initial zero need source proof.
- Construction rounding, signed delays and inherited units must survive fractional timing and backward jumps without phantom overlaps.
- A modifier, value callback or owner disposal during compilation/sampling must prevent later effects and partially published programs.

## Task 1: Compile source-normalized frame programs

**Files:** Create `src/Ranvier.Anime/KeyframeCompilation.fs`, `tools/keyframes.mjs`, `tests/Ranvier.Anime.Tests/KeyframeCompilationTests.fs`, `tests/reference/keyframes.json`. Modify ValueInterpolation.fs for normalization of already-decomposed endpoints, build.defs.fsx and compile/copy lists.

**Interfaces:**
- Internal FrameInput has optional From/To literals, Duration/Delay floats and per-frame Ease/Modifier callbacks.
- Internal FrameDefaults supplies Duration, Delay, Ease and Modifier.
- FrameProperty supplies Name, captured Original, Frames and optional unit converter.
- KeyframeCompilation.values converts literal arrays to FrameInput arrays.
- KeyframeCompilation.rows returns ordered property/frame arrays from sparse duration rows.
- KeyframeCompilation.offsets takes total duration and normalized offset rows and returns property/frame arrays with source easing transfer.
- KeyframeCompilation.compile takes defaults, captured properties and a liveness guard, returning a complete optional FrameProgram with Delay, IterationDuration and normalized FrameTween array.

- [x] Capture actual upstream animation construction and playback traces for each authoring shape, timing override, hold, offset/ease transfer, relative/unit/color/compound endpoint and fractional/zero boundary.
- [x] Demonstrate RED against the missing native compiler in SageFs.
- [x] Implement normalization and endpoint/timing compilation, reusing ValueInterpolation Data rather than round-tripping previous endpoints through text.
- [x] Verify exact source construction fields, ordering, original immutability, guarded modifier/unit effects and invalid authoring; persist and rerun.
- [x] Run complete normal/Traced suites and XML audit; obtain one fresh scoped review and fix findings.

Task1 checkpoint:44 isolated upstream cases, fixture SHA256 `510ed1fdd5cd7e532634f5a0dfc4a3d88b6413b85d1429dfa6cd7b02f2f84d04`;52 compiler tests and1540/1540 full normal/Traced tests pass. Review found eager parsing of unused originals for explicit nonnumeric endpoint pairs; reproduced and fixed, with three upstream regressions. XML audit0. Normal/Traced JIT and strict NativeAOT construction/interpolation/owner probes pass; immutable evidence is `docs/evidence/native-public45-p7-keyframe-compilation.json`. Both solution project sets remain current; only existing project compile lists changed.

Construction fixtures explicitly use source composition none to isolate this task. Their playback snapshots are retained for Task 2 and do not qualify a native keyframe driver by themselves.

## Task 2: Shared keyframe playback and composition within a sequence

**Files:** Create `src/Ranvier.Anime/KeyframePlayback.fs` and its tests; extend the reference generator with same-parent replace composition and interruption schedules.

**Interfaces:** Consume FrameProgram and existing Timer/ValueInterpolation.sample. Produce a guarded driver with source write eligibility, per-tween history and mutable stretch geometry, usable by numeric and CSS bindings.

- [x] Write RED comparisons for forward/backward/skipped/forced seeks, delayed gaps, short trailing frames, repeated/reversed/alternate playback and constructor-independent callbacks.
- [x] Implement same-parent handoff/hold gates, last-frame restoration, shared timer geometry and generation/ancestor guards.
- [x] Verify native sampling, modifier/ease interruption, owner disposal and staged stretch with full source traces and persisted code.
- [x] Run full normal/Traced gates and one scoped review before committing the connected driver.

Task2 checkpoint:106 isolated source schedules verify exact ordered writes, per-frame history, clock state and callback target snapshots; ten interruption tests pass. Fixture SHA256 `aca33e6acac820922900f9fb3c84dbc7264684d65b60d5e21d30424317a6b818`. Negative-delay reordering required replace predecessor selection during endpoint compilation for relative values and numeric unit inheritance. Review's pre-initialization cancellation callback gap was reproduced and fixed for direct cancellation and actual owner disposal. Full suites1656/1656 normal/Traced, XML0. Both public JIT and strict NativeAOT PEs pass connected native gap/tail/layout/stretch/scheduling/owner probes; evidence is `docs/evidence/native-public45-p7-keyframe-playback-final.json`. Public authoring, function values and scoped revert remain Task3.

## Task 3: Typed authoring, function values and native integration

**Files:** Create typed keyframe authoring/binding facade and tests; extend NativeSmoke, solution project sets if new projects are needed, README, parity inventory and licensing.

**Interfaces:** Public Keyframe<'Target,'Value> and sequence factories wrap compiled floats behind uint/precise duration methods. The target parameter gives callbacks a statically typed KeyframeContext rather than object casts. Bind numeric frames through existing NumericProperty and CSS frames through Declaration/native element contexts. The returned controller exposes shared playback, property-scoped revert and ownership.

Task3 numeric checkpoint: Keyframe factories, target/index/count/previous-cache context, Numeric/NumericPrecise, all seven callbacks, explicit pre-capture owner attachment and scoped Revert are implemented. Nine actual-source input/effect schedules and nineteen contract tests pass; fixture SHA256 `bc56b6fa3bbc06957145de8ef7021f186cf5c46de18e71aa020df2937a474aa4`. The source tests include callback-mutated originals and computed durations overriding subprecision defaults. Native restoration deliberately uses pre-construction snapshots; interpolation reads after input callbacks. Empty sequences use a positive epsilon timer with no property access. Review's pure preflight and shared-cancellation findings were reproduced and fixed in one pass. Full1684/1684 normal/Traced, XML0, both public JIT/strict NativeAOT PEs pass new public numeric probes. Evidence is `docs/evidence/native-public45-p7-public-numeric-keyframes.json`. CSS bindings, sparse row/offset authoring, broader function-return shapes and remaining Task3 checks stay open.

- [ ] Add source comparisons for target/index-aware from/to/duration/delay capture and sparse multi-property/target sequences, including native input adaptations.

CSS binding checkpoint: Declaration/DeclarationOn and CaptureInline/CaptureCss variants reuse Declaration, CSSStyleDeclaration, HTMLElement, ComputedStyleService and CssUnits. Fourteen actual CSS-dispatch schedules and seventeen ownership/snapshot/preflight/reentry contracts pass in SageFs. Captured auto normalizes to source zero. Ruling: native Revert restores one pre-construction snapshot per property, including priority/presence, rather than replaying each source frame's inline snapshot. The pinned source can remove a multi-frame property's inline value when later frame snapshots are absent; both the recorded source result and native restoration contract are asserted. This preserves the approved scoped snapshot contract and changes that cleanup result. Review's shorthand longhand-loss finding was reproduced against the real native CSSOM; known shorthand names now reject before style access. Expanded shorthand snapshots remain required, alongside public sparse rows and offsets.

Implementation latitude: user authorizes behavior-equivalent native implementations and measured optimization waves after the port. Source traces define observable compatibility, rather than requiring the same algorithms or data structures. Keep native adaptation results and qualification boundaries explicit.

CSS gates:1715/1715 normal Release and Traced Debug, XML0. Both public JIT consumers and strict NativeAOT PEs pass real computed capture, independent element unit contexts, layout/color/compound/gap/priority/restoration checks, cancellation after service Get and owner disposal during a scheduled component modifier. The shorthand rejection preserves native longhand priority in a real CSSOM regression. Evidence `docs/evidence/native-public45-p7-public-css-keyframes.json` preserves both immutable source-bound receipts/all eighteen verified files; live Debug provenance checks350 authored and190 compiler inputs. Full reference regeneration retains the earlier hashes and CSS SHA256a4342240e78bf6ffa110e23d9aee76bdf5d487b7fbdf0c50e3971f12e8ca8ee9. Task3 remains open for the authoring gaps above.
- [ ] Implement typed value/sequence/offset authoring and numeric/CSS bindings, callbacks, capture/restoration and current-owner attachment.
- [ ] Verify actual native layout, multiple unit contexts, color/compound output, backward gaps, cancellation and revert on normal/Traced JIT and strict NativeAOT executables.
- [ ] Run full compiled suites, XML audit, scoped review and immutable public-cohort evidence; update capability progress without closing global composition or the full port.

Global replace/add/blend arbitration across independent animations and full timeline controls remain required work beyond sequence-local composition.

## Task 3 follow-up: shorthand snapshots

Reuse the existing public CSS factories. Generate internal shorthand/alias and logical-counterpart metadata from the public NativeP7 manifest's WebKit commit b95c91b1c0ee43cc86d25ff7af73038f67a88e04, reading the pinned Git blob rather than a possibly modified working file. Keep the generator behind Partas.Build and retain provenance/notices. No new public CSS value contract or upstream modification is needed.

- [x] Prove the existing shorthand rejection in SageFs and native CSSOM; add mixed-priority, partial and absent-longhand cases.
- [x] Expand snapshots into affected longhands, including reset-only leaves such as border-image and vendor aliases. Capture declaration order to preserve logical/physical precedence. At restoration, retain current values of unrelated logical counterparts while restoring their required order; new counterpart edits remain after restored original declarations. Avoid whole-style replacement and per-frame reads.
- [x] Verify actual native shorthand expansion, values/priorities/absence, logical precedence, unrelated edits and guarded partial restoration. Remove the temporary rejection only when those cases pass.
- [x] Run full normal/Traced suites, XML audit, one scoped review and fresh source-bound JIT/strict NativeAOT evidence. Keep remaining Task3 authoring and full capability scope active.

Metadata derives from593 properties/110 shorthands/136 aliases and16 logical groups, yielding246 expansion/alias entries. The pinned blob SHA256 is17fcf24a2b1d4e7aee132768a7cc04c99a3de428b15e6bbff5f04f0747b81b74. Both generated lookup tables use FrozenDictionary with ordinal keys, per user instruction. A native all regression corrected the metadata's self-reference into universal longhand expansion, excluding direction/unicode-bidi/custom properties.

Review's Important pending-substitution finding was reproduced against real CSSOM. Recoverable shorthand variables and current pending logical counterparts now use lossless serialized expressions; tests verify later custom-property updates still affect layout. Mixed overrides can make WebCore's CSSOM serialization omit pending values, and a pending carrier can cross a requested longhand scope. These cases reject before dispatch rather than lose original data. Existing Typed OM APIs were inspected: CSSShorthandSubstitutionValue reification into CSSUnparsedValue drops the original shorthand context. Asked the authorized implementation coordinator for an existing scoped opaque snapshot/copy primitive; no upstream defect claim. Full opaque pending-value restoration remains required port work. The shared snapshot helper should also replace the older animatable CSS capture after this keyframe checkpoint.

Checkpoint gates:26 new metadata/parser/snapshot contracts; full1741/1741 normal Release and Traced Debug, XML0. Real JIT and strict NativeAOT PEs pass expanded margins, mixed priorities, reset-only border fields, aliases, both logical orders, universal all exclusions and dynamic variable-dependency restoration. Opaque cases preserve native inputs through pre-dispatch rejection. Immutable evidence `docs/evidence/native-public45-p7-css-shorthand-snapshots.json` verifies both receipts/all18 files; current strict Debug checks353 authored and190 compiler inputs. Remaining Task3 and the complete port goal stay active.

### Shared animatable snapshot follow-up

Replace the older InlineCapture in AnimatableCss.fs with the same scoped helper, retaining captured initial text and per-target evaluation order. No public signature changes. This closes the same native shorthand longhand-loss issue for cached retargetable handles.

- [x] Reproduce expanded-side rendering followed by lost original longhands in native animatable Revert.
- [x] Prove the shared binding in SageFs, persist it, and rerun existing CSS animatable source/lifetime cases plus registration, capture-cancellation and multi-target restoration regressions.
- [x] Verify native mixed-priority margins, logical precedence, alias/cache retargeting, actual computed shorthand components, variable dependence and non-destructive opaque admission.
- [x] Run full normal/Traced suites, XML audit, one scoped review and fresh JIT/strict NativeAOT evidence. Keep opaque pending snapshots and wider Task3/full-port scope active.

Shared animatable checkpoint:1745/1745 full normal Release and Traced Debug, XML0; scoped review no findings. Native JIT and strict NativeAOT verify mixed priorities/absence/unrelated edits, both logical orders, alias/cache retarget50→125px, computed margins55/110/165/220px, native variable dependence and later group use after rejected opaque capture. Four facade contracts cover admission/registration, getter cancellation and guarded first-fault restoration across leaves/targets. Evidence `docs/evidence/native-public45-p7-animatable-css-shorthands.json` binds both receipts/all18 archived files, with current Debug353 authored/190 compiler inputs. Public signatures and solution sets remain current. Full opaque pending snapshots and public sparse rows/offsets remain required Task3 work.

### Public sparse rows and offsets

Use immutable KeyframeRow<'Target,'Value> and KeyframeOffset<'Target,'Value> values inside KeyframeSequence<'Target,'Value>. Rows name constant or target-aware destinations, share uint/precise duration, signed delay, ease and modifier providers, and expand omitted properties to holds. Offsets retain stable sorting, sparse property timing, first-key source transfer, prior-key easing and removal of an initial zero key. Reuse existing Keyframe providers internally so destination/source/duration/delay effects retain their existing construction order.

Numeric sequence overloads bind named NumericProperty arrays with equal target counts, with a single-target convenience. CSS sequence overloads reuse existing declarations, inline/computed services, element-unit contexts and scoped snapshots. One FrameProgram, KeyframePlayback and Timer span every captured target/property; there are no independent clocks per property. Expose these as additional Keyframes overloads via an auto-open extension module, retaining the existing single-property facade.

- [x] Prove row/offset normalization and callback providers in SageFs, including sparse holds and zero/duplicate/sorted offsets. Verify public normalized sequences against the pinned construction/playback fixtures.
- [x] Bind named numeric properties and typed CSS targets through one owned driver, with pure preflight before getters, atomic staging, per-target contexts and guarded property-scoped restoration.
- [x] Compare actual source writes, callbacks and timing; cover input immutability, unused handles, unequal target counts, reentry, disposal and first faults.
- [x] Run real native multi-property numeric/CSS layout, contexts, callbacks, stretch and revert on normal/Traced JIT and strict NativeAOT. Finish full compiled suites/XML, one scoped review and immutable evidence; preserve the complete remaining port scope.

Checkpoint:79 added cases (9 normalization,18 public playback,32 new source input schedules,20 contracts),1824/1824 full normal/Traced tests, XML0 and scoped review with no introduced findings. All pinned fixtures regenerate unchanged; the new fixture hashes to274eec364b1ebeed242d5982357533f42c82b8ebca323868395cbbb8ea9b1c5a. Real native numeric/CSS rows and offsets pass layout, contexts, shared callbacks/stretch, scoped revert and owner guards on JIT/strict NativeAOT in both modes. Evidence docs/evidence/native-public45-p7-public-keyframe-sequences.json binds both receipts/all18 files and62 current library hashes plus both consumers;355 authored/190 compiler inputs verified. This completes the row/offset checkpoint; broader function-return shapes, refresh, opaque CSS snapshots, timeline integration and remaining full-port capabilities remain required.
