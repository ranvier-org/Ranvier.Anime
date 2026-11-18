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

// These endpoint values changed from the pinned v4 oracle when parsing absolute CSS colours.
let private modernEndpoint = function
    | "red" -> Some [|255.;0.;0.;1.|]
    | "blue" -> Some [|0.;0.;255.;1.|]
    | "hsl(0,0%,50%)" -> Some [|127.5;127.5;127.5;1.|]
    | "hsl(360,0%,100%)" -> Some [|255.;255.;255.;1.|]
    | _ -> None

let private compareDecomposition (item:JsonElement) =
    let actual=decompose(literal(item.GetProperty("raw")))
    let expected=item.GetProperty("value")
    let modern = modernEndpoint (item.GetProperty("raw").ToString())
    Expect.equal (int actual.Kind) (if modern.IsSome then int ValueKind.Color else expected.GetProperty("t").GetInt32()) "value kind"
    Expect.isTrue (same actual.Number (expected.GetProperty("n").GetDouble())) "scalar"
    components (defaultArg modern (values(expected.GetProperty("d")))) actual.Components
    let unitText=if expected.GetProperty("u").ValueKind=JsonValueKind.Null then null else expected.GetProperty("u").GetString()
    Expect.equal actual.Unit unitText "unit"
    let op=if expected.GetProperty("o").ValueKind=JsonValueKind.Null then None else Some(expected.GetProperty("o").GetString()[0])
    Expect.equal actual.Operator op "operator"
    let strings=if expected.GetProperty("s").ValueKind=JsonValueKind.Null then [||] else expected.GetProperty("s").EnumerateArray() |> Seq.map _.GetString() |> Seq.toArray
    Expect.sequenceEqual actual.Strings (if modern.IsSome then [||] else strings) "template"

let private modifierFor = function
    | "affine" -> fun value -> value*0.5+2.
    | "round2" -> PropertyNumbers.round 100.
    | _ -> id

// Reuse the recorded red-to-blue hex samples for their equivalent CSS names.
let private redBlue =
    [ for entry in cases "cases" do
          if entry.GetProperty("first").ToString() = "#ff0000" && entry.GetProperty("last").ToString() = "#0000ff" then
              for sample in entry.GetProperty("samples").EnumerateArray() do
                  yield (entry.GetProperty("modifier").GetString(), sample.GetProperty("progress").GetDouble()), sample ]
    |> Map.ofList

let private updatedColour (entry:JsonElement) (sample:JsonElement) =
    let modifierName = entry.GetProperty("modifier").GetString()
    let modify = modifierFor modifierName
    let progress = sample.GetProperty("progress").GetDouble()
    let first = entry.GetProperty("first").ToString()
    let sample = if first = "red" then redBlue[modifierName,progress] else sample
    let result = sample.GetProperty("result").ToString()
    if first = "hsl(0,0%,50%)" then
        // The old achromatic oracle mixed 0.5 and 1 on the unit scale.
        let grey = modify (127.5 * sqrt (1. + 3.*progress))
        let channel = number (PropertyNumbers.round 1. grey)
        let alpha = modify 1.
        Some ("rgba("+channel+","+channel+","+channel+","+number alpha+")", [|grey;grey;grey;alpha|])
    elif first = "red" || (result.StartsWith("rgba(") && result.Contains("NaN")) then
        let corrected = result.Replace("NaN", number (modify 0.))
        let hasComponents,channels = sample.TryGetProperty("components")
        let channels =
            if hasComponents then values channels |> Array.map (fun n -> if Double.IsNaN n then modify 0. else n)
            else [||]
        Some(corrected,channels)
    else None

let expectedColourText entry sample = updatedColour entry sample |> Option.map fst

