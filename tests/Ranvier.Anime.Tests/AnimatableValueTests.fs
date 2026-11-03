module Ranvier.Anime.Tests.AnimatableValueTests

open System
open System.IO
open System.Text.Json
open System.Collections.Generic
open Expecto
open Ranvier.Anime
open Ranvier.Anime.ValueInterpolation

let private yes () = true
let private literal (value:JsonElement) =
    if value.ValueKind=JsonValueKind.Number then Number(value.GetDouble()) else Text(value.GetString())
let private optional name (value:JsonElement) =
    match value.TryGetProperty(name:string) with true,item -> Some item | _ -> None
let private close expected actual =
    expected=actual || abs(expected-actual)<=1e-12*max 1. (abs expected)
let private array (value:JsonElement) = value.EnumerateArray() |> Seq.map _.GetDouble() |> Seq.toArray
let private compareComponents expected actual =
    Expect.equal (Array.length actual) (Array.length expected) "component count"
    Array.iter2 (fun expected actual -> Expect.isTrue (close expected actual) $"expected {expected:R} actual {actual:R}") expected actual
let private fixtures () =
    let root=Environment.GetEnvironmentVariable "RANVIER_ANIME_REFERENCE_ROOT"
    let path=Path.Combine((if String.IsNullOrWhiteSpace root then Path.Combine(AppContext.BaseDirectory,"reference") else root),"animatable.json")
    use doc=JsonDocument.Parse(File.ReadAllText path)
    Expect.equal (doc.RootElement.GetProperty("upstreamCommit").GetString()) "2c9cf8ea00329f6768c7d7902252ed977d75ce42" "pinned source"
    doc.RootElement.GetProperty("fixtures").EnumerateArray() |> Seq.map _.Clone() |> Seq.toList

let private replay (fixture:JsonElement) =
    let entries=Dictionary<struct(string*int),AnimatableValue*(float -> float)>()
    let parameters=fixture.GetProperty("parameters")
    let originals=fixture.GetProperty("targets")
    let initial=fixture.GetProperty("initial")
    for property in initial.GetProperty("state").GetProperty("properties").EnumerateObject() do
        let parameter=parameters.GetProperty property.Name
        let unit=if parameter.ValueKind=JsonValueKind.Object then optional "unit" parameter |> Option.map _.GetString() else None
        let modifierName=
            if property.Name="x" && (optional "propertyModifier" fixture |> Option.isSome) then fixture.GetProperty("propertyModifier").GetString()
            else optional "modifier" fixture |> Option.map _.GetString() |> Option.defaultValue ""
        let modifier=match modifierName with "double" -> (fun value -> value*2.) | "plusOne" -> (fun value -> value+1.) | _ -> id
        for index in 0..originals.GetArrayLength()-1 do
            entries.Add(struct(property.Name,index),(AnimatableValue(literal(originals[index].GetProperty property.Name),unit,false,None),modifier))
    let verify (state:JsonElement) (writes:JsonElement) sampleAll animateWrites (retarget:(string*JsonElement) option) =
        for property in state.GetProperty("properties").EnumerateObject() do
            let tweens=property.Value.GetProperty("tweens")
            for index in 0..tweens.GetArrayLength()-1 do
                let kernel,modifier=entries[struct(property.Name,index)]
                let tween=tweens[index]
                let isRetarget=retarget |> Option.exists(fun (name,_) -> name=property.Name)
                let wrote=animateWrites && (writes.EnumerateArray() |> Seq.exists(fun write ->
                    write.GetProperty("property").GetString()=property.Name && write.GetProperty("target").GetInt32()=index))
                if isRetarget then
                    let _,destination=Option.get retarget
                    let accepted=
                        if destination.ValueKind=JsonValueKind.Array then kernel.RetargetComponents(array destination,modifier,yes)
                        else kernel.RetargetScalar(destination.GetDouble(),modifier,yes)
                    Expect.isTrue accepted "retarget remains current"
                if sampleAll || wrote || isRetarget then
                    let output=kernel.Sample(tween.GetProperty("easedProgress").GetDouble(),4,modifier,yes) |> Option.get
                    if wrote then
                        let targets=state.GetProperty("targets")
                        let target=targets[index].GetProperty(property.Name)
                        if target.ValueKind=JsonValueKind.Number then
                            Expect.isTrue (output.Scalar |> Option.exists(close(target.GetDouble()))) "source property scalar"
                        else Expect.equal output.Text (target.GetString()) "source property text"
                if kernel.Kind=ValueKind.Number || kernel.Kind=ValueKind.Unit then
                    Expect.isTrue (close (tween.GetProperty("number").GetDouble()) kernel.CachedScalar) "source modified scalar cache"
                compareComponents (array(tween.GetProperty("components"))) kernel.ComponentValues
                let value=property.Value.GetProperty("value")
                if index=0 then
                    if value.ValueKind=JsonValueKind.Array then compareComponents (array value) kernel.ComponentValues
                    else Expect.isTrue (kernel.ScalarValue(modifier,yes) |> Option.exists(close(value.GetDouble()))) "source scalar getter"
    verify (initial.GetProperty("state")) (initial.GetProperty("writes")) true true None
    for sample in fixture.GetProperty("samples").EnumerateArray() do
        let step=sample.GetProperty("step")
        let retarget=
            if step.GetProperty("op").GetString()="retarget" then Some(step.GetProperty("property").GetString(),step.GetProperty("value"))
            else None
        verify (sample.GetProperty("state")) (sample.GetProperty("writes")) false (step.GetProperty("op").GetString()<>"external") retarget

