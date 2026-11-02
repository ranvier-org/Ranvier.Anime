# Numeric property tracks and sets

Execute inline with executing-plans, TDD, SageFs and warm Partas.Build.

## Contract

Add a sealed NumericProperty handle over explicit F# read/write functions. Reuse one handle for the same property to establish stable identity; no reflection and no alternative CSS contract. Add Timeline.Animate(property, target, duration, position/ease/from/modifier options) and Set(property, value, position). Durations remain finite nonnegative doubles, preserving fractional/long paths; set tracks use source zero-duration timing.

Port scalar replace composition: ordered siblings, internal composition sampling, captured from values, truncation and override state, handoff/gap gates and backward rendering. Use the pinned source's actual traces rather than a latest-track approximation. Property sampling and user easing/modifiers must retain controller/ancestor write guards. Internal preparation updates cached values without dispatching property writes. Initialization/reset modes must reflect source behavior.

## Verification

Generate source cases for sequential/gapped/overlapping tracks, short takeover, equal starts, out-of-order insertion, explicit from values, negative/fractional positions, sets and custom easing/modifiers. Include forward/backward and repeated seeks plus initialization/reset outputs. Observe placeholders failing; prove the scalar kernel in SageFs, persist and test actual compiled controllers. Verify independent property identity, reentrant cancellation/seek and unrelated property preservation.

Full Release/Traced suites, XML audit and fresh review; fix material findings with regressions. Update the inventory without claiming additive/blend, global cross-controller composition, keyframes, native CSS conversion or complete property parity.

Verified:56 source schedules (32 generated) compare initialization, forward/backward sampling and reset, including tiny and overflowed duration boundaries.299/299 full tests pass Release and Traced Debug; XML audit0. Fresh review's construction-precision finding was reproduced RED and corrected with separate 12-place tween/update and finite controller durations. The additive uint authoring overload retains the precise path. Fixed-zero source clock generations are byte-identical (SHA256 febe7dd6f587aec33f0a817d6fc87ab57e43ec5539a85d919c7d5180a4fffb75). Positive durations rounding to zero are rejected as documented.
