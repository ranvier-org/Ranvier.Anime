module BuildDefs

#i "nuget: https://nuget.cloudsmith.io/shayanhabibi/shayanhabibi-partas-build/v3/index.json"
#r "nuget: Partas.Build, 0.8.2"

open Partas.Build

let configuration =
    Input.option<string> "--configuration"
    |> Input.alias "-c"
    |> Input.def "Release"
    |> Input.acceptOnlyFromAmong ["Debug"; "Release"]

let restore = stage "restore" {
    workingDir __SOURCE_DIRECTORY__
    run "rtk dotnet restore Ranvier.Anime.slnx"
}

let build = input {
    let! config = configuration
    return stage "build" {
        workingDir __SOURCE_DIRECTORY__
        run (cmd $"rtk dotnet build Ranvier.Anime.slnx -c {config} --no-restore")
    }
}

let tests = input {
    let! config = configuration
    return stage "tests" {
        workingDir __SOURCE_DIRECTORY__
        run (cmd $"rtk dotnet run --project tests/Ranvier.Anime.Tests -c {config} --no-build -- --summary --sequenced")
    }
}

let root = Command.root {
    description "Native Ranvier animation build"
    command "restore" { restore }
    command "build" { restore; build }
    command "test" { restore; build; tests }
}
