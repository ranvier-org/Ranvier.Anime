module Ranvier.Anime.Tests.FrameSiblingTests

open System
open System.IO
open System.Text.Json
open Expecto
open Ranvier.Anime
open Ranvier.Dom

let private values input=KeyframeCompilation.values(input |> Array.map ValueInterpolation.Number)
let private prepare (registry:FrameSiblingRegistry) (keys:struct(obj*string) array) offset duration (frames:FrameInput array) resolve current =
    let struct(_,name)=keys[0]
    let property={FrameProperty.Target=0;Name=name;Original=ValueInterpolation.Number 10.;Frames=frames;ConvertUnit=None}
    let transaction=registry.Begin(keys,current)
    let program=KeyframeCompilation.compileWithSiblings({FrameDefaults.Default with Duration=duration;Offset=offset;Ease=id},[|property|],resolve,
                                                       (fun _ -> ValueInterpolation.Number 10.),transaction.Compilation,current)
    program,transaction
let private publish registry keys offset frames =
    let program,transaction=prepare registry keys offset 200. frames (fun _ index _ -> Some frames[index]) (fun () -> true)
    let program=Option.get program
    let nodes=transaction.Bind(program,1.,program.IterationDuration)
    Expect.isTrue (transaction.Commit()) "initial sibling commit"
    program,nodes
let private scene() =
    let registry=FrameSiblingRegistry()
    let keys=[|struct(obj(),"x")|]
    let _,nodes=publish registry keys 0. (values [|0.;100.;200.|])
    nodes[0].Value<-ValueInterpolation.compose(nodes[0].Definition.Last,75.,[||])
    nodes[0].Time<-75.
    registry,keys,nodes
let private source name =
    let root=Environment.GetEnvironmentVariable "RANVIER_ANIME_REFERENCE_ROOT"
    let root=if String.IsNullOrWhiteSpace root then Path.Combine(AppContext.BaseDirectory,"reference") else root
    use document=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"timeline-keyframes.json")))
    Expect.equal (document.RootElement.GetProperty("upstreamCommit").GetString()) "2c9cf8ea00329f6768c7d7902252ed977d75ce42" "pinned source"
    document.RootElement.GetProperty("fixtures").EnumerateArray()
    |> Seq.find(fun fixture -> fixture.GetProperty("name").GetString()=name) |> _.Clone()
let private sourceGeometry (nodes:FrameSibling array) (expected:JsonElement) =
    Expect.equal nodes.Length (expected.GetArrayLength()) "source tween count"
    for index in 0..nodes.Length-1 do
        let node=nodes[index]
        let frame=node.Definition
        for name,actual in ["start",node.Start;"delay",node.Delay;"updateDuration",node.Update;"changeDuration",node.Change
                            "absoluteStart",node.AbsoluteStart;"absoluteUpdateStart",node.AbsoluteUpdateStart;"absoluteEnd",node.AbsoluteEnd
                            "from",frame.First.Number;"to",frame.Last.Number] do
            Expect.equal actual (expected[index].GetProperty(name).GetDouble()) ("source "+name)

