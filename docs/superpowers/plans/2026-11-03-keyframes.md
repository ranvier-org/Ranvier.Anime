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

- [ ] Write RED comparisons for forward/backward/skipped/forced seeks, delayed gaps, short trailing frames, repeated/reversed/alternate playback and constructor-independent callbacks.
- [ ] Implement same-parent handoff/hold gates, last-frame restoration, shared timer geometry and generation/ancestor guards.
- [ ] Verify native sampling, modifier/ease interruption, owner disposal and staged stretch with full source traces and persisted code.
- [ ] Run full normal/Traced gates and one scoped review before committing the connected driver.

## Task 3: Typed authoring, function values and native integration

**Files:** Create typed keyframe authoring/binding facade and tests; extend NativeSmoke, solution project sets if new projects are needed, README, parity inventory and licensing.

**Interfaces:** Public Keyframe<'T> and sequence factories wrap compiled floats behind uint/precise duration methods. Bind numeric frames through existing NumericProperty and CSS frames through Declaration/native element contexts. The returned controller exposes shared playback, property-scoped revert and ownership.

- [ ] Add source comparisons for target/index-aware from/to/duration/delay capture and sparse multi-property/target sequences, including native input adaptations.
- [ ] Implement typed value/sequence/offset authoring and numeric/CSS bindings, callbacks, capture/restoration and current-owner attachment.
- [ ] Verify actual native layout, multiple unit contexts, color/compound output, backward gaps, cancellation and revert on normal/Traced JIT and strict NativeAOT executables.
- [ ] Run full compiled suites, XML audit, scoped review and immutable public-cohort evidence; update capability progress without closing global composition or the full port.

Global replace/add/blend arbitration across independent animations and full timeline controls remain required work beyond sequence-local composition.
