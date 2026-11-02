# Implementation ledger

Plan: docs/superpowers/plans/2026-11-03-owned-animation-foundation.md

- Design approved by user; use direct Ranvier and Ranvier.Css dependencies.
- Ruling: execute inline as a routine workflow choice under the user's implementation authorization; no further plan approval round trip.
- Ruling: new standalone repository on codex/native-anime provides isolation without an existing parent repository worktree. Existing editor files preserved.
- Pre-flight: Task 2 consumes the same NumericAnimation and FrameScheduler signatures produced/consumed in Task 1. No conflicting type names. Complete port remains a sequence of subsystem plans, not a claim that two foundation tasks cover full parity.
- Task 1: runner implemented; initial six tests RED on the throwing placeholder, then GREEN through Partas.Build Release. Documentation audit and independent milestone review still pending.
- User requires all project operations through a Cloudsmith Partas.Build build.fsx and a maintained .slnx; implemented with Partas.Build 0.8.2, build.defs.fsx, thin CLI shim, and .SageFs/init.fsx.
- User requested warm build invocation. Session 9aeeb8ae owns this directory; loaded build.defs.fsx and bound build(args). Actual warm test pipeline passed 6/6 in 1.8s. Retain this owned warm session while the goal continues; shared daemon untouched.
- SageFs bare Expecto loading hit TypeInitializationException with inner FileNotFoundException for Mono.Cecil 0.11.4.0; direct numeric REPL assertions passed and the actual compiled suite ran through the build command tree. This is not port behavior failure.
- Ruling: use `rtk proxy dotnet fsi` for the CLI shim; `rtk dotnet fsi` injected --preferreduilang:en-US into script arguments. Warm invocations avoid that cold-start path.
- Task 2: reactive numeric drive and typed CSS custom-property animation implemented; each group failed against its placeholder before passing. Full suite now 15/15 Release; XML compiler/convention audit 0 findings.
- Final milestone review: two P2 findings, both reproduced and fixed. Opposite-sign finite endpoints overflowed during interpolation; weighted interpolation fixes the midpoint. The pinned Ranvier package's createRoot already registers a removable parent link; duplicate Attach retained explicitly disposed drives. Removed duplicate registration; weak-reference regression now passes with the parent alive. Initial-reader disposal regression confirms zero writes after the owner ends.
- Ruling: source inspections and package XML contracts guide use of the novel Ranvier library; runtime regressions against pinned packages prove ownership assumptions. Do not infer Solid/React ownership rules or duplicate core primitives.
- Warm-session transport stalled on a build-definition reload. A later 1+1 eval also stalled; cancellation then stopping only owned session 9aeeb8ae returned transport closed. Shared daemon preserved. Replacement owned bare session 03978c87 is Ready with the warm build loaded. Actual test, reference and audit commands succeed there.
- Upstream fixture generator captures 34 named easings, 3 curves, 3 spring configurations and 5 stagger cases from the clean pinned source. Corrected the callback's third argument to the target array; literal step-case sanity check rejects zeroed fixtures. These fixtures are reference evidence, not native parity results.
- Input semantics audit requested by user: consider uint32 whole-millisecond authoring inputs converted exactly to float internally. Preserve a fractional-duration path for capability parity; do not apply unsigned types to signed timeline offsets. Public duration API revision remains next work, before relying on the initial float signature as final.
