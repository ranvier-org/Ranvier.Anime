module BuildNative

open System
open System.IO
open System.Text.Json
open System.Security.Cryptography
open System.Text.RegularExpressions
open System.Diagnostics

let private hash path =
    use stream = File.OpenRead path
    SHA256.HashData stream |> Convert.ToHexString |> fun value -> value.ToLowerInvariant()

let private beneath (root: string) (relative: string) =
    if Path.IsPathRooted relative then invalidArg "relative" "Manifest paths must be relative."
    let root = Path.GetFullPath root
    let path = Path.GetFullPath(Path.Combine(root, relative))
    if not(path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + string Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) then
        invalidArg "relative" "Manifest path escapes its staging root."
    path

/// <summary>Requires the independently copied source repositories to stay at the qualified detached commits.</summary>
let verifySources () =
    for name, expected in ["core-aot", "6f5e063c06a48af2c3375c1c986cb5925e814d3d"; "view-aot", "625b0e1baeaf2ccc023f69dd7f1e76626e27a084"] do
        let head = File.ReadAllText(Path.Combine(__SOURCE_DIRECTORY__, "upstream", name, ".git", "HEAD")).Trim()
        if head <> expected then failwithf "%s is not at its qualified detached source commit" name
    printfn "Verified pinned Core/View source commits"

/// <summary>Copies the granted ABI62 private runtime into the owned stage and verifies every manifest byte/hash.</summary>
let copyStage (manifestPath: string) =
    let manifestText = File.ReadAllText manifestPath
    use manifest = JsonDocument.Parse manifestText
    let root = manifest.RootElement
    if root.GetProperty("abi").GetInt32() <> 62 || root.GetProperty("nativeSource").GetString() <> "6f10bea17d7a1f22e5e97d472819c055fba3ef06" then
        failwith "Runtime manifest is not the agreed ABI62 native source."
    let source = root.GetProperty("sourceRoot").GetString()
    let stage = Path.Combine(__SOURCE_DIRECTORY__, "artifacts", "native-smoke")
    let destination = Path.Combine(stage, "runtime")
    let mutable count = 0
    let mutable total = 0L
    for entry in root.GetProperty("files").EnumerateObject() do
        let expectedBytes = entry.Value.GetProperty("bytes").GetInt64()
        let expectedHash = entry.Value.GetProperty("sha256").GetString()
        let sourceFile = beneath source entry.Name
        let destinationFile = beneath destination entry.Name
        if FileInfo(sourceFile).Length <> expectedBytes || hash sourceFile <> expectedHash then
            failwithf "Source runtime differs from grant: %s" entry.Name
        Directory.CreateDirectory(Path.GetDirectoryName destinationFile) |> ignore
        File.Copy(sourceFile, destinationFile, true)
        if FileInfo(destinationFile).Length <> expectedBytes || hash destinationFile <> expectedHash then
            failwithf "Copied runtime failed verification: %s" entry.Name
        count <- count + 1
        total <- total + expectedBytes
    let actualCount = Directory.EnumerateFiles(destination, "*", SearchOption.AllDirectories) |> Seq.length
    if actualCount <> count then failwith "Owned runtime contains files outside the source manifest."
    File.WriteAllText(Path.Combine(stage, "source-manifest.json"), manifestText)
    let evidence = Path.Combine(__SOURCE_DIRECTORY__, "artifacts", "qualification")
    Directory.CreateDirectory evidence |> ignore
    let receipt =
        {| SourceRoot = source; DestinationRoot = destination; Abi = 62; Private = root.GetProperty("private").GetBoolean()
           NativeSource = root.GetProperty("nativeSource").GetString(); Files = count; Bytes = total; ManifestSha256 = hash manifestPath |}
    File.WriteAllText(Path.Combine(evidence, "native-stage.json"), JsonSerializer.Serialize receipt)
    printfn "Verified owned private ABI62 stage: %d files, %d bytes" count total

