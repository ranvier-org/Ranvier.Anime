#i "nuget: https://nuget.cloudsmith.io/shayanhabibi/shayanhabibi-partas-build/v3/index.json"
#r "nuget: Partas.Build, 0.8.2"
#r "nuget: Partas.Build.Baked, 0.3.0"
#r "nuget: Partas.Build.EasyBuild.ShipIt, 0.1.0"

open System
open System.Diagnostics
open System.IO
open Partas.Build

module Spec =
    let root = __SOURCE_DIRECTORY__

    /// The packed libraries in dependency order. Each packs untraced and again as its `.Traced` package.
    let sources =
        [
            "src/Ranvier.Anime/Ranvier.Anime.fsproj"
            "src/Fable.Ranvier.Anime/Fable.Ranvier.Anime.fsproj"
        ]

    let groups = [ "all"; "native"; "bindings"; "workflows" ]

    /// The native port's sources keep the port's own layout; Fantomas formats the rest.
    let unformatted =
        [
            "src/Ranvier.Anime/"
            "src/Ranvier.View.Anime/"
            "tests/Ranvier.Anime."
            "tests/Ranvier.View.Anime.Browser/"
            "tests/Portable/"
            "examples/"
            ".SageFs/"
            "build.defs.fsx"
            "build.native.fsx"
        ]

    let localTools =
        use manifest =
            Text.Json.JsonDocument.Parse (File.ReadAllText (Path.Combine (root, ".config/dotnet-tools.json")))

        manifest.RootElement.GetProperty("tools").EnumerateObject()
        |> Seq.map (fun tool -> tool.Name, tool.Value.GetRawText ())
        |> Seq.toList

    /// The Python launcher: `python3` on Linux and macOS runners, `python` on Windows.
    let python = if OperatingSystem.IsWindows () then "python" else "python3"

/// Runs `file` with `arguments` in the repository root and returns its standard output; a non-zero exit fails.
let capture (file: string) (arguments: string list) : string =
    let info =
        ProcessStartInfo (file, WorkingDirectory = Spec.root, RedirectStandardOutput = true, UseShellExecute = false)

    for argument in arguments do
        info.ArgumentList.Add argument

    use proc = Process.Start info
    let output = proc.StandardOutput.ReadToEnd ()
    proc.WaitForExit ()

    if proc.ExitCode <> 0 then
        failwith $"""{file} {String.Join (" ", arguments)} exited with {proc.ExitCode}"""

    output

module Options =
    let group =
        Input.option "--group"
        |> Input.mapFromAmong (Spec.groups |> List.map (fun name -> name, name))
        |> Input.def "all"
        |> Input.description "Checks to run (default: all)"

    let dryFormat =
        Input.option<bool> "--dry-format"
        |> Input.description "Check formatting without rewriting files"

    let dryPublish =
        Input.option<bool> "--dry-publish"
        |> Input.description "Build and pack without uploading packages"

    let ranvierRoot =
        Input.option<string> "--ranvier-root"
        |> Input.def (Path.GetFullPath (Path.Combine (Spec.root, "../Ranvier")))
        |> Input.description "Path to the sibling Ranvier repository"

    let withCss =
        Input.option<bool> "--with-css"
        |> Input.description "Also use a sibling Ranvier.Css checkout"

    let ranvierCssRoot =
        Input.option<string> "--ranvier-css-root"
        |> Input.def (Path.GetFullPath (Path.Combine (Spec.root, "../Ranvier.Css")))
        |> Input.description "Path to the sibling Ranvier.Css repository (with --with-css)"

