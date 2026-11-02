module BuildCssCandidate

open System
open System.IO
open System.IO.Compression
open System.Security.Cryptography
open System.Text.Json
open System.Xml.Linq

let private hash path =
    use stream = File.OpenRead path
    SHA256.HashData stream |> Convert.ToHexString |> fun value -> value.ToLowerInvariant()

/// <summary>Copies the granted immutable CSS candidate and verifies hashes, package IDs and source commit.</summary>
let copy (receiptPath: string) =
    let text = File.ReadAllText receiptPath
    use receipt = JsonDocument.Parse text
    let root = receipt.RootElement
    let commit = root.GetProperty("sourceCommit").GetString()
    let version = root.GetProperty("privateVersion").GetString()
    if commit <> "70ac54af73543d86941da45c4dc499ca819a7e3e" || version <> "0.1.1-alpha.1-css-aot.2" then
        failwith "CSS candidate is not the agreed corrected immutable successor."
    let destination = Path.Combine(__SOURCE_DIRECTORY__, "artifacts", "css-candidate", "70ac")
    let feed = Path.Combine(destination, "packages")
    Directory.CreateDirectory feed |> ignore
    let expectedIds = Set.ofList ["Ranvier.Css"; "Ranvier.Css.Traced"; "Ranvier.Css.Bindings"; "Ranvier.Css.Bindings.Traced"]
    let mutable ids = Set.empty
    for package in root.GetProperty("packages").EnumerateArray() do
        let source = package.GetProperty("path").GetString()
        let expectedHash = package.GetProperty("sha256").GetString()
        if hash source <> expectedHash then failwith "Producer CSS package hash differs from receipt."
        let target = Path.Combine(feed, Path.GetFileName source)
        File.Copy(source, target, true)
        if hash target <> expectedHash then failwith "Copied CSS package hash differs from receipt."
        use archive = ZipFile.OpenRead target
        let nuspec = archive.Entries |> Seq.filter(fun entry -> entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase)) |> Seq.exactlyOne
        use stream = nuspec.Open()
        use reader = new StreamReader(stream)
        let actualNuspec = reader.ReadToEnd()
        if actualNuspec <> package.GetProperty("nuspec").GetString() then failwith "Captured nuspec differs from package payload."
        let document = XDocument.Parse actualNuspec
        let ns = document.Root.Name.Namespace
        let metadata = document.Root.Element(ns + "metadata")
        let id = metadata.Element(ns + "id").Value
        if not(expectedIds.Contains id) || ids.Contains id || metadata.Element(ns + "version").Value <> version ||
           metadata.Element(ns + "repository").Attribute(XName.Get "commit").Value <> commit then
            failwith "Unexpected CSS package identity or source provenance."
        ids <- ids.Add id
    if ids <> expectedIds || Directory.EnumerateFiles(feed, "*.nupkg") |> Seq.length <> 4 then
        failwith "CSS candidate feed is not the complete four-package cohort."
    File.WriteAllText(Path.Combine(destination, "receipt.json"), text)
    printfn "Copied and verified four private CSS packages at %s" version

/// <summary>Rechecks the owned candidate feed without reading the released producer.</summary>
let verify () =
    let directory = Path.Combine(__SOURCE_DIRECTORY__, "artifacts", "css-candidate", "70ac")
    use receipt = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "receipt.json")))
    if receipt.RootElement.GetProperty("sourceCommit").GetString() <> "70ac54af73543d86941da45c4dc499ca819a7e3e" then
        failwith "Owned CSS candidate provenance changed."
    let mutable count = 0
    for package in receipt.RootElement.GetProperty("packages").EnumerateArray() do
        let path = Path.Combine(directory, "packages", Path.GetFileName(package.GetProperty("path").GetString()))
        if hash path <> package.GetProperty("sha256").GetString() then failwith "Owned CSS candidate package changed."
        count <- count + 1
    if count <> 4 then failwith "Owned CSS candidate cohort is incomplete."
    printfn "Reverified four owned CSS candidate packages"

