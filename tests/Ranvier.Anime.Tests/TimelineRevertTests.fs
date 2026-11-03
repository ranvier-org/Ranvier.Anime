module Ranvier.Anime.Tests.TimelineRevertTests

open System
open System.IO
open System.Text.Json
open Expecto
open Ranvier
open Ranvier.Dom
open Ranvier.Anime

let private fixtures=
    let root=Environment.GetEnvironmentVariable("RANVIER_ANIME_REFERENCE_ROOT")
    let path=Path.Combine((if String.IsNullOrWhiteSpace root then Path.Combine(AppContext.BaseDirectory,"reference") else root),"timeline-revert.json")
    use document=JsonDocument.Parse(File.ReadAllText path)
    document.RootElement.GetProperty("fixtures").EnumerateArray() |> Seq.map(fun fixture -> fixture.Clone()) |> Seq.toArray

let private run (fixture:JsonElement)=
    let spec=fixture.GetProperty("spec")
    let name=spec.GetProperty("name").GetString()
    use scheduler=new Ranvier.Dom.ManualFrameScheduler()
    let mutable optionNode=Unchecked.defaultof<JsonElement>
    let options=if spec.TryGetProperty("options",&optionNode) then
                    let mutable v=Unchecked.defaultof<JsonElement>
                    {Ranvier.Anime.PlaybackOptions.Default with Reversed=optionNode.TryGetProperty("reversed",&v) && v.GetBoolean();Alternate=optionNode.TryGetProperty("alternate",&v) && v.GetBoolean();Repeat=if optionNode.TryGetProperty("loop",&v) then Ranvier.Anime.PlaybackRepeat.Count(v.GetUInt32()) else Ranvier.Anime.PlaybackRepeat.Count 0u}
                else Ranvier.Anime.PlaybackOptions.Default
    use root=new Timeline(scheduler,options=options)
    let mutable value=10.
    let writes=System.Collections.Generic.List<float>()
    let mutable reads=0
    let property=Ranvier.Anime.NumericProperty((fun () -> reads<-reads+1;value),(fun next -> value<-next;writes.Add next))
    let mutable borrowed:Ranvier.Anime.Timer option=None
    if name="borrowedTimer" then
        let timer=new Ranvier.Anime.Timer(scheduler,100u)
        borrowed<-Some timer
        root.Sync timer |> ignore
    for child in spec.GetProperty("children").EnumerateArray() do
        let x=child.GetProperty("x")
        let duration=child.GetProperty("duration").GetDouble()
        let mutable at=Unchecked.defaultof<JsonElement>
        let position=Ranvier.Anime.TimelinePosition.At(if child.TryGetProperty("at",&at) then at.GetDouble() else 0.)
        if x.ValueKind=JsonValueKind.Array && x.GetArrayLength()>2 then
            let frames=Ranvier.Anime.Keyframe.values(x.EnumerateArray() |> Seq.map(fun v -> v.GetDouble()) |> Seq.toArray)
            root.AddKeyframesPrecise(property,frames,duration,position=position,options={Ranvier.Anime.KeyframeAnimationOptions.Default with Ease=id;Precision=None}) |> ignore
        else
            let first,last=if x.ValueKind=JsonValueKind.Array then Some(x[0].GetDouble()),x[1].GetDouble() else None,x.GetDouble()
            root.Animate(property,last,duration,position=position,?from=first) |> ignore
    let constructionReads=reads
    if name<>"keyframesAndScalar" && name<>"setThenKeyframes" then
        let expectedReads=fixture.GetProperty("construction").EnumerateArray() |> Seq.filter(fun event -> event[0].GetString()="get") |> Seq.length
        Expect.equal constructionReads expectedReads "scalar originals are captured once, including explicit sources"
    root.Seek 75.
    writes.Clear()
    root.Revert()
    let expected=fixture.GetProperty("first").GetProperty("trace").EnumerateArray() |> Seq.filter(fun x -> x[0].GetString()="set") |> Seq.map(fun x -> x[1].GetDouble()) |> Seq.toArray
    let actual=writes.ToArray()
    let result=name,value=10. && root.CurrentTime=0. && root.Cancelled,actual=expected,constructionReads,expected,actual
    borrowed |> Option.iter(fun timer ->
        Expect.equal timer.CurrentTime 0. "borrowed timer rewinds"
        Expect.isFalse timer.Cancelled "borrowed timer retains its lifetime"
        timer.Cancel())
    result

let sourceTests=testList "timeline revert source" [
    for fixture in fixtures do
        let name=fixture.GetProperty("spec").GetProperty("name").GetString()
        testCase name <| fun _ ->
            let _,state,trace,_,expected,actual=run fixture
            Expect.isTrue state "original values and terminal root state"
            Expect.isTrue trace (sprintf "source restoration writes: expected%A, actual%A" expected actual)
]

