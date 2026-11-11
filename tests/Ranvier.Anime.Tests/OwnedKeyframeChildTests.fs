module Ranvier.Anime.Tests.OwnedKeyframeChildTests

open System
open Expecto
open Ranvier
open Ranvier.Dom
open Ranvier.Anime

let private compile first last modifier =
    let frame={FrameInput.Default with From=Some first;To=Some last}
    let property={FrameProperty.Target=0;Name="x";Original=first;Frames=[|frame|];ConvertUnit=None}
    KeyframeCompilation.compile({FrameDefaults.Default with Duration=100.;Ease=id;Modifier=modifier},[|property|],(fun () -> true))
    |> Option.get

let tests=testList "owned keyframe child sampling" [
    testCase "owned constructor notification reentry retains timeline scheduling" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let program=compile (ValueInterpolation.Number 0.) (ValueInterpolation.Number 100.) id
        let mutable writes=0
        let mutable once=false
        use driver=new KeyframePlayback(scheduler,program,(fun _ _ _ -> writes<-writes+1),false,false,-1,PlaybackOptions.Default,
            onRender=(fun clock -> if not once then once<-true;clock.Seek(50.,muteCallbacks=true);clock.Resume()))
        driver.Initialize(timelineChild=true,notifyConstruction=true)
        let before=writes
        scheduler.Advance 10.
        scheduler.RunFrame()
        Expect.equal writes before "notification cannot request an independent frame"
        Expect.equal driver.Clock.CurrentTime 50. "reentrant seek retained"

    testCase "reentrant owned initialization retains the nested seek lifecycle state" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let mutable active:Timer option=None
        let mutable armed=false
        let modifier value=
            if armed then armed<-false;active.Value.Seek(100.,muteCallbacks=true)
            value
        let program=compile (ValueInterpolation.Number 0.) (ValueInterpolation.Number 100.) modifier
        let mutable value=7.
        use driver=new KeyframePlayback(scheduler,program,(fun _ _ output -> value<-output.Scalar.Value),false,false,-1,PlaybackOptions.Default)
        active<-Some driver.Clock
        armed<-true
        driver.Initialize(timelineChild=true)
        Expect.equal (driver.Clock.CurrentTime,driver.Clock.Began,driver.Clock.Completed) (100.,true,true) "nested seek state survives outer attach"
        Expect.equal (value,driver.CurrentValues[0].Scalar,driver.RenderVersion) (100.,Some 100.,1L) "nested dispatch and cache survive"

    testCase "explicit owned construction notification runs once without dispatch" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let program=compile (ValueInterpolation.Number 0.) (ValueInterpolation.Number 100.) id
        let mutable calls=0
        use driver=new KeyframePlayback(scheduler,program,(fun _ _ _ -> failtest "owned construction dispatch"),false,false,-1,PlaybackOptions.Default,
                                         onRender=(fun _ -> calls<-calls+1))
        driver.Initialize(timelineChild=true,notifyConstruction=true)
        driver.Initialize(timelineChild=true,notifyConstruction=true)
        Expect.equal calls 1 "constructor notification only once"
        Expect.equal driver.RenderVersion 0L "cache-only resets retain zero dispatch"

    testCase "cache-only child context is scoped and normal child callbacks retain their policy" <| fun _ ->
        let events=ResizeArray<string>()
        let state=new PlaybackState(100.,1.,0.,0.,false,false,(fun event -> events.Add(event.ToString())))
        let samples=ResizeArray<bool*float>()
        state.Renderer<-fun sample _ _ -> samples.Add(state.InternalRender,sample.IterationTime)
        state.TickChild(50.,PlaybackTickMode.Force,true,false,true,(fun () -> true),internalRender=true)
        Expect.sequenceEqual samples [(true,50.)] "internal child context"
        Expect.isEmpty events "muted child"
        Expect.isFalse state.InternalRender "context restored"
        state.TickChild(75.,PlaybackTickMode.Force,false,false,true,(fun () -> true))
        Expect.equal samples[samples.Count-1] (false,75.) "ordinary child context"
        Expect.sequenceEqual events ["BeforeUpdate";"Update"] "existing lifecycle policy"

    testCase "renderer faults restore internal and inherited contexts" <| fun _ ->
        let state=new PlaybackState(100.,1.,0.,0.,false,false,ignore)
        let fault=InvalidOperationException("owned child context fault")
        state.Renderer<-fun _ _ _ ->
            Expect.isTrue state.InternalRender "inside internal context"
            raise fault
        let mutable actual:exn option=None
        try state.TickChild(50.,PlaybackTickMode.Force,true,false,true,(fun () -> true),internalRender=true)
        with error -> actual<-Some error
        Expect.isTrue (actual |> Option.exists(fun error -> obj.ReferenceEquals(error,fault))) "exact exception"
        Expect.isFalse state.InternalRender "internal context restored"
        state.Renderer<-fun _ _ _ -> Expect.isFalse state.InternalRender "normal context after fault"
        state.TickChild(75.,PlaybackTickMode.Force,true,false,true,(fun () -> true))

    testCase "nested sampling restores the outer internal context while superseding its guard" <| fun _ ->
        let state=new PlaybackState(100.,1.,0.,0.,false,false,ignore)
        let contexts=ResizeArray<bool>()
        let mutable nested=false
        state.Renderer<-fun _ _ _ ->
            contexts.Add state.InternalRender
            if not nested then
                nested<-true
                let outer=state.CaptureWriteGuard()
                state.TickChild(25.,PlaybackTickMode.Force,true,false,true,(fun () -> true))
                contexts.Add state.InternalRender
                Expect.isFalse (outer()) "inner operation supersedes outer sample"
        state.TickChild(75.,PlaybackTickMode.Force,true,false,true,(fun () -> true),internalRender=true)
        Expect.sequenceEqual contexts [true;false;true] "nested scoped contexts"
        Expect.isFalse state.InternalRender "outer context released"
        Expect.equal state.Sample.IterationTime 25. "inner clock state retained"

    testCase "owned initialization muted writes internal caches and skip feedback remain distinct" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let mutable value=10.
        let events=ResizeArray<string>()
        let program=compile (ValueInterpolation.Number 0.) (ValueInterpolation.Number 100.) id
        use driver=new KeyframePlayback(scheduler,program,(fun _ _ output -> value<-output.Scalar.Value),false,false,-1,PlaybackOptions.Default,
                                         onRender=(fun _ -> events.Add "render"),onUpdate=(fun _ -> events.Add "update"))
        driver.Initialize(timelineChild=true)
        Expect.equal (value,driver.RenderVersion) (10.,0L) "construction has no target write"
        Expect.isEmpty events "no standalone construction notification"
        driver.Clock.SampleChild(50.,PlaybackTickMode.Force,true,240.,false,true,(fun () -> true))
        Expect.equal (value,driver.RenderVersion) (50.,1L) "muted samples still render values"
        Expect.isEmpty events "owned child callbacks muted"
        driver.Clock.SampleChild(75.,PlaybackTickMode.Force,true,240.,false,true,(fun () -> true),internalRender=true)
        Expect.equal (value,driver.CurrentValues[0].Scalar,driver.RenderVersion) (50.,Some 75.,1L) "internal caches advance without dispatch"
        driver.Clock.SampleChild(80.,PlaybackTickMode.Skip,false,240.,false,true,(fun () -> true))
        Expect.equal driver.RenderVersion 1L "skipped sample cannot report a previous dispatch"
        driver.Clock.SampleChild(90.,PlaybackTickMode.Force,false,240.,false,true,(fun () -> true))
        Expect.equal (value,driver.RenderVersion) (90.,2L) "ordinary child dispatch"
        Expect.sequenceEqual events ["render";"update"] "child callback phases"

    testCase "compound cache snapshots cannot mutate retained components" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let program=compile (ValueInterpolation.Text "v(0,10)") (ValueInterpolation.Text "v(100,110)") id
        use driver=new KeyframePlayback(scheduler,program,(fun _ _ _ -> failtest "internal compound dispatch"),false,false,-1,PlaybackOptions.Default)
        driver.Initialize(timelineChild=true)
        driver.Clock.SampleChild(50.,PlaybackTickMode.Force,true,240.,false,true,(fun () -> true),internalRender=true)
        let snapshot=driver.CurrentValues
        Expect.sequenceEqual snapshot[0].Components [50.;60.] "sampled components"
        snapshot[0].Components[0]<-999.
        Expect.sequenceEqual driver.CurrentValues[0].Components [50.;60.] "independent snapshots"

    testCase "stretch retains sampled caches until the next owned child sample" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let mutable value=5.
        let program=compile (ValueInterpolation.Number 0.) (ValueInterpolation.Number 100.) id
        use driver=new KeyframePlayback(scheduler,program,(fun _ _ output -> value<-output.Scalar.Value),false,false,-1,PlaybackOptions.Default)
        driver.Initialize(timelineChild=true)
        driver.Clock.SampleChild(50.,PlaybackTickMode.Force,true,240.,false,true,(fun () -> true),internalRender=true)
        driver.Clock.Stretch 200.
        Expect.equal (value,driver.CurrentValues[0].Scalar,driver.RenderVersion) (5.,Some 50.,0L) "stretch does not recapture or dispatch"
        driver.Clock.SampleChild(150.,PlaybackTickMode.Force,true,240.,false,true,(fun () -> true))
        Expect.equal (value,driver.CurrentValues[0].Scalar) (75.,Some 75.) "scaled geometry samples the retained endpoints"

    testCase "false inherited guard prevents modifier effects caches and dispatch" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let mutable calls=0
        let modify value=calls<-calls+1;value
        let program=compile (ValueInterpolation.Number 0.) (ValueInterpolation.Number 100.) modify
        use driver=new KeyframePlayback(scheduler,program,(fun _ _ _ -> failtest "guarded dispatch"),false,false,-1,PlaybackOptions.Default)
        driver.Initialize(timelineChild=true)
        calls<-0
        let before=driver.CurrentValues[0].Scalar
        driver.Clock.SampleChild(50.,PlaybackTickMode.Force,false,240.,false,true,(fun () -> false),internalRender=true)
        Expect.equal (calls,driver.CurrentValues[0].Scalar,driver.RenderVersion) (0,before,0L) "no work after ancestor interruption"

    testCase "parented keyframes never request an independent frame after resume" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let program=compile (ValueInterpolation.Number 0.) (ValueInterpolation.Number 100.) id
        use driver=new KeyframePlayback(scheduler,program,(fun _ _ _ -> failtest "independently scheduled child"),false,false,-1,PlaybackOptions.Default)
        driver.Initialize(timelineChild=true)
        driver.Clock.Resume()
        scheduler.Advance 50.
        scheduler.RunFrame()
        Expect.equal driver.RenderVersion 0L "timeline owns scheduling"
        driver.Clock.SampleChild(50.,PlaybackTickMode.Force,true,240.,false,true,(fun () -> false))

    testCase "actual owner disposal in an internal modifier suppresses publication and later callbacks" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        use graph=new Graph()
        use activation=graph.Activate()
        let owner=Api.createRoot id
        let mutable armed=false
        let mutable calls=0
        let modify value=
            if armed then calls<-calls+1;owner.Dispose()
            value
        let program=compile (ValueInterpolation.Number 0.) (ValueInterpolation.Number 100.) modify
        use driver=new KeyframePlayback(scheduler,program,(fun _ _ _ -> failtest "cancelled child dispatch"),false,false,-1,PlaybackOptions.Default,
                                         onRender=(fun _ -> failtest "render after disposal"),onUpdate=(fun _ -> failtest "update after disposal"))
        owner.Attach driver
        driver.Initialize(timelineChild=true)
        armed<-true
        let before=driver.CurrentValues[0].Scalar
        driver.Clock.SampleChild(50.,PlaybackTickMode.Force,true,240.,false,true,(fun () -> true),internalRender=true)
        Expect.equal (calls,driver.CurrentValues[0].Scalar,driver.RenderVersion) (1,before,0L) "interrupted cache is not published"
        Expect.isTrue driver.Clock.Cancelled "actual owner owns the child clock"
]
