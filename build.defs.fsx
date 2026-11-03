module BuildDefs

#i "nuget: https://nuget.cloudsmith.io/shayanhabibi/shayanhabibi-partas-build/v3/index.json"
#r "nuget: Partas.Build, 0.8.2"
#load "build.native.fsx"
#load "build.core56.fsx"
#load "build.provenance.fsx"
#load "build.css-candidate.fsx"
#load "build.evidence-tests.fsx"
#load "build.public45.fsx"

open Partas.Build

let packageCache = System.IO.Path.Combine(__SOURCE_DIRECTORY__, "artifacts", "nuget-packages")

let nativeRoot candidate publicCore =
    if candidate && publicCore then invalidArg "public-core" "Public Core and the private CSS candidate are separate cohorts."
    System.IO.Path.Combine(__SOURCE_DIRECTORY__, "artifacts", if publicCore then "native-core56-css2" elif candidate then "native-css-70ac" else "native-css-public-alpha2")

let nativeCache candidate publicCore =
    System.IO.Path.Combine(__SOURCE_DIRECTORY__, "artifacts", if publicCore then "nuget-core56-css2" elif candidate then "nuget-css-70ac" else "nuget-css-public-alpha2")

let nativeNuget candidate = if candidate then "nuget.private-css.config" else "nuget.config"

let cohortArguments config candidate =
    let traced, suffix = if config = "Debug" then "true", ".Traced" else "false", ""
    [$"-p:Configuration={config}"; $"-p:RanvierTrace={traced}"; $"-p:RanvierPackageSuffix={suffix}"] @
    (if candidate then ["-p:RanvierCssVersion=0.1.1-alpha.1-css-aot.2"; "-p:RanvierCssBindingsVersion=0.1.1-alpha.1-css-aot.2"]
     else ["-p:RanvierCssVersion=0.1.1-alpha.2"; "-p:RanvierCssBindingsVersion=0.1.1-alpha.1"])

let nativeArguments config candidate publicCore =
    cohortArguments config candidate @
    [ (if publicCore then "-p:UseLocalRanvier=false" else "-p:UseLocalRanvier=true")
      (if publicCore then "-p:NativeSmokeGraph=core56" else "-p:NativeSmokeGraph=source")
      (if publicCore then "-p:RanvierDomVersion=0.1.0-alpha.56" else "-p:RanvierDomVersion=0.1.0-alpha.55"); $"-p:RanvierRoot={__SOURCE_DIRECTORY__}/upstream/core-aot"; "-p:UseLocalRanvierWebCore=false"
      "-p:RanvierWebCoreNativeVersion=0.1.1-preview.7"; $"-p:RanvierWebCoreNativeDir={__SOURCE_DIRECTORY__}/artifacts/native-smoke/runtime"
      "-p:RanvierWebCoreNativeRid=win-x64"; "-p:TargetFrameworks=net10.0"; "-p:FSharpCoreImplicitPackageVersion=10.1.401"
      "--artifacts-path"; nativeRoot candidate publicCore + "/build" ]

let nativeCommand arguments = { Cmd.create "rtk" "" with Arguments = arguments }

let configuration =
    Input.option<string> "--configuration"
    |> Input.alias "-c"
    |> Input.def "Release"
    |> Input.acceptOnlyFromAmong ["Debug"; "Release"]

let cssCandidate = Input.option<bool> "--css-candidate"
let publicCore = Input.option<bool> "--public-core"

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
    and! publicCore = publicCore
    let arguments = nativeArguments config candidate publicCore
    return stage "native source-cohort restore" {
        workingDir __SOURCE_DIRECTORY__
        envVars [("NUGET_PACKAGES", nativeCache candidate publicCore); ("OS", "Windows_NT")]
        run (fun _ -> if candidate then BuildCssCandidate.verify())
        run (fun _ -> if publicCore then BuildCore56.verifyAudit() |> ignore)
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
    and! publicCore = publicCore
    let arguments = nativeArguments config candidate publicCore
    return stage "native source-cohort build" {
        workingDir __SOURCE_DIRECTORY__
        envVars [("NUGET_PACKAGES", nativeCache candidate publicCore); ("OS", "Windows_NT")]
        run (nativeCommand (["dotnet"; "build"; "tests/Ranvier.Anime.NativeSmoke/Ranvier.Anime.NativeSmoke.fsproj"; "-c"; config; "-r"; "win-x64"] @ arguments @ ["--no-restore"; $"-flp:logfile={nativeRoot candidate publicCore}/build-{config}.log;verbosity=normal"]))
    }
}

