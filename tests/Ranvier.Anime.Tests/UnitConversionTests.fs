module Ranvier.Anime.Tests.UnitConversionTests

open System
open System.IO
open System.Text.Json
open Expecto
open Ranvier.Dom
open Ranvier.Css
open Ranvier.Anime

/// <summary>Dispatches only the DOM members exercised by unit-probe tests.</summary>
type UnitProxy() =
    inherit Reflection.DispatchProxy()
    let mutable handler:Reflection.MethodInfo -> obj array -> obj = fun _ _ -> failwith "Unconfigured proxy"
    /// <summary>Supplies the fixture's borrowed DOM behavior.</summary>
    member _.Handler with set value=handler<-value
    override _.Invoke(methodInfo,args)=handler methodInfo args

let private proxy<'T> handler =
    let value=Reflection.DispatchProxy.Create<'T,UnitProxy>()
    (box value:?>UnitProxy).Handler<-handler
    value

type private Probe =
    { Element:HTMLElement; Style:CSSStyleDeclaration; Writes:ResizeArray<string>
      Events:ResizeArray<string>; Attached:unit -> bool
      Widths:int array; FailAt:int ref; CleanupError:exn option ref }

let private probe fromUnit (widths:int array) parentMode =
    let events=ResizeArray<string>()
    let failAt=ref 0
    let cleanupError=ref None
    let mutable currentParent:Node=Unchecked.defaultof<_>
    let mutable width=""
    let mutable reads=0
    let mutable clone:HTMLElement=Unchecked.defaultof<_>
    let originalValues=Collections.Generic.Dictionary<string,string*string>()
    let originalWrites=ResizeArray<string>()
    let originalStyle=proxy<CSSStyleProperties>(fun methodInfo args ->
        let name=unbox<string> args[0]
        match methodInfo.Name with
        | "getPropertyValue" -> box(match originalValues.TryGetValue name with true,(value,_) -> value | _ -> "")
        | "getPropertyPriority" -> box(match originalValues.TryGetValue name with true,(_,priority) -> priority | _ -> "")
        | "setProperty" ->
            let value=unbox<string> args[1]
            let priority=unbox<string option> args[2] |> Option.defaultValue ""
            originalValues[name]<-value,priority
            originalWrites.Add value
            null
        | memberName -> failwith ("Unexpected original style member "+memberName))
    let mutable parent:HTMLElement=Unchecked.defaultof<_>
    let mutable body:HTMLElement=Unchecked.defaultof<_>
    let createContainer label =
        proxy<HTMLElement>(fun methodInfo args ->
            match methodInfo.Name with
            | "get_nodeType" -> box 1us
            | "appendChild" ->
                events.Add("append:"+label)
                currentParent<-(if label="body" then body:>Node else parent:>Node)
                args[0]
            | "removeChild" ->
                events.Add("remove:"+label)
                currentParent<-Unchecked.defaultof<_>
                match cleanupError.Value with Some error -> raise error | None -> args[0]
            | name -> failwith ("Unexpected container member "+name))
    parent<-createContainer "parent"
    body<-createContainer "body"
    let document=proxy<Document>(fun methodInfo _ ->
        match methodInfo.Name with "get_body" -> box body | "get_nodeType" -> box 9us | name -> failwith ("Unexpected document member "+name))
    let style=proxy<CSSStyleProperties>(fun methodInfo args ->
        match methodInfo.Name with
        | "setProperty" ->
            if unbox<string> args[0]<>"width" then failwith "Only probe width may change"
            width<-unbox args[1]
            events.Add("width:"+width)
            null
        | name -> failwith ("Unexpected probe style member "+name))
    clone<-proxy<HTMLElement>(fun methodInfo _ ->
        match methodInfo.Name with
        | "get_style" -> box style
        | "get_parentNode" -> box currentParent
        | "get_offsetWidth" ->
            reads<-reads+1
            events.Add("measure:"+width)
            if reads=failAt.Value then raise(InvalidOperationException "measurement failure")
            box widths[if width="100"+fromUnit then 0 else 1]
        | name -> failwith ("Unexpected clone member "+name))
    let target=proxy<HTMLElement>(fun methodInfo args ->
        match methodInfo.Name with
        | "get_ownerDocument" -> box document
        | "get_style" -> box originalStyle
        | "get_parentNode" -> if parentMode="document" then box document elif parentMode="none" then null else box parent
        | "cloneNode" ->
            Expect.isNull args[0] "shallow clone requested"
            events.Add "clone"
            box clone
        | name -> failwith ("Unexpected original target mutation/access "+name))
    { Element=target;Style=originalStyle;Writes=originalWrites;Events=events;Attached=(fun () -> not(isNull(box currentParent)))
      Widths=widths;FailAt=failAt;CleanupError=cleanupError }

