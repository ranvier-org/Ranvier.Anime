# Native integration

Open `Ranvier.Anime.Native.slnx` for the native host consumer (`tests/Ranvier.Anime.NativeSmoke`) and the isolated typed-CSS AOT probe (`tests/Ranvier.Anime.CssAotProbe`). The main solution keeps the portable managed tests.

The smoke host builds against the public package cohort pinned in `Directory.Build.props` (Ranvier.Dom, Ranvier.Css, Ranvier.Css.Bindings and Ranvier.View.WebCore). Debug selects the `.Traced` variants and defines `EXPECT_TRACED`; Release selects the normal packages.

```fsharp
build ["native-build"; "-c"; "Release"]           // restore + build win-x64 JIT host
build ["native-run"; "-c"; "Release"]             // run the JIT host
build ["native-publish"; "-c"; "Release"]         // strict NativeAOT publish
build ["native-run"; "-c"; "Release"; "--aot"]    // run the published PE with --require-aot
build ["css-aot-probe"; "-c"; "Release"]          // strict NativeAOT publish of the CSS probe
```

The CLI uses the same tree: `rtk proxy dotnet fsi build.fsx -- native-build -c Debug`. Native outputs go to `artifacts/native`; packages restore into the shared repository cache `artifacts/nuget-packages`. Publishes treat ILLink and ILC warnings as errors, and the build additionally fails if the publish log reports a call that will always throw.

Coverage is Windows headless CPU smoke. It does not establish GPU presentation, hardware acceleration, Linux execution, host-destruction safety or full Anime.js parity.

`NativeSmokeGraph=source` still switches the host to local Core/View projects (`UseLocalRanvier=true`, `RanvierRoot=<core checkout>`, and a View checkout under `upstream/view-aot`). The build script does not drive it; pass those properties to `dotnet build` directly when validating unreleased Core/View source.

The earlier per-cohort replays (private CSS candidate 70ac, public Core alpha.56, public View45/NativeP7) and their evidence receipts were retired once the public cohort superseded them. Their scripts, receipts and write-ups remain in git history up to commit `3def5a4`.
