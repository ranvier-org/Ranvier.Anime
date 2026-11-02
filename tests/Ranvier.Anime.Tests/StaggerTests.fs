module Ranvier.Anime.Tests.StaggerTests

open System
open System.IO
open System.Text.Json
open Expecto
open Ranvier.Anime
open Ranvier.Dom

/// <summary>Records the DOM members exercised by geometry adapter tests.</summary>
type GeometryProxy() =
    inherit Reflection.DispatchProxy()
    let mutable handler : Reflection.MethodInfo -> obj array -> obj = fun _ _ -> failwith "Unconfigured proxy"
    /// <summary>Handles calls to the test's borrowed DOM interface.</summary>
    member _.Handler with set value = handler <- value
    override _.Invoke(methodInfo, args) = handler methodInfo args

let private proxy<'T> handler =
    let value = Reflection.DispatchProxy.Create<'T, GeometryProxy>()
    (box value :?> GeometryProxy).Handler <- handler
    value

let private recordingElements () =
    let reads = Array.zeroCreate 3
    let targets = [| -5., 10.; 10., 20.; 30., 40. |] |> Array.mapi(fun index (left, width) ->
        let rect = proxy<DOMRect> (fun methodInfo _ ->
            match methodInfo.Name with
            | "get_left" -> box left
            | "get_top" -> box -2.
            | "get_width" -> box width
            | "get_height" -> box 4.
            | name -> failwithf "Unexpected rectangle member %s" name)
        proxy<Element> (fun methodInfo _ ->
            if methodInfo.Name <> "getBoundingClientRect" then failwithf "Unexpected element member %s" methodInfo.Name
            reads[index] <- reads[index] + 1
            box rect))
    targets, reads

let private field name (item: JsonElement) =
    match item.TryGetProperty(name: string) with true, value -> Some value | _ -> None

let private options (fixture: JsonElement) =
    let parameters = fixture.GetProperty "parameters"
    let origin =
        match field "from" parameters with
        | None -> StaggerOrigin.First
        | Some value when value.ValueKind = JsonValueKind.Number -> StaggerOrigin.Index(value.GetUInt32())
        | Some value when value.ValueKind = JsonValueKind.Array ->
            if value.GetArrayLength() = 2 then StaggerOrigin.Position2D(value[0].GetDouble(), value[1].GetDouble())
            else StaggerOrigin.Position3D(value[0].GetDouble(), value[1].GetDouble(), value[2].GetDouble())
        | Some value ->
            match value.GetString() with
            | "center" -> StaggerOrigin.Center
            | "last" -> StaggerOrigin.Last
            | "first" -> StaggerOrigin.First
            | "random" -> StaggerOrigin.Random
            | unknown -> failwithf "Unknown origin %s" unknown
    let grid =
        match field "grid" parameters with
        | None -> StaggerGrid.Flat
        | Some value when value.ValueKind = JsonValueKind.True ->
            let positions = fixture.GetProperty("targets").EnumerateArray() |> Seq.map(fun target ->
                match field "rect" target with
                | Some rect -> Some(DOMPointInit(x = rect.GetProperty("left").GetDouble() + rect.GetProperty("width").GetDouble() / 2.,
                                                 y = rect.GetProperty("top").GetDouble() + rect.GetProperty("height").GetDouble() / 2.))
                | None ->
                    match field "x" target, field "y" target with
                    | Some x, Some y -> Some(DOMPointInit(x = x.GetDouble(), y = y.GetDouble(),
                                                        z = (field "z" target |> Option.map _.GetDouble() |> Option.defaultValue 0.)))
                    | _ -> None) |> Seq.toArray
            StaggerGrid.Automatic positions
        | Some value when value.GetArrayLength() = 2 -> StaggerGrid.Grid2D(value[0].GetUInt32(), value[1].GetUInt32())
        | Some value -> StaggerGrid.Grid3D(value[0].GetUInt32(), value[1].GetUInt32(), value[2].GetUInt32())
    { StaggerOptions.Default with
        Origin = origin; Grid = grid
        Axis = field "axis" parameters |> Option.map(fun item -> match item.GetString() with "x" -> StaggerAxis.X | "y" -> StaggerAxis.Y | _ -> StaggerAxis.Z)
        Reversed = field "reversed" parameters |> Option.map _.GetBoolean() |> Option.defaultValue false
        Start = field "start" parameters |> Option.map _.GetDouble()
        Total = field "total" parameters |> Option.map _.GetUInt32()
        Seed = field "seed" parameters |> Option.map _.GetDouble()
        Jitter = field "jitter" parameters |> Option.map(fun item ->
            if item.ValueKind = JsonValueKind.Array then StaggerJitter.Range(item[0].GetDouble(), item[1].GetDouble())
            else StaggerJitter.Magnitude(item.GetDouble()))
        Ease = field "ease" parameters |> Option.map(fun item -> if item.GetString() = "spring" then (new Spring()).Ease else Ease.named (item.GetString()))
        Modifier = field "modifier" parameters |> Option.map(fun _ -> fun value -> value * 2.)
        IndexMap = field "map" parameters |> Option.map(fun item ->
            if item.GetString() = "reverse" then fun index total -> int total - 1 - int index
            else fun index total -> int total + int index) }

