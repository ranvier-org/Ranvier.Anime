module BuildDefs

#i "nuget: https://nuget.cloudsmith.io/shayanhabibi/shayanhabibi-partas-build/v3/index.json"
#r "nuget: Partas.Build, 0.8.2"
#load "build.native.fsx"

open Partas.Build

let packageCache = System.IO.Path.Combine(__SOURCE_DIRECTORY__, "artifacts", "nuget-packages")

let nativeArguments config =
    let traced, suffix = if config = "Debug" then "true", ".Traced" else "false", ""
    [ $"-p:Configuration={config}"; "-p:UseLocalRanvier=true"; $"-p:RanvierRoot={__SOURCE_DIRECTORY__}/upstream/core-aot"
      $"-p:RanvierTrace={traced}"; $"-p:RanvierPackageSuffix={suffix}"; "-p:UseLocalRanvierWebCore=false"
      "-p:RanvierWebCoreNativeVersion=0.1.1-preview.7"; $"-p:RanvierWebCoreNativeDir={__SOURCE_DIRECTORY__}/artifacts/native-smoke/runtime"
      "-p:RanvierWebCoreNativeRid=win-x64"; "-p:TargetFrameworks=net10.0"; "-p:FSharpCoreImplicitPackageVersion=10.1.401"
      "--artifacts-path"; $"{__SOURCE_DIRECTORY__}/artifacts/native-smoke/build" ]

let nativeCommand arguments = { Cmd.create "rtk" "" with Arguments = arguments }

let configuration =
    Input.option<string> "--configuration"
    |> Input.alias "-c"
    |> Input.def "Release"
    |> Input.acceptOnlyFromAmong ["Debug"; "Release"]

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
    let arguments = nativeArguments config
    return stage "native source-cohort restore" {
        workingDir __SOURCE_DIRECTORY__
        envVars [("NUGET_PACKAGES", packageCache); ("OS", "Windows_NT")]
        run (fun _ -> BuildNative.verifySources())
        run (fun _ -> BuildNative.verifyStage())
        run (nativeCommand ["git"; "-C"; "upstream/core-aot"; "diff"; "--exit-code"; "HEAD"])
        run (nativeCommand ["git"; "-C"; "upstream/view-aot"; "diff"; "--exit-code"; "HEAD"])
        run (nativeCommand (["dotnet"; "restore"; "Ranvier.Anime.Native.slnx"; "-r"; "win-x64"] @ arguments @ ["--configfile"; "nuget.config"; "--force"; "--no-http-cache"]))
    }
}

let nativeBuild = input {
    let! config = configuration
    let arguments = nativeArguments config
    return stage "native source-cohort build" {
        workingDir __SOURCE_DIRECTORY__
        envVars [("NUGET_PACKAGES", packageCache); ("OS", "Windows_NT")]
        run (nativeCommand (["dotnet"; "build"; "tests/Ranvier.Anime.NativeSmoke/Ranvier.Anime.NativeSmoke.fsproj"; "-c"; config; "-r"; "win-x64"] @ arguments @ ["--no-restore"; $"-flp:logfile={__SOURCE_DIRECTORY__}/artifacts/native-smoke/build-{config}.log;verbosity=normal"]))
    }
}

let nativeRun = input {
    let! config = configuration
    and! aot = Input.option<bool> "--aot"
    let output =
        if aot then $"{__SOURCE_DIRECTORY__}/artifacts/native-smoke/publish/{config}"
        else $"{__SOURCE_DIRECTORY__}/artifacts/native-smoke/build/bin/Ranvier.Anime.NativeSmoke/{config.ToLowerInvariant()}_net10.0_win-x64"
    return stage "native source-cohort smoke" {
        workingDir output
        envVars [("OS", "Windows_NT")]
        run (fun _ -> BuildNative.verifyStage())
        run (fun _ -> BuildNative.verifyOutput output)
        run (nativeCommand (["proxy"; output + "/Ranvier.Anime.NativeSmoke.exe"] @ if aot then ["--require-aot"] else []))
    }
}

let nativePublish = input {
    let! config = configuration
    let arguments = nativeArguments config
    let output = $"{__SOURCE_DIRECTORY__}/artifacts/native-smoke/publish/{config}"
    let log = $"{__SOURCE_DIRECTORY__}/artifacts/native-smoke/publish-{config}.log"
    return stage "strict NativeAOT publish" {
        workingDir __SOURCE_DIRECTORY__
        envVars [("NUGET_PACKAGES", packageCache); ("OS", "Windows_NT")]
        run (nativeCommand (["proxy"; "dotnet"; "publish"; "tests/Ranvier.Anime.NativeSmoke/Ranvier.Anime.NativeSmoke.fsproj"; "-c"; config; "-r"; "win-x64"] @ arguments @ ["--no-restore"; "-o"; output; "-p:ILLinkTreatWarningsAsErrors=true"; "-p:IlcTreatWarningsAsErrors=true"; "-p:TrimmerSingleWarn=false"; "-p:IlcGenerateDgmlFile=true"; $"-flp:logfile={log};verbosity=normal"]))
        run (fun _ -> BuildNative.verifyPublishLog log)
    }
}

let cssProbeRestore = input {
    let! config = configuration
    return stage "isolated typed CSS restore" {
        workingDir __SOURCE_DIRECTORY__
        envVars [("NUGET_PACKAGES", packageCache); ("OS", "Windows_NT")]
        run (nativeCommand ["dotnet"; "restore"; "tests/Ranvier.Anime.CssAotProbe/Ranvier.Anime.CssAotProbe.fsproj"; "-r"; "win-x64"; $"-p:Configuration={config}"; "-p:FSharpCoreImplicitPackageVersion=10.1.401"; "--artifacts-path"; $"{__SOURCE_DIRECTORY__}/artifacts/native-smoke/build"; "--configfile"; "nuget.config"; "--force"; "--no-http-cache"])
    }
}

let cssAotProbe = input {
    let! config = configuration
    let arguments = [$"-p:Configuration={config}"; "-p:FSharpCoreImplicitPackageVersion=10.1.401"; "--artifacts-path"; $"{__SOURCE_DIRECTORY__}/artifacts/native-smoke/build"]
    let log = $"{__SOURCE_DIRECTORY__}/artifacts/native-smoke/css-probe-{config}.log"
    return stage "isolated typed CSS AOT probe" {
        workingDir __SOURCE_DIRECTORY__
        envVars [("NUGET_PACKAGES", packageCache); ("OS", "Windows_NT")]
        run (nativeCommand (["proxy"; "dotnet"; "publish"; "tests/Ranvier.Anime.CssAotProbe/Ranvier.Anime.CssAotProbe.fsproj"; "-c"; config; "-r"; "win-x64"] @ arguments @ ["--no-restore"; "-o"; $"{__SOURCE_DIRECTORY__}/artifacts/native-smoke/css-probe/{config}"; $"-flp:logfile={log};verbosity=normal"]))
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
