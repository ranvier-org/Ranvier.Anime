namespace Ranvier.Anime.Tests

open System
open System.Text.Json
open Expecto
open Ranvier.Anime

module DragReference =
    let readNumber (e:JsonElement) (name:string)=e.GetProperty(name).GetDouble()
    let compareFixture (velocity:DragVelocity ref) (fixture:JsonElement) =
        let input=fixture.GetProperty "input"
        let output=fixture.GetProperty "output"
        let family=fixture.GetProperty("family").GetString()
        let number=readNumber input
        let equal a b=abs(a-b)<1e-9
        match family with
        | "velocity" ->
            if number "time"=16. then velocity.Value<-DragVelocity(0.)
            equal (velocity.Value.Sample(number "time",number "dx",number "dy",number "multiplier",number "min",number "max")) (readNumber output "velocity")
        | "bounds" ->
            let values=input.GetProperty("bounds").EnumerateArray() |> Seq.map _.GetDouble() |> Seq.toArray
            let disabled=input.GetProperty("disabled").EnumerateArray() |> Seq.map(fun x -> x.GetInt32()=1) |> Seq.toArray
            let bounds:DragBounds={Top=values[0];Right=values[1];Bottom=values[2];Left=values[3]}
            DragPhysics.outOfBounds true disabled[0] disabled[1] bounds (number "x") (number "y")=output.GetInt32()
        | "threshold" ->
            let disabled=input.GetProperty("disabled").EnumerateArray() |> Seq.map(fun x -> x.GetInt32()=1) |> Seq.toArray
            DragPhysics.threshold disabled[0] disabled[1] (number "threshold") (number "x") (number "y")=output.GetProperty("dragged").GetBoolean()
        | "release" ->
            let snap (name:string) =
                let value=input.GetProperty name
                if value.ValueKind=JsonValueKind.Array then DragSnap.Points(value.EnumerateArray() |> Seq.map _.GetDouble() |> Seq.toList)
                else DragSnap.Step(value.GetDouble())
            let displacement=number "velocity"*150.
            let struct(x,_)=DragPhysics.releaseAxis 0. 100. (number "friction") 1. (snap "snapX") (number "x") (Math.Cos(number "angle")*displacement)
            let struct(y,_)=DragPhysics.releaseAxis 0. 100. (number "friction") 1. (snap "snapY") (number "y") (Math.Sin(number "angle")*displacement)
            equal x (readNumber output "destX") && equal y (readNumber output "destY")
        | _ -> false

    let physicsProjectionTests fixturePath =
        testList "Draggable source physics" [
            testCase "66 clean pinned source physics projections match" <| fun _ ->
                use document=JsonDocument.Parse(System.IO.File.ReadAllText fixturePath)
                let fixtures=document.RootElement.GetProperty("fixtures")
                Expect.equal (fixtures.GetArrayLength()) 66 "Fixture coverage changed"
                let velocity=ref(DragVelocity(0.))
                for fixture in fixtures.EnumerateArray() do
                    Expect.isTrue (compareFixture velocity fixture) (fixture.ToString())
        ]

