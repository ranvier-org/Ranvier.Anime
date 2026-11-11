# Remaining easing curves

Execute inline with the approved native-port design, executing-plans, TDD, SageFs and warm Partas.Build.

## Contract

Add typed LinearStop.Value and LinearStop.At(value, progress) cases and Ease.linear over an array of stops. Explicit progress is normalized; it replaces upstream percentage strings. First/last positions are ignored as upstream does. Preserve authored order, implicit index spacing, duplicate/descending/outside positions, pre-segment extrapolation, final-value fallback and IEEE results at singular segments. Do not sort, repair or clamp stop progress. Empty input is identity; one value is constant.

Add Ease.irregularWith(random, count, randomness) in curried F# form plus Ease.irregular using Random.Shared. Counts use uint, checked against array capacity;0/1/2 still produce source's two endpoints. Randomness accepts finite signed/overshooting values. Generate interior values once, drawing in source order, use Math.Clamp between previous value and1, then reuse linear. Injected draws must follow Math.random's finite [0,1) contract. Evaluation never draws again.

Retain the existing family/Bezier/step APIs. Expand source comparisons across parameter domains, directions and boundaries. No JavaScript string coercion/parser is introduced; direct typed families already expose parameterized authoring.

## Work

- [x] Capture independent JSON fixtures from clean pinned Anime.js4.5.0 source for explicit/implicit/singular/out-of-order linear stops, irregular counts/randomness and deterministic draws, parameterized families, Bezier and step boundaries. Encode nonfinite results explicitly. Generate via build.fsx.
- [x] Observe absent/placeholder curve behavior fail in SageFs, port exact logic, and prove source comparisons plus validation/capture/random-call tests in the REPL before persisting.
- [x] Persist API/tests/fixtures, run full normal/Traced suites, XML audit and one fresh scoped review. Check actual easing use through NumericAnimation. Keep .slnx current; no new project is expected.
- [x] Update parity/native-evidence scope honestly: prior public PE archives do not qualify newly changed source. Fresh native replay is needed before claiming current curves are NativeAOT qualified.

Verified158 source cases ×18 progress points, plus10 validation/runner cases. Identity placeholders failed815 of1314 linear/irregular samples; source port passes all. Full799/799 normal/Traced suites pass. Fresh scoped review found no Critical/Important/Minor issues; fslangmcp remained unknown0/1 analyzed. Both public CPU JIT and strict NativeAOT PEs pass the added curve/layout sentinels. Immutable public45-p7-curves snapshots retain the exact current source; broader engine/interpolation/composition families remain open.

Final XML audit0. Both receipt hashes, all18 archived files and current library/consumer source hashes reverified. Live Release publication still validates338 authored and190 ILC inputs. No build/test/run leases remain held.
