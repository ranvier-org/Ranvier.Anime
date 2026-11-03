module BuildPublic45

open System
open System.IO
open System.Security.Cryptography
open System.IO.Compression
open System.Text.Json
open System.Xml.Linq

/// <summary>Owned output tree for the public View45/NativeP7 cohort.</summary>
let stage = Path.Combine(__SOURCE_DIRECTORY__, "artifacts", "native-public45-p7")
/// <summary>Isolated package cache for the public View45/NativeP7 cohort.</summary>
let cache = Path.Combine(__SOURCE_DIRECTORY__, "artifacts", "nuget-public45-p7")

let private hash path =
    use stream = File.OpenRead path
    SHA256.HashData stream |> Convert.ToHexString |> fun value -> value.ToLowerInvariant()

/// <summary>Explicit public package/configuration pins; source projects and private native directories are disabled.</summary>
let arguments config =
    let traced, suffix =
        if config = "Debug" then "true", ".Traced"
        elif config = "Release" then "false", ""
        else invalidArg "config" "Expected Debug or Release."
    [ "-p:NativeSmokeGraph=public45"; "-p:UseLocalRanvier=false"; "-p:UseLocalRanvierWebCore=false"
      "-p:RanvierWebCoreNativeDir="; "-p:RanvierWebCoreNativeVersion=0.1.1-preview.7"; "-p:RanvierWebCoreNativeRid=win-x64"
      "-p:RanvierDomVersion=0.1.0-alpha.56"; "-p:RanvierCssVersion=0.1.1-alpha.2"; "-p:RanvierCssBindingsVersion=0.1.1-alpha.1"
      "-p:FSharpCoreImplicitPackageVersion=10.1.401"; "-p:TargetFrameworks=net10.0"
      "-p:Configuration=" + config; "-p:RanvierTrace=" + traced; "-p:RanvierPackageSuffix=" + suffix
      "--artifacts-path"; Path.Combine(stage, "build") ]

let private auditPins =
    [ "view-audit.json", "cfcd97c55ab76d2b293f20387da36f30fe7fd66f0541229e6a47eaf7e4cf6b1a"
      "native-cpu-audit.json", "668b0f5a32461a563c6917fd926752b5c5d84454a2685db489133890b7cdf8d2" ]

/// <summary>Copies exact independently supplied public audit receipts into the owned cohort tree.</summary>
let copyAudits view native =
    Directory.CreateDirectory stage |> ignore
    for source, (name, expected) in List.zip [view; native] auditPins do
        if hash source <> expected then failwith "Public audit receipt differs from the accepted handoff."
        let target = Path.Combine(stage, name)
        if File.Exists target && hash target <> expected then failwith "Owned public audit receipt changed."
        File.Copy(source, target, true)
        if hash target <> expected then failwith "Copied public audit receipt differs from the handoff."

/// <summary>Rechecks owned public receipts without reading the producer workspace.</summary>
let verifyAudits () =
    for name, expected in auditPins do
        if hash (Path.Combine(stage, name)) <> expected then failwith "Owned public audit receipt changed."

let private packageFile (id:string) (version:string) =
    let directory=Path.Combine(cache,id.ToLowerInvariant(),version)
    directory,Path.Combine(directory,id.ToLowerInvariant()+"."+version+".nupkg")

let private nuspec id version path =
    use archive=ZipFile.OpenRead path
    let entry=archive.Entries |> Seq.filter(fun entry -> entry.FullName.EndsWith(".nuspec",StringComparison.OrdinalIgnoreCase)) |> Seq.exactlyOne
    use stream=entry.Open()
    let document=XDocument.Load stream
    let ns=document.Root.Name.Namespace
    let metadata=document.Root.Element(ns+"metadata")
    if metadata.Element(ns+"id").Value<>id || metadata.Element(ns+"version").Value<>version then failwith "Package identity mismatch"
    let repository=metadata.Element(ns+"repository")
    if isNull repository || isNull(repository.Attribute(XName.Get "commit")) then failwith "Missing package source commit"
    repository.Attribute(XName.Get "commit").Value

