# Native integration

Development home: `C:/wk/ranvier-anime`. Open `Ranvier.Anime.Native.slnx` for the host consumer and focused CSS probe; the main solution retains portable managed tests.

## Current public cohort

The latest replay is [public45-p7-public-numeric-animatable](evidence/native-public45-p7-public-numeric-animatable.json). Normal/Traced JIT consumers and strict NativeAOT PEs pass public numeric handles driving typed width/native layout, cached getters, captured-value restoration with unrelated style retained, callback-clock rewind and actual Ranvier owner disposal stopping every driver. Existing cached-value/group/retarget/stretch/declaration/unit/layout/ownership checks remain green, with zero host listener errors. Portable suites pass 1464/1464 in each mode and XML audit reports zero findings. Public CSS/component handles and inline priority/absence restoration remain unfinished; full-native and acceleration qualification flags remain false.

The preceding [cached snapshot replay](evidence/native-public45-p7-animatable-snapshots.json) retains its exact earlier source and verifies signed color snapshots, independent component arrays, compound snapshots driving native transforms and unit-bearing track snapshots. Its portable gates passed 1420/1420 per mode.

The earlier [public45-p7-units](evidence/native-public45-p7-units.json) replay verified element-context em/rem/percentage/physical/angle conversion, independent16/32px font contexts, retained priority and removal of every temporary clone. Its suites passed1275/1275, XML0 and evidence regressions. Its source-bound archives retain340 authored/190 ILC inputs. Conversion runs once during construction and intentionally avoids the source global cache across contexts. The private deeply curried factory first failed IL3054; optional flat-converter selection/direct preparation calls remove that generic expansion without public API changes or suppression.

The earlier connected declaration checkpoint passed1246/1246 portable tests in both modes. Existing Ranvier.Css.Declaration endpoints use StyleDeclaration.apply and return the shared Timer. Default duration inputs are uint; precise variants retain fractional floats. Its normal/Traced JIT and strict NativeAOT publication/PE runs verified typed width/layout, source legacy color blending, compound transformed geometry and actual owner disposal inside a component modifier, alongside previous sentinels and host.Errors0.

That declaration replay is [public45-p7-declarations](evidence/native-public45-p7-declarations.json), with immutable `native-public45-p7-declarations-{Release,Debug}` snapshots. Its receipts bind339 authored and190 ILC inputs. Strict ILC initially exposed this port's generated Literal/Data/Output diagnostics; actual graph edges reached PrintFormatToString. Authored BCL diagnostics/direct debugger displays remove those roots, without upstream edits or IL suppression. Those and earlier curves/unlabelled snapshots retain their own recorded source. Use a fresh `--label <unique-label>` for both evidence commands and summary when preserving another replay.

The style-only declaration factory still requires matching scalar units. Element-context declarationOn variants use native clone measurements for differing HTML scalar units; compound templates retain source behavior even when suffixes differ. Source angle/measurement rules and native cache/cleanup adaptations are documented in the contextual-units plan. P7's inspected manifest has no structured color-channel accessor, and computed modern/wide-gamut colors need not serialize as sRGB rgba. Modern colors, SVG-specific contexts, host destruction, GPU presentation and broader coverage remain required.

Normal Release and Traced Debug now pass fresh JIT builds/runs and strict NativeAOT publications/PE executions using public NuGet packages only: Core0.1.1-preview.1, DOM0.1.0-alpha.56, CSS0.1.1-alpha.2, Bindings0.1.1-alpha.1, CSS-DOM0.1.0-alpha.1, View/View.Dom0.1.45, View.WebCore0.1.0-alpha.45 and WebCore/Native0.1.1-preview.7. The headless consumer builds only the port and consumer projects; View/Core are package dependencies. Normal and Traced modes use matching managed packages.

`build.public45.fsx` owns `artifacts/nuget-public45-p7` and `artifacts/native-public45-p7`. Every run verifies nine managed identities from nupkg entry through extracted cache and app; strict PE evidence also verifies the actual ILC DLL inputs. Each nupkg has an independently pinned hash, nuspec source commit and public-feed provenance. The View audit pins source6b1119c8; native CPU/meta audits pin source6f10bea, ABI62, win-x64/slim and all24 exact payload members. Sdl packages in the eight-package View audit are not consumed by this headless host.

The CPU package hash is `bfe3ec181b73de82d68f3bda62b512d08a012e26c694b15e6fb5f1c03d5f3ab9`. An initial audit-selection inference confused the separate26-file GPU receipt with CPU; hash verification rejected it before runtime. The corrected independently pinned seven-package audit identifies CPU explicitly. This was a consumer receipt-selection error, not an upstream mismatch. Neither the unused GPU receipt nor earlier private runtime files supply this public consumer.

