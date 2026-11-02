module BuildCore56

open System
open System.IO
open System.Security.Cryptography
open System.Text.Json

let private auditPath = Path.Combine(__SOURCE_DIRECTORY__, "artifacts", "core56", "public-byte-audit.json")
let private expected =
    ["Ranvier.Dom", "518866e8bc643a52cb069ad597e6fc307e042cfb9ff9fb8b0156a92095caeae2"
     "Ranvier.Dom.Traced", "ca60a9a05bd355941e615c044b5a2ad897a347be23278e89422d345820a7586c"]

let private hash path =
    use stream = File.OpenRead path
    SHA256.HashData stream |> Convert.ToHexString |> fun value -> value.ToLowerInvariant()

let private validate text =
    use document = JsonDocument.Parse(text: string)
    for id, checksum in expected do
        let entry = document.RootElement.GetProperty id
        if entry.GetProperty("public_sha256").GetString() <> checksum ||
           not(entry.GetProperty("members_equal").GetBoolean()) || not(entry.GetProperty("signed").GetBoolean()) then
            failwith "Core alpha.56 audit does not match the accepted public package identities."

/// <summary>Copies the coordinator's public byte-audit metadata into the owned evidence tree.</summary>
let copyAudit source =
    let text = File.ReadAllText source
    validate text
    Directory.CreateDirectory(Path.GetDirectoryName auditPath) |> ignore
    if File.Exists auditPath && File.ReadAllText auditPath <> text then failwith "The owned Core audit is already preserved with different bytes."
    File.WriteAllText(auditPath, text)
    printfn "Preserved public Core audit SHA256 %s" (hash auditPath)

/// <summary>Requires the owned accepted audit and returns its file hash.</summary>
let verifyAudit () =
    validate (File.ReadAllText auditPath)
    hash auditPath

/// <summary>Rejects a restored DOM package whose bytes differ from the accepted public normal/Traced nupkg.</summary>
let verifyDomPackage id path =
    verifyAudit() |> ignore
    let expectedHash = expected |> List.find(fun (name, _) -> name = id) |> snd
    if hash path <> expectedHash then failwith "Restored DOM alpha.56 differs from the independently audited public nupkg."