let private selected configuration =
    let suffix=if configuration="Debug" then ".Traced" elif configuration="Release" then "" else invalidArg "configuration" "Expected Debug or Release."
    ["Ranvier"+suffix,"0.1.1-preview.1","Ranvier.dll"
     "Ranvier.Dom"+suffix,"0.1.0-alpha.56","Ranvier.Dom.dll"
     "Ranvier.Css"+suffix,"0.1.1-alpha.2","Ranvier.Css.dll"
     "Ranvier.Css.Bindings"+suffix,"0.1.1-alpha.1","Ranvier.Css.Bindings.dll"
     "Ranvier.Css.Dom"+suffix,"0.1.0-alpha.1","Ranvier.Css.Dom.dll"
     "Ranvier.View"+suffix,"0.1.45","Ranvier.View.dll"
     "Ranvier.View.Dom"+suffix,"0.1.45","Ranvier.View.Dom.dll"
     "Ranvier.View.WebCore"+suffix,"0.1.0-alpha.45","Ranvier.View.WebCore.dll"
     "Ranvier.WebCore","0.1.1-preview.7","Ranvier.WebCore.dll"]

/// <summary>Verifies nine managed public packages against audited nupkgs, cache/app DLLs and optional resolved ILC inputs.</summary>
let packageProof (configuration:string) compiler =
    verifyAudits()
    let obj=Path.Combine(stage,"build","obj","Ranvier.Anime.NativeSmoke")
    let output=Path.Combine(stage,"build","bin","Ranvier.Anime.NativeSmoke",configuration.ToLowerInvariant()+"_net10.0_win-x64")
    use assets=JsonDocument.Parse(File.ReadAllText(Path.Combine(obj,"project.assets.json")))
    let libraries=assets.RootElement.GetProperty "libraries"
    let expectedIds=selected configuration |> List.map(fun(id,_,_)->id) |> Set.ofList
    for library in libraries.EnumerateObject() do
        let id=library.Name.Split('/')[0]
        if library.Value.GetProperty("type").GetString()="project" && id<>"Ranvier.Anime" then failwith "Producer project in public graph"
        if id.StartsWith("Ranvier") && library.Value.GetProperty("type").GetString()="package" &&
           not(id.StartsWith("Ranvier.WebCore.Native",StringComparison.Ordinal)) && not(expectedIds.Contains id) then failwith "Mixed or unexpected managed package graph"
    let target=assets.RootElement.GetProperty("targets").GetProperty("net10.0/win-x64")
    let nativeIds=
        target.EnumerateObject()
        |> Seq.choose(fun package ->
            match package.Value.TryGetProperty "native" with
            | true,_ when package.Name.StartsWith("Ranvier.WebCore.Native",StringComparison.Ordinal) -> Some package.Name
            | _ -> None) |> Seq.toList
    if nativeIds<>["Ranvier.WebCore.Native.win-x64/0.1.1-preview.7"] then failwith "Unexpected native runtime selection"
    use prior=JsonDocument.Parse(File.ReadAllText(Path.Combine(__SOURCE_DIRECTORY__,"docs","evidence","native-core56-css2-hardened-playback-reviewed.json")))
    let priorMode=prior.RootElement.GetProperty("Modes").EnumerateArray() |> Seq.find(fun mode -> mode.GetProperty("Configuration").GetString()=configuration)
    let oldPins=priorMode.GetProperty("Evidence").GetProperty("Packages").EnumerateArray() |> Seq.map(fun package -> package.GetProperty("Id").GetString(),package) |> dict
    use audit=JsonDocument.Parse(File.ReadAllText(Path.Combine(stage,"view-audit.json")))
    selected configuration |> List.map(fun(id,version,dll) ->
        let library=libraries.GetProperty(id+"/"+version)
        if library.GetProperty("type").GetString()<>"package" then failwith "Expected public package dependency"
        let directory,payload=packageFile id version
        let expectedHash,expectedSource=
            if id.StartsWith("Ranvier.View",StringComparison.Ordinal) then
                let pin=audit.RootElement.GetProperty id
                if pin.GetProperty("version").GetString()<>version || not(pin.GetProperty("signatureVerified").GetBoolean()) then failwith "View audit pin mismatch"
                pin.GetProperty("publicSha256").GetString(),"6b1119c8dbcefc683194dec510859f1137d1e96a"
            else oldPins[id].GetProperty("NupkgSha256").GetString(),oldPins[id].GetProperty("SourceCommit").GetString()
        if hash payload<>expectedHash then failwith "Public nupkg differs from its independent pin"
        let source=nuspec id version payload
        if source<>expectedSource then failwith "Public package source commit mismatch"
        let cacheDll=Path.Combine(directory,"lib","net10.0",dll)
        let response=Path.Combine(obj,configuration.ToLowerInvariant()+"_net10.0_win-x64","native","Ranvier.Anime.NativeSmoke.ilc.rsp")
        let compilerDll=if compiler then BuildProvenance.compilerDll __SOURCE_DIRECTORY__ response dll else ""
        let copies=[cacheDll;Path.Combine(output,dll)] @ (if compiler then [compilerDll] else [])
        BuildProvenance.verifyDll payload ("lib/net10.0/"+dll) copies
        use metadata=JsonDocument.Parse(File.ReadAllText(Path.Combine(directory,".nupkg.metadata")))
        let feed=metadata.RootElement.GetProperty("source").GetString()
        if feed<>"https://api.nuget.org/v3/index.json" then failwith "Package was not restored from the public feed"
        {| Id=id;Version=version;NupkgSha256=hash payload;DllSha256=hash cacheDll;SourceCommit=source;Feed=feed;CompilerInput=compilerDll |})

