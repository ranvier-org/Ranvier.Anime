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

The first numeric runner supports explicit scheduling, linear interpolation, pause/resume, seek, retargeting, cancellation and Ranvier ownership. The initial six behavior tests pass under Release. Reactive drive, typed CSS animation, full Anime.js parity and native host integration remain in progress. Direct dependencies are Ranvier, Ranvier.Dom and Ranvier.Css; no replacement CSS or scheduler contract is introduced.

Upstream baseline: Anime.js v4.5.0, commit `2c9cf8ea00329f6768c7d7902252ed977d75ce42`. The research checkout is ignored under `upstream/anime`; the tracked provenance and parity inventory record it. Preserve its MIT license when deriving source or tests. This initial numeric runner is independently authored; upstream behavioral comparison remains required.
