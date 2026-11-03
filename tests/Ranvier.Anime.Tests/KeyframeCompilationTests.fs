module Ranvier.Anime.Tests.KeyframeCompilationTests

open System
open System.IO
open System.Text.Json
open Expecto
open Ranvier.Anime
open Ranvier.Anime.ValueInterpolation

let private yes ()=true
let private optional name (item:JsonElement)=match item.TryGetProperty(name:string) with true,value -> Some value | _ -> None
let private literal (item:JsonElement)=if item.ValueKind=JsonValueKind.Number then Number(item.GetDouble()) else Text(item.GetString())
let private modifier=function "double" -> (fun value -> value*2.) | "plusOne" -> (fun value -> value+1.) | _ -> id
let private frame (item:JsonElement) : FrameInput =
    if item.ValueKind<>JsonValueKind.Object then {FrameInput.Default with To=Some(literal item)}
    else
        let pair=optional "to" item |> Option.filter(fun value -> value.ValueKind=JsonValueKind.Array)
        let first=match pair with Some values -> Some(literal(values[0])) | None -> optional "from" item |> Option.map literal
        let last=match pair with Some values -> Some(literal(values[1])) | None -> optional "to" item |> Option.map literal
        { From=first;To=last
          Duration=optional "duration" item |> Option.map _.GetDouble();Delay=optional "delay" item |> Option.map _.GetDouble()
          Ease=optional "ease" item |> Option.map(fun value -> Ease.named(value.GetString()))
          Modifier=optional "modifier" item |> Option.map(fun value -> modifier(value.GetString())) }
let private reserved=set ["at";"key";"duration";"delay";"ease";"modifier"]
let private named (item:JsonElement) =
    item.EnumerateObject() |> Seq.filter(fun property -> not(reserved.Contains property.Name))
    |> Seq.map(fun property -> property.Name,literal property.Value) |> Seq.toArray
let private fixtures () =
    let root=Environment.GetEnvironmentVariable "RANVIER_ANIME_REFERENCE_ROOT"
    let root=if String.IsNullOrWhiteSpace root then Path.Combine(AppContext.BaseDirectory,"reference") else root
    use document=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"keyframes.json")))
    Expect.equal (document.RootElement.GetProperty("upstreamCommit").GetString()) "2c9cf8ea00329f6768c7d7902252ed977d75ce42" "pinned source"
    document.RootElement.GetProperty("fixtures").EnumerateArray() |> Seq.map _.Clone() |> Seq.toList
