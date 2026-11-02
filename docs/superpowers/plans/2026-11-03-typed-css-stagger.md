# Typed CSS unit staggering

Execute inline with executing-plans, TDD and the warm SageFs/Partas.Build workflow.

## API

Add CssStagger.step and CssStagger.range accepting an existing numeric Ranvier.Css.Value, a constructor `float -> 'value` whose result inherits the same Value grammar, existing StaggerOptions and unsigned target count. Range endpoints must have the same unit. Return the constructor's concrete typed value array.

Use RuntimeApi.tryNumericValue, the existing numeric distribution and the caller's checked constructor. Never create unchecked values with a guessed grammar marker or duplicate CSS value/unit types. Confirm constructor results preserve magnitude and unit. Reject nonnumeric inputs, mixed units and nonfinite computed outputs. Constructor range failures propagate. Numeric Start, Jitter and Modifier use the endpoint unit's magnitudes.

This is a deliberate typed adaptation: source string ranges can combine mismatched unit suffixes without conversion; the typed API rejects that ambiguity. Untyped native CSS interpolation/conversion remains separate work.

## Verification

- Inspect public Ranvier.Css source/metadata and prove generic subtype signatures in SageFs.
- Generate pinned source unit-step/range fixtures, including px/rem/percent/angle/time, negative values and randomized/grid options. Observe a placeholder failing before implementation.
- Test preservation of concrete generated type, constructor validation, nonnumeric and mixed-unit rejection, and nonfinite source outputs. Prove pure logic in SageFs, then persist.
- Full Release/Traced suites, XML audit, one fresh final review; fix concrete findings with regressions and commit evidence.

Timeline positioning and actual native unit/layout integration remain open.

Verified:177/177 full Release tests,177/177 Traced Debug tests and XML audit0. Fresh review found no material issues. An optional constructor-magnitude regression is recorded as deferred in the ledger; constructor-unit mismatch and constructor validation propagation already have tests.