let tests = testList "animatable value cache" [
    for fixture in fixtures() do
        let name=fixture.GetProperty("name").GetString()
        if name<>"componentAlias" && name<>"noTargets" && name<>"emptyProperties" then
            testCase ("source "+name) <| fun _ -> replay fixture
    testCase "component getters and outputs cannot mutate cached state" <| fun _ ->
        let kernel=AnimatableValue(Text "v(10,20)",None,false,None)
        let output=kernel.Sample(0.,4,id,yes) |> Option.get
        let snapshot=kernel.ComponentValues
        snapshot[0]<-999.
        output.Components[1]<-999.
        compareComponents [|10.;20.|] kernel.ComponentValues
    testCase "cancelled component retarget discards staged endpoints" <| fun _ ->
        let kernel=AnimatableValue(Text "v(10,20)",None,false,None)
        kernel.Sample(0.,4,id,yes) |> ignore
        let mutable live=true
        let mutable calls=0
        let modifier value = calls<-calls+1; live<-false; value
        Expect.isFalse (kernel.RetargetComponents([|100.;200.|],modifier,(fun () -> live))) "retarget aborted"
        Expect.equal calls 1 "later modifiers suppressed"
        Expect.equal ((kernel.Sample(1.,4,id,yes) |> Option.get).Text) "v(0,0)" "original relative-zero endpoints retained"
    testCase "nested retarget supersedes outer retarget" <| fun _ ->
        let kernel=AnimatableValue(Number 10.,None,false,None)
        kernel.Sample(0.,4,id,yes) |> ignore
        let modifier value =
            Expect.isTrue (kernel.RetargetScalar(300.,id,yes)) "inner retarget"
            value
        Expect.isFalse (kernel.RetargetScalar(100.,modifier,yes)) "outer retarget discarded"
        Expect.equal ((kernel.Sample(1.,4,id,yes) |> Option.get).Scalar) (Some 300.) "inner destination survives"
    testCase "nested retarget supersedes outer sample cache" <| fun _ ->
        let kernel=AnimatableValue(Number 10.,None,false,None)
        kernel.RetargetScalar(100.,id,yes) |> ignore
        let modifier value = kernel.RetargetScalar(300.,id,yes) |> ignore; value
        Expect.isNone (kernel.Sample(0.5,4,modifier,yes)) "outer sample discarded"
        Expect.equal kernel.CachedScalar 10. "discarded sample cache"
        Expect.equal ((kernel.Sample(1.,4,id,yes) |> Option.get).Scalar) (Some 300.) "inner destination survives"
    testCase "inactive operations have no modifier effects" <| fun _ ->
        let kernel=AnimatableValue(Number 10.,None,false,None)
        let modifier _ = failtest "inactive modifier invoked"
        Expect.isFalse (kernel.RetargetScalar(100.,modifier,(fun () -> false))) "inactive retarget"
        Expect.isNone (kernel.Sample(0.5,4,modifier,(fun () -> false))) "inactive sample"
        Expect.isNone (kernel.ScalarValue(modifier,(fun () -> false))) "inactive getter"
    testCase "invalid destinations preserve endpoints before modifiers" <| fun _ ->
        let kernel=AnimatableValue(Text "v(10,20)",None,false,None)
        for value in [nan;infinity;-infinity] do
            Expect.throwsT<ArgumentException> (fun () -> kernel.RetargetComponents([|100.;value|],(fun _ -> failtest "invalid modifier invoked"),yes) |> ignore) "finite values"
        Expect.throwsT<ArgumentException> (fun () -> kernel.RetargetScalar(100.,id,yes) |> ignore) "component shape"
        Expect.throwsT<ArgumentNullException> (fun () -> kernel.RetargetComponents(null,id,yes) |> ignore) "null components"
        Expect.equal ((kernel.Sample(0.,4,id,yes) |> Option.get).Text) "v(10,20)" "invalid authoring retains start"
    testCase "unit normalization happens during capture" <| fun _ ->
        let mutable calls=0
        let convert value _ _ = calls<-calls+1; value*32.
        let kernel=AnimatableValue(Text "1em",Some "px",true,Some convert)
        Expect.equal calls 1 "construction conversion"
        kernel.Sample(0.,4,id,yes) |> ignore
        kernel.RetargetScalar(100.,id,yes) |> ignore
        Expect.equal ((kernel.Sample(0.5,4,id,yes) |> Option.get).Text) "66px" "normalized interpolation"
        Expect.equal calls 1 "cached normalized endpoints"
    testCase "modifier edits to the input array preserve captured destinations" <| fun _ ->
        let kernel=AnimatableValue(Text "v(10,20)",None,false,None)
        let destinations=[|100.;200.|]
        let modifier value = destinations[1]<-nan; value
        Expect.isTrue (kernel.RetargetComponents(destinations,modifier,yes)) "retarget captured input"
        Expect.equal ((kernel.Sample(1.,4,id,yes) |> Option.get).Text) "v(100,200)" "input changes cannot alter staged endpoints"
]