let private compareComposition (entry:JsonElement) =
    let evaluate=prepare (literal(entry.GetProperty("base"))) (literal(entry.GetProperty("first"))) (literal(entry.GetProperty("last")))
    let modifier=modifierFor (entry.GetProperty("modifier").GetString())
    for sample in entry.GetProperty("samples").EnumerateArray() do
        let progress=sample.GetProperty("progress").GetDouble()
        let actual=evaluate progress (entry.GetProperty("precision").GetInt32()) modifier (fun () -> true) |> Option.get
        let expected=sample.GetProperty("result")
        match updatedColour entry sample with
        | Some(text,channels) ->
            Expect.equal actual.Text text $"colour at {progress}"
            components channels actual.Components
        | None ->
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
    testCase "modern and legacy functions produce the same channels" <| fun _ ->
        for text in ["rgb(10 20 30 / 50%)";"rgba(10,20,30,.5)";"RGB(10,20,30,0.5)"] do
            components [|10.;20.;30.;0.5|] (Utils.parseColor text)
        for text in ["hsla(.5turn 100% 50%)";"hsl(-180deg,100%,50%)";"hwb(200grad 0% 0%)"] do
            components [|0.;255.;255.;1.|] (Utils.parseColor text)
        components [|0.;255.;127.5;0.|] (Utils.parseColor "rgb(-7 110% 50% / -1)")
        components [|0.;0.;0.;0.|] (Utils.parseColor "transparent")
        components [|0.;0.;0.;0.|] (Utils.parseColor "hsl(none none none / none)")
        let shadow = decompose(Text "rgb(10 20 30 / .5) 4px 7px")
        Expect.equal shadow.Kind ValueKind.Complex "a leading colour is only one component of a shadow"
        components [|102.;51.;153.;1.|] (Utils.parseColor "RebeccaPurple")
    testCase "CSS Color 4 reference blue resolves across profiles and OKLab representations" <| fun _ ->
        for text in ["oklch(65% .15 270 / 40%)";"oklab(.65 0 -37.5% / .4)";
                     "color(display-p3 .445 .529 .891 / .4)";"color(rec2020 .522 .557 .892 / .4)"] do
            let channels = Utils.parseColor text
            Array.iter2 (fun actual expected -> Expect.isLessThan (abs(actual-expected)) 0.6 text) channels[..2] [|108.;136.;234.|]
            Expect.equal channels[3] 0.4 "straight alpha"
            components channels (decompose(Text text)).Components
        // Independent sRGB reference channels from Chromium's relative-color conversion.
        for input,expected in
            [ "color(srgb-linear .15 .52 .31)", [|108.026;190.847;151.135|]
              "color(prophoto-rgb .28 .34 .55)", [|0.;108.876;164.410|]
              "color(xyz-d50 .24 .18 .56)", [|119.547;100.490;226.335|] ] do
            Array.iter2 (fun actual target -> Expect.isLessThan (abs(actual-target)) 0.05 input)
                        ((Utils.parseColor input)[..2]) expected
        for profile in ["xyz";"xyz-d65";"srgb";"display-p3-linear";"a98-rgb"] do
            components [|0.;0.;0.;1.|] (Utils.parseColor $"color({profile} 0 0 0)")
        let sample = prepare (Text "red") (Text "oklab(0 0 0)") (Text "color(display-p3 1 0 0)") 0.5 4 id (fun () -> true) |> Option.get
        Expect.equal sample.Text "rgba(180,0,0,1)" "mixed-space interpolation after gamut clipping"
    testCase "malformed and context-dependent colours are rejected by parseColor" <| fun _ ->
        for text in [null;"";"currentColor";"var(--accent)";"rgb(1, 2 3)";"rgb(1 2 3 /)";
                     "rgb(10%,2,3)";"rgb(1 2 3) trailing";"oklab(.5 0)";"#abxz";"color(unknown 1 2 3)";
                     "rgb(none% 0 0)";"hsl(nonedeg 0% 0%)";"hsl(0,0,0)";"hwb(bad 50% 50%)"] do
            Expect.throwsT<ArgumentException> (fun () -> Utils.parseColor text |> ignore) "unsupported colour"
        Expect.throwsT<ArgumentException> (fun () -> decompose(Text "rgb(calc(1 + 2) 0 0)") |> ignore) "nested CSS math requires a CSS evaluator"
    testCase "interpolation diagnostics use authored BCL output" <| fun _ ->
        Expect.equal ((Number 1.25).ToString()) "Number 1.25" "literal diagnostic"
        Expect.equal ((Text null).ToString()) "Text <null>" "null literal diagnostic"
        Expect.equal ((decompose(Text "2px")).ToString()) "ValueData(Unit, 2)" "decomposed diagnostic"
        let output=prepare (Number 0.) (Text "0px") (Text "10px") 0.5 4 id (fun () -> true) |> Option.get
        Expect.equal (output.ToString()) "5px" "sample diagnostic"
]