let private replay (fixture:JsonElement) =
    let input=fixture.GetProperty("input")
    let defaults:FrameDefaults=
        {Duration=input.GetProperty("duration").GetDouble();Delay=optional "delay" input |> Option.map _.GetDouble() |> Option.defaultValue 0.
         Offset=optional "offset" input |> Option.map _.GetDouble() |> Option.defaultValue 0.;Ease=optional "ease" input |> Option.map(fun value -> Ease.named(value.GetString())) |> Option.defaultValue id
         Modifier=optional "modifier" input |> Option.map(fun value -> modifier(value.GetString())) |> Option.defaultValue id}
    let properties =
        match optional "rows" input,optional "offsets" input with
        | Some rows,_ ->
            rows.EnumerateArray() |> Seq.map(fun item -> {FrameRow.Values=named item;Frame={frame item with From=None;To=None}}) |> Seq.toArray |> KeyframeCompilation.rows
        | _,Some rows ->
            let rows=rows.EnumerateArray() |> Seq.map(fun item ->
                {FrameOffset.Offset=item.GetProperty("at").GetDouble();Values=named item
                 Ease=optional "ease" item |> Option.map(fun value -> Ease.named(value.GetString()))}) |> Seq.toArray
            KeyframeCompilation.offsets(defaults.Duration,rows)
        | _ ->
            input.GetProperty("properties").EnumerateObject() |> Seq.map(fun property ->
                let values=property.Value.EnumerateArray() |> Seq.toArray
                let frames=
                    if values.Length>0 && values[0].ValueKind=JsonValueKind.Object then values |> Array.map frame
                    else KeyframeCompilation.values(values |> Array.map literal)
                property.Name,frames) |> Seq.toArray
    let targets=optional "targets" input |> Option.map(fun items -> items.EnumerateArray() |> Seq.toArray)
                |> Option.defaultWith(fun () -> [|input.GetProperty("original")|])
    let captured:FrameProperty array=
        targets |> Array.mapi(fun target values ->
            properties |> Array.map(fun (name,frames) -> {Target=target;Name=name;Original=(optional name values |> Option.map literal |> Option.defaultValue(Number 0.));Frames=frames;
                ConvertUnit=(if fixture.GetProperty("name").GetString()="angleUnits" then Some(CssUnits.convertMeasured(fun _ _ -> failtest "unexpected angle measurement")) else None)}))
        |> Array.concat
    let program=KeyframeCompilation.compile(defaults,captured,yes) |> Option.get
    let expected=fixture.GetProperty("construction")
    Expect.equal program.Delay (expected.GetProperty("delay").GetDouble()) "source initial delay"
    Expect.equal program.IterationDuration (expected.GetProperty("iterationDuration").GetDouble()) "source iteration duration"
    let tweens=expected.GetProperty("tweens")
    Expect.equal program.Tweens.Length (tweens.GetArrayLength()) "source tween count"
    for index in 0..program.Tweens.Length-1 do
        let actual=program.Tweens[index]
        let source=tweens[index]
        Expect.equal (actual.Target,actual.Name) (source.GetProperty("target").GetInt32(),source.GetProperty("property").GetString()) "target/property order"
        for name,value in ["start",actual.Start;"delay",actual.Delay;"updateDuration",actual.UpdateDuration
                           "absoluteStart",actual.AbsoluteStart;"absoluteUpdateStart",actual.AbsoluteUpdateStart;"absoluteEnd",actual.AbsoluteEnd
                           "from",actual.First.Number;"to",actual.Last.Number] do
            Expect.equal value (source.GetProperty(name).GetDouble()) name
        Expect.equal actual.HasFrom (source.GetProperty("hasFrom").GetBoolean()) "explicit from flag"
        Expect.equal (int actual.Last.Kind) (source.GetProperty("kind").GetInt32()) "value kind"
        let unit=optional "unit" source |> Option.bind(fun value -> if value.ValueKind=JsonValueKind.Null then None else Some(value.GetString()))
        Expect.equal (Option.ofObj actual.Last.Unit) unit "inherited unit"
        let numbers (name:string)=source.GetProperty(name).EnumerateArray() |> Seq.map _.GetDouble() |> Seq.toArray
        Expect.sequenceEqual actual.First.Components (numbers "fromComponents") "raw first components"
        Expect.sequenceEqual actual.Last.Components (numbers "toComponents") "raw destination components"
        Expect.sequenceEqual actual.Last.Strings (source.GetProperty("strings").EnumerateArray() |> Seq.map _.GetString()) "component template"
        let cached=source.GetProperty("cachedEnd")
        if cached.ValueKind=JsonValueKind.Number then Expect.equal actual.CachedEnd.Scalar (Some(cached.GetDouble())) "modified numeric end cache"
        else Expect.equal actual.CachedEnd.Text (cached.GetString()) "composed end cache"
        Array.iter2(fun progress expected ->
            let actual=actual.Ease progress
            Expect.isTrue (actual=expected || abs(actual-expected)<=1e-12*max 1. (abs expected)) "per-frame easing transfer")
            [|-0.25;0.;0.25;0.5;1.;1.25|] (numbers "ease")