let private cases section =
    let root=Environment.GetEnvironmentVariable "RANVIER_ANIME_REFERENCE_ROOT"
    let path=Path.Combine((if String.IsNullOrWhiteSpace root then Path.Combine(AppContext.BaseDirectory,"reference") else root),"unit-conversion.json")
    use document=JsonDocument.Parse(File.ReadAllText path)
    if document.RootElement.GetProperty("upstreamCommit").GetString()<>"2c9cf8ea00329f6768c7d7902252ed977d75ce42" then failwith "Unexpected unit oracle revision."
    document.RootElement.GetProperty(section:string).EnumerateArray() |> Seq.map _.Clone() |> Seq.toList

let tests = testList "contextual unit conversion" [
    for entry in cases "cases" do
        testCase (entry.GetProperty("name").GetString()) <| fun _ ->
            let widths=entry.GetProperty("widths").EnumerateArray() |> Seq.map _.GetInt32() |> Seq.toArray
            let parentMode=match entry.TryGetProperty "parent" with true,value -> value.GetString() | _ -> "parent"
            let fixture=probe (entry.GetProperty("from").GetString()) widths parentMode
            let actual=CssUnits.convert fixture.Element (entry.GetProperty("value").GetDouble()) (entry.GetProperty("from").GetString()) (entry.GetProperty("to").GetString())
            let expected=entry.GetProperty("result").GetDouble()
            Expect.isTrue (abs(actual-expected)<=1e-12*max 1. (abs expected)) "source conversion"
            Expect.sequenceEqual fixture.Events (entry.GetProperty("events").EnumerateArray() |> Seq.map _.GetString()) "source clone/measurement/parent order"
            Expect.isFalse (fixture.Attached()) "temporary clone removed"
    testCase "construction contexts do not share the source global cache" <| fun _ ->
        let entries=cases "cacheReplay"
        let first=entries[0]
        let second=entries[1]
        Expect.equal (first.GetProperty("result").GetDouble()) (second.GetProperty("result").GetDouble()) "recorded source cache collision"
        Expect.equal (second.GetProperty("events").GetArrayLength()) 0 "source skips the second context"
        let firstProbe=probe "em" [|1600;100|] "parent"
        let secondProbe=probe "em" [|3200;100|] "parent"
        Expect.equal (CssUnits.convert firstProbe.Element 17. "em" "px") 272. "first context"
        Expect.equal (CssUnits.convert secondProbe.Element 17. "em" "px") 544. "second context is measured independently"
        Expect.equal secondProbe.Events.Count 7 "second context performs two measurements"
    testCase "measurement faults remove probes" <| fun _ ->
        for read in 1..2 do
            let fixture=probe "em" [|1600;100|] "parent"
            fixture.FailAt.Value<-read
            Expect.throwsT<InvalidOperationException> (fun () -> CssUnits.convert fixture.Element 2. "em" "px" |> ignore) "measurement fault"
            Expect.isFalse (fixture.Attached()) "failed probe removed"
            Expect.equal fixture.Events[fixture.Events.Count-1] "remove:parent" "cleanup follows fault"
    testCase "cleanup faults preserve an active measurement error" <| fun _ ->
        let fixture=probe "em" [|1600;100|] "parent"
        fixture.FailAt.Value<-1
        fixture.CleanupError.Value<-Some(ArgumentException "cleanup failure")
        let mutable failure:exn option=None
        try CssUnits.convert fixture.Element 2. "em" "px" |> ignore with error -> failure<-Some error
        Expect.isTrue (failure |> Option.exists(fun error -> error.Message="measurement failure")) "primary fault retained"
    testCase "cross-dimension angle conversions reject before DOM access" <| fun _ ->
        let fixture=probe "deg" [|100;100|] "parent"
        for first,last in ["deg","px";"em","rad"] do
            Expect.throwsT<ArgumentException> (fun () -> CssUnits.convert fixture.Element 2. first last |> ignore) "incompatible dimensions"
        Expect.isEmpty fixture.Events "no probe"
    testCase "nonfinite values reject before DOM access" <| fun _ ->
        let fixture=probe "em" [|1600;100|] "parent"
        for value in [nan;infinity;-infinity] do
            Expect.throwsT<ArgumentException> (fun () -> CssUnits.convert fixture.Element value "em" "px" |> ignore) "finite input"
        Expect.isEmpty fixture.Events "invalid input does not create a probe"
    for entry in cases "animationCases" do
        testCase (entry.GetProperty("name").GetString()) <| fun _ ->
            let firstText=entry.GetProperty("first").GetString()
            let lastText=entry.GetProperty("last").GetString()
            let first=ValueInterpolation.decompose(ValueInterpolation.Text firstText)
            let last=ValueInterpolation.decompose(ValueInterpolation.Text lastText)
            let fromUnit,targetUnit=if not(String.IsNullOrEmpty last.Unit) then first.Unit,last.Unit else last.Unit,first.Unit
            let width = function "em" -> 1600 | "rem" -> 2400 | "%" -> 400 | "px" -> 100 | _ -> 0
            let fixture=probe (if isNull fromUnit then "null" else fromUnit) [|width fromUnit;width targetUnit|] "parent"
            fixture.Style.setProperty("width",entry.GetProperty("base").GetString())
            use scheduler=new ManualFrameScheduler()
            use clock=CssAnimation.declarationOn scheduler fixture.Element (Declaration.create "width" firstText) (Declaration.create "width" lastText) 100u
            for sample in entry.GetProperty("samples").EnumerateArray() do
                clock.Seek(sample.GetProperty("time").GetDouble())
                Expect.equal (fixture.Style.getPropertyValue "width") (sample.GetProperty("value").GetString()) "source normalized playback"
            Expect.isFalse (fixture.Attached()) "no retained measurement element"
    testCase "normalized endpoints do not remeasure during playback" <| fun _ ->
        let fixture=probe "em" [|1600;100|] "parent"
        use scheduler=new ManualFrameScheduler()
        use clock=CssAnimation.declarationOn scheduler fixture.Element (Css.width(Css.em 1.)) (Css.width(Css.px 100.)) 100u
        Expect.equal (fixture.Style.getPropertyValue "width") "16px" "initial converted endpoint"
        let events=fixture.Events.Count
        fixture.Widths[0]<-3200
        scheduler.Advance(50.-12.)
        scheduler.RunFrame()
        Expect.equal (fixture.Style.getPropertyValue "width") "58px" "captured endpoint"
        Expect.equal fixture.Events.Count events "no per-frame measurements"
        Expect.isFalse (fixture.Attached()) "probe removed before factory returns"
    testCase "invalid authoring options reject before measuring" <| fun _ ->
        let fixture=probe "em" [|1600;100|] "parent"
        use scheduler=new ManualFrameScheduler()
        let first=Css.width(Css.em 1.)
        let last=Css.width(Css.px 100.)
        Expect.throwsT<ArgumentException>
            (fun () -> CssAnimation.declarationOnPrecise scheduler fixture.Element first last 1e-14 |> ignore) "precision-invalid duration"
        let settings={DeclarationAnimationOptions.Default with Precision=Some 309u}
        Expect.throwsT<ArgumentException>
            (fun () -> CssAnimation.declarationOnWith settings scheduler fixture.Element first last 100u |> ignore) "invalid precision"
        Expect.isEmpty fixture.Events "invalid setup never clones"
        Expect.isEmpty fixture.Writes "invalid setup never writes"
]