let guardTests=testList "timeline revert lifetime" [
    testCase "cancel closes restoration and repeated revert writes nothing" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        use root=new Timeline(scheduler)
        let mutable value=10.
        let writes=Collections.Generic.List<float>()
        let property=NumericProperty((fun () -> value),(fun next -> value<-next;writes.Add next))
        root.Animate(property,100.,100u,from=0.) |> ignore
        root.Seek 50.
        root.Cancel()
        writes.Clear()
        root.Revert()
        root.Revert()
        Expect.equal (value,writes.Count) (50.,0) "cancel retains rendered value"
    testCase "restore faults retain first identity and attempt other property restoration" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        use root=new Timeline(scheduler)
        let fault=Exception("restore fault")
        let mutable a,b,armed=10.,20.,false
        let first=NumericProperty((fun () -> a),(fun value -> a<-value))
        let last=NumericProperty((fun () -> b),(fun value -> if armed && value=20. then raise fault else b<-value))
        root.Animate(first,100.,100u,from=0.) |> ignore
        root.Animate(last,200.,100u,from=0.,position=TimelinePosition.At 0.) |> ignore
        root.Seek 50.
        armed<-true
        let mutable observed:exn option=None
        try root.Revert() with error -> observed<-Some error
        Expect.isTrue (observed |> Option.exists(fun error -> obj.ReferenceEquals(fault,error))) "original failure identity"
        Expect.equal a 10. "other property still restored"
        Expect.isTrue root.Cancelled "root cleanup completes"
    testCase "cancellation in rewind suppresses every later restoration write" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        use root=new Timeline(scheduler)
        let mutable a,b,armed=10.,20.,false
        let writes=Collections.Generic.List<string>()
        let first=NumericProperty((fun () -> a),(fun value -> a<-value;writes.Add "a"))
        let last=NumericProperty((fun () -> b),(fun value -> b<-value;writes.Add "b";if armed then root.Cancel()))
        root.Animate(first,100.,100u,from=0.) |> ignore
        root.Animate(last,200.,100u,from=0.,position=TimelinePosition.At 0.) |> ignore
        root.Seek 50.
        armed<-true
        writes.Clear()
        root.Revert()
        Expect.equal (writes |> Seq.toList) ["b"] "ancestor cancellation ends all later effects"
    testCase "reentrant revert from original setter is harmless" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        use root=new Timeline(scheduler)
        let mutable value,armed=10.,false
        let property=NumericProperty((fun () -> value),(fun next -> value<-next;if armed && next=10. then root.Revert()))
        root.Animate(property,100.,100u,from=0.) |> ignore
        root.Seek 50.
        armed<-true
        root.Revert()
        Expect.equal value 10. "single original restoration"
        Expect.isTrue root.Cancelled "terminal root"
    testCase "owner disposal from a restore setter stops later property restoration" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        use graph=new Graph()
        use activation=graph.Activate()
        let owner=Api.createRoot id
        use root=new Timeline(scheduler)
        owner.Attach root
        let mutable a,b,armed=10.,20.,false
        let writes=Collections.Generic.List<string*float>()
        let first=NumericProperty((fun () -> a),(fun value -> a<-value;writes.Add("a",value)))
        let last=NumericProperty((fun () -> b),(fun value ->
            b<-value
            writes.Add("b",value)
            if armed && value=20. then owner.Dispose()))
        root.Animate(first,100.,100u,from=0.) |> ignore
        root.Animate(last,200.,100u,from=0.,position=TimelinePosition.At 0.) |> ignore
        root.Seek 50.
        writes.Clear()
        armed<-true
        root.Revert()
        Expect.isFalse (writes.Contains("a",10.)) "no original write follows actual owner disposal"
        Expect.equal (writes |> Seq.last) ("b",20.) "disposing setter is the last write"
        Expect.isTrue root.Cancelled "owner cancellation closes every child"
        Expect.equal owner.ErrorCount 0 "actual cleanup succeeds"
    testCase "a control call during restoration cannot revive the cancelled clock" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        use root=new Timeline(scheduler)
        let mutable a,b,armed=10.,20.,false
        let writes=Collections.Generic.List<string*float>()
        let first=NumericProperty((fun () -> a),(fun value -> a<-value;writes.Add("a",value)))
        let last=NumericProperty((fun () -> b),(fun value ->
            b<-value
            writes.Add("b",value)
            if armed && value=20. then root.Seek 25.))
        root.Animate(first,100.,100u,from=0.) |> ignore
        root.Animate(last,200.,100u,from=0.,position=TimelinePosition.At 0.) |> ignore
        root.Seek 50.
        writes.Clear()
        armed<-true
        Expect.throwsT<ObjectDisposedException> root.Revert "native cancellation remains terminal"
        Expect.isFalse (writes.Contains("a",10.)) "superseding generation stops later restoration"
        Expect.isTrue root.Cancelled "cleanup completes after the control failure"
]

let restorationTests=testList "timeline restoration" [sourceTests;guardTests]