/// <summary>Verifies the private CSS graph and archive/cache/app/resolved-compiler DLL identities.</summary>
let packageProof (configuration: string) (stage: string) (cache: string) candidate =
    if candidate then verify()
    let suffix = if configuration = "Debug" then ".Traced" elif configuration = "Release" then "" else invalidArg "configuration" "Expected Debug or Release."
    let obj = Path.Combine(stage, "build", "obj", "Ranvier.Anime.NativeSmoke")
    let bin = Path.Combine(stage, "build", "bin", "Ranvier.Anime.NativeSmoke", configuration.ToLowerInvariant() + "_net10.0_win-x64")
    use assets = JsonDocument.Parse(File.ReadAllText(Path.Combine(obj, "project.assets.json")))
    let libraries = assets.RootElement.GetProperty("libraries")
    let expectedCss = Set.ofList ["Ranvier.Css" + suffix; "Ranvier.Css.Bindings" + suffix; "Ranvier.Css.Dom" + suffix]
    for library in libraries.EnumerateObject() do
        let id = library.Name.Split('/')[0]
        if id.StartsWith("Ranvier.Css", StringComparison.Ordinal) && not(expectedCss.Contains id) then
            failwith "Native restore graph mixes CSS package variants."
    let response = Path.Combine(obj, configuration.ToLowerInvariant() + "_net10.0_win-x64", "native", "Ranvier.Anime.NativeSmoke.ilc.rsp")
    let cssVersion = if candidate then "0.1.1-alpha.1-css-aot.2" else "0.1.1-alpha.2"
    let bindingsVersion = if candidate then "0.1.1-alpha.1-css-aot.2" else "0.1.1-alpha.1"
    [("Ranvier.Css" + suffix, cssVersion, "Ranvier.Css.dll");
     ("Ranvier.Css.Bindings" + suffix, bindingsVersion, "Ranvier.Css.Bindings.dll");
     ("Ranvier.Css.Dom" + suffix, "0.1.0-alpha.1", "Ranvier.Css.Dom.dll")]
        |> List.map(fun (id, version, dll) ->
            libraries.GetProperty(id + "/" + version) |> ignore
            let directory = Path.Combine(cache, id.ToLowerInvariant(), version)
            let payload = Path.Combine(directory, id.ToLowerInvariant() + "." + version + ".nupkg")
            let cacheDll = Path.Combine(directory, "lib", "net10.0", dll)
            let compilerDll = BuildProvenance.compilerDll __SOURCE_DIRECTORY__ response dll
            BuildProvenance.verifyDll payload ("lib/net10.0/" + dll) [cacheDll; Path.Combine(bin, dll); compilerDll]
            if version.EndsWith("css-aot.2", StringComparison.Ordinal) then
                let owned = Path.Combine(__SOURCE_DIRECTORY__, "artifacts", "css-candidate", "70ac", "packages", id + "." + version + ".nupkg")
                if hash payload <> hash owned then failwith "Restored private CSS package differs from the copied feed."
            elif id = "Ranvier.Css" + suffix then
                let expected = if suffix = "" then "2665336dbac9deabd5f641cee78e654389b46720f7d262f866b123a6340e2f1a" else "2713ff055b570c62ce7db25ffff4b48ff8e8a2efc25f4e039a298fa94b5dc0de"
                if hash payload <> expected then failwith "Public CSS alpha.2 package differs from the independently audited nupkg."
            {| Id = id; Version = version; NupkgSha256 = hash payload; DllSha256 = hash cacheDll; CompilerInput = compilerDll |})

