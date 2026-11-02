# Playback timing implementation plan

> **For agentic workers:** Use superpowers:executing-plans inline. Keep the approved full native-port scope intact.

The approved design needs a common clock model for timers, numeric animations and timelines. First prove the timing arithmetic independently, then connect controls and callback transitions; do not count the arithmetic alone as timer or animation playback parity.

## Task 1: source timing geometry

- [x] Generate deterministic standalone timer traces from the clean pinned Anime.js source: repeat counts, loop gaps, reversed/alternate directions, signed delay, zero/fractional/long/infinite durations and signed/backward seeks.
- [x] Add an internal `PlaybackTiming.fs` kernel and `PlaybackTimingTests.fs`. Watch the placeholder fail the source traces in SageFs before implementing.
- [x] Match raw/bounded time, iteration index/time, backwards state, progress and source render eligibility. Use `Math.Clamp` for ordered bounds; preserve source's inverted-delay-bound behavior explicitly rather than changing its signed-time semantics. Reuse the existing source-compatible rounding routine.
- [x] Prove the persisted kernel in SageFs, then run full normal/Traced suites and XML audit through warm Partas.Build. Keep both solutions current; record the exact reference hash and internal-only status.

## Task 2: owned standalone timer

- [ ] Add public timers over the existing Ranvier FrameScheduler, using Task 1 arithmetic. Keep float milliseconds internally; provide uint convenience inputs and explicit finite repeat counts/forever rather than negative or infinity input sentinels.
- [ ] Add source oracle traces for begin/before-update/update/loop/complete/pause callbacks and Seek/Reset/Restart/Play/Reverse/Alternate/Complete controls, including muted callbacks.
- [ ] Reproduce failures first, then implement scheduler speed/anchors, frame cancellation, actual Ranvier ownership and reentrant callback guards. Preserve the established terminal-disposal native contract and document deliberate differences.
- [ ] Verify deterministic scheduler traces, disposal during callbacks, same-frame reentrancy, zero duration, loop-gap boundaries and speed changes. Keep source behavior and native lifetime adaptations distinct.

## Task 3: shared playback integration

- [ ] Integrate the common model with numeric animations and timelines, including timer tracks and nested controllers. Add source comparisons for real numeric/property writes across loops, alternate/reverse boundaries and changing playback speed; a standalone timer passing is insufficient.
- [ ] Preserve current sibling composition, cached construction, callback crossing and ancestor-write guards. Verify owner cleanup and native host scheduling with the existing adapters when qualification permits.
- [ ] Run the full suites, inspect API impact with fslangmcp, update documentation/inventory and obtain one fresh-context review for this scoped plan. Address material findings RED→GREEN in one fix pass. Do not mark the full goal complete.

## Review focus

Check skipped iterations versus callback counts, delay/loop-gap boundaries, 32-bit JavaScript truncation and negative zero-duration iteration indices, infinite versus clamped total durations, mixed forward/backward sampling, render eligibility after completion, sample time versus public rounded getters, reentrant controls during callbacks, and float precision at maximum uint/large values. Current native lifetime adaptations must not silently change ordinary Anime.js clock geometry.
