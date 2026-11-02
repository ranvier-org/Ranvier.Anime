module BuildDefs

#i "nuget: https://nuget.cloudsmith.io/shayanhabibi/shayanhabibi-partas-build/v3/index.json"
#r "nuget: Partas.Build, 0.8.2"
#load "build.native.fsx"
#load "build.css-candidate.fsx"

open Partas.Build

let packageCache = System.IO.Path.Combine(__SOURCE_DIRECTORY__, "artifacts", "nuget-packages")

let nativeRoot candidate =
    System.IO.Path.Combine(__SOURCE_DIRECTORY__, "artifacts", if candidate then "native-css-70ac" else "native-smoke")

let nativeCache candidate =
    if candidate then System.IO.Path.Combine(__SOURCE_DIRECTORY__, "artifacts", "nuget-css-70ac") else packageCache

let nativeNuget candidate = if candidate then "nuget.private-css.config" else "nuget.config"

let cohortArguments config candidate =
    let traced, suffix = if config = "Debug" then "true", ".Traced" else "false", ""
    [$"-p:Configuration={config}"; $"-p:RanvierTrace={traced}"; $"-p:RanvierPackageSuffix={suffix}"] @
    (if candidate then ["-p:RanvierCssVersion=0.1.1-alpha.1-css-aot.2"] else [])

let nativeArguments config candidate =
    cohortArguments config candidate @
    [ "-p:UseLocalRanvier=true"; $"-p:RanvierRoot={__SOURCE_DIRECTORY__}/upstream/core-aot"; "-p:UseLocalRanvierWebCore=false"
      "-p:RanvierWebCoreNativeVersion=0.1.1-preview.7"; $"-p:RanvierWebCoreNativeDir={__SOURCE_DIRECTORY__}/artifacts/native-smoke/runtime"
      "-p:RanvierWebCoreNativeRid=win-x64"; "-p:TargetFrameworks=net10.0"; "-p:FSharpCoreImplicitPackageVersion=10.1.401"
      "--artifacts-path"; nativeRoot candidate + "/build" ]

let nativeCommand arguments = { Cmd.create "rtk" "" with Arguments = arguments }

let configuration =
    Input.option<string> "--configuration"
    |> Input.alias "-c"
    |> Input.def "Release"
    |> Input.acceptOnlyFromAmong ["Debug"; "Release"]

let cssCandidate = Input.option<bool> "--css-candidate"

let restore = input {
    let! config = configuration
    return stage "restore" {
        workingDir __SOURCE_DIRECTORY__
        envVars [("NUGET_PACKAGES", packageCache)]
        run (cmd $"rtk dotnet restore Ranvier.Anime.slnx -p:Configuration={config} --configfile nuget.config --force --no-http-cache")
    }
}

let nativeRestore = input {
    let! config = configuration
    and! candidate = cssCandidate
    let arguments = nativeArguments config candidate
    return stage "native source-cohort restore" {
        workingDir __SOURCE_DIRECTORY__
        envVars [("NUGET_PACKAGES", nativeCache candidate); ("OS", "Windows_NT")]
        run (fun _ -> if candidate then BuildCssCandidate.verify())
        run (fun _ -> BuildNative.verifySources())
        run (fun _ -> BuildNative.verifyStage())
        run (nativeCommand ["git"; "-C"; "upstream/core-aot"; "diff"; "--exit-code"; "HEAD"])
        run (nativeCommand ["git"; "-C"; "upstream/view-aot"; "diff"; "--exit-code"; "HEAD"])
        run (nativeCommand (["dotnet"; "restore"; "Ranvier.Anime.Native.slnx"; "-r"; "win-x64"] @ arguments @ ["--configfile"; nativeNuget candidate; "--force"; "--no-http-cache"]))
    }
}

let nativeBuild = input {
    let! config = configuration
    and! candidate = cssCandidate
    let arguments = nativeArguments config candidate
    return stage "native source-cohort build" {
        workingDir __SOURCE_DIRECTORY__
        envVars [("NUGET_PACKAGES", nativeCache candidate); ("OS", "Windows_NT")]
        run (nativeCommand (["dotnet"; "build"; "tests/Ranvier.Anime.NativeSmoke/Ranvier.Anime.NativeSmoke.fsproj"; "-c"; config; "-r"; "win-x64"] @ arguments @ ["--no-restore"; $"-flp:logfile={nativeRoot candidate}/build-{config}.log;verbosity=normal"]))
    }
}