/// <summary>Verifies the public CPU package and every native output member against its independent ABI62 manifest.</summary>
let nativeProof (output:string) =
    verifyAudits()
    use audit=JsonDocument.Parse(File.ReadAllText(Path.Combine(stage,"native-cpu-audit.json")))
    let pin id = audit.RootElement.EnumerateArray() |> Seq.find(fun entry -> entry.GetProperty("id").GetString()=id)
    let verifyPackage id =
        let entry=pin id
        if not(entry.GetProperty("exactMembers").GetBoolean() && entry.GetProperty("signatureVerified").GetBoolean()) then failwith "Native public payload/signature audit incomplete"
        let directory,payload=packageFile id "0.1.1-preview.7"
        if hash payload<>entry.GetProperty("publicSha256").GetString() then failwith "Native public nupkg differs from audited payload"
        if nuspec id "0.1.1-preview.7" payload<>"6f10bea17d7a1f22e5e97d472819c055fba3ef06" then failwith "Native package source mismatch"
        use metadata=JsonDocument.Parse(File.ReadAllText(Path.Combine(directory,".nupkg.metadata")))
        if metadata.RootElement.GetProperty("source").GetString()<>"https://api.nuget.org/v3/index.json" then failwith "Native package came from a private feed"
        directory,payload,entry
    let _,meta,_=verifyPackage "Ranvier.WebCore.Native"
    let directory,payload,entry=verifyPackage "Ranvier.WebCore.Native.win-x64"
    let manifest=entry.GetProperty("manifestFacts").EnumerateArray() |> Seq.exactlyOne
    if manifest.GetProperty("abi").GetInt32()<>62 || manifest.GetProperty("rid").GetString()<>"win-x64" ||
       manifest.GetProperty("flavor").GetString()<>"slim" || manifest.GetProperty("source").GetProperty("dirty").GetBoolean() then failwith "Unexpected native ABI/RID/flavor/source"
    let files=manifest.GetProperty("files").EnumerateArray() |> Seq.toArray
    if files.Length<>24 then failwith "Incomplete native CPU manifest"
    use archive=ZipFile.OpenRead payload
    let prefix="runtimes/win-x64/native/"
    let names=archive.Entries |> Seq.filter(fun memberEntry -> memberEntry.FullName.StartsWith(prefix) && memberEntry.Length>0L)
              |> Seq.map(fun memberEntry -> memberEntry.FullName.Substring(prefix.Length)) |> Set.ofSeq
    let expected=files |> Seq.map(fun file -> file.GetProperty("name").GetString()) |> Set.ofSeq
    if names<>expected then failwith "Native archive member set differs from audited CPU manifest"
    let copies=files |> Array.map(fun file ->
        let relative=file.GetProperty("name").GetString()
        if Path.IsPathRooted relative || relative.Split('/') |> Array.contains ".." then failwith "Unsafe native member path"
        let source=Path.Combine(directory,"runtimes","win-x64","native",relative)
        let target=Path.Combine(output,relative)
        BuildProvenance.verifyDll payload (prefix+relative) [source;target]
        if hash target<>file.GetProperty("sha256").GetString() || FileInfo(target).Length<>file.GetProperty("bytes").GetInt64() then failwith "Native output differs from independent manifest"
        if relative.StartsWith("WebKit.resources/",StringComparison.Ordinal) then
            let duplicate=Path.Combine(output,Path.GetFileName relative)
            if File.Exists duplicate then BuildProvenance.verifyDll payload (prefix+relative) [duplicate]
        {| Path=relative;Bytes=FileInfo(target).Length;Sha256=hash target |})
    for file in Directory.EnumerateFiles(output,"*",SearchOption.AllDirectories) do
        if file.EndsWith(".so",StringComparison.OrdinalIgnoreCase) || file.EndsWith(".dylib",StringComparison.OrdinalIgnoreCase) ||
           Path.GetFileName(file).Contains("WebGPU",StringComparison.OrdinalIgnoreCase) then failwith "Unexpected backend/RID payload in CPU output"
    {| Id="Ranvier.WebCore.Native.win-x64";Version="0.1.1-preview.7";Backend="CPU";Abi=62
       NupkgSha256=hash payload;MetaNupkgSha256=hash meta;Files=copies |}

