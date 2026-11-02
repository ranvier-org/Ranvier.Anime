module BuildProvenance

open System
open System.IO
open System.IO.Compression
open System.Security.Cryptography
open System.Text.Json
open System.Text.RegularExpressions

/// <summary>A file identity used by a successful publication.</summary>
type FileStamp = { Path: string; Sha256: string }

/// <summary>A successful publish bound to source, dependency, compiler and executable inputs.</summary>
type PublishReceipt =
    { Schema: int; Configuration: string; Inputs: FileStamp array; Assets: FileStamp; Response: FileStamp
      CompilerInputs: FileStamp array; Executable: FileStamp; Log: FileStamp; Packages: JsonElement }

let private hashStream stream = SHA256.HashData(stream: Stream) |> Convert.ToHexString |> fun value -> value.ToLowerInvariant()
let private stamp path =
    use stream = File.OpenRead path
    { Path = Path.GetFullPath path; Sha256 = hashStream stream }

let private sourceInputs root =
    let trees = ["src/Ranvier.Anime"; "tests/Ranvier.Anime.NativeSmoke"; "upstream/core-aot/src"; "upstream/view-aot/src"]
    let extensions = Set.ofList [".fs"; ".fsproj"; ".props"; ".targets"]
    [ for tree in trees do
          let directory = Path.Combine(root, tree)
          if Directory.Exists directory then
              yield! Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                     |> Seq.filter(fun path ->
                         let segments = Path.GetRelativePath(directory, path).Replace('\\', '/').Split('/')
                         extensions.Contains(Path.GetExtension path) &&
                         not(segments |> Array.exists(fun segment -> segment.Equals("obj", StringComparison.OrdinalIgnoreCase) || segment.Equals("bin", StringComparison.OrdinalIgnoreCase))))
      yield! Directory.EnumerateFiles(root, "build*.fsx")
      for relative in ["Directory.Build.props"; "Directory.Build.targets"; "global.json"; "nuget.config"; "nuget.private-css.config"
                       "upstream/core-aot/.git/HEAD"; "upstream/core-aot/Directory.Build.props"; "upstream/core-aot/Directory.Build.targets"
                       "upstream/core-aot/Local.Build.props"; "upstream/view-aot/Local.Build.props"
                       "upstream/view-aot/.git/HEAD"; "upstream/view-aot/Directory.Build.props"; "upstream/view-aot/Directory.Build.targets"] do
          let path = Path.Combine(root, relative)
          if File.Exists path then yield path ]
    |> List.distinct |> List.map stamp |> List.sortBy _.Path |> List.toArray

let private receiptPath stage configuration = Path.Combine(stage, "publish-" + configuration + ".receipt.json")
let private pendingPath stage configuration = Path.Combine(stage, "publish-" + configuration + ".inputs.json")

/// <summary>Resolves the actual entry assembly and reference DLLs named by an ILC response file.</summary>
let compilerInputs (root: string) (response: string) =
    File.ReadAllLines response
    |> Array.choose(fun line ->
        let line = line.Trim()
        if line.StartsWith("@", StringComparison.Ordinal) then failwith "Nested compiler response files require explicit provenance support."
        let path =
            if line.StartsWith("-r:", StringComparison.Ordinal) then Some(line.Substring(3).Trim('"'))
            elif not(line.StartsWith("-", StringComparison.Ordinal)) && line.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) then Some(line.Trim('"'))
            else None
        path |> Option.map(fun path -> Path.GetFullPath(path, root)))
    |> Array.distinctBy _.ToUpperInvariant()

/// <summary>Finds the unique resolved compiler input for an assembly filename.</summary>
let compilerDll root response name =
    compilerInputs root response
    |> Array.filter(fun path -> String.Equals(Path.GetFileName path, name, StringComparison.OrdinalIgnoreCase))
    |> Array.exactlyOne

/// <summary>Invalidates prior success and records source/project inputs before invoking publish.</summary>
let beginPublish root stage configuration =
    Directory.CreateDirectory stage |> ignore
    File.Delete(receiptPath stage configuration)
    File.WriteAllText(Path.Combine(stage, "publish-" + configuration + ".log"), "")
    File.WriteAllText(pendingPath stage configuration, JsonSerializer.Serialize(sourceInputs root))

/// <summary>Records success only after the command and strict log checks passed with unchanged source inputs.</summary>
let recordPublish root stage configuration (packages: JsonElement) =
    let inputs = JsonSerializer.Deserialize<FileStamp array>(File.ReadAllText(pendingPath stage configuration))
    if inputs <> sourceInputs root then failwith "Source/project inputs changed during native publish."
    let log = Path.Combine(stage, "publish-" + configuration + ".log")
    let text = File.ReadAllText log
    if not(Regex.IsMatch(text, @"(?m)^\s*Build succeeded\.\s*$")) ||
       Regex.IsMatch(text, @"(?im)\berror\s+[A-Z]+\d+\b|Build FAILED|\bwarning\s+IL\d{4}\b|will always throw") then
        failwith "Publish log does not establish a successful strict build."
    let obj = Path.Combine(stage, "build", "obj", "Ranvier.Anime.NativeSmoke")
    let response = Path.Combine(obj, configuration.ToLowerInvariant() + "_net10.0_win-x64", "native", "Ranvier.Anime.NativeSmoke.ilc.rsp")
    let receipt =
        { Schema = 2; Configuration = configuration; Inputs = inputs; Assets = stamp (Path.Combine(obj, "project.assets.json"))
          Response = stamp response; CompilerInputs = compilerInputs root response |> Array.map stamp
          Executable = stamp (Path.Combine(stage, "publish", configuration, "Ranvier.Anime.NativeSmoke.exe"))
          Log = stamp log; Packages = packages.Clone() }
    File.WriteAllText(receiptPath stage configuration, JsonSerializer.Serialize receipt)

/// <summary>Rejects a publish whose successful receipt no longer matches its source and compiler inputs.</summary>
let requirePublish (root: string) (stage: string) (configuration: string) =
    let receipt = JsonSerializer.Deserialize<PublishReceipt>(File.ReadAllText(receiptPath stage configuration))
    if receipt.Schema <> 2 || receipt.Configuration <> configuration || receipt.Inputs <> sourceInputs root then
        failwith "Successful publish receipt does not match current source/project inputs."
    for expected in Array.concat [receipt.Inputs; receipt.CompilerInputs; [|receipt.Assets; receipt.Response; receipt.Executable; receipt.Log|]] do
        if stamp expected.Path <> expected then failwith ("Publish input changed: " + expected.Path)
    receipt

/// <summary>Checks one archive DLL against extracted, application and resolved compiler copies.</summary>
let verifyDll (package: string) (entry: string) (copies: string list) =
    use archive = ZipFile.OpenRead package
    let memberEntry = archive.GetEntry entry
    if isNull memberEntry then failwith "Expected DLL entry is missing from package."
    use stream = memberEntry.Open()
    let expected = hashStream stream
    if copies.IsEmpty || copies |> List.exists(fun path -> (stamp path).Sha256 <> expected) then
        failwith "Package DLL entry differs from an extracted, application or compiler copy."