let nativeRun = input {
    let! config = configuration
    and! aot = Input.option<bool> "--aot"
    and! candidate = cssCandidate
    and! publicCore = publicCore
    let output =
        if aot then $"{nativeRoot candidate publicCore}/publish/{config}"
        else $"{nativeRoot candidate publicCore}/build/bin/Ranvier.Anime.NativeSmoke/{config.ToLowerInvariant()}_net10.0_win-x64"
    return stage "native source-cohort smoke" {
        workingDir output
        envVars [("OS", "Windows_NT")]
        run (fun _ -> if aot then BuildProvenance.requirePublish __SOURCE_DIRECTORY__ (nativeRoot candidate publicCore) config |> ignore)
        run (fun _ -> BuildNative.verifyStage())
        run (fun _ -> BuildNative.verifyOutput output)
        run (fun _ -> BuildNative.runSmoke output aot ($"{nativeRoot candidate publicCore}/run-{config}-" + (if aot then "aot" else "jit") + ".log"))
    }
}

let nativePublish = input {
    let! config = configuration
    and! candidate = cssCandidate
    and! publicCore = publicCore
    let arguments = nativeArguments config candidate publicCore
    let output = $"{nativeRoot candidate publicCore}/publish/{config}"
    let log = $"{nativeRoot candidate publicCore}/publish-{config}.log"
    return stage "strict NativeAOT publish" {
        workingDir __SOURCE_DIRECTORY__
        envVars [("NUGET_PACKAGES", nativeCache candidate publicCore); ("OS", "Windows_NT")]
        run (fun _ -> BuildProvenance.beginPublish __SOURCE_DIRECTORY__ (nativeRoot candidate publicCore) config)
        run (nativeCommand (["proxy"; "dotnet"; "publish"; "tests/Ranvier.Anime.NativeSmoke/Ranvier.Anime.NativeSmoke.fsproj"; "-c"; config; "-r"; "win-x64"] @ arguments @ ["--no-restore"; "-o"; output; "-p:ILLinkTreatWarningsAsErrors=true"; "-p:IlcTreatWarningsAsErrors=true"; "-p:TrimmerSingleWarn=false"; "-p:IlcGenerateDgmlFile=true"; $"-flp:logfile={log};verbosity=normal"]))
        run (fun _ ->
            BuildNative.verifyPublishLog log
            let packages =
                System.Text.Json.JsonSerializer.SerializeToElement(BuildCssCandidate.packageProof config (nativeRoot candidate publicCore) (nativeCache candidate publicCore) candidate publicCore)
            BuildProvenance.recordPublish __SOURCE_DIRECTORY__ (nativeRoot candidate publicCore) config packages)
    }
}

let cssProbeRestore = input {
    let! config = configuration
    and! candidate = cssCandidate
    and! publicCore = publicCore
    let arguments = cohortArguments config candidate
    return stage "isolated typed CSS restore" {
        workingDir __SOURCE_DIRECTORY__
        envVars [("NUGET_PACKAGES", nativeCache candidate publicCore); ("OS", "Windows_NT")]
        run (fun _ -> if candidate then BuildCssCandidate.verify())
        run (nativeCommand (["dotnet"; "restore"; "tests/Ranvier.Anime.CssAotProbe/Ranvier.Anime.CssAotProbe.fsproj"; "-r"; "win-x64"] @ arguments @ ["-p:FSharpCoreImplicitPackageVersion=10.1.401"; "--artifacts-path"; nativeRoot candidate publicCore + "/build"; "--configfile"; nativeNuget candidate; "--force"; "--no-http-cache"]))
    }
}

