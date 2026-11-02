# Ranvier.Anime

Native F# Anime.js port in progress. Working home: `C:/wk/ranvier-anime`.

Open `Ranvier.Anime.slnx` in Rider. It contains the library and test project and will be updated as projects are added.

Project operations use Partas.Build 0.8.2 from the public Cloudsmith feed configured in `nuget.config`:

```powershell
rtk proxy dotnet fsi build.fsx -- restore
rtk proxy dotnet fsi build.fsx -- build
rtk proxy dotnet fsi build.fsx -- test
```

For repeated operations, load `.SageFs/init.fsx` into a SageFs bare session rooted in this directory. Its text loads `build.defs.fsx` and binds:

```fsharp
build ["test"; "--explain"]
build ["test"]
```

The command tree is shared with the CLI and returns a structured `RunResult`. Reload the definitions and rebind `build` after editing them. Load the definitions rather than the CLI shim, whose final line exits the process.

The foundation supports numeric scheduling, easing, pause/resume, seek, retargeting, cancellation, Ranvier ownership and reactive drive. Typed CSS custom-property animations accept existing numeric values with matching units. Seventy tests pass in normal Release and Traced Debug; XML documentation audit reports zero findings. Easing comparisons cover 46 upstream default names and three curves at nine sample points each. Full Anime.js parity and native host integration remain in progress. Direct dependencies reuse Ranvier, Ranvier.Dom, Ranvier.Css and Ranvier.Css.Dom; no replacement CSS or scheduler contract is introduced.

Use `build ["test"; "-c"; "Debug"]` for the matched Traced Ranvier dependency cohort. Restore uses the same configuration as build, so switching back to Release restores normal dependencies. `Ranvier.Css` and generated CSS bindings are shared between modes.

The typed easing API exposes `Ease.create (Power 2.) InOut`, `Ease.steps 4u false`, and `Ease.cubicBezier 0.42 0. 0.58 1.`. `Ease.named` supplies Anime.js names. Native validation rejects unknown names, nonfinite parameters, zero steps and out-of-range normalized progress (steps clamp finite input as upstream does). Springs, irregular easing and multi-stop linear easing remain required work. Custom easing outputs may overshoot, including at initialization/completion; cancellation or a seek during an easing callback supersedes the interrupted render.

Upstream baseline: Anime.js v4.5.0, commit `2c9cf8ea00329f6768c7d7902252ed977d75ce42`. The research checkout is ignored under `upstream/anime`; the tracked provenance and parity inventory record it. Preserve its MIT license when deriving source or tests. This initial numeric runner is independently authored; upstream behavioral comparison remains required.
