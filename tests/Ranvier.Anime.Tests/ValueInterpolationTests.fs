module Ranvier.Anime.Tests.ValueInterpolationTests

open System
open System.IO
open System.Globalization
open System.Text.Json
open Expecto
open Ranvier.Anime
open Ranvier.Anime.ValueInterpolation

let private cases section =
    let root=Environment.GetEnvironmentVariable "RANVIER_ANIME_REFERENCE_ROOT"
    let path=Path.Combine((if String.IsNullOrWhiteSpace root then Path.Combine(AppContext.BaseDirectory,"reference") else root),"value-interpolation.json")
    use document=JsonDocument.Parse(File.ReadAllText path)
    if document.RootElement.GetProperty("upstreamCommit").GetString()<>"2c9cf8ea00329f6768c7d7902252ed977d75ce42" then failwith "Unexpected interpolation oracle revision."
    document.RootElement.GetProperty(section:string).EnumerateArray() |> Seq.map _.Clone() |> Seq.toList

let private literal (value:JsonElement) =
    if value.ValueKind=JsonValueKind.Number then Number(value.GetDouble()) else Text(value.GetString())

let private same first last =
    if Double.IsNaN first then Double.IsNaN last
    else first=last || (Double.IsFinite first && Double.IsFinite last && abs(first-last)<=1e-12*max 1. (abs first))

let private values (value:JsonElement) =
    if value.ValueKind=JsonValueKind.Null then [||]
    else value.EnumerateArray() |> Seq.map(fun item -> if item.ValueKind=JsonValueKind.Null then Double.NaN else item.GetDouble()) |> Seq.toArray

let private components expected actual =
    Expect.equal (Array.length actual) (Array.length expected) "component count"
    Array.iter2 (fun first last -> Expect.isTrue (same first last) $"expected {first:R} actual {last:R}") expected actual

let private compareDecomposition (item:JsonElement) =
    let actual=decompose(literal(item.GetProperty("raw")))
    let expected=item.GetProperty("value")
    Expect.equal (int actual.Kind) (expected.GetProperty("t").GetInt32()) "value kind"
    Expect.isTrue (same actual.Number (expected.GetProperty("n").GetDouble())) "scalar"
    components (values(expected.GetProperty("d"))) actual.Components
    let unitText=if expected.GetProperty("u").ValueKind=JsonValueKind.Null then null else expected.GetProperty("u").GetString()
    Expect.equal actual.Unit unitText "unit"
    let op=if expected.GetProperty("o").ValueKind=JsonValueKind.Null then None else Some(expected.GetProperty("o").GetString()[0])
    Expect.equal actual.Operator op "operator"
    let strings=if expected.GetProperty("s").ValueKind=JsonValueKind.Null then [||] else expected.GetProperty("s").EnumerateArray() |> Seq.map _.GetString() |> Seq.toArray
    Expect.sequenceEqual actual.Strings strings "template"

let private compareComposition (entry:JsonElement) =
    let evaluate=prepare (literal(entry.GetProperty("base"))) (literal(entry.GetProperty("first"))) (literal(entry.GetProperty("last")))
    let modifier=match entry.GetProperty("modifier").GetString() with "affine" -> (fun value -> value*0.5+2.) | "round2" -> PropertyNumbers.round 100. | _ -> id
    for sample in entry.GetProperty("samples").EnumerateArray() do
        let progress=sample.GetProperty("progress").GetDouble()
        let actual=evaluate progress (entry.GetProperty("precision").GetInt32()) modifier (fun () -> true) |> Option.get
        let expected=sample.GetProperty("result")
        if expected.ValueKind=JsonValueKind.Number then
            Expect.isTrue (actual.Scalar |> Option.exists(same(expected.GetDouble()))) $"scalar at {progress}"
        else
            Expect.isNone actual.Scalar "string result"
            Expect.equal actual.Text (expected.GetString()) $"text at {progress}"
        components (values(sample.GetProperty("components"))) actual.Components