let tests=testList "same-parent keyframe sibling registry" [
    testCase "explicit pairs preserve each raw getter effect without parsing an unused original" <| fun _ ->
        let registry,keys,_=scene()
        let transaction=registry.Begin(keys,(fun () -> true))
        let frames=[|{FrameInput.Default with From=Some(ValueInterpolation.Number 1.);To=Some(ValueInterpolation.Number 2.)}
                     {FrameInput.Default with From=Some(ValueInterpolation.Text "v(3,4)");To=Some(ValueInterpolation.Text "v(5,6)")} |]
        let property={FrameProperty.Target=0;Name="x";Original=ValueInterpolation.Number 10.;Frames=frames;ConvertUnit=None}
        let mutable reads=0
        let program=KeyframeCompilation.compileWithSiblings({FrameDefaults.Default with Duration=100.;Offset=75.;Ease=id},[|property|],
            (fun _ index _ -> Some frames[index]),(fun _ -> reads<-reads+1;ValueInterpolation.Text "rgb(invalid)"),
            transaction.Compilation,(fun () -> true))
        transaction.Rollback()
        Expect.isSome program "unused malformed original is not decomposed"
        Expect.equal reads 2 "every explicit pair invokes its raw getter"

    testCase "first explicit numeric pair reads once and inherits original units" <| fun _ ->
        let registry=FrameSiblingRegistry()
        let transaction=registry.Begin([|struct(obj(),"x")|],(fun () -> true))
        let frames=values [|0.;100.|]
        let property={FrameProperty.Target=0;Name="x";Original=ValueInterpolation.Number 10.;Frames=frames;ConvertUnit=None}
        let mutable reads=0
        let program=KeyframeCompilation.compileWithSiblings({FrameDefaults.Default with Duration=100.;Ease=id},[|property|],
            (fun _ index _ -> Some frames[index]),(fun _ -> reads<-reads+1;ValueInterpolation.Text "10px"),
            transaction.Compilation,(fun () -> true)) |> Option.get
        transaction.Rollback()
        Expect.equal reads 1 "raw original reused for required decomposition"
        Expect.equal (program.Tweens[0].First.Unit,program.Tweens[0].Last.Unit) ("px","px") "numeric pair unit inheritance"

    testCase "explicit original getter faults retain identity and cannot publish overlap" <| fun _ ->
        let registry,keys,a=scene()
        let transaction=registry.Begin(keys,(fun () -> true))
        let frames=values [|0.;100.|]
        let property={FrameProperty.Target=0;Name="x";Original=ValueInterpolation.Number 10.;Frames=frames;ConvertUnit=None}
        let fault=InvalidOperationException("explicit original getter")
        let mutable actual:exn option=None
        try
            KeyframeCompilation.compileWithSiblings({FrameDefaults.Default with Duration=100.;Offset=75.;Ease=id},[|property|],
                (fun _ index _ -> Some frames[index]),(fun _ -> raise fault),transaction.Compilation,(fun () -> true)) |> ignore
        with error -> actual<-Some error
        transaction.Rollback()
        Expect.isTrue (actual |> Option.exists(fun error -> obj.ReferenceEquals(error,fault))) "original getter fault retained"
        Expect.equal (a[0].Change,a[1].Overridden) (100.,false) "failed getter leaves live geometry intact"

    testCase "explicit getter cancellation prevents conversion modification and publication" <| fun _ ->
        let registry,keys,a=scene()
        let mutable live=true
        let transaction=registry.Begin(keys,(fun () -> live))
        let mutable conversions=0
        let mutable modifications=0
        let frame={FrameInput.Default with From=Some(ValueInterpolation.Text "0px");To=Some(ValueInterpolation.Text "1em");
                                          Modifier=Some(fun value -> modifications<-modifications+1;value)}
        let property={FrameProperty.Target=0;Name="x";Original=ValueInterpolation.Number 10.;Frames=[|frame|]
                      ConvertUnit=Some(fun value _ _ -> conversions<-conversions+1;value)}
        let program=KeyframeCompilation.compileWithSiblings({FrameDefaults.Default with Duration=100.;Offset=75.;Ease=id},[|property|],
            (fun _ _ _ -> Some frame),(fun _ -> live<-false;ValueInterpolation.Number 10.),transaction.Compilation,(fun () -> live))
        Expect.isNone program "getter superseded construction"
        Expect.equal (conversions,modifications) (0,0) "no callbacks after cancellation"
        Expect.isFalse (transaction.Commit()) "cancelled construction cannot publish"
        Expect.equal (a[0].Change,a[1].Overridden) (100.,false) "live siblings survive"

    testCase "missing endpoints use the selected cache without reading original" <| fun _ ->
        let registry,keys,_=scene()
        let transaction=registry.Begin(keys,(fun () -> true))
        let frame={FrameInput.Default with To=Some(ValueInterpolation.Number 300.)}
        let property={FrameProperty.Target=0;Name="x";Original=ValueInterpolation.Number 10.;Frames=[|frame|];ConvertUnit=None}
        let program=KeyframeCompilation.compileWithSiblings({FrameDefaults.Default with Duration=100.;Offset=75.;Ease=id},[|property|],
            (fun _ _ _ -> Some frame),(fun _ -> failtest "selected cache replaces original read"),
            transaction.Compilation,(fun () -> true)) |> Option.get
        transaction.Rollback()
        Expect.equal program.Tweens[0].First.Number 75. "sampled predecessor cache"

    for stretch in [false;true] do
        testCase (if stretch then "direct child stretch invalidates staged geometry" else "direct child seek invalidates staged cache") <| fun _ ->
            use scheduler=new ManualFrameScheduler()
            let registry=FrameSiblingRegistry()
            let keys=[|struct(obj(),"x")|]
            let frames=values [|0.;100.|]
            let program,first=prepare registry keys 0. 100. frames (fun _ index _ -> Some frames[index]) (fun () -> true)
            let program=program.Value
            let nodes=first.Bind(program,1.,100.)
            use driver=new KeyframePlayback(scheduler,program,(fun _ _ _ -> ()),true,false,-1,PlaybackOptions.Default,siblings=nodes)
            driver.Initialize(timelineChild=true)
            Expect.isTrue (first.Commit()) "initial child publication"
            let transaction=registry.Begin(keys,(fun () -> true))
            if stretch then driver.Clock.Stretch(200.) else driver.Clock.Seek(50.,muteCallbacks=true)
            Expect.isFalse (transaction.Commit()) "direct child operation supersedes captured state"
            if stretch then Expect.equal nodes[0].Update 200. "stretched geometry retained"
            else Expect.equal driver.CurrentValues[0].Scalar (Some 50.) "sought cache retained"

    testCase "callback tails sampled starts raw local chains and final geometry match source" <| fun _ ->
        let registry,keys,a=scene()
        let calls=ResizeArray<float>()
        let frames=[|FrameInput.Default;FrameInput.Default|]
        let resolve _ index (previous:FrameTween option) =
            let value=previous.Value.CachedEnd.Scalar.Value
            calls.Add value
            Some {FrameInput.Default with To=Some(ValueInterpolation.Number(value+(if index=0 then 10. else 20.)))}
        let program,transaction=prepare registry keys 75. 200. frames resolve (fun () -> true)
        let program=Option.get program
        let b=transaction.Bind(program,1.,program.IterationDuration)
        Expect.equal (a[0].Change,a[1].Overridden) (100.,false) "uncommitted overlap cannot alter live playback"
        Expect.isTrue (transaction.Commit()) "publish captured program"
        let fixture=source "replace-overlapInputTail"
        let expected=fixture.GetProperty("construction").[1].GetProperty("effects").EnumerateArray()
                     |> Seq.filter(fun effect -> effect.GetProperty("id").GetString()="b")
                     |> Seq.map(fun effect -> effect.GetProperty("previous").GetDouble())
        Expect.sequenceEqual calls expected "input tail differs from overlap-selected predecessor"
        sourceGeometry a (fixture.GetProperty("programs").GetProperty("a"))
        sourceGeometry b (fixture.GetProperty("programs").GetProperty("b"))
        Expect.isTrue (obj.ReferenceEquals(a[0].Next,b[0]) && obj.ReferenceEquals(b[1].Next,a[1])) "links cross driver arrays"

    testCase "relative first source uses raw predecessor destination not its sampled cache" <| fun _ ->
        let registry,keys,_=scene()
        let frames=[|{FrameInput.Default with From=Some(ValueInterpolation.Text "+=5");To=Some(ValueInterpolation.Number 300.)}|]
        let program,transaction=prepare registry keys 75. 100. frames (fun _ index _ -> Some frames[index]) (fun () -> true)
        let program=Option.get program
        let b=transaction.Bind(program,1.,program.IterationDuration)
        Expect.isTrue (transaction.Commit()) "publish relative frame"
        sourceGeometry b ((source "replace-relativeSiblingSource").GetProperty("programs").GetProperty("b"))

    testCase "discarded overlap leaves existing geometry links and caches untouched" <| fun _ ->
        let registry,keys,a=scene()
        let previous=a[0].Next
        let transaction=registry.Begin(keys,(fun () -> true))
        transaction.Select(0,25.) |> ignore
        transaction.Rollback()
        Expect.equal (a[0].Change,a[0].Value.Scalar,a[1].Overridden) (100.,Some 75.,false) "rollback isolation"
        Expect.isTrue (obj.ReferenceEquals(a[0].Next,previous)) "original live links retained"

    testCase "a false construction guard refuses publication without truncation" <| fun _ ->
        let registry,keys,a=scene()
        let mutable live=true
        let frames=values [|300.|]
        let program,transaction=prepare registry keys 75. 100. frames (fun _ index _ -> Some frames[index]) (fun () -> live)
        let program=Option.get program
        transaction.Bind(program,1.,program.IterationDuration) |> ignore
        live<-false
        Expect.isFalse (transaction.Commit()) "interrupted generation"
        Expect.equal (a[0].Change,a[1].Overridden) (100.,false) "no partial publication"

    testCase "a stale registry generation cannot publish over a newer child" <| fun _ ->
        let registry,keys,a=scene()
        let frames=values [|300.|]
        let program,stale=prepare registry keys 75. 100. frames (fun _ index _ -> Some frames[index]) (fun () -> true)
        stale.Bind(program.Value,1.,100.) |> ignore
        let _,b=publish registry keys 250. (values [|400.|])
        Expect.isFalse (stale.Commit()) "new child superseded captured registry"
        Expect.isTrue (obj.ReferenceEquals(a[1].Next,b[0])) "newer graph survives"

    testCase "target identity and ordinal property names isolate equal-looking keys" <| fun _ ->
        let registry=FrameSiblingRegistry()
        let first=String('a',1):>obj
        let second=String('a',1):>obj
        Expect.isFalse (obj.ReferenceEquals(first,second)) "distinct equal-valued identities"
        let keys=[|struct(first,"x")|]
        publish registry keys 0. (values [|100.|]) |> ignore
        publish registry [|struct(second,"x")|] 0. (values [|200.|]) |> ignore
        publish registry [|struct(first,"X")|] 0. (values [|300.|]) |> ignore
        let transaction=registry.Begin(keys,(fun () -> true))
        Expect.equal (transaction.Tail(0).Value.Last.Number) 100. "reference identity and case-sensitive property"
        transaction.Rollback()

    testCase "construction captures the key array before caller mutation" <| fun _ ->
        let registry=FrameSiblingRegistry()
        let identity=obj()
        let keys=[|struct(identity,"x")|]
        let frames=values [|100.|]
        let program,transaction=prepare registry keys 0. 100. frames (fun _ index _ -> Some frames[index]) (fun () -> true)
        keys[0]<-struct(obj(),"other")
        transaction.Bind(program.Value,1.,100.) |> ignore
        Expect.isTrue (transaction.Commit()) "captured keys publish"
        let captured=registry.Begin([|struct(identity,"x")|],(fun () -> true))
        Expect.equal (captured.Tail(0).Value.Last.Number) 100. "original key retained"
        captured.Rollback()
        let changed=registry.Begin(keys,(fun () -> true))
        Expect.isNone (changed.Tail 0) "mutated caller key was not captured"
        changed.Rollback()

    testCase "input snapshots cannot mutate retained compound cache components" <| fun _ ->
        let registry=FrameSiblingRegistry()
        let keys=[|struct(obj(),"x")|]
        let frames=[|{FrameInput.Default with From=Some(ValueInterpolation.Text "v(0,10)");To=Some(ValueInterpolation.Text "v(100,110)")} |]
        let _,nodes=publish registry keys 0. frames
        nodes[0].Value<-ValueInterpolation.compose(nodes[0].Definition.Last,0.,[|50.;60.|])
        let transaction=registry.Begin(keys,(fun () -> true))
        let snapshot=transaction.Tail(0).Value
        snapshot.CachedEnd.Components[0]<-999.
        Expect.sequenceEqual nodes[0].Value.Components [50.;60.] "live cache remains independent"
        Expect.sequenceEqual (transaction.Tail(0).Value.CachedEnd.Components) [50.;60.] "staged cache remains independent"
        transaction.Rollback()

    testCase "removing a child unlinks its nodes without reviving source overrides" <| fun _ ->
        let registry,keys,a=scene()
        let _,b=publish registry keys 75. (values [|300.;400.|])
        registry.Remove b
        Expect.isTrue (obj.ReferenceEquals(a[0].Next,a[1]) && obj.ReferenceEquals(a[1].Previous,a[0])) "remaining source chain"
        Expect.equal (a[0].Change,a[1].Overridden) (75.,true) "source truncation persists after cancellation"
        Expect.isTrue (b |> Array.forall(fun node -> isNull node.Previous && isNull node.Next)) "removed links released"

    testCase "clearing the owner graph releases links and invalidates in-flight publication" <| fun _ ->
        let registry,keys,a=scene()
        let stale=registry.Begin(keys,(fun () -> true))
        registry.Clear()
        Expect.isFalse (stale.Commit()) "cleared generation"
        Expect.isTrue (a |> Array.forall(fun node -> isNull node.Previous && isNull node.Next)) "owner links released"
        let empty=registry.Begin(keys,(fun () -> true))
        Expect.isNone (empty.Tail 0) "owner registry empty"
        empty.Rollback()

    testCase "repeated predecessor override geometry matches source" <| fun _ ->
        let registry=FrameSiblingRegistry()
        let keys=[|struct(obj(),"x")|]
        let frames=values [|0.;100.;200.|]
        let program,first=prepare registry keys 0. 200. frames (fun _ index _ -> Some frames[index]) (fun () -> true)
        let a=first.Bind(program.Value,2.,400.)
        Expect.isTrue (first.Commit()) "repeated predecessor"
        let nextFrames=values [|300.|]
        let nextProgram,next=prepare registry keys 250. 100. nextFrames (fun _ index _ -> Some nextFrames[index]) (fun () -> true)
        let b=next.Bind(nextProgram.Value,1.,100.)
        Expect.isTrue (next.Commit()) "later child"
        let fixture=source "replace-loopingPredecessor"
        sourceGeometry a (fixture.GetProperty("programs").GetProperty("a"))
        sourceGeometry b (fixture.GetProperty("programs").GetProperty("b"))

    testCase "interruption after a staged frame never leaks earlier truncation" <| fun _ ->
        let registry,keys,a=scene()
        let mutable live=true
        let frames=[|FrameInput.Default;FrameInput.Default|]
        let resolve _ index _ =
            if index=1 then live<-false
            Some {FrameInput.Default with To=Some(ValueInterpolation.Number(300.+float index))}
        let program,transaction=prepare registry keys 75. 200. frames resolve (fun () -> live)
        Expect.isNone program "interrupted compilation discarded"
        transaction.Rollback()
        Expect.equal (a[0].Change,a[1].Overridden) (100.,false) "no earlier staged effect escaped"

    testCase "modifier faults preserve identity and leave live siblings unchanged" <| fun _ ->
        let registry,keys,a=scene()
        let fault=InvalidOperationException("staged sibling modifier")
        let transaction=registry.Begin(keys,(fun () -> true))
        let frame={FrameInput.Default with To=Some(ValueInterpolation.Number 300.);Modifier=Some(fun _ -> raise fault)}
        let property={FrameProperty.Target=0;Name="x";Original=ValueInterpolation.Number 10.;Frames=[|frame|];ConvertUnit=None}
        let mutable actual:exn option=None
        try
            KeyframeCompilation.compileWithSiblings({FrameDefaults.Default with Duration=100.;Offset=75.;Ease=id},[|property|],
                (fun _ _ _ -> Some frame),(fun _ -> failtest "selected sibling should replace original read"),transaction.Compilation,(fun () -> true)) |> ignore
        with error -> actual<-Some error
        transaction.Rollback()
        Expect.isTrue (actual |> Option.exists(fun error -> obj.ReferenceEquals(error,fault))) "primary fault retained"
        Expect.equal (a[0].Change,a[1].Overridden) (100.,false) "failed generation cannot mutate playback"

    testCase "empty programs publish no sibling nodes or target reads" <| fun _ ->
        let registry=FrameSiblingRegistry()
        let transaction=registry.Begin([||],(fun () -> true))
        let program=KeyframeCompilation.compileWithSiblings(FrameDefaults.Default,[||],
            (fun _ _ _ -> failtest "empty frame resolver"),(fun _ -> failtest "empty original read"),transaction.Compilation,(fun () -> true)) |> Option.get
        Expect.isEmpty (transaction.Bind(program,1.,program.IterationDuration)) "epsilon controller has no siblings"
        Expect.isTrue (transaction.Commit()) "empty generation"

    testCase "shared drivers render forward takeover and backward recovery from source endpoints" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let registry=FrameSiblingRegistry()
        let keys=[|struct(obj(),"x")|]
        let mutable value=10.
        let writes=ResizeArray<float>()
        let build offset (frames:FrameInput array) computed =
            let resolve _ index (previous:FrameTween option) =
                if computed then Some {FrameInput.Default with To=Some(ValueInterpolation.Number(previous.Value.CachedEnd.Scalar.Value+(if index=0 then 10. else 20.)))}
                else Some frames[index]
            let program,transaction=prepare registry keys offset 200. frames resolve (fun () -> true)
            let program=program.Value
            let nodes=transaction.Bind(program,1.,program.IterationDuration)
            let driver=new KeyframePlayback(scheduler,program,(fun _ _ output -> value<-output.Scalar.Value;writes.Add value),
                                             true,false,-1,PlaybackOptions.Default,siblings=nodes)
            driver.Initialize(timelineChild=true)
            Expect.isTrue (transaction.Commit()) "owned driver commit"
            driver
        use a=build 0. (values [|0.;100.;200.|]) false
        a.Clock.SampleChild(75.,PlaybackTickMode.Auto,true,240.,false,false,(fun () -> true),internalRender=true)
        use b=build 75. [|FrameInput.Default;FrameInput.Default|] true
        Expect.equal value 10. "both constructors remain cache-only"
        b.Clock.SampleChild(-75.,PlaybackTickMode.Force,true,240.,true,false,(fun () -> true),internalRender=true)
        a.Clock.SampleChild(0.,PlaybackTickMode.Force,true,240.,true,false,(fun () -> true),internalRender=true)
        writes.Clear()
        a.Clock.SampleChild(125.,PlaybackTickMode.Auto,true,240.,false,true,(fun () -> true))
        b.Clock.SampleChild(50.,PlaybackTickMode.Auto,true,240.,false,true,(fun () -> true))
        Expect.equal value 142.5 "sampled75 to computed210 at half duration"
        Expect.sequenceEqual writes [142.5] "overridden earlier driver cannot dispatch"
        b.Clock.SampleChild(-25.,PlaybackTickMode.Auto,true,240.,true,true,(fun () -> true))
        a.Clock.SampleChild(50.,PlaybackTickMode.Auto,true,240.,true,true,(fun () -> true))
        Expect.equal value 50. "backward recovery selects earlier driver"
]