let nativeRun = input {
    let! config = configuration
    and! aot = Input.option<bool> "--aot"
    and! candidate = cssCandidate
    let output =
        if aot then $"{nativeRoot candidate}/publish/{config}"
        else $"{nativeRoot candidate}/build/bin/Ranvier.Anime.NativeSmoke/{config.ToLowerInvariant()}_net10.0_win-x64"
    return stage "native source-cohort smoke" {
        workingDir output
        envVars [("OS", "Windows_NT")]
        run (fun _ -> BuildNative.verifyStage())
        run (fun _ -> BuildNative.verifyOutput output)
        run (fun _ -> BuildNative.runSmoke output aot ($"{nativeRoot candidate}/run-{config}-" + (if aot then "aot" else "jit") + ".log"))
    }
}

let nativePublish = input {
    let! config = configuration
    and! candidate = cssCandidate
    let arguments = nativeArguments config candidate
    let output = $"{nativeRoot candidate}/publish/{config}"
    let log = $"{nativeRoot candidate}/publish-{config}.log"
    return stage "strict NativeAOT publish" {
        workingDir __SOURCE_DIRECTORY__
        envVars [("NUGET_PACKAGES", nativeCache candidate); ("OS", "Windows_NT")]
        run (nativeCommand (["proxy"; "dotnet"; "publish"; "tests/Ranvier.Anime.NativeSmoke/Ranvier.Anime.NativeSmoke.fsproj"; "-c"; config; "-r"; "win-x64"] @ arguments @ ["--no-restore"; "-o"; output; "-p:ILLinkTreatWarningsAsErrors=true"; "-p:IlcTreatWarningsAsErrors=true"; "-p:TrimmerSingleWarn=false"; "-p:IlcGenerateDgmlFile=true"; $"-flp:logfile={log};verbosity=normal"]))
        run (fun _ -> BuildNative.verifyPublishLog log)
    }
}

let cssProbeRestore = input {
    let! config = configuration
    and! candidate = cssCandidate
    let arguments = cohortArguments config candidate
    return stage "isolated typed CSS restore" {
        workingDir __SOURCE_DIRECTORY__
        envVars [("NUGET_PACKAGES", nativeCache candidate); ("OS", "Windows_NT")]
        run (fun _ -> if candidate then BuildCssCandidate.verify())
        run (nativeCommand (["dotnet"; "restore"; "tests/Ranvier.Anime.CssAotProbe/Ranvier.Anime.CssAotProbe.fsproj"; "-r"; "win-x64"] @ arguments @ ["-p:FSharpCoreImplicitPackageVersion=10.1.401"; "--artifacts-path"; nativeRoot candidate + "/build"; "--configfile"; nativeNuget candidate; "--force"; "--no-http-cache"]))
    }
}

let cssAotProbe = input {
    let! config = configuration
    and! candidate = cssCandidate
    let arguments = cohortArguments config candidate @ ["-p:FSharpCoreImplicitPackageVersion=10.1.401"; "--artifacts-path"; nativeRoot candidate + "/build"]
    let log = $"{nativeRoot candidate}/css-probe-{config}.log"
    return stage "isolated typed CSS AOT probe" {
        workingDir __SOURCE_DIRECTORY__
        envVars [("NUGET_PACKAGES", nativeCache candidate); ("OS", "Windows_NT")]
        run (nativeCommand (["proxy"; "dotnet"; "publish"; "tests/Ranvier.Anime.CssAotProbe/Ranvier.Anime.CssAotProbe.fsproj"; "-c"; config; "-r"; "win-x64"] @ arguments @ ["--no-restore"; "-o"; $"{nativeRoot candidate}/css-probe/{config}"; $"-flp:logfile={log};verbosity=normal"]))
        run (fun _ -> BuildNative.verifyPublishLog log)
    }
}

let build = input {
    let! config = configuration
    and! rebuild = Input.option<bool> "--rebuild"
    let command =
        if rebuild then cmd $"rtk dotnet build Ranvier.Anime.slnx -c {config} --no-restore --no-incremental"
        else cmd $"rtk dotnet build Ranvier.Anime.slnx -c {config} --no-restore"
    return stage "build" {
        workingDir __SOURCE_DIRECTORY__
        envVars [("NUGET_PACKAGES", packageCache)]
        run command
    }
}

let tests = input {
    let! config = configuration
    return stage "tests" {
        workingDir __SOURCE_DIRECTORY__
        envVars [("NUGET_PACKAGES", packageCache)]
        run (cmd $"rtk dotnet run --project tests/Ranvier.Anime.Tests -c {config} --no-build -- --summary --sequenced")
    }
}

let audit = input {
    let! script = Input.option<string> "--script" |> Input.def ""
    return stage "XML documentation audit" {
        workingDir __SOURCE_DIRECTORY__
        run (fun _ -> if System.String.IsNullOrWhiteSpace script then invalidArg "--script" "Supply the fsharp-xml-docs audit.fsx path.")
        run (cmd $"rtk proxy dotnet fsi {script} --compiler src tests")
    }
}

