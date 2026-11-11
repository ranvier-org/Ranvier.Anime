namespace Ranvier.Anime.Tests

open System.Collections.Generic
open System.Text.Json
open Expecto
open Ranvier.Anime

module TransformCacheTests =
    let transformCacheTests fixturePath =
        testCase "15 source transform caches, reads and serialized mutations match" <| fun _ ->
            use document=JsonDocument.Parse(System.IO.File.ReadAllText fixturePath)
            let fixtures=document.RootElement.GetProperty "fixtures"
            Expect.equal (fixtures.GetArrayLength()) 15 "Source fixture coverage"
            for fixture in fixtures.EnumerateArray() do
                let struct(cache,full)=TransformCache.parse(fixture.GetProperty("inline").GetString())
                let expected=fixture.GetProperty "parsed"
                Expect.equal cache.Count (expected.EnumerateObject() |> Seq.length) "Cache size"
                for property in expected.EnumerateObject() do
                    Expect.equal cache[property.Name] (property.Value.GetString()) property.Name
                for property in fixture.GetProperty("reads").EnumerateObject() do
                    Expect.equal (TransformCache.read cache full property.Name) (property.Value.GetString()) property.Name
                Expect.equal (TransformCache.build cache) (fixture.GetProperty("built").GetString()) "Canonical function order"
                let moved=Dictionary<string,string>(cache)
                moved["translateX"]<-"7px"
                moved["translateY"]<-"9px"
                Expect.equal (TransformCache.build moved) (fixture.GetProperty("moved").GetString()) "Source axis mutation"
