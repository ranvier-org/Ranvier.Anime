# Retargetable animatables

Continue the approved full port using the pinned Anime.js 4.5 source as a behavioral reference. Implementation structure need not match JavaScript: prefer Ranvier ownership, shared scheduling, typed CSS and efficient cached state. Preserve observable behavior unless a native adaptation is explicitly documented. Measure uncertain optimizations in later waves.

## Work

- [x] Capture isolated actual-engine traces for scalar, unit, color and compound getters/retargets; partial component updates; multiple targets/properties; duration/easing retention and stretch; modifiers; callbacks and revert.
- [ ] Specify typed property handles and group lifetime using the existing NumericProperty, Ranvier.Css and owner APIs. Do not introduce a competing CSS value contract. Keep uint duration conveniences plus precise float paths.
- [x] Prove a retargetable sampling kernel against the source fixtures in SageFs before persisting it. Cache normalized endpoints and keep DOM reads/layout out of frame sampling.
- [ ] Connect property handles and the group callback lifecycle to the shared scheduler with generation/write guards. Cover reentry, owner disposal, revert and terminal handles.
- [ ] Verify typed CSS and multiple targets/properties in the native consumer; run full normal/Traced gates, XML audit, one fresh scoped review and strict public NativeAOT evidence.

## Questions the source traces must settle

The source getter reads the tween cache, not the current external target. A numeric getter and retarget start call the modifier on that cache. Component getters expose a live array; partial retargets update only supplied existing components. Determine which of these observations require preservation and document any deliberate safe native adaptation, such as returning snapshots instead of exposing mutable arrays.

Duration/ease omitted on a later retarget retain earlier settings. Source reset(true) refreshes caches without target writes; an ordinary public Reset may introduce an observable extra write. A group's callback animation has its own default duration and can complete before a long property does. Trace actual engine traversal, rather than replacing this with an assumed all-properties-complete rule.

Existing NumericAnimation.Retarget reads the accessor and has fixed duration, so it is not yet an adequate implementation of this family. Reuse its proven timing/guard machinery where appropriate, while allowing a different internal layout for mutable endpoints and timing.

Closing the reference checkpoint does not close the animatable family. Component/CSS slots, callback scheduling, reentry/lifetime and actual native execution remain required.

## Reference checkpoint

`build.fsx -- animatable-reference` captures 33 cases, each in a separate Node process with the actual pinned engine traversal and clock throttle. Two complete generations matched SHA256 `0dfbd7bf7e2c512361214bd2f5a4b34855d049aebc449b942ea5bdd930df4aaf`. The root `reference` operation also includes the value/unit generators now; their individual commands remain available.

Verified observations: constructing a scalar 10 with a doubling modifier writes 20 and its getter returns 40. Retargeting uses that cache; an external target edit does not become the getter's value. Soft reset emits no write. A partial compound update retains previous endpoints for omitted components rather than freezing their last sampled values; surplus components are ignored. Global duration overrides scalar property shorthand, while a nested property duration overrides global duration. A 2000 ms property outlives the default 1000 ms callback animation. Starting another property can restart that completed callback clock. Revert restores original object values in the captured restoration case, clears targets and replaces property methods with noops.

The oracle itself initially assumed cleanup would run at the same clock value; source engine throttling disproved that assumption. Its final cleanup advances 5 ms before verifying the engine list is empty. Empty-target getters also enter the source setter branch; the oracle records that as an explicit operation instead of accidentally invoking it while taking snapshots. Snapshot arrays are copied so later source mutations cannot rewrite earlier evidence.

These are behavioral facts for implementation decisions, not a demand to copy source data structures. The source's live component getter array is explicitly recorded; a safe typed native getter can return a snapshot if that adaptation is documented. No runtime implementation or new native acceptance is claimed by these fixtures. Existing native archives retain their historical source binding; editing build orchestration requires a fresh publish before claiming a current live publication.

The full reference pipeline passes with existing fixture bytes unchanged; the new file contains 168 operation snapshots. A fresh scoped read-only review found no Critical/Important issues; its spacing findings were corrected. The external F# semantic check still reports unknown because project options cannot load, so no clean-workspace claim is inferred from it. The changed build definitions were loaded and both reference operations actually executed in the existing warm SageFs session.

## Value kernel checkpoint

`AnimatableValue` is an internal cached scalar/unit/color/compound track. Endpoint capture reuses the existing normalization logic; frame sampling reuses a flat shared sampler returning the modified scalar cache and composed output. Normalization, parsing and context conversion stay outside sampling. There is no additional public CSS/value contract.

Thirty source schedules compare target output, modified cache, component values and first-target getter behavior. They use source-eased tween progress to isolate value behavior; this does not prove native clock/stretch/easing or group callback scheduling. The three excluded schedules are explicit source array aliasing, no targets and no properties, which require public group/handle behavior. Eight other tests cover snapshot isolation, component input mutation, guard cancellation, scalar reentry, finite/shape validation and capture-only unit conversion. Unsupported retarget operations produced 35 RED failures; the implementation and persisted files pass in SageFs. Existing interpolation tests also pass against the extracted sampler.

Native adaptations: component getters return independent arrays, and component destinations are copied before modifier calls. Callers cannot edit the pending destination by changing their input array inside a modifier. Stale or cancelled modifiers discard pending endpoint/cache changes atomically. Source aliases and unguarded reentrant side effects are not exposed as a native requirement.

The oracle now includes tween time/update duration and eased progress, without changing the existing observations. Two generations match SHA256 `26babde5a298ed854122d0a9bc10c642bcda0bb7c5b597748dae4ae1f3b874cc`. Full unfiltered normal Release and Traced Debug suites pass 1313/1313, XML audit reports zero findings, and a fresh scoped kernel review found no Critical/Important/Minor issues. Both solutions retain their correct project sets; compile lists and reference copying include the new kernel/tests.

Both public45/P7 CPU JIT consumers and strict NativeAOT publications/executables pass. Six new sentinels cover scalar cache/modifier behavior, partial component transform geometry in real WebCore, safe getter snapshots and nested retarget interruption. The smoke assembly has friend access to the internal kernel; this is kernel acceptance, not an implemented public animatable driver. Earlier declaration/layout/unit/priority/timeline/timer/ownership assertions and zero host errors remain GREEN. Immutable `native-public45-p7-animatable-kernel-{Release,Debug}` archives preserve nine files per mode; the tracked summary rehashes both snapshots. Hardware acceleration and full native qualification remain false.

Next: specify property handles using existing NumericProperty/Ranvier.Css, implement mutable playback stretching and soft cache reset, and connect the source callback lifecycle to the shared scheduler and Ranvier ownership. Revert, group reentry and complete native family acceptance remain open.
