# Easing parity implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans inline with red/green tests.

**Goal:** Match pinned Anime.js named easing families, cubic Bezier and step functions, then use them in numeric animations.

**Architecture:** Existing scheduler/ownership runner consumes easing functions. Typed F# family and direction unions provide authoring; a named lookup supports upstream fixture comparison. Pure functions are checked in SageFs, then persisted tests run through the warm Partas.Build command tree.

**Tech Stack:** Existing .NET 10 projects; Ranvier normal Release and matched Traced Debug packages; System.Text.Json reads test fixtures.

**Spec:** ../specs/2026-11-03-native-anime-design.md

## Global constraints

- Preserve fractional internal time and upstream sampling behavior; duration input changes must keep an explicit fractional path.
- Reference v4.5.0 / 2c9cf8ea00329f6768c7d7902252ed977d75ce42; retain upstream and embedded formula attribution.
- All project operations use build.fsx/shared warm build definitions. Maintain Ranvier.Anime.slnx when projects change.
- Spring, irregular and piecewise-linear easing remain later required tasks; do not claim complete easing parity from this task.

## Review focus

- Exact endpoints and exponential zero handling.
- Bounce intervals and direction composition, especially midpoint boundaries.
- Step start versus end behavior at exact boundaries.
- Cubic solver convergence matches the upstream tolerance and iteration bound.
- Easing overshoot must reach the target setter rather than being clamped.

## Task 1: Pure easing families and curves

**Files:** src/Ranvier.Anime/Ease.fs; tests/Ranvier.Anime.Tests/EaseTests.fs; existing project compile lists; tests/reference/pure.json.

**Interfaces:** EaseFamily and EaseDirection discriminated unions; Ease.create : EaseFamily -> EaseDirection -> (float -> float); Ease.named : string -> (float -> float); Ease.steps : uint -> bool -> (float -> float); Ease.cubicBezier : float -> float -> float -> float -> (float -> float).

- [ ] Test each of the 34 named functions at all nine upstream fixture points with absolute tolerance 1e-12; test the three curve fixtures. Tests must load the tracked fixture copied into the test output, not regenerate expectations from native code.
- [ ] Add explicit test that steps 0u raises; malformed named easing produces a useful argument error; named Back and Elastic defaults return finite endpoints.
- [ ] Run identity placeholders to observe comparison failures, then port the exact directional/formula logic from pinned source. Preserve bisection tolerance 1e-7 and bound 100.
- [ ] Verify pure functions in SageFs, persist, run all Release and Traced Debug tests and XML audit through build commands, then commit.

## Task 2: Runner easing integration

**Files:** NumericAnimation.fs; RunnerTests.fs.

**Interfaces:** NumericAnimation gains an optional easing function, defaulting to linear. Existing convenience factory remains linear; a named factory exposes a custom easing function without changing existing calls.

- [ ] Run refactor impact before public constructor changes.
- [ ] Test a quadratic midpoint of 25 for 0→100 at 50/100ms; custom easing 1.25 renders 125 at initialization and completion as proven by the pinned upstream fixture. Linear easing retains exact endpoints. Verify cancellation and reentrant seek from an easing callback supersede the interrupted render.
- [ ] Implement easing of normalized progress before interpolation; preserve finite-endpoint handling.
- [ ] Run complete normal/Traced tests and XML audit. Commit, update ledger and parity inventory. This task remains part of the full port goal, not its completion.
