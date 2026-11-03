# Timeline Keyframes Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans to implement inline. The user has already approved the native port design and implementation; this plan records the next authorized integration work.

**Goal:** Add owned numeric and typed CSS keyframe animations to Timeline with source-backed child playback, callback phases, construction, offsets and lifetime semantics.

**Architecture:** Reuse KeyframeCompilation, KeyframePlayback and the existing Timer child phase. Extend that phase for cache-only preparation and rendered-child feedback. Timeline owns each added controller, attaches before construction effects and passes an ancestor/generation guard; borrowed Sync retains its separate contract. Existing Keyframe and KeyframeSequence values and Ranvier.Css declarations remain the authoring model.

**Tech Stack:** F#, Ranvier ownership/FrameScheduler/Css, warm SageFs, Cloudsmith Partas.Build, pinned Anime.js4.5 source.

**Spec:** docs/superpowers/specs/2026-11-03-native-anime-design.md.

## Global Constraints

- Full16-family parity scope remains active; a successful child adapter does not close global composition, refresh, removal, revert, promises or other families.
- Source baseline2c9cf8ea00329f6768c7d7902252ed977d75ce42; retain MIT notices and the clean source checkout.
- Public whole-ms conveniences use uint with named precise float paths; delays/positions remain signed.
- Use actual Ranvier owners and existing CSS values/services, not replacement public contracts.
- All project operations use build.fsx/Partas.Build, hot-loaded into SageFs. Full normal/Traced suites and strict native PE evidence remain the gate.
- Current public native cohort is Corepreview1/DOM56/CSS2/View45/NativeP7, Windows headless CPU. No acceleration, successor or broader platform claim.

## Review Focus

- Parent cancellation or a reentrant seek during input callbacks must stop later getters/providers and child publication.
- Adding a child prepares earlier child caches at the insertion position and resets history without target writes.
- Parent mute/direction/iteration rules differ from borrowed current-time setter semantics.
- Owned child writes must contribute to one parent render notification in source order.
- Same-property overlaps require predecessor endpoint selection and replace arbitration across children; independent clocks cannot substitute.

## Task1: Capture the actual source timeline contract

**Files:** tools/timeline-keyframes.mjs; tests/reference/timeline-keyframes.json; build.defs.fsx.

**Interface:** Isolated pinned JSAnimation children created through Timeline.add. Capture constructor/reset writes, input/modifier effects, root/child callback order and state snapshots, child geometry and sampled outputs. Include raw arrays, explicit frames, rows/offsets, multiple targets/properties, signed/fractional offsets, gaps, overlap, loop/reversal/alternate, speed/FPS and muted/reset/complete operations.

- [x] Generate source schedules via build ["timeline-keyframe-reference"]; assert a clean pinned source, actual child parentage and animation identity.
- [x] Repeat regeneration and verify byte stability; retain all earlier fixture hashes.
- [x] Demonstrate missing owned keyframe facade in SageFs and the failure of a naive borrowed wrapper against owned child callbacks.

Task1 evidence:36 isolated source schedules include cross-child providers and modified caches, pre/post-overlap programs, constructor writes/effects/callbacks and full sample histories. One ordered trace retains observations across all channels. Generator assertions require zero construction dispatch, muted owned lifecycle suppression,75ms overlap truncation/new start and101→111 modified-cache chaining. An exact lifecycle/effect/write order check rejects a cross-channel mutation. Final fixture SHA2561300cbc21ce79770c0c0765767601a1911a9d1e9ee4382251407cbd732ecf7bb. Sage confirms AddKeyframes is missing. Normal raw playback through Sync matches one source sample, while muted playback emits four borrowed child callbacks versus none for the owned source child. A persisted borrowed-contract regression protects that intentional existing Sync behavior. These are oracle/boundary proofs, not a passing owned adapter.

## Task2: Owned child phase and construction

**Files:** PlaybackState.fs, Timer.fs, Timeline.fs, KeyframePlayback.fs, Keyframes.fs, KeyframeSequenceBinding.fs; new TimelineKeyframes.fs and TimelineKeyframeTests.fs; existing compile lists.

**Interfaces:** Add a cache-only child sample context to the existing internal child phase. Report actual keyframe dispatch without inferring it from a nonzero clock. Add an internal timeline factory transaction that resolves position, registers pending cleanup before input effects, prepares existing children at the offset, constructs under the parent guard, attaches an owned child and resets cache/history. Public Timeline.AddKeyframes/AddKeyframesPrecise overloads return owned KeyframeAnimation controllers for existing numeric handles and typed CSS factories; existing standalone factories keep their behavior.

Source construction evidence: adding child b at75 internally samples a to75, uses that value as b's source, truncates a's first change duration to75 and overrides a's future tween to epsilon. A separate boundary case passes modified101 from a into b's first input provider, then111 into its next provider. This requires a same-parent predecessor/arbitration registry spanning programs; independent compilation against target getters cannot implement the captured behavior. Registry entries need raw endpoint metadata, mutable sampled cache and absolute timing, with lifetime cleanup owned by the timeline. The source fixture's per-tween value is the current cache after internal resets, not a permanently cached endpoint.

Source animation.js430-507 distinguishes raw preceding endpoints within a child from sampled sibling values for a child's first missing endpoint. Relative source operands still use predecessor raw to metadata. Preserve these as separate fields. Render feedback should compare a dispatch generation before/after sampling, so a skipped child cannot retain a stale true rendered flag. Parent cache-only samples update current caches and modifier effects while suppressing target writes.

- [ ] RED: replay Task1 root/child callbacks, self-state and target snapshots using a minimal owned factory, not Sync.
- [ ] GREEN: implement inherited mute/direction/FPS/loop and cache-only construction through one Timer child phase; pass source offsets into compilation.
- [ ] Preserve all existing owned timer and borrowed tests; prove owner cancellation, reentry, getter faults, child cancellation, constructor interruption and no independent frame lease.
- [ ] Implement same-parent overlap predecessor/cache/arbitration against the captured cases; retain independent-animation/global registry work explicitly if separate.
- [ ] Add numeric raw/explicit/row/offset and typed CSS inline/computed/declaration overloads, with uint/precise durations and existing scoped restoration.

## Task3: Qualify and record

**Files:** NativeSmoke Program.fs; README, parity-inventory.json, implementation-ledger.md and native-integration.md.

- [ ] Prove real multi-property native layout and unit contexts, shared root callback, offset/gap/repeat behavior, owner interruption and scoped restoration.
- [ ] Run full normal/Traced suites, XML audit, all reference regeneration and one scoped review; fix concrete findings in one pass.
- [ ] Stage/diff-check source, then run matched public normal/Traced JIT and strict NativeAOT PE publication/execution; archive under a fresh immutable label.
- [ ] Verify receipt/archive/source hashes, update both solution sets and current capability progress, commit; keep the full port goal active.