module Stage =
    let restoreTools =
        stage "restore tools in isolated SDK processes" {
            for name, registration in Spec.localTools do
                stage $"restore {name}" {
                    run (fun _ ->
                        let directory = Path.Combine (Spec.root, ".config/.tool-restore")
                        Directory.CreateDirectory directory |> ignore

                        let json =
                            "{\"version\":1,\"isRoot\":true,\"tools\":{"
                            + Text.Json.JsonSerializer.Serialize (name)
                            + ":"
                            + registration
                            + "}}"

                        File.WriteAllText (Path.Combine (directory, name + ".json"), json))

                    run (
                        Cmd.ofList
                            "dotnet"
                            [
                                "tool"
                                "restore"
                                "--tool-manifest"
                                Path.Combine (Spec.root, ".config/.tool-restore", name + ".json")
                                "--verbosity"
                                "q"
                            ]
                    )
                }
        }

    let restore =
        input {
            let! quick = Baked.Common.quick
            and! config = Baked.Dotnet.configOrRelease

            return
                stage "restore" {
                    when' (not quick)
                    restoreTools
                    stage "restore solution" { run (cmd $"dotnet restore Ranvier.Anime.slnx -p:Configuration={config}") }
                }
        }

    let build =
        input {
            let! config = Baked.Dotnet.configOrRelease

            return
                stage "build" {
                    for project in Spec.sources do
                        stage $"build {project}" { run (cmd $"dotnet build {project} -c {config} -m:1 --no-restore") }
                }
        }

    let buildSolution =
        input {
            let! config = Baked.Dotnet.configOrRelease
            return stage "build solution" { run (cmd $"dotnet build Ranvier.Anime.slnx -c {config} --no-restore") }
        }

    let format =
        input {
            let! check = Options.dryFormat

            return
                stage "format" {
                    run (fun _ ->
                        // Tracked sources in batches, so each command stays under the Windows command-line limit.
                        let files =
                            (capture "git" [ "ls-files"; "*.fs"; "*.fsx" ])
                                .Split(
                                    '\n',
                                    StringSplitOptions.RemoveEmptyEntries
                                    ||| StringSplitOptions.TrimEntries
                                )
                            |> Array.toList
                            |> List.filter (fun file -> not (Spec.unformatted |> List.exists file.StartsWith))

                        for batch in files |> List.chunkBySize 150 do
                            let arguments =
                                [ "fantomas" ]
                                @ batch
                                @ (if check then [ "--check" ] else [])

                            capture "dotnet" arguments |> ignore)
                }
        }

    /// Installs a directory's pinned npm packages, unless `--quick`.
    let npm (directory: string) =
        input {
            let! quick = Baked.Common.quick

            return
                stage $"npm ci in {directory}" {
                    when' (not quick)
                    workingDir (Path.Combine (Spec.root, directory))
                    run "npm ci"
                }
        }

    /// Installs Playwright's Chromium for a directory's npm package, unless `--quick`.
    let chromium (directory: string) =
        let directory = Path.Combine (Spec.root, directory)

        let installer =
            Path.GetRelativePath(directory, Path.Combine (Spec.root, "tools/install-chromium.mjs")).Replace('\\', '/')

        input {
            let! quick = Baked.Common.quick

            return
                stage "install Chromium" {
                    when' (not quick)
                    workingDir directory
                    run $"node {installer}"
                }
        }

    /// The Fable.Ranvier.Anime bindings under jsdom, their emit, type errors and bundle budget, and typed CSS
    /// animation in Chromium.
    let bindings =
        input {
            let! npm = npm "tests/Fable.Ranvier.Anime.Tests"
            and! chromium = chromium "tests/Fable.Ranvier.Anime.Tests"

            return
                stage "bindings" {
                    npm
                    chromium

                    stage "bindings tests" {
                        workingDir (Path.Combine (Spec.root, "tests/Fable.Ranvier.Anime.Tests"))
                        run "npm test"
                        run "npm run test:browser"
                    }
                }
        }

    /// The native port's Expecto suite, against the build the solution stage produced.
    let native =
        input {
            let! config = Baked.Dotnet.configOrRelease

            return
                stage "native tests" { run (cmd $"dotnet run --project tests/Ranvier.Anime.Tests -c {config} --no-build -- --summary --sequenced") }
        }

    /// The contribution and version ownership checks the workflows run.
    let workflows =
        stage "workflows" {
            run "node --test .github/scripts/contrib.test.cjs"
            run (Cmd.ofList Spec.python [ "-I"; "tests/test_versioning.py" ])
            run (Cmd.ofList Spec.python [ "-I"; ".github/scripts/check_versioning.py" ])
        }

    let testsWith (group: InputSpec<string>) =
        input {
            let! group = group
            and! skip = Baked.Common.skipTests
            and! bindings = bindings
            and! native = native

            let chosen name =
                group = "all" || group = name

            return
                stage "checks" {
                    when' (not skip)

                    stage "native group" {
                        when' (chosen "native")
                        native
                    }

                    stage "bindings group" {
                        when' (chosen "bindings")
                        bindings
                    }

                    stage "workflows group" {
                        when' (chosen "workflows")
                        workflows
                    }
                }
        }

    let tests = testsWith (InputSpec.ofInput Options.group)
    let allTests = testsWith (InputSpec.ret "all")

    let packWith (traced: bool) =
        input {
            let! config = Baked.Dotnet.configOrRelease
            let label = if traced then "traced" else "untraced"

            return
                stage $"pack {label} libraries" {
                    for project in Spec.sources do
                        stage $"pack {label} {project}" {
                            run (
                                if traced then
                                    cmd $"dotnet pack {project} -c {config} -p:RanvierTrace=true -m:1 -o bin"
                                else
                                    cmd $"dotnet pack {project} -c {config} --no-build --no-restore -o bin"
                            )
                        }
                }
        }

    /// <summary>Packs the libraries and their <c>.Traced</c> variants.</summary>
    /// <remarks>The traced packs restore and build again after every untraced pack.</remarks>
    let pack =
        input {
            let! clean =
                Baked.Stages.cleanWith (InputSpec.ret false) [] [ "bin/*.nupkg"; "bin/*.snupkg" ]

            and! untraced = packWith false
            and! traced = packWith true

            return
                stage "pack" {
                    clean
                    untraced
                    traced
                }
        }

    let publish =
        input {
            let! key = Baked.NuGet.apiKey.option
            and! dry = Options.dryPublish

            return
                stage "publish" {
                    when' (not dry)

                    stage "push (requires NUGET_API_KEY or --nuget-key)" {
                        failIfIgnored
                        when' key.IsSome

                        run (
                            cmd $"dotnet nuget push bin/*.nupkg --source https://api.nuget.org/v3/index.json --skip-duplicate"
                            |> Cmd.secretOption "--api-key" (Option.defaultValue "" key)
                        )
                    }
                }
        }

    let local =
        input {
            let! root = Options.ranvierRoot
            and! withCss = Options.withCss
            and! cssRoot = Options.ranvierCssRoot
            let root = Path.GetFullPath (root, Spec.root)
            let cssRoot = Path.GetFullPath (cssRoot, Spec.root)

            return
                stage "configure sibling checkouts" {
                    stage "clone Ranvier if missing" {
                        when' (not (Directory.Exists root))
                        run (cmd $"git clone https://github.com/ranvier-org/Ranvier.git {root}")
                    }

                    stage "clone Ranvier.Css if missing" {
                        when' (withCss && not (Directory.Exists cssRoot))
                        run (cmd $"git clone https://github.com/ranvier-org/Ranvier.Css.git {cssRoot}")
                    }

                    stage "write local configuration" {
                        run (fun _ ->
                            if not (File.Exists (Path.Combine (root, "src/Ranvier/Ranvier.fsproj"))) then
                                invalidArg "ranvier-root" "The path is not a Ranvier checkout."

                            if
                                withCss
                                && not (File.Exists (Path.Combine (cssRoot, "src/Ranvier.Css/Ranvier.Css.fsproj")))
                            then
                                invalidArg "ranvier-css-root" "The path is not a Ranvier.Css checkout."

                            let escape (path: string) =
                                Security.SecurityElement.Escape path

                            let css =
                                if withCss then
                                    $"<UseLocalRanvierCss>true</UseLocalRanvierCss><RanvierCssRoot>{escape cssRoot}</RanvierCssRoot>"
                                else
                                    ""

                            File.WriteAllText (
                                Path.Combine (Spec.root, "Local.Build.props"),
                                $"<Project><PropertyGroup><UseLocalRanvier>true</UseLocalRanvier><RanvierRoot>{escape root}</RanvierRoot>{css}</PropertyGroup></Project>"
                            ))
                    }
                }
        }

    let packages =
        stage "configure published package dependencies" {
            run (fun _ ->
                File.WriteAllText (
                    Path.Combine (Spec.root, "Local.Build.props"),
                    "<Project><PropertyGroup><UseLocalRanvier>false</UseLocalRanvier><UseLocalRanvierCss>false</UseLocalRanvierCss></PropertyGroup></Project>"
                ))
        }

    let docs =
        input {
            let! watch = Baked.Common.watch

            return
                stage "documentation" {
                    run (
                        cmd $"dotnet run --project docs/docs.fsproj --"
                        |> Cmd.arg (if watch then "watch" else "build")
                    )
                }
        }