/// <summary>Archives an executed private AOT replay whose successful publication and inputs remain unchanged.</summary>
let capture (configuration: string) (stage: string) (cache: string) candidate =
    let publication = BuildProvenance.requirePublish __SOURCE_DIRECTORY__ stage configuration
    let packages = packageProof configuration stage cache candidate
    if JsonSerializer.SerializeToElement(packages).GetRawText() <> publication.Packages.GetRawText() then
        failwith "Current package provenance differs from the successful publication."
    let obj = Path.Combine(stage, "build", "obj", "Ranvier.Anime.NativeSmoke")
    let executable = Path.Combine(stage, "publish", configuration, "Ranvier.Anime.NativeSmoke.exe")
    let log = Path.Combine(stage, "run-" + configuration + "-aot.log")
    use run = JsonDocument.Parse(File.ReadAllText(log + ".json"))
    if run.RootElement.GetProperty("Status").GetString() <> "succeeded" || run.RootElement.GetProperty("ExitCode").GetInt32() <> 0 ||
       not(run.RootElement.GetProperty("RequiredAot").GetBoolean()) || run.RootElement.GetProperty("ExecutableSha256").GetString() <> hash executable ||
       run.RootElement.GetProperty("LogSha256").GetString() <> hash log then
        failwith "A successful matching NativeAOT execution receipt is required."
    let cohort = if candidate then "native-css-70ac-hardened" else "native-css-public-alpha2-hardened"
    let destination = Path.Combine(__SOURCE_DIRECTORY__, "artifacts", "qualification", cohort + "-" + configuration)
    if File.Exists(Path.Combine(destination, "receipt.json")) then failwith "This private replay snapshot is already preserved."
    Directory.CreateDirectory destination |> ignore
    let files =
        ["publish.log", Path.Combine(stage, "publish-" + configuration + ".log")
         "publish.json", Path.Combine(stage, "publish-" + configuration + ".receipt.json")
         "run.log", log; "run.json", log + ".json"
         "project.assets.json", Path.Combine(obj, "project.assets.json")
         "compiler.rsp", Path.Combine(obj, configuration.ToLowerInvariant() + "_net10.0_win-x64", "native", "Ranvier.Anime.NativeSmoke.ilc.rsp")
         "Ranvier.Anime.NativeSmoke.exe", executable]
        |> List.map(fun (name, source) ->
            let target = Path.Combine(destination, name)
            File.Copy(source, target, true)
            {| File = name; Bytes = FileInfo(target).Length; Sha256 = hash target |})
    let sourceDirectory = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "src", "Ranvier.Anime")) + string Path.DirectorySeparatorChar
    let sources = publication.Inputs |> Array.filter(fun input -> input.Path.StartsWith(sourceDirectory, StringComparison.OrdinalIgnoreCase) && input.Path.EndsWith(".fs", StringComparison.Ordinal))
                  |> Array.map(fun input -> {| File = Path.GetFileName input.Path; Sha256 = input.Sha256 |}) |> Array.sortBy _.File
    let consumer = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "tests", "Ranvier.Anime.NativeSmoke", "Program.fs"))
    let receipt =
        {| Schema = 2; Configuration = configuration; PrivateCandidate = candidate; PublicCssPackages = not candidate; PublicQualified = false
           CssSource = if candidate then "70ac54af73543d86941da45c4dc499ca819a7e3e" else "public-alpha.2-audited-packages"
           CoreSource = "6f5e063c06a48af2c3375c1c986cb5925e814d3d"; ViewSource = "625b0e1baeaf2ccc023f69dd7f1e76626e27a084"
           NativeSource = "6f10bea17d7a1f22e5e97d472819c055fba3ef06"; NativeAbi = 62
           Packages = packages; Files = files; LibrarySources = sources
           ConsumerSourceSha256 = (publication.Inputs |> Array.find(fun input -> input.Path = consumer)).Sha256 |}
    File.WriteAllText(Path.Combine(destination, "receipt.json"), JsonSerializer.Serialize receipt)
    printfn "Preserved successful private %s replay and verified three CSS package/DLL identities" configuration

/// <summary>Writes the tracked private-replay summary from both immutable archived mode receipts.</summary>
let summarize candidate =
    let cohort = if candidate then "native-css-70ac-hardened" else "native-css-public-alpha2-hardened"
    let modes =
        ["Release"; "Debug"] |> List.map(fun configuration ->
            let path = Path.Combine(__SOURCE_DIRECTORY__, "artifacts", "qualification", cohort + "-" + configuration, "receipt.json")
            use receipt = JsonDocument.Parse(File.ReadAllText path)
            {| Configuration = configuration; ReceiptSha256 = hash path; Evidence = receipt.RootElement.Clone() |})
    let summary = {| PrivateCandidate = candidate; PublicCssPackages = not candidate; PublicAccepted = false; HardwareAccelerationQualified = false; Modes = modes |}
    let path = Path.Combine(__SOURCE_DIRECTORY__, "docs", "evidence", cohort + ".json")
    File.WriteAllText(path, JsonSerializer.Serialize(summary, JsonSerializerOptions(WriteIndented = true)))
    printfn "Wrote compact two-mode private replay evidence"