let private fixtures =
    use document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "reference", "pure.json")))
    [for item in document.RootElement.GetProperty("staggering").EnumerateArray() -> item.Clone()]

let tests = testList "upstream staggering" [
    for fixture in fixtures do
        testCase (fixture.GetProperty("name").GetString()) <| fun _ ->
            let input = fixture.GetProperty "value"
            let value =
                if input.ValueKind = JsonValueKind.Array then StaggerValue.Range(input[0].GetDouble(), input[1].GetDouble())
                else StaggerValue.Step(input.GetDouble())
            let expected = fixture.GetProperty("values").EnumerateArray() |> Seq.map(fun value ->
                if value.ValueKind = JsonValueKind.String then Double.Parse(value.GetString(), Globalization.CultureInfo.InvariantCulture)
                else value.GetDouble()) |> Seq.toArray
            let actual = Stagger.distribute value (options fixture) (uint expected.Length)
            Expect.equal actual.Length expected.Length "target count"
            Array.iteri(fun index expected ->
                Expect.isTrue (actual[index] = expected || (Double.IsNaN actual[index] && Double.IsNaN expected) ||
                    (Double.IsFinite actual[index] && abs(actual[index] - expected) <= 1e-9))
                    $"index {index}: expected {expected:R}, got {actual[index]:R}") expected
    testCase "empty targets produce an empty distribution" <| fun _ ->
        Expect.isEmpty (Stagger.distribute (StaggerValue.Step 10.) StaggerOptions.Default 0u) "empty target set"
    testCase "zero grid dimensions are rejected" <| fun _ ->
        let options = { StaggerOptions.Default with Grid = StaggerGrid.Grid2D(0u, 3u) }
        Expect.throwsT<ArgumentException> (fun () -> Stagger.distribute (StaggerValue.Step 10.) options 3u |> ignore) "zero grid width"
    testCase "nonfinite inputs fail before distribution" <| fun _ ->
        Expect.throwsT<ArgumentException>
            (fun () -> Stagger.distribute (StaggerValue.Step nan) StaggerOptions.Default 3u |> ignore) "invalid step"
    testCase "named easing accepts the signed axis domain used by staggering" <| fun _ ->
        Expect.equal (Ease.named "inQuad" -1.) 1. "signed quadratic input"
    testCase "bounce rejects extrapolation where the source search cannot terminate" <| fun _ ->
        Expect.throwsT<ArgumentException> (fun () -> Ease.named "inBounce" -1. |> ignore) "nonterminating source domain"
    testCase "unseeded random origins preserve the offset multiset" <| fun _ ->
        let options = { StaggerOptions.Default with Origin = StaggerOrigin.Random }
        let actual = Stagger.distribute (StaggerValue.Step 10.) options 32u |> Array.sort
        Expect.equal actual (Array.init 32 (fun index -> float index * 10.)) "shuffle preserves offsets"
    testCase "unseeded jitter remains within its magnitude" <| fun _ ->
        let options = { StaggerOptions.Default with Jitter = Some(StaggerJitter.Magnitude 5.) }
        Stagger.distribute (StaggerValue.Step 10.) options 32u
        |> Array.iteri(fun index actual -> Expect.isTrue (abs(actual - float index * 10.) <= 5.) "jitter magnitude")
    testCase "automatic elements measure box centers once" <| fun _ ->
        let targets, reads = recordingElements()
        let actual = Stagger.autoElements (StaggerValue.Step 10.) StaggerOptions.Default targets
        Expect.equal actual [|0.; 10.; 25.|] "measured centers, not element widths or indices"
        Expect.equal reads [|1; 1; 1|] "one measurement per element"
    testCase "automatic element measurement respects the total override" <| fun _ ->
        let targets, reads = recordingElements()
        let options = { StaggerOptions.Default with Total = Some 2u }
        let actual = Stagger.autoElements (StaggerValue.Step 10.) options targets
        Expect.equal reads [|1; 1; 0|] "only relevant elements measured"
        Expect.equal actual [|0.; 10.; 0.|] "missing distance follows source fallback"
]
