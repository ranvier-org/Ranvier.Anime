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

Strict Debug NativeAOT currently fails with a 48-error summary. Removing the consumer's F# formatting did not resolve it. ILC's dependency graph reaches reflection-based generated `ToString` methods in public `Ranvier.Css` alpha.1, including `Declaration`, `Calculation`, `NumericDimension` and `ValueStorage`. The isolated `Ranvier.Anime.CssAotProbe` reproduces the same failure with only `Ranvier.Css` and `Ranvier.Css.Bindings`: `Css.px 12.5` followed by `RuntimeApi.tryNumericValue`. It references no Anime/Core/View/host project. This literal probe passes in SageFs. The coordinator confirmed that this public CSS surface is not yet AOT-qualified and assigned investigation upstream; no upstream source edits or IL suppressions have been made here.

Exact logs, both dependency asset graphs, ILC response files and the full dependency graph are preserved in `artifacts/qualification/native-css-gap-Debug`. Its `receipt.json` hashes seven files and records 46 unique IL diagnostic lines for each publish; complete logs retain repeated diagnostics and the 48-error totals. The CSS package nuspec names source commit `1222e8ffcfb956f5535dbef1dc1ad642419505db`. Publish gates treat IL warnings as errors and also scan the retained full log for IL diagnostics and ILC “will always throw” messages. After a successful publish, `native-run --aot` additionally requires `RuntimeFeature.IsDynamicCodeSupported = false`.

Remaining qualification includes the focused CSS resolution, strict normal/Traced published consumer execution, broader native feature coverage and a matched public Core/Native/View cohort. The private rebuilt GPU payload is not public GPU evidence, and successful style/layout checks do not prove hardware acceleration.
