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

The CLI uses the same command tree, for example `rtk proxy dotnet fsi build.fsx -- native-build -c Debug`. The source checkouts and granted runtime must already exist for host operations. `native-stage --manifest <path>` is an explicit initial copy operation for a currently granted manifest; it has no producer-path default. Never reuse an expired producer grant. Native operations set `OS=Windows_NT` and use owned caches and output trees. The runtime stays under `artifacts/native-smoke/runtime`; current public-CSS outputs use `artifacts/native-css-public-alpha2`. Debug explicitly selects Traced dependencies, including package suffixes outside View's property scope.

The normal Release and Traced Debug managed consumers pass on the real headless host. They verify assembly trace metadata, every application's native payload hash, typed CSS custom-property seek/play and retained priority, native layout consuming the animated value, real `Frames.forHost` scheduling, actual Ranvier signal/effect ownership and disposal, and absence of host listener errors. They create no window. This is a narrow real-host adapter smoke, not full Anime.js parity, painting/compositor proof or native qualification of every timeline/stagger feature.

The archived public CSS alpha.1 baseline failed NativeAOT with a 48-error summary. Removing the consumer's F# formatting did not resolve it. ILC's dependency graph reaches reflection-based generated `ToString` methods in public `Ranvier.Css` alpha.1, including `Declaration`, `Calculation`, `NumericDimension` and `ValueStorage`. The isolated `Ranvier.Anime.CssAotProbe` reproduced the same failure with only normal `Ranvier.Css` and `Ranvier.Css.Bindings`: `Css.px 12.5` followed by `RuntimeApi.tryNumericValue`. It references no Anime/Core/View/host project. This literal probe passes in SageFs. Investigation and the corrective source change remained upstream; no upstream source edits or IL suppressions have been made here.

Exact logs, both dependency asset graphs, ILC response files and the full dependency graph are preserved in `artifacts/qualification/native-css-gap-Debug`. Its `receipt.json` hashes seven files and records 46 unique IL diagnostic lines for each publish; complete logs retain repeated diagnostics and the 48-error totals. The CSS package nuspec names source commit `1222e8ffcfb956f5535dbef1dc1ad642419505db`. Publish gates treat IL warnings as errors and also scan the retained full log for IL diagnostics and ILC “will always throw” messages. After a successful publish, `native-run --aot` additionally requires `RuntimeFeature.IsDynamicCodeSupported = false`.

The historical corrected private CSS source is `70ac54af73543d86941da45c4dc499ca819a7e3e`, packaged as `0.1.1-alpha.1-css-aot.2`. Four actual normal/Traced CSS and Bindings nupkgs were copied into `artifacts/css-candidate/70ac/packages`, hash-checked against the coordinator's receipt, and checked against their captured nuspec IDs/version/source commit. The producer reader was then released. Private replay uses only that owned feed, `nuget.private-css.config`, a separate `artifacts/nuget-css-70ac` cache and `artifacts/native-css-70ac` outputs. The private version remains opt-in; defaults now use public CSS alpha.2.

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

Public CSS alpha.2 has now been published and independently audited upstream. A fresh public-feed-only restore into `artifacts/nuget-css-public-alpha2` passes strict normal Release and Traced Debug NativeAOT publish and published PE execution. Bindings remains public 0.1.1-alpha.1; CSS-DOM remains public 0.1.0-alpha.1. The root CSS nupkg hashes match the coordinator's audited public bytes: normal `2665336dbac9deabd5f641cee78e654389b46720f7d262f866b123a6340e2f1a`, Traced `2713ff055b570c62ce7db25ffff4b48ff8e8a2efc25f4e039a298fa94b5dc0de`. No private feed is configured for this replay.

The final fresh-context review identified three evidence automation issues, now fixed with failing-then-passing regressions. Publish begins by invalidating prior success and snapshots authored inputs; success requires command success, a successful strict log and unchanged inputs. Its schema-2 receipt binds 334 source/project inputs, assets, ILC response, 190 resolved compiler DLLs, executable and log. Capture requires that receipt and unchanged hashes. CSS DLL entries are hashed directly from each nupkg and compared with cache, app and actual ILC reference copies. Run attempts invalidate old success before launch, preserve stdout/stderr and persist explicit failed/start-failed/timed-out outcomes. A deadline regression kills only its owned child tree and retains its marker output. Generated obj/bin files are excluded from authored inputs; the receipt still validates after both managed suites.

New immutable snapshots are `artifacts/qualification/native-css-public-alpha2-hardened-{Release,Debug}` with [tracked two-mode receipts](evidence/native-css-public-alpha2-hardened.json). They include the successful-publish receipt as well as complete logs/assets/ILC inputs and the PE. Historical private and failure snapshots remain intact. The public capture's inherited console wording says "private"; the receipt's `PrivateCandidate=false` and `PublicCssPackages=true`, actual asset versions and package hashes identify this replay. Reproduce in this order, capturing each mode before another restore changes shared assets:

```fsharp
build ["native-publish"; "-c"; "Release"]
build ["native-run"; "-c"; "Release"; "--aot"]
build ["css-public-evidence"; "-c"; "Release"]
// Repeat publish/run/evidence with Debug, then summarize.
build ["css-public-summary"]
build ["evidence-test"]
```

Both full public-package managed suites pass 354/354 with CSS alpha.2; XML audit reports zero findings. Remaining qualification includes broader native feature coverage and a matched public Core/Native/View/CSS cohort. Core/View and ABI62 runtime remain private. Ownership coverage currently proves disposal after completion prevents subsequent retargeting; active-frame cancellation and host destruction remain required. The private rebuilt GPU payload is not public GPU evidence, and successful style/layout checks do not prove hardware acceleration.

The subsequent standalone Timer addition passes 441/441 full managed tests in each configuration. A fresh Traced JIT host build/run now also verifies repeated-timer completion and disposal of its actual Ranvier owner inside before-update: the timer cancels during that active sample, emits zero update callbacks and stays quiet on later native frames. Its current run log is `artifacts/native-css-public-alpha2/run-Debug-jit.log`. Earlier immutable AOT snapshots bind their earlier source; they do not qualify this Timer addition. A new published-PE replay, numeric/timeline shared playback, host-destruction coverage and broad native feature tests remain required.

Numeric animation and typed CSS now share the timer clock as well. Current suites pass 492/492 normal and Traced; the fresh successful Traced JIT build/run also verifies repeated/alternated typed-CSS output reaches its source-directed endpoint with importance retained. The same current JIT log includes that explicit check. Numeric source comparisons cover 46 value/callback/state traces and three interruption tests. Timeline shared playback, fresh NativeAOT qualification of these added sources, host destruction and broad native feature coverage remain open.

The current playback foundation now also passes strict NativeAOT with actual public Core packages: Ranvier 0.1.1-preview.1, DOM 0.1.0-alpha.56, CSS 0.1.1-alpha.2, Bindings 0.1.1-alpha.1, CSS-DOM 0.1.0-alpha.1 and managed WebCore 0.1.1-preview.7. Both normal Release and Traced Debug restore into the fresh `artifacts/nuget-core56-css2` cache using public feeds only. `--public-core` sets `UseLocalRanvier=false` and selects `artifacts/native-core56-css2`; Core/DOM are package dependencies, rather than producer project references. The existing private View625b source and owned ABI62 runtime remain pinned. The portable managed-test default remains DOM alpha.55; this native replay explicitly overrides it to alpha.56.

DOM normal/traced nupkg SHA256 values match the coordinator's independently signed public audit: `518866e8bc643a52cb069ad597e6fc307e042cfb9ff9fb8b0156a92095caeae2` and `ca60a9a05bd355941e615c044b5a2ad897a347be23278e89422d345820a7586c`. Their nuspec source is `fc7a751babe9ee6df6369776451a7fa71a06a00e`. Six selected package DLLs in each mode match their nupkg entries, extracted cache copies, app copies and actual ILC references. Receipts record full nupkg/DLL hashes, feed URLs and each package's source commit, including public Ranvier source1f101370 and CSS source5ed51b9f. The accepted audit metadata is copied into the owned evidence tree; no producer package or released reader path is used.

The first strict replay found our own newly introduced F# diagnostic formatting roots. Explicit BCL-based ToString implementations for PlaybackOptions/PlaybackRepeat/PlaybackEvent and direct DU debugger displays remove those paths. The dependency graph identified the generated ToString and __DebugDisplay calls; no upstream bug, source edit or IL suppression was needed. PlaybackOptions diagnostic text is now invariant and one-line. Both published PEs confirm NativeAOT and pass all current style/layout/repeat/priority/owner checks, including owner disposal during an active callback.

Reproduce the public-Core replay through warm Partas.Build, preserving Release before restoring Debug:

```fsharp
build ["core56-audit-copy"; "--receipt"; "C:/wk/project2/core56-public-byte-audit.json"]
build ["native-publish"; "-c"; "Release"; "--public-core"]
build ["native-run"; "-c"; "Release"; "--public-core"; "--aot"]
build ["core56-evidence"; "-c"; "Release"]
// Repeat publish/run/evidence with Debug, then summarize.
build ["core56-summary"]
```

Immutable schema-3 snapshots `artifacts/qualification/native-core56-css2-hardened-{Release,Debug}` include successful publication/run receipts, full logs, assets, ILC response, executable, runtime manifest and accepted Core audit. [Tracked two-mode evidence](evidence/native-core56-css2-hardened.json) binds them. Both receipt hashes and all 18 archived files were reverified. Current Debug publication still validates 337 authored inputs and 190 compiler inputs after both full 492/492 managed suites; XML audit and evidence regressions pass. Source snapshots conservatively include the owned Core clone, but the assets and compiler inputs prove that public Core packages supplied the actual binaries.

This closes the current timer/numeric NativeAOT replay requirement, with public Core/CSS and private View/runtime. It does not establish matched public View/native acceptance, host-destruction safety, full Anime.js parity, Linux execution or GPU presentation. Timeline clock integration and the remaining capability families stay open. Existing historical snapshots remain intact.