let cssAotProbe = input {
    let! config = configuration
    and! candidate = cssCandidate
    and! publicCore = publicCore
    let arguments = cohortArguments config candidate @ ["-p:FSharpCoreImplicitPackageVersion=10.1.401"; "--artifacts-path"; nativeRoot candidate publicCore + "/build"]
    let log = $"{nativeRoot candidate publicCore}/css-probe-{config}.log"
    return stage "isolated typed CSS AOT probe" {
        workingDir __SOURCE_DIRECTORY__
        envVars [("NUGET_PACKAGES", nativeCache candidate publicCore); ("OS", "Windows_NT")]
        run (nativeCommand (["proxy"; "dotnet"; "publish"; "tests/Ranvier.Anime.CssAotProbe/Ranvier.Anime.CssAotProbe.fsproj"; "-c"; config; "-r"; "win-x64"] @ arguments @ ["--no-restore"; "-o"; $"{nativeRoot candidate publicCore}/css-probe/{config}"; $"-flp:logfile={log};verbosity=normal"]))
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
        run (cmd $"rtk proxy dotnet fsi {script} --check src tests")
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
    command "public-native-audits" {
        input {
            let! view = Input.option<string> "--view"
            and! native = Input.option<string> "--native"
            return stage "preserve public cohort audits" {
                run (fun _ -> BuildPublic45.copyAudits view native)
            }
        }
    }
    command "public-native-restore" {
        input {
            let! config = configuration
            return stage "isolated public cohort restore" {
                workingDir __SOURCE_DIRECTORY__
                envVars [("NUGET_PACKAGES", BuildPublic45.cache); ("OS", "Windows_NT")]
                run (fun _ -> BuildPublic45.verifyAudits())
                run (nativeCommand (["dotnet"; "restore"; "Ranvier.Anime.Native.slnx"; "-r"; "win-x64"] @ BuildPublic45.arguments config @ ["--configfile"; "nuget.config"; "--force"; "--no-http-cache"]))
            }
        }
    }
    command "public-native-build" {
        input {
            let! config = configuration
            return stage "build public cohort consumer" {
                workingDir __SOURCE_DIRECTORY__
                envVars [("NUGET_PACKAGES", BuildPublic45.cache); ("OS", "Windows_NT")]
                run (fun _ -> BuildPublic45.verifyAudits())
                run (nativeCommand (["dotnet"; "build"; "tests/Ranvier.Anime.NativeSmoke/Ranvier.Anime.NativeSmoke.fsproj"; "-c"; config; "-r"; "win-x64"] @ BuildPublic45.arguments config @ ["--no-restore"; $"-flp:logfile={BuildPublic45.stage}/build-{config}.log;verbosity=normal"]))
            }
        }
    }
    command "public-native-smoke" {
        input {
            let! config = configuration
            return stage "fresh public cohort JIT smoke" {
                workingDir __SOURCE_DIRECTORY__
                envVars [("NUGET_PACKAGES", BuildPublic45.cache); ("OS", "Windows_NT")]
                run (fun _ -> BuildPublic45.verifyAudits())
                run (nativeCommand (["dotnet"; "build"; "tests/Ranvier.Anime.NativeSmoke/Ranvier.Anime.NativeSmoke.fsproj"; "-c"; config; "-r"; "win-x64"] @ BuildPublic45.arguments config @ ["--no-restore"; $"-flp:logfile={BuildPublic45.stage}/build-{config}.log;verbosity=normal"]))
                run (fun _ ->
                    let output = System.IO.Path.Combine(BuildPublic45.stage, "build", "bin", "Ranvier.Anime.NativeSmoke", config.ToLowerInvariant() + "_net10.0_win-x64")
                    let managed = BuildPublic45.packageProof config false
                    let native = BuildPublic45.nativeProof output
                    let identity = System.Text.Json.JsonSerializer.Serialize({| Configuration = config; PublicCorePackages = true; PublicViewPackages = true; PublicNativePayload = true; HardwareAccelerationQualified = false; Managed = managed; Native = native |})
                    System.IO.File.WriteAllText(System.IO.Path.Combine(BuildPublic45.stage, "identity-" + config + "-jit.json"), identity)
                    BuildNative.runSmoke output false (System.IO.Path.Combine(BuildPublic45.stage, "run-" + config + "-jit.log")))
            }
        }
    }
    command "public-native-publish" {
        input {
            let! config = configuration
            let output = System.IO.Path.Combine(BuildPublic45.stage, "publish", config)
            let log = System.IO.Path.Combine(BuildPublic45.stage, "publish-" + config + ".log")
            return stage "strict public cohort NativeAOT publish" {
                workingDir __SOURCE_DIRECTORY__
                envVars [("NUGET_PACKAGES", BuildPublic45.cache); ("OS", "Windows_NT")]
                run (fun _ -> BuildPublic45.verifyAudits())
                run (fun _ -> BuildProvenance.beginPublish __SOURCE_DIRECTORY__ BuildPublic45.stage config)
                run (nativeCommand (["proxy"; "dotnet"; "publish"; "tests/Ranvier.Anime.NativeSmoke/Ranvier.Anime.NativeSmoke.fsproj"; "-c"; config; "-r"; "win-x64"] @ BuildPublic45.arguments config @ ["--no-restore"; "-o"; output; "-p:ILLinkTreatWarningsAsErrors=true"; "-p:IlcTreatWarningsAsErrors=true"; "-p:TrimmerSingleWarn=false"; "-p:IlcGenerateDgmlFile=true"; $"-flp:logfile={log};verbosity=normal"]))
                run (fun _ ->
                    BuildNative.verifyPublishLog log
                    BuildProvenance.recordPublish __SOURCE_DIRECTORY__ BuildPublic45.stage config (BuildPublic45.publicationProof config))
            }
        }
    }
    command "public-native-aot-run" {
        input {
            let! config = configuration
            return stage "execute verified public cohort PE" {
                envVars [("OS", "Windows_NT")]
                run (fun _ -> BuildPublic45.runAot config)
            }
        }
    }
    command "public-native-evidence" {
        input {
            let! config = configuration
            and! label = Input.option<string> "--label" |> Input.def ""
            return stage "preserve executed public cohort evidence" {
                run (fun _ -> BuildPublic45.capture config label)
            }
        }
    }
    command "public-native-summary" {
        input {
            let! label = Input.option<string> "--label" |> Input.def ""
            return stage "summarize public cohort evidence" {
                run (fun _ -> BuildPublic45.summarize label)
            }
        }
    }
    command "public-native-evidence-test" {
        input {
            let! config = configuration
            return stage "reject public provenance substitutions" {
                run (fun _ -> BuildPublic45.regressions config)
            }
        }
    }
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
                    BuildNative.verifyPublishLog ($"{nativeRoot true false}/publish-{config}.log")
                    BuildCssCandidate.capture config (nativeRoot true false) (nativeCache true false) true false)
            }
        }
    }
    command "css-candidate-summary" {
        stage "summarize private candidate replay" {
            run (fun _ -> BuildCssCandidate.summarize true false)
        }
    }
    command "evidence-test" {
        stage "native evidence regressions" {
            run (fun _ -> BuildEvidenceTests.stalePublish())
            run (fun _ -> BuildEvidenceTests.publishBinding())
            run (fun _ -> BuildEvidenceTests.substitutedDll())
            run (fun _ -> BuildEvidenceTests.timeout())
        }
    }
    command "css-public-evidence" {
        input {
            let! config = configuration
            return stage "preserve public CSS replay" {
                run (fun _ -> BuildCssCandidate.capture config (nativeRoot false false) (nativeCache false false) false false)
            }
        }
    }
    command "css-public-summary" {
        stage "summarize public CSS replay" {
            run (fun _ -> BuildCssCandidate.summarize false false)
        }
    }
    command "core56-audit-copy" {
        input {
            let! receipt = Input.option<string> "--receipt" |> Input.def ""
            return stage "preserve public Core byte audit" {
                run (fun _ ->
                    if System.String.IsNullOrWhiteSpace receipt then invalidArg "--receipt" "Supply the coordinator's public byte-audit path."
                    BuildCore56.copyAudit receipt)
            }
        }
    }
    command "core56-evidence" {
        input {
            let! config = configuration
            and! label = Input.option<string> "--label" |> Input.def ""
            return stage "preserve public Core/CSS native replay" {
                run (fun _ -> BuildCssCandidate.captureNamed config (nativeRoot false true) (nativeCache false true) false true label)
            }
        }
    }
    command "core56-summary" {
        input {
            let! label = Input.option<string> "--label" |> Input.def ""
            return stage "summarize public Core/CSS native replay" {
                run (fun _ -> BuildCssCandidate.summarizeNamed false true label)
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
    command "animatable-reference" {
        stage "upstream animatable reference" {
            workingDir __SOURCE_DIRECTORY__
            run "rtk node tools/animatable.mjs"
        }
    }
    command "keyframe-reference" {
        stage "upstream keyframe reference" {
            workingDir __SOURCE_DIRECTORY__
            run "rtk node tools/keyframes.mjs"
            run "rtk node tools/keyframes.mjs --playback"
            run "rtk node tools/keyframes.mjs --inputs"
            run "rtk node tools/keyframes.mjs --computed"
            run "rtk node tools/timeline-keyframes.mjs"
            run "rtk node tools/timeline-css-keyframes.mjs"
            run "rtk node tools/keyframe-css.mjs"
            run "rtk node tools/keyframe-sequences.mjs"
        }
    }
    command "css-metadata" {
        description "Regenerate or verify snapshot metadata from the matched WebKit Git blob"
        input {
            let! source = Input.option<string> "--source" |> Input.def "C:/wk/src"
            and! check = Input.option<bool> "--check"
            return stage "matched native CSS metadata" {
                workingDir __SOURCE_DIRECTORY__
                run ({ Cmd.create "rtk" "" with Arguments=["node";"tools/css-metadata.mjs";"--source";source] @ (if check then ["--check"] else []) })
            }
        }
    }
    command "sequence-reference" {
        stage "upstream sparse sequence reference" {
            workingDir __SOURCE_DIRECTORY__
            run "rtk node tools/keyframe-sequences.mjs"
        }
    }
    command "computed-reference" {
        stage "upstream computed keyframe reference" {
            workingDir __SOURCE_DIRECTORY__
            run "rtk node tools/keyframes.mjs --computed"
            run "rtk node tools/timeline-keyframes.mjs"
            run "rtk node tools/timeline-css-keyframes.mjs"
        }
    }
    command "timeline-keyframe-reference" {
        stage "upstream owned timeline keyframe reference" {
            workingDir __SOURCE_DIRECTORY__
            run "rtk node tools/timeline-keyframes.mjs"
            run "rtk node tools/timeline-css-keyframes.mjs"
        }
    }
    command "unit-reference" {
        stage "upstream unit conversion reference" {
            workingDir __SOURCE_DIRECTORY__
            run "rtk node tools/unit-conversion.mjs"
        }
    }
    command "value-reference" {
        stage "upstream value interpolation reference" {
            workingDir __SOURCE_DIRECTORY__
            run "rtk node tools/value-interpolation.mjs"
            run "rtk node tools/unit-conversion.mjs"
        }
    }
    command "easing-reference" {
        stage "upstream easing curves reference" {
            workingDir __SOURCE_DIRECTORY__
            run "rtk node tools/easing-curves.mjs"
            run "rtk node tools/value-interpolation.mjs"
        }
    }
    command "reference" {
        description "Generate comparison fixtures from the pinned upstream Anime.js source"
        stage "upstream reference" {
            workingDir __SOURCE_DIRECTORY__
            run "rtk node tools/reference.mjs"
            run "rtk node tools/timer-controls.mjs"
            run "rtk node tools/animation-playback.mjs"
            run "rtk node tools/timeline-playback.mjs"
            run "rtk node tools/easing-curves.mjs"
            run "rtk node tools/value-interpolation.mjs"
            run "rtk node tools/unit-conversion.mjs"
            run "rtk node tools/animatable.mjs"
            run "rtk node tools/keyframes.mjs"
            run "rtk node tools/keyframes.mjs --playback"
            run "rtk node tools/keyframes.mjs --inputs"
            run "rtk node tools/keyframes.mjs --computed"
            run "rtk node tools/keyframe-css.mjs"
            run "rtk node tools/keyframe-sequences.mjs"
            run "rtk node tools/timeline-keyframes.mjs"
            run "rtk node tools/timeline-css-keyframes.mjs"
        }
    }
}