All current smoke sentinels pass: typed CSS/style/priority, native layout, frame scheduling, repeated/alternate animations and timelines, owned timer tracks, borrowed repeated timers, actual Ranvier ownership and cancellation during active callbacks, and zero host listener errors. Both PEs confirm dynamic code is unavailable. Strict publish logs have no IL diagnostics or unconditional-throw messages. Negative checks reject wrong audit bytes, producer-project substitution, altered managed/native DLLs and unsafe evidence labels, then restore and reverify owned files.

Reproduce through the warm Partas.Build binding; copy the accepted audits once, then preserve each mode before another restore changes shared assets:

```fsharp
build ["public-native-audits"; "--view"; "C:/wk/project2/view-public-pin/public-package-audit.json"; "--native"; "C:/wk/project2/native-preview7-public-audit/seven-byte-audit.json"]
build ["public-native-restore"; "-c"; "Release"]
build ["public-native-smoke"; "-c"; "Release"]
build ["public-native-evidence-test"; "-c"; "Release"]
build ["public-native-publish"; "-c"; "Release"]
build ["public-native-aot-run"; "-c"; "Release"]
build ["public-native-evidence"; "-c"; "Release"]
// Repeat restore/smoke/test/publish/run/evidence with Debug.
build ["public-native-summary"]
```

[Public two-mode evidence](evidence/native-public45-p7.json) binds immutable `artifacts/qualification/native-public45-p7-{Release,Debug}` snapshots. Each preserves nine files: publish/run logs and receipts, assets, ILC response, PE and both independent audits. Capture requires a successful current-source publication and matching executed PE/log hashes; existing receipts cannot be overwritten.

This is Windows headless CPU smoke coverage. It does not establish GPU presentation, hardware acceleration, Linux execution, host-destruction safety or full Anime.js parity. P7's independently confirmed first-owner worker-thread exit issue remains outside this main-thread consumer; the announced P8 fix has not been consumed here. The historical private/source replays below retain their original scope.

## Historical source and package replays

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

Immutable schema-3 snapshots `artifacts/qualification/native-core56-css2-hardened-{Release,Debug}` include successful publication/run receipts, full logs, assets, ILC response, executable, runtime manifest and accepted Core audit. [Tracked two-mode evidence](evidence/native-core56-css2-hardened.json) binds them. Both receipt hashes and all 18 archived files were reverified. At capture, Debug publication validated 337 authored inputs and 190 compiler inputs after both full 492/492 managed suites; XML audit and evidence regressions passed. Source snapshots conservatively include the owned Core clone, but the assets and compiler inputs prove that public Core packages supplied the actual binaries.

Later timeline preparation adds an internal child-render phase and source root-clock tests; full portable suites now pass527/527 normal Release and Traced Debug. The immutable Core56 archives above retain their earlier exact source. They do not qualify this later child-phase code or the forthcoming public timeline integration; a fresh source-bound publication/run replay remains required.

Public Timeline now uses the shared timer clock. Full normal/Traced suites pass557/557. A fresh successful Traced `native-build --public-core` followed by its new JIT binary verifies native timeline repeat/alternate output and retained priority, layout consuming that output, and actual Ranvier owner disposal inside before-update preventing later property writes and update callbacks. The host reports zero listener errors. The run log is `artifacts/native-core56-css2/run-Debug-jit.log`; this added source still requires fresh strict publication/PE evidence. Earlier immutable archives stay unchanged.

Coordinator update: View release PR70 merged as6b1119c8dbcefc683194dec510859f1137d1e96a; public publishers await main/analyzer checks. Accepted public View payload/signature audit is pending. Public NativeP7 package audits are available, but this consumer still uses its recorded private ABI62 payload. The upcoming ABI63/347 cohort contains further native fixes and has not been consumed or accepted here.

Owned/borrowed timer integration now passes623/623 normal/Traced tests. A new successful Traced native build/run adds explicit owned timer-track sampling and borrowed repeated-timer playback assertions; parent cancellation retains the borrowed timer and disposes its owned timers. Native timeline style/layout, active owner cancellation and host.Errors=0 still pass. The same owned JIT log records the new sentinels. No new strict PE/public-View/public-native/acceleration acceptance is claimed.