let tests = testList "value interpolation kernel" [
    for index,entry in cases "decomposed" |> List.indexed do
        testCase ("decomposition "+string index) <| fun _ -> compareDecomposition entry
    for entry in cases "cases" do
        testCase (entry.GetProperty("name").GetString()) <| fun _ -> compareComposition entry
    for entry in cases "numberStrings" do
        testCase ("number text "+entry.GetProperty("value").GetString()) <| fun _ ->
            let input=Double.Parse(entry.GetProperty("value").GetString(),CultureInfo.InvariantCulture)
            Expect.equal (number input) (entry.GetProperty("result").GetString()) "source number serialization"
    testCase "inactive sampling never invokes modifiers" <| fun _ ->
        let mutable calls=0
        let evaluate=prepare (Number 0.) (Text "v(0,10)") (Text "v(10,20)")
        let output=evaluate 0.5 4 (fun value -> calls<-calls+1; value) (fun () -> false)
        Expect.isNone output "inactive sample"
        Expect.equal calls 0 "no modifier effects"
    testCase "scalar modifier cancellation suppresses output" <| fun _ ->
        let mutable live=true
        let evaluate=prepare (Number 0.) (Number 0.) (Number 100.)
        let output=evaluate 0.5 4 (fun value -> live<-false; value) (fun () -> live)
        Expect.isNone output "guard after modifier"
    testCase "compound modifier cancellation suppresses later components" <| fun _ ->
        let mutable live=true
        let mutable calls=0
        let evaluate=prepare (Number 0.) (Text "v(0,10,20)") (Text "v(10,20,30)")
        let output=evaluate 0.5 4 (fun value -> calls<-calls+1; live<-false; value) (fun () -> live)
        Expect.isNone output "cancelled sample"
        Expect.equal calls 1 "no effects in later components"
    testCase "color cancellation is checked after every channel" <| fun _ ->
        for stop in 1..4 do
            let mutable live=true
            let mutable calls=0
            let evaluate=prepare (Number 0.) (Text "#1234") (Text "#abcdef80")
            let modify value =
                calls<-calls+1
                if calls=stop then live<-false
                value
            let output=evaluate 0.5 4 modify (fun () -> live)
            Expect.isNone output "cancelled color"
            Expect.equal calls stop "remaining channels are skipped"
    testCase "sample arrays cannot mutate captured endpoints" <| fun _ ->
        let evaluate=prepare (Number 0.) (Text "v(0,10)") (Text "v(10,20)")
        let first=evaluate 0.5 4 id (fun () -> true) |> Option.get
        first.Components[0]<-999.
        let second=evaluate 0.5 4 id (fun () -> true) |> Option.get
        Expect.equal second.Text "v(5,15)" "later samples retain endpoint capture"
        Expect.equal second.Components[0] 5. "per-sample storage"
    testCase "modifier faults preserve the original exception" <| fun _ ->
        let expected=InvalidOperationException "component fault"
        let evaluate=prepare (Number 0.) (Text "v(0,10)") (Text "v(10,20)")
        let mutable actual:exn option=None
        try evaluate 0.5 4 (fun _ -> raise expected) (fun () -> true) |> ignore with error -> actual<-Some error
        Expect.isTrue (actual |> Option.exists(fun error -> obj.ReferenceEquals(error,expected))) "fault identity"
    testCase "native authored scalar inputs must be finite" <| fun _ ->
        for value in [nan;infinity;-infinity] do
            Expect.throwsT<ArgumentException> (fun () -> decompose(Number value) |> ignore) "finite scalar required"
    testCase "context-dependent unit conversion is explicit" <| fun _ ->
        Expect.throwsT<ArgumentException> (fun () -> prepare (Number 0.) (Text "1em") (Text "10px") |> ignore) "cannot approximate native unit conversion"
    testCase "sample progress must be finite" <| fun _ ->
        let evaluate=prepare (Number 0.) (Text "0px") (Text "100px")
        for progress in [nan;infinity;-infinity] do
            Expect.throwsT<ArgumentException> (fun () -> evaluate progress 4 id (fun () -> true) |> ignore) "finite progress required"
    testCase "unsupported source color syntax is rejected" <| fun _ ->
        for text in ["rgba(1,2,3)";"rgb(1,2,3,0.5)";"hsla(0,50%,50%)";"hsl(0,50%,50%,0.5)";"rgb(10 20 30 / 0.5)"] do
            Expect.throwsT<ArgumentException> (fun () -> decompose(Text text) |> ignore) "no implicit browser color normalization"
]