let identity = input {
    let! config = configuration
    return stage "package identity" {
        workingDir __SOURCE_DIRECTORY__
        run (fun _ ->
            let output = System.IO.Path.Combine(__SOURCE_DIRECTORY__, "tests", "Ranvier.Anime.Tests", "bin", config, "net10.0")
            let suffix = if config = "Debug" then ".Traced" else ""
            let hash path =
                use stream = System.IO.File.OpenRead path
                System.Security.Cryptography.SHA256.HashData stream |> System.Convert.ToHexString |> fun value -> value.ToLowerInvariant()
            let packages =
                ["Ranvier", "0.1.1-preview.1"; "Ranvier.Dom", "0.1.0-alpha.55"; "Ranvier.Css.Dom", "0.1.0-alpha.1"]
                |> List.map(fun (name, version) ->
                    let package = name + suffix
                    let packageDirectory = System.IO.Path.Combine(packageCache, package.ToLowerInvariant(), version)
                    let cacheDll = System.IO.Path.Combine(packageDirectory, "lib", "net10.0", name + ".dll")
                    let outputDll = System.IO.Path.Combine(output, name + ".dll")
                    let cacheHash, outputHash = hash cacheDll, hash outputDll
                    if cacheHash <> outputHash then failwithf "%s output differs from restored public package" name
                    if config = "Release" && name = "Ranvier.Dom" && cacheHash <> "c9b58dd32f9f217d312581a6d2b008fa9c03b1a42ecd6da650c7a74830289987" then
                        failwith "Ranvier.Dom alpha.55 differs from the coordinator's verified public DLL"
                    {| Package = package; Version = version; DllSha256 = cacheHash
                       PackageSha256 = hash (System.IO.Path.Combine(packageDirectory, package.ToLowerInvariant() + "." + version + ".nupkg")) |})
            let evidence = System.IO.Path.Combine(__SOURCE_DIRECTORY__, "artifacts", "qualification")
            System.IO.Directory.CreateDirectory evidence |> ignore
            let json = System.Text.Json.JsonSerializer.Serialize({| Configuration = config; PackageCache = packageCache; Packages = packages |})
            System.IO.File.WriteAllText(System.IO.Path.Combine(evidence, "packages-" + config + ".json"), json)
            printfn "%s: %d output assemblies match isolated package bytes" config packages.Length)
    }
}

let root = Command.root {
    description "Native Ranvier animation build"
    command "restore" { restore }
    command "build" { restore; build }
    command "test" { restore; build; tests }
    command "audit" { description "Audit F# XML documentation using the supplied skill script"; audit }
    command "identity" { description "Verify app assemblies against the isolated public package restore"; identity }
    command "native-restore" { nativeRestore }
    command "native-build" { nativeRestore; nativeBuild }
    command "native-run" { nativeRun }
    command "native-publish" { nativeRestore; nativePublish }
    command "css-aot-probe" { cssProbeRestore; cssAotProbe }
    command "native-gap-evidence" {
        input {
            let! config = configuration
            return stage "preserve typed CSS AOT evidence" {
                run (fun _ -> BuildNative.captureCssGap config)
            }
        }
    }
    command "css-candidate-copy" {
        input {
            let! receipt = Input.option<string> "--receipt" |> Input.def ""
            return stage "copy corrected private CSS cohort" {
                run (fun _ ->
                    if System.String.IsNullOrWhiteSpace receipt then invalidArg "--receipt" "Supply a currently granted CSS candidate receipt."
                    BuildCssCandidate.copy receipt)
            }
        }
    }
    command "css-candidate-evidence" {
        input {
            let! config = configuration
            return stage "preserve corrected private CSS replay" {
                run (fun _ ->
                    BuildNative.verifyPublishLog ($"{nativeRoot true}/publish-{config}.log")
                    BuildCssCandidate.capture config (nativeRoot true) (nativeCache true))
            }
        }
    }
    command "css-candidate-summary" {
        stage "summarize private candidate replay" {
            run (fun _ -> BuildCssCandidate.summarize())
        }
    }
    command "native-stage" {
        description "Copy and verify the coordinator-granted private runtime into the owned stage"
        input {
            let! manifest = Input.option<string> "--manifest" |> Input.def ""
            return stage "copy and verify native stage" {
                workingDir __SOURCE_DIRECTORY__
                run (fun _ ->
                    if System.String.IsNullOrWhiteSpace manifest then invalidArg "--manifest" "Supply a currently granted runtime manifest."
                    BuildNative.copyStage manifest)
            }
        }
    }
    command "reference" {
        description "Generate comparison fixtures from the pinned upstream Anime.js source"
        stage "upstream reference" {
            workingDir __SOURCE_DIRECTORY__
            run "rtk node tools/reference.mjs"
        }
    }
}
