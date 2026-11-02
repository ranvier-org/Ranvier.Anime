module BuildDefs

#i "nuget: https://nuget.cloudsmith.io/shayanhabibi/shayanhabibi-partas-build/v3/index.json"
#r "nuget: Partas.Build, 0.8.2"

open Partas.Build

let packageCache = System.IO.Path.Combine(__SOURCE_DIRECTORY__, "artifacts", "nuget-packages")

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
    command "reference" {
        description "Generate comparison fixtures from the pinned upstream Anime.js source"
        stage "upstream reference" {
            workingDir __SOURCE_DIRECTORY__
            run "rtk node tools/reference.mjs"
        }
    }
}
