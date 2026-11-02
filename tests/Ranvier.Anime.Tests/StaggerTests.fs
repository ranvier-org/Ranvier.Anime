module Ranvier.Anime.Tests.StaggerTests

open System
open System.IO
open System.Text.Json
open Expecto
open Ranvier.Anime

let private field name (item: JsonElement) =
    match item.TryGetProperty(name: string) with true, value -> Some value | _ -> None

let private options (parameters: JsonElement) =
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
            | unknown -> failwithf "Unknown origin %s" unknown
    let grid =
        match field "grid" parameters with
        | None -> StaggerGrid.Flat
        | Some value when value.GetArrayLength() = 2 -> StaggerGrid.Grid2D(value[0].GetUInt32(), value[1].GetUInt32())
        | Some value -> StaggerGrid.Grid3D(value[0].GetUInt32(), value[1].GetUInt32(), value[2].GetUInt32())
    { StaggerOptions.Default with
        Origin = origin; Grid = grid
        Axis = field "axis" parameters |> Option.map(fun item -> match item.GetString() with "x" -> StaggerAxis.X | "y" -> StaggerAxis.Y | _ -> StaggerAxis.Z)
        Reversed = field "reversed" parameters |> Option.map _.GetBoolean() |> Option.defaultValue false
        Start = field "start" parameters |> Option.map _.GetDouble()
        Total = field "total" parameters |> Option.map _.GetUInt32()
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
            let actual = Stagger.distribute value (options (fixture.GetProperty "parameters")) (uint expected.Length)
            Expect.equal actual.Length expected.Length "target count"
            Array.iteri(fun index expected ->
                Expect.isTrue (actual[index] = expected || (Double.IsFinite actual[index] && abs(actual[index] - expected) <= 1e-9))
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
]