/// <summary>Rechecks the owned runtime against the retained manifest without accessing its producer.</summary>
let verifyStage () =
    let stage = Path.Combine(__SOURCE_DIRECTORY__, "artifacts", "native-smoke")
    let destination = Path.Combine(stage, "runtime")
    use manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(stage, "source-manifest.json")))
    let root = manifest.RootElement
    if root.GetProperty("abi").GetInt32() <> 62 || root.GetProperty("nativeSource").GetString() <> "6f10bea17d7a1f22e5e97d472819c055fba3ef06" then
        failwith "Owned stage manifest is not the agreed ABI62 native source."
    let mutable count = 0
    for entry in root.GetProperty("files").EnumerateObject() do
        let path = beneath destination entry.Name
        if FileInfo(path).Length <> entry.Value.GetProperty("bytes").GetInt64() || hash path <> entry.Value.GetProperty("sha256").GetString() then
            failwithf "Owned runtime differs from retained manifest: %s" entry.Name
        count <- count + 1
    if Directory.EnumerateFiles(destination, "*", SearchOption.AllDirectories) |> Seq.length <> count then
        failwith "Owned runtime contains files outside the retained manifest."
    printfn "Reverified %d owned runtime files" count

/// <summary>Verifies every staged native payload at its flattened application output path.</summary>
let verifyOutput (output: string) =
    let manifestPath = Path.Combine(__SOURCE_DIRECTORY__, "artifacts", "native-smoke", "source-manifest.json")
    use manifest = JsonDocument.Parse(File.ReadAllText manifestPath)
    let prefix = "runtimes/win-x64/native/"
    let mutable count = 0
    for entry in manifest.RootElement.GetProperty("files").EnumerateObject() do
        let relative = entry.Name.Replace('\\', '/')
        if not(relative.StartsWith(prefix, StringComparison.Ordinal)) then failwith "Unexpected native manifest layout."
        let path = beneath output (relative.Substring prefix.Length)
        if FileInfo(path).Length <> entry.Value.GetProperty("bytes").GetInt64() || hash path <> entry.Value.GetProperty("sha256").GetString() then
            failwithf "Application native payload differs from retained manifest: %s" relative
        count <- count + 1
    printfn "Verified %d application runtime files" count

/// <summary>Rejects IL diagnostics and unconditional-throw compiler messages in a complete publish log.</summary>
let verifyPublishLog (path: string) =
    let log = File.ReadAllText path
    if String.IsNullOrWhiteSpace log then failwith "Native publish log is empty."
    let failures = Regex.Matches(log, @"(?im)^.*(?:\b(?:warning|error)\s+IL\d{4}\b|will always throw).*$")
    if failures.Count > 0 then
        failures |> Seq.cast<Match> |> Seq.map _.Value |> String.concat "\n" |> failwith
    printfn "Native publish log has no IL diagnostics or unconditional-throw messages"

/// <summary>Captures a caller-owned process using a bounded wait and the smoke's success protocol.</summary>
let runCaptured (start: ProcessStartInfo) (executable: string) requireAot (logPath: string) (limitMilliseconds: int) =
    if limitMilliseconds <= 0 then invalidArg "limitMilliseconds" "Expected a positive process deadline."
    File.Delete(logPath + ".json")
    File.WriteAllText(logPath, "Native smoke attempt started.\n")
    let mutable recorded = false
    let save (status: string) (exitCode: int) (outputText: string) (errorText: string) =
        File.WriteAllText(logPath, outputText + errorText)
        let receipt =
            {| Status = status; ExitCode = exitCode; RequiredAot = requireAot; Executable = executable
               ExecutableSha256 = if File.Exists executable then hash executable else ""
               LogSha256 = hash logPath; CompletedUtc = DateTimeOffset.UtcNow.ToString("O") |}
        File.WriteAllText(logPath + ".json", JsonSerializer.Serialize receipt)
        recorded <- true
    try
        use child = Process.Start start
        let stdout = child.StandardOutput.ReadToEndAsync()
        let stderr = child.StandardError.ReadToEndAsync()
        let timedOut = not(child.WaitForExit limitMilliseconds)
        if timedOut then
            child.Kill(true)
            child.WaitForExit()
        let outputText, errorText = stdout.GetAwaiter().GetResult(), stderr.GetAwaiter().GetResult()
        let success = not timedOut && child.ExitCode = 0 && outputText.Contains("NATIVE_ANIME_SMOKE_PASS", StringComparison.Ordinal) &&
                      (not requireAot || outputText.Contains("PASS consumer is NativeAOT", StringComparison.Ordinal))
        let status = if timedOut then "timedOut" elif success then "succeeded" else "failed"
        save status (if timedOut || (child.ExitCode = 0 && not success) then -1 else child.ExitCode) outputText errorText
        Console.Write outputText
        Console.Error.Write errorText
        if not success then failwith ("Native smoke " + status + "; inspect the retained run log.")
    with error ->
        if not recorded then save "startFailed" -1 "" (error.ToString())
        reraise()

