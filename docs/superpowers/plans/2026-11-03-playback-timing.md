# Playback timing implementation plan

> **For agentic workers:** Use superpowers:executing-plans inline. Keep the approved full native-port scope intact.

The approved design needs a common clock model for timers, numeric animations and timelines. First prove the timing arithmetic independently, then connect controls and callback transitions; do not count the arithmetic alone as timer or animation playback parity.

## Task 1: source timing geometry

- [x] Generate deterministic standalone timer traces from the clean pinned Anime.js source: repeat counts, loop gaps, reversed/alternate directions, signed delay, zero/fractional/long/infinite durations and signed/backward seeks.
- [x] Add an internal `PlaybackTiming.fs` kernel and `PlaybackTimingTests.fs`. Watch the placeholder fail the source traces in SageFs before implementing.
- [x] Match raw/bounded time, iteration index/time, backwards state, progress and source render eligibility. Use `Math.Clamp` for ordered bounds; preserve source's inverted-delay-bound behavior explicitly rather than changing its signed-time semantics. Reuse the existing source-compatible rounding routine.
- [x] Prove the persisted kernel in SageFs, then run full normal/Traced suites and XML audit through warm Partas.Build. Keep both solutions current; record the exact reference hash and internal-only status.

## Task 2: owned standalone timer

- [x] Add public timers over the existing Ranvier FrameScheduler, using Task 1 arithmetic. Keep float milliseconds internally; provide uint convenience inputs and explicit finite repeat counts/forever rather than negative or infinity input sentinels.
- [x] Add source oracle traces for begin/before-update/update/loop/complete/pause callbacks and Seek/Reset/Restart/Play/Reverse/Alternate/Complete controls, including muted callbacks.
- [x] Prove and persist internal standalone callback/control state against 51 source traces and seven native interruption/validation tests. This is the wrapper's state machine, not a public scheduled timer.
- [x] Reproduce failures first, then implement scheduler speed/anchors, frame cancellation, actual Ranvier ownership and reentrant callback guards. Preserve the established terminal-disposal native contract and document deliberate differences.
- [x] Verify deterministic scheduler traces, disposal during callbacks, same-frame reentrancy, zero duration, loop-gap boundaries and speed changes. Keep source behavior and native lifetime adaptations distinct.

Task 2 evidence: 18 admitted-frame source traces plus 11 lifetime/input tests; full suites 441/441 Release and Traced Debug, XML audit zero. Source wake lead, speed anchors, low-FPS tick modes, setters and infinite rollover are retained. A real Traced WebCore JIT smoke verifies repeats/completion and owner disposal inside an active timer callback. No new NativeAOT claim is made for this added source. Task 3 and the final scoped review remain open.

## Task 3: shared playback integration

- [x] Connect numeric animation and typed CSS property rendering to the shared timer; compare source property values and callback phases across repeats/directions/speed changes. Verify current ancestor guards and active-owner cancellation.
- [x] Integrate the common model with numeric animations and timelines, including timer tracks and nested controllers. Add source comparisons for real numeric/property writes across loops, alternate/reverse boundaries and changing playback speed; a standalone timer passing is insufficient.
- [x] Preserve current sibling composition, cached construction, callback crossing and ancestor-write guards. Verify owner cleanup and native host scheduling with the existing adapters when qualification permits.
- [ ] Run the full suites, inspect API impact with fslangmcp, update documentation/inventory and obtain one fresh-context review for this scoped plan. Address material findings RED→GREEN in one fix pass. Do not mark the full goal complete.

Task 3 numeric progress: 46 source animation traces (24 generated), three interruption tests and two typed CSS playback tests; full suites 492/492 normal Release and Traced Debug, XML audit zero. Additional source cases cover negative periods, inverted bounds and the signed32 iteration boundary. A fresh successful Traced native JIT build/run verifies typed CSS repeat/alternate output and retained priority. Timelines still use their prior clock; timer tracks, nested repeated controllers and the final scoped review remain open. Historical NativeAOT receipts do not qualify the changed source.

Current native replay: the added timer/numeric code now passes both strict normal and Traced NativeAOT publishes and PE executions against actual public Core/DOM alpha.56 and CSS alpha.2. Our generated F# diagnostic/debugger formatting roots were corrected without upstream edits or IL suppression. New schema3 receipts preserve exact source/package/native identities. View/runtime remain private; this closes current playback PE replay, not public-cohort/GPU acceptance or the remaining Task3 timeline work.

## Review focus

Task3 timer/borrowed integration: AddTimer owns timer scheduling/lifetimes; Sync now supports Timer and samples borrowed timer/animation/nested controllers through scalar property gates. All57 source fixtures pass actual Timeline comparisons (including owned/borrowed callback self-state), plus52 root-clock aggregate replays. Eight new lifetime cases bring the full suites to623/623 normal Release and Traced Debug; XML audit0. Child speed is applied before rounding/backwards skips; source reverse-loop setter completions and FPS endpoint admission are retained. A fresh Traced public-Core host build/run verifies owned timer tracks, borrowed repeated-timer output, owner cancellation and zero host errors. Current source still needs strict PE replay and one fresh-context scoped review before this plan closes. Full property-track callback/keyframe/composition families remain part of the overall goal.

Task3 public timeline integration: Timeline now composes the shared Timer and resizes its iteration geometry as tracks are authored. Twenty-one source comparisons verify actual numeric property/set writes, root state/callback observations and call crossings, including repeat/alternate/reverse, signed delay/gaps, skipped iterations, zero call tracks and a scheduled speed/control trace. Eight owner/reentrancy/authoring tests pass. The source oracle now contains33 cases (SHA2569d60a261ca219d0dc425bcfe0721a808cd3e82e083b61d6837b78f1ae0ace9e5), including30 root-clock cases with recorded child aggregates. Full normal/Traced suites557/557, XML audit0 and fresh Traced public-Core JIT host checks pass. Added timer tracks, per-child callbacks, broader borrowed/nested playback and low-FPS source scheduling remain open; Task3 is not complete and the final scoped review is still pending.

Task 3 timeline preparation:31 additional source traces now record root/child state, property values, all callback phases, repeated/alternate/reversed timelines, timer tracks, borrowed timer/animation/nested controllers, muting and admitted frames. Internal PlaybackState child-phase support passes29 root clock traces using recorded child completion aggregates, plus six ordering/interruption cases. This proves the root clock boundary rather than native child rendering; Timeline still uses its earlier driver, and the two scheduled cases await integration. Full suites527/527 normal Release and Traced Debug, XML audit0. No new NativeAOT claim for this source change.

Check skipped iterations versus callback counts, delay/loop-gap boundaries, 32-bit JavaScript truncation and negative zero-duration iteration indices, infinite versus clamped total durations, mixed forward/backward sampling, render eligibility after completion, sample time versus public rounded getters, reentrant controls during callbacks, and float precision at maximum uint/large values. Current native lifetime adaptations must not silently change ordinary Anime.js clock geometry.