The reviewed playback source now passes both strict normal Release and Traced Debug NativeAOT publication and real PE execution. Current PEs include typed CSS/native layout, repeat/alternate timelines, owned timer tracks, borrowed repeated timers and active owner-disposal assertions with zero host errors. ILC dependency graphs traced failures to this port's generated TimelineEntry and TimelinePosition ToString/debugger methods; authored BCL diagnostics remove those roots. No upstream source edits or IL suppression were needed. One final review found two Important reentrancy/setter issues; both are fixed with three source traces and five regressions. Full normal/Traced suites631/631 and XML audit0 pass.

New `native-core56-css2-hardened-playback-reviewed-{Release,Debug}` archives preserve nine files per mode. [Reviewed two-mode evidence](evidence/native-core56-css2-hardened-playback-reviewed.json) binds both receipts. Both receipt hashes, all18 files, and every current library/consumer source hash were reverified. Current Debug publication binds337 authored and190 compiler inputs. The separate `playback` archives retain the earlier pre-review source. Capture/summarize accept validated `--label` values and preserve existing snapshots. These replays still use public Core/DOM56/CSS2 with private View625b/ABI62 runtime.

Public View handoff has now arrived: coordinator audit `C:/wk/project2/view-public-pin/public-package-audit.json` pins all eight packages to producer main6b1119c8 / CI41557769303. View/View.Dom normal+Traced are0.1.45; WebCore/Sdl normal+Traced are0.1.0-alpha.45. Matching published Native/InteropP7 is ABI62. This port has read the receipt but has not yet restored or qualified that fully public graph; public flags in the current replay remain false. Coordinator reports the P7 first-owner worker-exit fix is only in the upcoming P8 cohort. No P8, GPU or public consumer acceptance is inferred.

Those earlier replays close their recorded timer/numeric NativeAOT requirement with public Core/CSS and private View/runtime. The public cohort section above supersedes their package status. Full property-track callbacks and the remaining capability families, host destruction, Linux and GPU coverage stay open. Existing historical snapshots remain intact.

The animatable value kernel now has fresh public45/P7 CPU evidence in both normal Release and Traced Debug. Strict publications and actual NativeAOT executable runs pass cached scalar modifier behavior, partial-component transforms affecting WebCore geometry, independent getter arrays and reentrant retarget interruption, plus the existing declaration/layout/unit/ownership regressions. The smoke consumer has friend access to this internal kernel; public animatable handles, group callbacks and scheduling are still unfinished. [Kernel evidence](evidence/native-public45-p7-animatable-kernel.json) binds nine archived files per configuration under immutable `native-public45-p7-animatable-kernel-{Release,Debug}` labels. Full native and hardware-acceleration flags remain false. Portable suites pass 1313/1313 in each mode, XML audit is clear, and the scoped kernel review found no issues.

Mutable Timer/NumericAnimation stretching and declaration Timer stretching now have fresh normal/Traced public45/P7 CPU JIT and NativeAOT execution evidence. New checks retain the old style until sampling, use the new iteration duration for numeric/typed width output, preserve fractional milliseconds, scale initial delays and complete a stretched timer through the actual host scheduler. [Stretch evidence](evidence/native-public45-p7-animatable-stretch.json) binds the immutable two-mode archives. Full compiled suites pass 1341/1341 per mode, XML audit is clear and scoped review found no issues. Public animatable handles, soft cache reset, group callbacks and full Timeline stretching remain open; this checkpoint does not claim them or hardware acceleration.

The internal retarget track now passes normal/Traced public45/P7 CPU JIT and strict NativeAOT execution. New sentinels verify cache-only reset without target dispatch, changed duration/easing after completion, typed px output/importance/native layout, and real owner disposal inside soft reset. [Track evidence](evidence/native-public45-p7-animatable-track-final.json) binds both immutable archives. The named-easing consumer exposed this port's generated EaseDirection/EaseFamily formatting roots; authored BCL diagnostics remove them without upstream edits or suppression. Full suites pass 1378/1378 in each mode, XML audit is clear and scoped track review found no issues. Public animatable handles, group callbacks and revert remain open; native coverage is Windows headless CPU with acceleration/full qualification false.

The internal group callback driver now passes normal/Traced public45/P7 CPU JIT and strict NativeAOT execution. Two independently timed tracks drive typed width/height with retained priority and native layout; callback completion uses its independent 1000 ms endpoint. Owner disposal from group begin stops later callback phases and every property root. [Group evidence](evidence/native-public45-p7-animatable-groups.json) binds both immutable archives. Thirty exact source callback schedules and seven group lifetime/root-lease tests pass; full suites are 1415/1415 per mode and XML audit is clear. Review's minor completed-work lease retention finding is fixed with a regression. Typed public authoring, revert and full-family/native/GPU acceptance remain open.
