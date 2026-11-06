module BuildDefs

#i "nuget: https://nuget.cloudsmith.io/shayanhabibi/shayanhabibi-partas-build/v3/index.json"
#r "nuget: Partas.Build, 0.8.2"

open System.IO
open Partas.Build

let private root' = __SOURCE_DIRECTORY__
let private packageCache = Path.Combine(root', "artifacts", "nuget-packages")
let private nativeRoot = Path.Combine(root', "artifacts", "native")
let private nativeSmoke = "tests/Ranvier.Anime.NativeSmoke/Ranvier.Anime.NativeSmoke.fsproj"
let private cssProbe = "tests/Ranvier.Anime.CssAotProbe/Ranvier.Anime.CssAotProbe.fsproj"
let private rid = "win-x64"

let private rtk arguments = { Cmd.create "rtk" "" with Arguments = arguments }

let configuration =
    Input.option<string> "--configuration"
    |> Input.alias "-c"
    |> Input.def "Release"
    |> Input.acceptOnlyFromAmong ["Debug"; "Release"]

/// Native projects build into their own artifacts tree so a win-x64 restore never disturbs the portable solution.
/// NativeSmokeAot must reach restore as well as publish, or --no-restore publishes a plain JIT build without ILC.
let private nativeArguments config aot =
    [$"-p:Configuration={config}"; $"-p:NativeSmokeAot={aot}"; "--artifacts-path"; nativeRoot]

/// Strict AOT publishes still exit 0 when ILC reports a call that will always throw, so scan the log for it.
let private verifyPublishLog path =
    let failures =
        File.ReadLines path
        |> Seq.filter(fun line -> line.Contains("will always throw", System.StringComparison.OrdinalIgnoreCase))
        |> Seq.toList
    if not failures.IsEmpty then failwith (String.concat "\n" failures)

let restore = input {
    let! config = configuration
    return stage "restore" {
        workingDir root'
        envVars [("NUGET_PACKAGES", packageCache)]
        run (cmd $"rtk dotnet restore Ranvier.Anime.slnx -p:Configuration={config} --configfile nuget.config")
    }
}

let build = input {
    let! config = configuration
    and! rebuild = Input.option<bool> "--rebuild"
    let incremental = if rebuild then " --no-incremental" else ""
    return stage "build" {
        workingDir root'
        envVars [("NUGET_PACKAGES", packageCache)]
        run (cmd $"rtk dotnet build Ranvier.Anime.slnx -c {config} --no-restore{incremental}")
    }
}

let tests = input {
    let! config = configuration
    return stage "tests" {
        workingDir root'
        envVars [("NUGET_PACKAGES", packageCache)]
        run (cmd $"rtk dotnet run --project tests/Ranvier.Anime.Tests -c {config} --no-build -- --summary --sequenced")
    }
}

let audit = input {
    let! script = Input.option<string> "--script" |> Input.def ""
    return stage "XML documentation audit" {
        workingDir root'
        run (fun _ -> if System.String.IsNullOrWhiteSpace script then invalidArg "--script" "Supply the fsharp-xml-docs audit.fsx path.")
        run (cmd $"rtk proxy dotnet fsi {script} --check src tests")
    }
}

let nativeRestore aot = input {
    let! config = configuration
    return stage "native restore" {
        workingDir root'
        envVars [("NUGET_PACKAGES", packageCache)]
        run (rtk (["dotnet"; "restore"; "Ranvier.Anime.Native.slnx"; "-r"; rid] @ nativeArguments config aot @ ["--configfile"; "nuget.config"]))
    }
}

let nativeBuild = input {
    let! config = configuration
    return stage "native build" {
        workingDir root'
        envVars [("NUGET_PACKAGES", packageCache)]
        run (rtk (["dotnet"; "build"; nativeSmoke; "-c"; config; "-r"; rid; "--no-restore"] @ nativeArguments config false))
    }
}

let nativePublish = input {
    let! config = configuration
    let log = Path.Combine(nativeRoot, $"publish-{config}.log")
    return stage "strict NativeAOT publish" {
        workingDir root'
        envVars [("NUGET_PACKAGES", packageCache)]
        run (rtk (["proxy"; "dotnet"; "publish"; nativeSmoke; "-c"; config; "-r"; rid; "--no-restore"
                   "-o"; Path.Combine(nativeRoot, "publish", config); $"-flp:logfile={log};verbosity=normal"]
                  @ nativeArguments config true))
        run (fun _ -> verifyPublishLog log)
    }
}

let nativeRun = input {
    let! config = configuration
    and! aot = Input.option<bool> "--aot"
    let output =
        if aot then Path.Combine(nativeRoot, "publish", config)
        else Path.Combine(nativeRoot, "bin", "Ranvier.Anime.NativeSmoke", $"{config.ToLowerInvariant()}_{rid}")
    let exe = Path.Combine(output, "Ranvier.Anime.NativeSmoke.exe")
    return stage "native smoke" {
        workingDir output
        run (rtk (["proxy"; exe] @ (if aot then ["--require-aot"] else [])))
    }
}

let cssAotProbe = input {
    let! config = configuration
    let log = Path.Combine(nativeRoot, $"css-probe-{config}.log")
    return stage "isolated typed CSS AOT probe" {
        workingDir root'
        envVars [("NUGET_PACKAGES", packageCache)]
        run (rtk (["proxy"; "dotnet"; "publish"; cssProbe; "-c"; config; "-r"; rid; "--no-restore"
                   "-o"; Path.Combine(nativeRoot, "css-probe", config); $"-flp:logfile={log};verbosity=normal"]
                  @ nativeArguments config true))
        run (fun _ -> verifyPublishLog log)
    }
}

/// Every source oracle under tools/, with the extra modes some of them expose. Each writes into tests/reference.
let private referenceScripts =
    [ "reference.mjs", []; "timer-controls.mjs", []; "animation-playback.mjs", []; "timeline-playback.mjs", []
      "timeline-playback-ease-reference.mjs", []; "easing-curves.mjs", []; "value-interpolation.mjs", []
      "unit-conversion.mjs", []; "animatable.mjs", []
      "keyframes.mjs", []; "keyframes.mjs", ["--playback"]; "keyframes.mjs", ["--inputs"]; "keyframes.mjs", ["--computed"]
      "keyframe-css.mjs", []; "keyframe-sequences.mjs", []; "timeline-keyframes.mjs", []; "timeline-css-keyframes.mjs", []
      "timeline-mixed-keyframes.mjs", []; "timeline-revert.mjs", []
      "scope-reference.mjs", []; "scope-clock-defaults-reference.mjs", []; "scope-animation-defaults-reference.mjs", []
      "engine-reference.mjs", []; "engine-roots-reference.mjs", []; "engine-quiescence-reference.mjs", []
      "engine-idle-phase-reference.mjs", []; "transform-reference.mjs", []; "svg-reference.mjs", []
      "scroll-reference.mjs", []; "scroll-construction-reference.mjs", []; "scroll-debug-reference.mjs", []
      "draggable-reference.mjs", []; "inline-reference.mjs", []; "release-reference.mjs", []; "layout-reference.mjs", [] ]

let reference = input {
    let! only = Input.option<string> "--only" |> Input.def ""
    let scripts =
        if System.String.IsNullOrWhiteSpace only then referenceScripts
        else referenceScripts |> List.filter(fun (script, _) -> script.StartsWith(only, System.StringComparison.OrdinalIgnoreCase))
    return stage "upstream reference fixtures" {
        workingDir root'
        run (fun _ ->
            if scripts.IsEmpty then invalidArg "--only" $"No tools/*.mjs oracle starts with '{only}'."
            for script, arguments in scripts do
                let start = System.Diagnostics.ProcessStartInfo("rtk", UseShellExecute = false, WorkingDirectory = root')
                for argument in ["node"; $"tools/{script}"] @ arguments do start.ArgumentList.Add argument
                start.Environment["RANVIER_ANIME_ROOT"] <- root'
                use node = System.Diagnostics.Process.Start start
                node.WaitForExit()
                if node.ExitCode <> 0 then failwithf "tools/%s %A exited %d" script arguments node.ExitCode)
    }
}

let root = Command.root {
    description "Ranvier.Anime build"
    command "restore" { restore }
    command "build" { restore; build }
    command "test" { restore; build; tests }
    command "audit" { description "Audit F# XML documentation using the supplied skill script"; audit }
    command "native-build" { description "Restore and build the win-x64 native smoke host"; nativeRestore false; nativeBuild }
    command "native-run" { description "Run the built (or, with --aot, published) native smoke host"; nativeRun }
    command "native-publish" { description "Strict NativeAOT publish of the native smoke host"; nativeRestore true; nativePublish }
    command "css-aot-probe" { description "Strict NativeAOT publish of the isolated typed CSS probe"; nativeRestore true; cssAotProbe }
    command "css-metadata" {
        description "Regenerate or verify snapshot metadata from the matched WebKit Git blob"
        input {
            let! source = Input.option<string> "--source" |> Input.def "C:/wk/src"
            and! check = Input.option<bool> "--check"
            return stage "matched native CSS metadata" {
                workingDir root'
                run (rtk (["node"; "tools/css-metadata.mjs"; "--source"; source] @ (if check then ["--check"] else [])))
            }
        }
    }
    command "reference" {
        description "Regenerate tests/reference fixtures from the pinned upstream Anime.js source (--only <prefix> to narrow)"
        reference
    }
}