exit
<| rootCommand fsi.CommandLineArgs[1..] {
    description "Ranvier.Anime development, validation and releases"
    workingDir Spec.root

    command "tools" {
        description "Restore pinned tools individually to avoid SDK tool-metadata collisions on fresh runners"
        Command.pipeline { Stage.restoreTools }
    }

    command "dev" {
        description "Switch between published and sibling Ranvier references"

        command "local" {
            description "Clone Ranvier if missing and configure local project references (--with-css for Ranvier.Css too)"

            Command.pipeline {
                Stage.local
                Stage.restore
            }
        }

        command "packages" {
            description "Switch back to pinned published Ranvier and Ranvier.Css dependencies"

            Command.pipeline {
                Stage.packages
                Stage.restore
            }
        }
    }

    command "build" {
        description "Build the whole solution"

        Command.pipeline {
            Stage.restore
            Stage.buildSolution
        }
    }

    command "test" {
        alias "tests"
        description "Run the checks; --group selects a CI shard (default: all)"

        Command.pipeline {
            Stage.restore
            Stage.buildSolution
            Stage.tests
        }
    }

    command "ci" {
        description "Run every CI gate locally and pack the validated libraries"

        Command.pipeline {
            Stage.restore
            Stage.format
            Stage.buildSolution
            Stage.allTests
            Stage.build
            Stage.pack
        }
    }

    command "format" {
        description "Format tracked F# sources (--dry-format to check)"

        Command.pipeline {
            Stage.restore
            Stage.format
        }
    }

    command "pack" {
        description "Build and pack the libraries untraced and traced, with Fable sources"

        Command.pipeline {
            Stage.restore
            Stage.build
            Stage.pack
        }
    }

    command "publish" {
        description "Validate every gate and publish (--dry-publish to skip upload)"

        Command.pipeline {
            Stage.restore
            Stage.buildSolution
            Stage.allTests
            Stage.build
            Stage.pack
            Stage.publish
        }
    }

    command "docs" {
        description "Build the documentation (--watch to serve it)"

        Command.pipeline {
            Stage.restore
            Stage.docs
        }
    }

    command "bump" {
        description "Calculate release versions with ShipIt in local mode"
        Command.pipeline { Partas.Build.EasyBuild.ShipIt.Stages.bump }
    }

    addCommand Partas.Build.EasyBuild.ShipIt.Commands.shipit
}
