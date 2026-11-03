# Retargetable animatables

Continue the approved full port using the pinned Anime.js 4.5 source as a behavioral reference. Implementation structure need not match JavaScript: prefer Ranvier ownership, shared scheduling, typed CSS and efficient cached state. Preserve observable behavior unless a native adaptation is explicitly documented. Measure uncertain optimizations in later waves.

## Work

- [x] Capture isolated actual-engine traces for scalar, unit, color and compound getters/retargets; partial component updates; multiple targets/properties; duration/easing retention and stretch; modifiers; callbacks and revert.
- [ ] Specify typed property handles and group lifetime using the existing NumericProperty, Ranvier.Css and owner APIs. Do not introduce a competing CSS value contract. Keep uint duration conveniences plus precise float paths.
- [ ] Prove a retargetable sampling kernel against the source fixtures in SageFs before persisting it. Cache normalized endpoints and keep DOM reads/layout out of frame sampling.
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
