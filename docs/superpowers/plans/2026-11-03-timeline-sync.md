# Timeline positioning and synchronized numeric children

Execute inline with executing-plans, TDD, SageFs and warm Partas.Build operations.

## Contract

Introduce TimelinePosition.At(float) and Expression(string), plus a paused Timeline driven by the existing Ranvier.Dom.FrameScheduler. Position resolution supports append, numeric text, labels, previous-child start/end and relative addition/subtraction/multiplication from the pinned source. Signed positions remain floating point. Unknown plain labels append; invalid/nonfinite relative results raise an argument error. Previous-end with no child raises an argument error instead of copying the source null dereference.

Sync borrows and pauses an existing NumericAnimation and samples its currentTime using its exposed Duration. Children sample in insertion order forward and reverse order backward; untouched future children do not overwrite active values. Guard all callbacks with the timeline generation, so reentrant seeks/cancellation supersede an outer sample. The timeline controls one frame subscription and reuses Ranvier ownership through an optional own helper. Cancellation is terminal and retains rendered values, matching the native runner's established lifetime policy; borrowed children remain usable and paused.

Controls: Play, Pause, Seek, Reset, Restart, Complete, Cancel and read-only Duration/CurrentTime/Playing/Completed. Labels resolve against the current schedule. SyncStaggered captures the original timeline duration once, applies offsets before modifiers, and adds all children against that stable base.

## Verification

- Generate pinned-source position and synchronized child traces, including backward seeks and overlapping/gapped schedules. Compare actual outputs before claiming behavior.
- Observe placeholders failing, then prove pure parsing/calculation in SageFs before persistence.
- Test manual scheduling, reentrant child writes, borrowed-child lifetime and Ranvier owner cleanup.
- Full Release/Traced suites, XML audit, fresh final review and regression fixes.

This milestone does not complete timelines: direct property composition, nested timelines, timer/call/set tracks, direction/loop/speed controls, removal, stretch, refresh/revert and completion promises remain required work.

Verified:29 source position cases,3 source synchronization traces and7 source stagger position cases;222/222 full tests pass normal Release and Traced Debug with XML audit0. Fresh review's two findings were reproduced RED and fixed: cancellation from easing cannot write afterward, and repeated synchronized children follow upstream backward past-end skipping. Borrowed-child lifetime and actual Ranvier owner attachment are tested.