/// <summary>Managed and native identities bound to a strict public-cohort publication.</summary>
let publicationProof configuration =
    let output=Path.Combine(stage,"publish",configuration)
    JsonSerializer.SerializeToElement({| Managed=packageProof configuration true; Native=nativeProof output |})

/// <summary>Runs a successful current-source public-cohort NativeAOT publication after rechecking package/native identities.</summary>
let runAot configuration =
    let publication=BuildProvenance.requirePublish __SOURCE_DIRECTORY__ stage configuration
    if (publicationProof configuration).GetRawText()<>publication.Packages.GetRawText() then failwith "Public package/native identities changed since publication."
    BuildNative.runSmoke (Path.Combine(stage,"publish",configuration)) true (Path.Combine(stage,"run-"+configuration+"-aot.log"))

let private snapshotName (label:string) =
    let cohort="native-public45-p7"
    if label="" then cohort
    elif label.Length<=48 && System.Text.RegularExpressions.Regex.IsMatch(label,@"\A[a-z0-9]+(?:-[a-z0-9]+)*\z") then cohort+"-"+label
    else invalidArg "label" "Expected at most 48 lowercase letters/digits separated by single hyphens."

/// <summary>Preserves an exact executed public-cohort PE and its successful source/package/run receipts under an immutable label.</summary>
let capture configuration label =
    let publication=BuildProvenance.requirePublish __SOURCE_DIRECTORY__ stage configuration
    let identities=publicationProof configuration
    if identities.GetRawText()<>publication.Packages.GetRawText() then failwith "Publication identity changed."
    let executable=Path.Combine(stage,"publish",configuration,"Ranvier.Anime.NativeSmoke.exe")
    let runLog=Path.Combine(stage,"run-"+configuration+"-aot.log")
    use execution=JsonDocument.Parse(File.ReadAllText(runLog+".json"))
    let run=execution.RootElement
    if run.GetProperty("Status").GetString()<>"succeeded" || run.GetProperty("ExitCode").GetInt32()<>0 ||
       not(run.GetProperty("RequiredAot").GetBoolean()) || run.GetProperty("ExecutableSha256").GetString()<>hash executable ||
       run.GetProperty("LogSha256").GetString()<>hash runLog then failwith "Matching successful public PE execution is required."
    let cohort=snapshotName label
    let destination=Path.Combine(__SOURCE_DIRECTORY__,"artifacts","qualification",cohort+"-"+configuration)
    if File.Exists(Path.Combine(destination,"receipt.json")) then failwith "This public replay snapshot is already preserved."
    Directory.CreateDirectory destination |> ignore
    let obj=Path.Combine(stage,"build","obj","Ranvier.Anime.NativeSmoke")
    let files=
        ["publish.log",Path.Combine(stage,"publish-"+configuration+".log")
         "publish.json",Path.Combine(stage,"publish-"+configuration+".receipt.json")
         "run.log",runLog;"run.json",runLog+".json"
         "project.assets.json",Path.Combine(obj,"project.assets.json")
         "compiler.rsp",Path.Combine(obj,configuration.ToLowerInvariant()+"_net10.0_win-x64","native","Ranvier.Anime.NativeSmoke.ilc.rsp")
         "Ranvier.Anime.NativeSmoke.exe",executable
         "view-audit.json",Path.Combine(stage,"view-audit.json")
         "native-cpu-audit.json",Path.Combine(stage,"native-cpu-audit.json")]
        |> List.map(fun(name,path) ->
            let target=Path.Combine(destination,name)
            File.Copy(path,target,true)
            {| File=name;Bytes=FileInfo(target).Length;Sha256=hash target |})
    let library=Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__,"src","Ranvier.Anime"))+string Path.DirectorySeparatorChar
    let sources=publication.Inputs |> Array.filter(fun source -> source.Path.StartsWith(library,StringComparison.OrdinalIgnoreCase) && source.Path.EndsWith(".fs",StringComparison.Ordinal))
                |> Array.map(fun source -> {| File=Path.GetFileName source.Path;Sha256=source.Sha256 |}) |> Array.sortBy _.File
    let consumer=Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__,"tests","Ranvier.Anime.NativeSmoke","Program.fs"))
    let receipt=
        {| Schema=4;Configuration=configuration;Scope="win-x64-headless-cpu-smoke"
           PublicCorePackages=true;PublicViewPackages=true;PublicNativePayload=true;HardwareAccelerationQualified=false
           FullNativeQualified=false;KnownNativeLimit="P7 first-owner worker-thread exit remains outside this main-thread smoke; no P8 or GPU claim."
           ViewSource="6b1119c8dbcefc683194dec510859f1137d1e96a";NativeSource="6f10bea17d7a1f22e5e97d472819c055fba3ef06"
           Identities=identities;Files=files;LibrarySources=sources
           ConsumerSourceSha256=(publication.Inputs |> Array.find(fun source -> source.Path=consumer)).Sha256 |}
    File.WriteAllText(Path.Combine(destination,"receipt.json"),JsonSerializer.Serialize receipt)
    printfn "Preserved public %s %s: nine managed identities and 24 native CPU members" cohort configuration

