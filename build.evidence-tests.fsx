module BuildEvidenceTests

open System
open System.IO
open System.IO.Compression
open System.Diagnostics
open System.Text.Json

let private scratch = Path.Combine(__SOURCE_DIRECTORY__, "artifacts", "evidence-regressions")
let private expectRejected description action =
    let rejected = try action(); false with _ -> true
    if not rejected then failwith ("Regression: " + description)

/// <summary>Rejects capture without a successful publication binding.</summary>
let stalePublish () =
    Directory.CreateDirectory scratch |> ignore
    expectRejected "capture accepted a stage without a successful publish receipt" (fun () ->
        BuildProvenance.requirePublish scratch (Path.Combine(scratch, "unpublished")) "Release" |> ignore)

/// <summary>Rejects substituted extracted DLLs even when application and cache copies agree.</summary>
let substitutedDll () =
    Directory.CreateDirectory scratch |> ignore
    let package = Path.Combine(scratch, "fixture.nupkg")
    use stream = File.Create package
    use archive = new ZipArchive(stream, ZipArchiveMode.Create)
    let entry = archive.CreateEntry "lib/net10.0/Fixture.dll"
    use output = entry.Open()
    output.Write([|1uy; 2uy; 3uy|])
    output.Dispose()
    archive.Dispose()
    stream.Dispose()
    let cache = Path.Combine(scratch, "cache.dll")
    let app = Path.Combine(scratch, "app.dll")
    File.WriteAllBytes(cache, [|9uy; 9uy; 9uy|])
    File.Copy(cache, app, true)
    expectRejected "archive bytes were not compared with substituted DLLs" (fun () ->
        BuildProvenance.verifyDll package "lib/net10.0/Fixture.dll" [cache; app])
    File.WriteAllBytes(cache, [|1uy; 2uy; 3uy|])
    File.Copy(cache, app, true)
    let compiler = Path.Combine(scratch, "compiler.dll")
    File.WriteAllBytes(compiler, [|9uy|])
    expectRejected "a substituted compiler input was accepted" (fun () ->
        BuildProvenance.verifyDll package "lib/net10.0/Fixture.dll" [cache; app; compiler])
    File.Copy(cache, compiler, true)
    BuildProvenance.verifyDll package "lib/net10.0/Fixture.dll" [cache; app; compiler]

/// <summary>Binds a successful fixture publish and rejects later source, assets, compiler or PE changes.</summary>
let publishBinding () =
    let root = Path.Combine(scratch, Guid.NewGuid().ToString("N"))
    let sourceDir = Path.Combine(root, "src", "Ranvier.Anime")
    Directory.CreateDirectory sourceDir |> ignore
    let source = Path.Combine(sourceDir, "Fixture.fs")
    File.WriteAllText(source, "let value = 1")
    let stage = Path.Combine(root, "stage")
    let obj = Path.Combine(stage, "build", "obj", "Ranvier.Anime.NativeSmoke")
    let native = Path.Combine(obj, "release_net10.0_win-x64", "native")
    Directory.CreateDirectory native |> ignore
    let assets = Path.Combine(obj, "project.assets.json")
    File.WriteAllText(assets, "{}")
    let input = Path.Combine(root, "Fixture.dll")
    File.WriteAllText(input, "fixture compiler input")
    let rsp = Path.Combine(native, "Ranvier.Anime.NativeSmoke.ilc.rsp")
    File.WriteAllText(rsp, input)
    let publish = Path.Combine(stage, "publish", "Release")
    Directory.CreateDirectory publish |> ignore
    let executable = Path.Combine(publish, "Ranvier.Anime.NativeSmoke.exe")
    File.WriteAllText(executable, "fixture executable")
    use packages = JsonDocument.Parse "[]"
    let establish () =
        BuildProvenance.beginPublish root stage "Release"
        File.WriteAllText(Path.Combine(stage, "publish-Release.log"), "Build succeeded.\n    0 Error(s)\n")
        BuildProvenance.recordPublish root stage "Release" packages.RootElement
        BuildProvenance.requirePublish root stage "Release" |> ignore
    establish()
    let generated = Path.Combine(sourceDir, "obj", "generated.fs")
    Directory.CreateDirectory(Path.GetDirectoryName generated) |> ignore
    File.WriteAllText(generated, "unrelated generated build output")
    BuildProvenance.requirePublish root stage "Release" |> ignore
    for path in [source; assets; input; rsp; executable] do
        let original = File.ReadAllText path
        File.WriteAllText(path, original + "changed")
        expectRejected ("changed publication input was accepted: " + Path.GetFileName path) (fun () ->
            BuildProvenance.requirePublish root stage "Release" |> ignore)
        File.WriteAllText(path, original)
    let added = Path.Combine(sourceDir, "Added.fs")
    File.WriteAllText(added, "let added = 1")
    expectRejected "a new source file was omitted from publication identity" (fun () ->
        BuildProvenance.requirePublish root stage "Release" |> ignore)
    File.Delete added
    BuildProvenance.beginPublish root stage "Release"
    File.WriteAllText(source, "let value = 2")
    File.WriteAllText(Path.Combine(stage, "publish-Release.log"), "Build succeeded.\n")
    expectRejected "source changed during publication without invalidating it" (fun () ->
        BuildProvenance.recordPublish root stage "Release" packages.RootElement)
    BuildProvenance.beginPublish root stage "Release"
    File.WriteAllText(Path.Combine(stage, "publish-Release.log"), "Build FAILED.\nerror FS0039: missing binding\n")
    expectRejected "an ordinary compile failure could produce a publish receipt" (fun () ->
        BuildProvenance.recordPublish root stage "Release" packages.RootElement)
    expectRejected "failed publish retained a prior success receipt" (fun () ->
        BuildProvenance.requirePublish root stage "Release" |> ignore)

/// <summary>Retains timeout output and replaces a prior successful receipt with a failed outcome.</summary>
let timeout () =
    Directory.CreateDirectory scratch |> ignore
    let log = Path.Combine(scratch, "timeout.log")
    File.WriteAllText(log, "OLD_SUCCESS")
    File.WriteAllText(log + ".json", "{\"ExitCode\":0,\"RequiredAot\":true}")
    let start = ProcessStartInfo("rtk")
    start.UseShellExecute <- false
    start.CreateNoWindow <- true
    start.RedirectStandardOutput <- true
    start.RedirectStandardError <- true
    for arg in ["proxy"; "powershell"; "-NoProfile"; "-Command"; "Write-Output OWNED_TIMEOUT_STARTED; Start-Sleep -Seconds 60"] do
        start.ArgumentList.Add arg
    expectRejected "timeout was accepted" (fun () -> BuildNative.runCaptured start "timeout-fixture" true log 2000)
    use receipt = JsonDocument.Parse(File.ReadAllText(log + ".json"))
    if receipt.RootElement.GetProperty("ExitCode").GetInt32() = 0 then failwith "Regression: timeout left a prior successful receipt"
    if receipt.RootElement.GetProperty("Status").GetString() <> "timedOut" then failwith "Regression: timeout status was lost"
    if not(File.ReadAllText(log).Contains "OWNED_TIMEOUT_STARTED") then failwith "Regression: timeout output was discarded"
