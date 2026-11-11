namespace Ranvier.Anime.Tests

open System
open System.Collections.Generic
open System.Text.Json
open Expecto
open Ranvier.Anime

module DraggableInlineSourceTests =
    let inlineContractTests fixturePath =
        use document=JsonDocument.Parse(System.IO.File.ReadAllText fixturePath)
        let fixtures=document.RootElement.GetProperty("fixtures").EnumerateArray() |> Seq.map _.Clone() |> Seq.toArray
        testList "source scalar units and explicit complex adaptation" [
            testCase "three valid scalar mappings match source units and serialization" <| fun _ ->
                for fixture in fixtures |> Array.filter(fun f -> f.GetProperty("name").GetString().StartsWith("scalar",StringComparison.Ordinal)) do
                    let struct(cache,full)=TransformCache.parse(fixture.GetProperty("initialText").GetString())
                    let xName=match fixture.TryGetProperty("mapX") with true,value->value.GetString() | _->"translateX"
                    let struct(initialX,unitX)=DraggableTransformValue.scalar cache full xName
                    let struct(_,unitY)=DraggableTransformValue.scalar cache full "translateY"
                    Expect.equal initialX (fixture.GetProperty("initial").GetProperty("x").GetDouble()) "source scalar getter"
                    let updated=Dictionary<string,string>(cache)
                    updated[xName]<-"12"+unitX
                    updated["translateY"]<-"7"+unitY
                    let samples=fixture.GetProperty("samples")
                    Expect.equal (TransformCache.build updated) (samples[1].GetProperty("state").GetProperty("inline").GetString()) "source retarget serialization"
            testCase "source multiple-number transforms expose nonfinite getters" <| fun _ ->
                for name in ["multipleNumberCalc";"multipleNumberSkew"] do
                    let fixture=fixtures |> Array.find(fun f->f.GetProperty("name").GetString()=name)
                    let samples=fixture.GetProperty("samples")
                    Expect.isFalse (fixture.GetProperty("initial").GetProperty("xFinite").GetBoolean()) name
                    Expect.isFalse (samples[1].GetProperty("state").GetProperty("xFinite").GetBoolean()) name
            testCase "source single-number expressions are zeroed and ignore numeric retarget" <| fun _ ->
                for name in ["singleNumberCalc";"variableFallback"] do
                    let fixture=fixtures |> Array.find(fun f->f.GetProperty("name").GetString()=name)
                    let samples=fixture.GetProperty("samples")
                    Expect.equal (fixture.GetProperty("initial").GetProperty("x").GetDouble()) 0. name
                    Expect.equal (samples[1].GetProperty("state").GetProperty("x").GetDouble()) 0. name
            testCase "native scalar validation rejects all four source-broken shapes" <| fun _ ->
                for fixture in fixtures |> Array.filter(fun f->not(f.GetProperty("name").GetString().StartsWith("scalar",StringComparison.Ordinal))) do
                    let struct(cache,full)=TransformCache.parse(fixture.GetProperty("initialText").GetString())
                    let xName=match fixture.TryGetProperty("mapX") with true,value->value.GetString() | _->"translateX"
                    Expect.throwsT<NotSupportedException> (fun()->DraggableTransformValue.scalar cache full xName |> ignore) (fixture.GetProperty("name").GetString())
        ]
