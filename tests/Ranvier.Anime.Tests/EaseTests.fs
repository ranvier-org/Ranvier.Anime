module Ranvier.Anime.Tests.EaseTests

open System
open System.IO
open System.Text.Json
open Expecto
open Ranvier.Anime

let private samples section =
    let path = Path.Combine(AppContext.BaseDirectory, "reference", "pure.json")
    use document = JsonDocument.Parse(File.ReadAllText path)
    [for item in document.RootElement.GetProperty(section: string).EnumerateArray() do
        let points = [for pair in item.GetProperty("values").EnumerateArray() -> pair[0].GetDouble(), pair[1].GetDouble()]
        yield item.GetProperty("name").GetString(), points]

let private matches name evaluate points =
    testCase name <| fun _ ->
        for time, expected in points do
            let actual = evaluate time
            Expect.isTrue (Double.IsFinite actual && abs(actual - expected) <= 1e-12)
                $"at {time}: expected {expected:R}, got {actual:R}"

let tests = testList "upstream easing comparisons" [
    for name, points in samples "named" do
        matches name (Ease.named name) points
    for name, points in samples "curves" do
        let evaluate =
            match name with
            | "cubicBezier" -> Ease.cubicBezier 0.42 0. 0.58 1.
            | "stepsEnd" -> Ease.steps 4u false
            | "stepsStart" -> Ease.steps 4u true
            | _ -> failwithf "Unknown fixture %s" name
        matches name evaluate points
    testCase "zero steps is rejected before evaluation" <| fun _ ->
        Expect.throwsT<ArgumentException> (fun () -> Ease.steps 0u false |> ignore) "zero has no step interval"
    testCase "unknown easing names are rejected" <| fun _ ->
        Expect.throwsT<ArgumentException> (fun () -> Ease.named "misspelled" |> ignore) "unknown name"
]