/// <summary>Runs the owned headless consumer and preserves its output, exit code and executable hash.</summary>
let runSmoke (output: string) requireAot (logPath: string) =
    let executable = Path.Combine(output, "Ranvier.Anime.NativeSmoke.exe")
    let start = ProcessStartInfo("rtk")
    start.WorkingDirectory <- output
    start.UseShellExecute <- false
    start.CreateNoWindow <- true
    start.RedirectStandardOutput <- true
    start.RedirectStandardError <- true
    start.Environment["OS"] <- "Windows_NT"
    start.ArgumentList.Add "proxy"
    start.ArgumentList.Add executable
    if requireAot then start.ArgumentList.Add "--require-aot"
    runCaptured start executable requireAot logPath 30000

/// <summary>Preserves the failed strict typed-CSS publishes, exact dependency assets and compiler graph.</summary>
let captureCssGap (configuration: string) =
    if configuration <> "Debug" && configuration <> "Release" then invalidArg "configuration" "Expected Debug or Release."
    let stage = Path.Combine(__SOURCE_DIRECTORY__, "artifacts", "native-smoke")
    let evidence = Path.Combine(__SOURCE_DIRECTORY__, "artifacts", "qualification", "native-css-gap-" + configuration)
    Directory.CreateDirectory evidence |> ignore
    let obj = Path.Combine(stage, "build", "obj")
    use assets = JsonDocument.Parse(File.ReadAllText(Path.Combine(obj, "Ranvier.Anime.NativeSmoke", "project.assets.json")))
    let traced = assets.RootElement.GetProperty("libraries").EnumerateObject() |> Seq.exists(fun library -> library.Name.StartsWith("Ranvier.Css.Dom.Traced/", StringComparison.OrdinalIgnoreCase))
    if traced <> (configuration = "Debug") then failwith "Restore the requested native configuration before capturing its evidence."
    let intermediate project = Path.Combine(obj, project, configuration.ToLowerInvariant() + "_net10.0_win-x64", "native")
    let snapshots =
        [ "publish.log", Path.Combine(stage, "publish-" + configuration + ".log")
          "css-probe.log", Path.Combine(stage, "css-probe-" + configuration + ".log")
          "native.assets.json", Path.Combine(obj, "Ranvier.Anime.NativeSmoke", "project.assets.json")
          "css-probe.assets.json", Path.Combine(obj, "Ranvier.Anime.CssAotProbe", "project.assets.json")
          "native.ilc.rsp", Path.Combine(intermediate "Ranvier.Anime.NativeSmoke", "Ranvier.Anime.NativeSmoke.ilc.rsp")
          "css-probe.ilc.rsp", Path.Combine(intermediate "Ranvier.Anime.CssAotProbe", "Ranvier.Anime.CssAotProbe.ilc.rsp")
          "native.codegen.dgml.xml", Path.Combine(intermediate "Ranvier.Anime.NativeSmoke", "Ranvier.Anime.NativeSmoke.codegen.dgml.xml") ]
        |> List.map(fun (name, source) ->
            let destination = Path.Combine(evidence, name)
            File.Copy(source, destination, true)
            {| File = name; Bytes = FileInfo(destination).Length; Sha256 = hash destination |})
    let diagnostics file =
        Regex.Matches(File.ReadAllText(Path.Combine(evidence, file)), @"(?m)^.*?([A-Z]:\\.*?\berror IL\d{4}:.*)$")
        |> Seq.cast<Match> |> Seq.map(fun matched -> matched.Groups[1].Value.Trim()) |> Seq.distinct |> Seq.toArray
    let receipt =
        {| Configuration = configuration; PrivateSourceCohort = true
           Core = File.ReadAllText(Path.Combine(__SOURCE_DIRECTORY__, "upstream", "core-aot", ".git", "HEAD")).Trim()
           View = File.ReadAllText(Path.Combine(__SOURCE_DIRECTORY__, "upstream", "view-aot", ".git", "HEAD")).Trim()
           Files = snapshots; NativeIlDiagnostics = diagnostics "publish.log"; IsolatedCssIlDiagnostics = diagnostics "css-probe.log" |}
    File.WriteAllText(Path.Combine(evidence, "receipt.json"), JsonSerializer.Serialize receipt)
    printfn "Preserved %d files, %d native IL diagnostics and %d isolated CSS IL diagnostics" snapshots.Length receipt.NativeIlDiagnostics.Length receipt.IsolatedCssIlDiagnostics.Length
