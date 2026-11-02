# Native integration

Development home: `C:/wk/ranvier-anime`. Open `Ranvier.Anime.Native.slnx` for the host consumer and focused CSS probe; the main solution retains portable managed tests.

The current private source graph pins Core `6f5e063c06a48af2c3375c1c986cb5925e814d3d` and View `625b0e1baeaf2ccc023f69dd7f1e76626e27a084` in independent ignored clones under `upstream/core-aot` and `upstream/view-aot`. It uses public managed WebCore `0.1.1-preview.7` with the coordinator's private ABI62 runtime built from native source `6f10bea17d7a1f22e5e97d472819c055fba3ef06`. The owned runtime copy contains 26 verified files totaling 89,778,897 bytes. The producer reader was released after copying; subsequent operations use only the owned stage and retained manifest.

Run operations from the warm SageFs build binding:

```fsharp
build ["native-build"; "-c"; "Debug"]
build ["native-run"; "-c"; "Debug"]
build ["native-publish"; "-c"; "Debug"]
build ["css-aot-probe"; "-c"; "Debug"]
build ["native-gap-evidence"; "-c"; "Debug"]
```

The CLI uses the same command tree, for example `rtk proxy dotnet fsi build.fsx -- native-build -c Debug`. The source checkouts and granted runtime must already exist for host operations. `native-stage --manifest <path>` is an explicit initial copy operation for a currently granted manifest; it has no producer-path default. Never reuse an expired producer grant. Native operations set `OS=Windows_NT`, use the owned NuGet cache and keep outputs under `artifacts/native-smoke`. Debug explicitly selects Traced Core/DOM/CSS-DOM dependencies, including package suffixes outside View's property scope.

The normal Release and Traced Debug managed consumers pass on the real headless host. They verify assembly trace metadata, every application's native payload hash, typed CSS custom-property seek/play and retained priority, native layout consuming the animated value, real `Frames.forHost` scheduling, actual Ranvier signal/effect ownership and disposal, and absence of host listener errors. They create no window. This is a narrow real-host adapter smoke, not full Anime.js parity, painting/compositor proof or native qualification of every timeline/stagger feature.

The archived public CSS alpha.1 baseline failed NativeAOT with a 48-error summary. Removing the consumer's F# formatting did not resolve it. ILC's dependency graph reaches reflection-based generated `ToString` methods in public `Ranvier.Css` alpha.1, including `Declaration`, `Calculation`, `NumericDimension` and `ValueStorage`. The isolated `Ranvier.Anime.CssAotProbe` reproduced the same failure with only normal `Ranvier.Css` and `Ranvier.Css.Bindings`: `Css.px 12.5` followed by `RuntimeApi.tryNumericValue`. It references no Anime/Core/View/host project. This literal probe passes in SageFs. Investigation and the corrective source change remained upstream; no upstream source edits or IL suppressions have been made here.

Exact logs, both dependency asset graphs, ILC response files and the full dependency graph are preserved in `artifacts/qualification/native-css-gap-Debug`. Its `receipt.json` hashes seven files and records 46 unique IL diagnostic lines for each publish; complete logs retain repeated diagnostics and the 48-error totals. The CSS package nuspec names source commit `1222e8ffcfb956f5535dbef1dc1ad642419505db`. Publish gates treat IL warnings as errors and also scan the retained full log for IL diagnostics and ILC “will always throw” messages. After a successful publish, `native-run --aot` additionally requires `RuntimeFeature.IsDynamicCodeSupported = false`.

The corrected private CSS source is `70ac54af73543d86941da45c4dc499ca819a7e3e`, packaged as `0.1.1-alpha.1-css-aot.2`. Four actual normal/Traced CSS and Bindings nupkgs were copied into `artifacts/css-candidate/70ac/packages`, hash-checked against the coordinator's receipt, and checked against their captured nuspec IDs/version/source commit. The producer reader was then released. Native replay uses only that owned feed, `nuget.private-css.config`, a separate `artifacts/nuget-css-70ac` cache and `artifacts/native-css-70ac` outputs. The private version is opt-in; public defaults and public package bytes remain unchanged.

Both normal Release and Traced Debug strict publishes and published PE executions pass with this candidate. Each executable confirms NativeAOT, expected trace metadata, the existing real-host style/layout/frame/ownership checks and zero host listener errors. Publish logs contain no IL diagnostics or unconditional-throw messages. The source View build retains its existing F# native-pointer warnings; these are not IL warning suppressions. Replay operations are:

```fsharp
build ["native-publish"; "-c"; "Debug"; "--css-candidate"]
build ["native-run"; "-c"; "Debug"; "--css-candidate"; "--aot"]
build ["css-candidate-evidence"; "-c"; "Debug"]
// Repeat publish/run/evidence with Release before summarizing both modes.
build ["css-candidate-summary"]
```

Snapshots under `artifacts/qualification/native-css-70ac-{Debug,Release}` retain full publish/run logs, successful run receipts, dependency assets, ILC inputs and executables. The capture verifies restored private nupkg hashes against the owned feed and all three CSS app DLL hashes against restored package bytes. It rejects mixed normal/Traced CSS graphs. [Tracked mode receipts](evidence/native-css-70ac.json) bind these snapshots to consumer/library source hashes. Existing snapshots are immutable; the capture command refuses to overwrite them.

Coherence correction: the earlier Debug graph directly referenced normal CSS/Bindings alongside `Css.Dom.Traced`, which itself depends on `Css.Traced`. Direct CSS and Bindings now follow the same suffix as Core/DOM. Fresh public-mode suites pass354/354 in each configuration. Earlier mixed-graph Debug evidence establishes only its recorded Core/DOM trace and adapter behavior, not a coherent full CSS cohort. The CSS-only normal-package failure still independently establishes the historical AOT issue.

Remaining qualification includes independent upstream review/publication of the exact packaged CSS successor, broader native feature coverage and a matched public Core/Native/View/CSS cohort. This successful private candidate replay does not establish those. The private rebuilt GPU payload is not public GPU evidence, and successful style/layout checks do not prove hardware acceleration.
