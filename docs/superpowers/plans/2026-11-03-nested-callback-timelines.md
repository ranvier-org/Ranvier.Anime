# Nested timelines and callback tracks

Execute inline with executing-plans, TDD, SageFs and warm Partas.Build.

## Contract

- Generalize private child storage to numeric, nested and callback entries; do not expose a replacement scheduler or lifetime contract.
- Sync(Timeline) borrows and pauses a child controller, captures its duration, propagates the outer write guard through all descendants and rejects direct/indirect synchronization cycles.
- Call(unit -> unit, position) follows source zero-duration track offsets, crossing/completion state, insertion order forward and reverse order backward. Repeated seeks must not refire without the source transition. Seek accepts optional muteCallbacks; Reset suppresses direct callback tracks and resets their completion flags.
- Use actual pinned source traces to establish callback propagation through synchronized nested controllers, including parent mute/reset behavior. Do not assume recursive muting.
- Keep raw negative sampling time for signed-offset tracks while CurrentTime remains clamped, if source fixtures require it.
- Mark callback state before invoking user code so reentrant seeks cannot recursively repeat the same forward event; outer sampling must stop after the generation changes. Record this deliberate safe adaptation where source reentrancy would recurse indefinitely.

## Verification

Generate source callback traces at start/middle/end, equal-time ordering, repeated boundaries, muted seeks, backward seeks, reset/restart and negative positions. Generate actual nested sync traces. Observe unsupported methods failing before implementation; prove pure event transitions and nested guards in SageFs before persistence. Test cycle rejection, ancestor cancellation during nested easing/callbacks and borrowed lifetime. Full normal/Traced suites, XML audit and a fresh review; fix material findings through regressions and commit.

Direct property composition, set/timer tracks, loop/direction/speed, removal and full native integration remain required work.

Verified:235/235 full tests pass Release and Traced Debug; XML audit0. Fresh review's Reset reentrancy finding was reproduced RED against persisted source in SageFs, fixed with a generation guard and verified GREEN after reload and in the compiled suite. No other material findings.