/// <summary>Writes the tracked public-cohort summary after rechecking each immutable receipt's archived files.</summary>
let summarize label =
    let cohort=snapshotName label
    let modes=["Release";"Debug"] |> List.map(fun configuration ->
        let root=Path.Combine(__SOURCE_DIRECTORY__,"artifacts","qualification",cohort+"-"+configuration)
        let path=Path.Combine(root,"receipt.json")
        use receipt=JsonDocument.Parse(File.ReadAllText path)
        for file in receipt.RootElement.GetProperty("Files").EnumerateArray() do
            let archived=Path.Combine(root,file.GetProperty("File").GetString())
            if hash archived<>file.GetProperty("Sha256").GetString() || FileInfo(archived).Length<>file.GetProperty("Bytes").GetInt64() then failwith "Public archived evidence file changed."
        {| Configuration=configuration;ReceiptSha256=hash path;Evidence=receipt.RootElement.Clone() |})
    let summary=
        {| PublicCorePackages=true;PublicViewPackages=true;PublicNativePayload=true
           Scope="win-x64-headless-cpu-smoke";FullNativeQualified=false;HardwareAccelerationQualified=false;Modes=modes |}
    let path=Path.Combine(__SOURCE_DIRECTORY__,"docs","evidence",cohort+".json")
    File.WriteAllText(path,JsonSerializer.Serialize(summary,JsonSerializerOptions(WriteIndented=true)))
    printfn "Verified and summarized public %s replay" cohort


/// <summary>Rejects altered audit, project-graph, managed DLL and native DLL inputs, restoring owned files after each probe.</summary>
let regressions (configuration:string) =
    let output=Path.Combine(stage,"build","bin","Ranvier.Anime.NativeSmoke",configuration.ToLowerInvariant()+"_net10.0_win-x64")
    packageProof configuration false |> ignore
    nativeProof output |> ignore
    let rejected name action =
        let mutable failed=false
        try action() with _ -> failed<-true
        if not failed then failwith ("Evidence accepted "+name)
    rejected "wrong CPU audit" (fun () -> copyAudits (Path.Combine(stage,"view-audit.json")) (Path.Combine(stage,"view-audit.json")))
    for label in ["../prior";@"..\prior";"C:/elsewhere";"-bad";"a--b"] do
        let mutable invalid=false
        try summarize label with :? ArgumentException as error when error.ParamName="label" -> invalid<-true
        if not invalid then failwith "Unsafe public evidence label accepted."
    let assetsPath=Path.Combine(stage,"build","obj","Ranvier.Anime.NativeSmoke","project.assets.json")
    let original=File.ReadAllBytes assetsPath
    try
        let document=System.Text.Json.Nodes.JsonNode.Parse original
        let suffix=if configuration="Debug" then ".Traced" else ""
        document.["libraries"].["Ranvier.View"+suffix+"/0.1.45"].["type"]<-System.Text.Json.Nodes.JsonValue.Create("project")
        File.WriteAllText(assetsPath,document.ToJsonString())
        rejected "producer project dependency" (fun () -> packageProof configuration false |> ignore)
    finally File.WriteAllBytes(assetsPath,original)
    for name,verify in
        ["Ranvier.View.dll",(fun () -> packageProof configuration false |> ignore)
         "RanvierWebCoreHost.dll",(fun () -> nativeProof output |> ignore)] do
        let path=Path.Combine(output,name)
        let original=File.ReadAllBytes path
        try
            File.WriteAllBytes(path,[|0uy;1uy;2uy|])
            rejected (name+" substitution") verify
        finally File.WriteAllBytes(path,original)
    packageProof configuration false |> ignore
    nativeProof output |> ignore
    printfn "Public audit/graph/DLL substitution regressions GREEN"