let compilationTests=testList "keyframe compilation" [
    for fixture in fixtures() do testCase ("source "+fixture.GetProperty("name").GetString()) <| fun _ -> replay fixture
    testCase "raw arrays are captured independently" <| fun _ ->
        let input=[|Number 0.;Number 100.;Number 50.|]
        let frames=KeyframeCompilation.values input
        input[0]<-Number 999.
        Expect.equal frames[0].From (Some(Number 0.)) "captured first value"
        Expect.equal frames.Length 2 "raw sequence count"
    testCase "compilation captures later frame inputs before modifier effects" <| fun _ ->
        let frames=[|{FrameInput.Default with To=Some(Number 100.)};{FrameInput.Default with To=Some(Number 200.)}|]
        let modify value=frames[1]<-{frames[1] with To=Some(Number 999.)};value
        let defaults={FrameDefaults.Default with Duration=100.;Modifier=modify}
        let properties=[|{Target=0;Name="x";Original=Number 10.;Frames=frames;ConvertUnit=None}|]
        let program=KeyframeCompilation.compile(defaults,properties,yes) |> Option.get
        Expect.equal program.Tweens[1].Last.Number 200. "later captured endpoint"
    testCase "interrupted component modifiers discard the complete program" <| fun _ ->
        let mutable active=true
        let mutable calls=0
        let modify value=calls<-calls+1;active<-false;value
        let defaults={FrameDefaults.Default with Duration=100.;Modifier=modify}
        let frames=[|{FrameInput.Default with To=Some(Text "v(100,200)")}|]
        let properties=[|{Target=0;Name="x";Original=Text "v(10,20)";Frames=frames;ConvertUnit=None}|]
        Expect.isNone (KeyframeCompilation.compile(defaults,properties,(fun () -> active))) "atomic compilation"
        Expect.equal calls 1 "later component effects suppressed"
    testCase "unit conversion cancellation prevents later modifier effects" <| fun _ ->
        let mutable active=true
        let mutable conversions=0
        let convert _ _ _=conversions<-conversions+1;active<-false;32.
        let defaults={FrameDefaults.Default with Modifier=(fun _ -> failtest "modifier after conversion cancellation")}
        let properties=[|{Target=0;Name="x";Original=Text "1em";Frames=[|{FrameInput.Default with To=Some(Text "100px")}|];ConvertUnit=Some convert}|]
        Expect.isNone (KeyframeCompilation.compile(defaults,properties,(fun () -> active))) "conversion interrupted"
        Expect.equal conversions 1 "one conversion"
    testCase "invalid later timing rejects before any modifier effects" <| fun _ ->
        let defaults={FrameDefaults.Default with Modifier=(fun _ -> failtest "modifier before complete validation")}
        let frames=[|{FrameInput.Default with To=Some(Number 100.)};{FrameInput.Default with To=Some(Number 200.);Duration=Some -1.}|]
        let properties=[|{Target=0;Name="x";Original=Number 10.;Frames=frames;ConvertUnit=None}|]
        Expect.throwsT<ArgumentException> (fun () -> KeyframeCompilation.compile(defaults,properties,yes) |> ignore) "nonnegative duration contract"
    testCase "an inactive compiler has no user effects" <| fun _ ->
        let defaults={FrameDefaults.Default with Modifier=(fun _ -> failtest "inactive modifier")}
        let properties=[|{Target=0;Name="x";Original=Number 10.;Frames=[|{FrameInput.Default with To=Some(Number 100.)}|];ConvertUnit=None}|]
        Expect.isNone (KeyframeCompilation.compile(defaults,properties,(fun () -> false))) "inactive compile"
    testCase "empty sequences do not parse an unused original" <| fun _ ->
        let properties=[|{Target=0;Name="x";Original=Text "rgb(invalid)";Frames=[||];ConvertUnit=None}|]
        let program=KeyframeCompilation.compile(FrameDefaults.Default,properties,yes) |> Option.get
        Expect.equal program.Tweens.Length 0 "empty program"
        Expect.equal program.IterationDuration 1e-11 "source empty duration"
    testCase "existing normalization retains lazy original parsing" <| fun _ ->
        let first,last=normalize None (Text "rgb(invalid)") (Number 0.) (Number 100.)
        Expect.equal (first.Number,last.Number) (0.,100.) "unused original remains unused"
]
