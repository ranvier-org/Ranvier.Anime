# Owned animation foundation implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox syntax for tracking.

**Goal:** Establish a deterministic numeric animation runner integrated with Ranvier ownership and reactive retargeting, and reuse existing typed CSS numeric metadata.

**Architecture:** A portable F# library references Ranvier, Ranvier.Dom and Ranvier.Css. The runner consumes the existing FrameScheduler, writes object properties directly and attaches owned animations to Ranvier owners. The CSS entry point consumes existing Value and CustomProperty types.

**Tech Stack:** .NET 10, FSharp.Core 8.0.100, Ranvier 0.1.1-preview.1, Ranvier.Dom 0.1.0-alpha.55, Ranvier.Css 0.1.1-alpha.1, Expecto 10.2.3, SageFs.

**Spec:** C:/wk/ranvier-anime/docs/superpowers/specs/2026-11-03-native-anime-design.md (approved in chat with early Ranvier integration and direct CSS dependency).

## Global constraints

- Working home C:/wk/ranvier-anime; Partas.AnimeJs excluded.
- Upstream Anime.js v4.5.0 at 2c9cf8ea00329f6768c7d7902252ed977d75ce42.
- No new public CSS or scheduler contract. No JavaScript execution in the native implementation.
- Preserve upstream MIT notice for derived code and tests.
- Native source drafting baseline and public qualification constraints are recorded in the design and coordinator handoff.
- This milestone does not satisfy the complete port goal. Full parity, native integration and acceleration remain required.

## Review focus

- Disposing during a target setter must prevent a replacement frame from being queued.
- Retargeting at a partial value must start from that value, not the previous endpoint.
- A dead owner must prevent writes even when a queued callback was already captured.
- Invalid duration or nonfinite endpoints must fail before target mutation or scheduling.
- Typed endpoints with different units must fail before a CSS write.

## Task 1: Numeric runner and ownership

**Files:** src/Ranvier.Anime/NumericAnimation.fs, src/Ranvier.Anime/Ranvier.Anime.fsproj; tests/Ranvier.Anime.Tests/RunnerTests.fs and test project; shared build props.

**Interfaces:** NumericAnimation(scheduler: FrameScheduler, read: unit -> float, write: float -> unit, target: float, duration: float) implements IDisposable. Methods Play, Pause, Seek(milliseconds), Retarget(target), Cancel; properties CurrentTime, Completed, Playing. Starts paused. Animation.animate scheduler read write target duration constructs and plays; Animation.own attaches an instance to the current Ranvier owner.

- [ ] Write failing tests: 0→100 over 100ms yields 25 at 25ms and 100 at 100ms; pause holds the value; resume uses remaining duration; seek renders immediately; retarget from 25→125 gives 75 after 50ms; disposing an owner prevents subsequent writes; a setter that cancels on its first frame receives no later writes; duration NaN/negative and infinite endpoint are rejected without writes.
- [ ] Run tests against a minimal throwing placeholder in SageFs; record the expected missing behavior failure.
- [ ] Implement the runner using FrameScheduler.Frame/Now and disposable frame subscriptions; enforce graph-thread access through the scheduler consumer's native adapter later, not a second dispatcher contract.
- [ ] Verify tests in SageFs, persist the source, build/load the test project and run the same tests against the persisted assembly.
- [ ] Run full Release test executable, then commit feat: add owned numeric animation runner.

## Task 2: Reactive retargeting and typed CSS endpoints

**Files:** src/Ranvier.Anime/Reactive.fs, Css.fs; tests/Ranvier.Anime.Tests/ReactiveTests.fs, CssTests.fs.

**Interfaces:** Reactive.drive scheduler duration read write : IDisposable seeds the first read and retargets later reads using an owned numeric animation. CssAnimation.endpoints(variable: CustomProperty<'syntax>, first: Value<'syntax>, last: Value<'syntax>) validates numeric metadata and returns numeric endpoints plus unit internally. Expose CSS behavior through a shared CSSStyleDeclaration entry point rather than a duplicate public endpoint type.

- [ ] Write failing tests: signal 0 seeds immediately, changing to 100 then stepping 25ms yields 25, retargeting to 200 then 50ms yields 112.5; owner disposal prevents updates and later signal reads. Explicit disposal detaches the effect. Test mismatched units fail before writes, and serialized midpoint/end values retain the unit and property name.
- [ ] Use real Ranvier Graph/Owner/effects and ManualFrameScheduler. Validate in SageFs before persisting.
- [ ] Implement using Api.createEffectOn or an owned root, existing numeric animation and RuntimeApi.tryNumericValue/customPropertyName. Use invariant numeric formatting and preserve custom property name; DOM reads and broader interpolation remain later tasks.
- [ ] Run the complete suite and documentation audit; commit feat: add reactive and typed CSS animation foundations.

## Remaining port roadmap

After this foundation, write focused plans against the approved design for the next coherent subsystems, using upstream comparison fixtures before ported behavior: all easing/spring/stagger/interpolation families; complete timers/controls/callback ordering; composition and timelines; animatables and utilities; DOM/typed CSS and native host integration; SVG/scroll/drag/text/scope/layout; View Motion integration; accelerated eligibility and equivalent controls; matched public package consumers, AOT/platform evidence and the final full parity audit. These remain required and are tracked by parity-inventory.json. Do not mark the goal complete at this milestone.

## Final verification

Run `rtk dotnet run --project tests/Ranvier.Anime.Tests -c Release` unfiltered. Expected: all tests pass with nonzero test count. Run XML documentation audit and inspect the actual source comments under comment-hygiene. Request one fresh review for this milestone after these checks; correct material findings with a failing regression first. Preserve concrete evidence and resume from the first incomplete roadmap area.
