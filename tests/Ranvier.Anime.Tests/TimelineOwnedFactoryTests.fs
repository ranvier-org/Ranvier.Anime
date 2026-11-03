module Ranvier.Anime.Tests.TimelineOwnedFactoryTests

open System
open Expecto
open Ranvier
open Ranvier.Dom
open Ranvier.Anime

let private attachTimer (context:TimelineChildContext) =
    let child=new Timer(context.Scheduler,100u)
    context.Attach child
    child.AssignToTimeline()
    child

let private build (root:Timeline) identity offset (frames:FrameInput array) computed write callback =
    root.TryAddOwned((fun context ->
        let transaction=context.BeginSiblings [|struct(identity,"x")|]
        let property={FrameProperty.Target=0;Name="x";Original=ValueInterpolation.Number 10.;Frames=frames;ConvertUnit=None}
        let resolve _ index (previous:FrameTween option) =
            if computed then Some {FrameInput.Default with To=Some(ValueInterpolation.Number(previous.Value.CachedEnd.Scalar.Value+(if index=0 then 10. else 20.)))}
            else Some frames[index]
        let program=KeyframeCompilation.compileWithSiblings({FrameDefaults.Default with Duration=200.;Offset=context.Offset;Ease=id},[|property|],
            resolve,(fun _ -> ValueInterpolation.Number 10.),transaction.Compilation,context.CanWrite) |> Option.get
        let nodes=transaction.Bind(program,1.,program.IterationDuration)
        let driver=new KeyframePlayback(context.Scheduler,program,(fun _ _ output -> write output.Scalar.Value),
            true,false,-1,PlaybackOptions.Default,onRender=callback,siblings=nodes)
        context.Attach driver
        driver.Clock.OnCancel<-(fun () -> context.RemoveSiblings nodes)
        driver.Initialize(timelineChild=true,notifyConstruction=true,canWrite=context.CanWrite)
        Some(TimelinePreparedChild(driver.Clock,(fun () -> driver.RenderVersion),transaction.Commit,driver,ignore))),
        position=TimelinePosition.At offset) |> Option.get

let tests=testList "owned timeline factory transactions" [
    testCase "a seek during final reset retains the already published handle and nested state" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        use root=new Timeline(scheduler)
        let mutable armed=true
        let mutable value=0.
        let result=root.TryAddOwned(fun context ->
            let child=attachTimer context
            child.ConfigureRenderer(fun sample _ _ ->
                if armed then armed<-false;root.Seek(25.,muteCallbacks=true)
                if not child.InternalRender then value<-sample.IterationTime)
            Some(TimelinePreparedChild(child,(fun () -> 0L),(fun () -> true),child,ignore)))
        Expect.isSome result "published child remains an owned root entry"
        Expect.equal (root.CurrentTime,result.Value.CurrentTime,value) (25.,25.,25.) "nested seek supersedes the outer cache reset"
        root.Cancel()
        Expect.isTrue result.Value.Cancelled "published lifetime is still owned"

    testCase "actual root prepares caches arbitrates drivers and reports one render" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let mutable value=10.
        let events=ResizeArray<string>()
        let writes=ResizeArray<float>()
        use root=new Timeline(scheduler,onRender=(fun _ -> events.Add "root"))
        let identity=obj()
        let write next=value<-next;writes.Add next
        let callback (_:Timer)=events.Add "child"
        let frames=KeyframeCompilation.values [|ValueInterpolation.Number 0.;ValueInterpolation.Number 100.;ValueInterpolation.Number 200.|]
        use first=build root identity 0. frames false write callback
        use second=build root identity 75. [|FrameInput.Default;FrameInput.Default|] true write callback
        Expect.equal value 10. "construction leaves target untouched"
        Expect.isEmpty writes "internal root resets remain cache-only"
        events.Clear()
        root.Seek 125.
        Expect.equal value 142.5 "source sampled75 to computed210"
        Expect.sequenceEqual writes [142.5] "one eligible driver"
        Expect.sequenceEqual events ["child";"root"] "child render precedes root feedback"
        events.Clear()
        root.Seek(50.,muteCallbacks=true)
        Expect.equal value 50. "backward predecessor recovers"
        Expect.isEmpty events "parent muting reaches owned children"
        root.Cancel()
        Expect.isTrue (first.Clock.Cancelled && second.Clock.Cancelled) "root owns both lifetimes"

    testCase "pending child is cancelled immediately when construction cancels the root" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        use root=new Timeline(scheduler)
        let result=root.TryAddOwned(fun context ->
            let child=attachTimer context
            root.Cancel()
            Expect.isTrue child.Cancelled "pending cancellation is synchronous"
            Expect.isFalse (context.CanWrite()) "later providers must stop"
            Some(TimelinePreparedChild(child,(fun () -> 0L),(fun () -> failtest "cancelled commit"),child,ignore)))
        Expect.isNone result "cancelled child is not published"

    testCase "a parent seek supersedes construction without cancelling the parent or its time" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        use root=new Timeline(scheduler)
        root.AddTimer(100u) |> ignore
        let mutable pending:Timer option=None
        let result=root.TryAddOwned(fun context ->
            let child=attachTimer context
            pending<-Some child
            root.Seek(25.,muteCallbacks=true)
            Some(TimelinePreparedChild(child,(fun () -> 0L),(fun () -> failtest "superseded commit"),child,ignore)))
        Expect.isNone result "superseded construction not published"
        Expect.isTrue pending.Value.Cancelled "pending lifetime released"
        Expect.isFalse root.Cancelled "nested control remains reusable"
        Expect.equal root.CurrentTime 25. "nested seek survives"

    testCase "on-pause reentry prevents the factory from running" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let mutable armed=false
        use root=new Timeline(scheduler,onPause=(fun root -> if armed then armed<-false;root.Seek(25.,muteCallbacks=true)))
        root.AddTimer(100u) |> ignore
        root.Resume()
        armed<-true
        let result:unit option=root.TryAddOwned(fun _ -> failtest "factory after pause reentry")
        Expect.isNone result "pause callback superseded authoring"
        Expect.equal root.CurrentTime 25. "nested time survives"
        Expect.isFalse root.Cancelled "root remains usable"

    testCase "failed commit disposes only the unpublished child" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        use root=new Timeline(scheduler)
        let prior=root.AddTimer(100u)
        let mutable pending:Timer option=None
        let result=root.TryAddOwned(fun context ->
            let child=attachTimer context
            pending<-Some child
            Some(TimelinePreparedChild(child,(fun () -> 0L),(fun () -> false),child,ignore)))
        Expect.isNone result "stale sibling transaction rejected"
        Expect.isTrue pending.Value.Cancelled "unpublished child disposed"
        Expect.isFalse prior.Cancelled "existing child retained"
        Expect.equal root.IterationDuration 100. "duration unchanged"

    testCase "construction faults cancel existing and pending children while retaining the primary error" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        use root=new Timeline(scheduler)
        let prior=root.AddTimer(100u)
        let mutable pending:Timer option=None
        let fault=InvalidOperationException("owned constructor")
        let mutable actual:exn option=None
        try
            root.TryAddOwned(fun context ->
                let child=attachTimer context
                pending<-Some child
                raise fault) |> ignore
        with error -> actual<-Some error
        Expect.isTrue (actual |> Option.exists(fun error -> obj.ReferenceEquals(error,fault))) "first fault identity"
        Expect.isTrue (root.Cancelled && prior.Cancelled && pending.Value.Cancelled) "complete cleanup"

    testCase "actual Ranvier owner disposal cancels the child before its factory returns" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        use graph=new Graph()
        use activation=graph.Activate()
        let owner=Api.createRoot id
        use root=new Timeline(scheduler)
        owner.Attach root
        let result=root.TryAddOwned(fun context ->
            let child=attachTimer context
            owner.Dispose()
            Expect.isTrue child.Cancelled "actual owner interrupts pending child"
            Some(TimelinePreparedChild(child,(fun () -> 0L),(fun () -> failtest "owner-disposed commit"),child,ignore)))
        Expect.isNone result "owner-disposed generation cannot publish"

    testCase "cancelled child attach is disposed instead of retained" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        use root=new Timeline(scheduler)
        let result:unit option=root.TryAddOwned(fun context ->
            root.Cancel()
            use child=new Timer(scheduler,100u)
            context.Attach child
            Expect.isTrue child.Cancelled "attach after cancellation closes immediately"
            None)
        Expect.isNone result "closed factory"

    testCase "parent cancellation continues across a throwing child cleanup" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        use root=new Timeline(scheduler)
        let prior=root.AddTimer(100u)
        let fault=InvalidOperationException("child cancellation")
        prior.OnCancel<-(fun () -> raise fault)
        let mutable actual:exn option=None
        let result:unit option=root.TryAddOwned(fun context ->
            let child=attachTimer context
            try root.Cancel() with error -> actual<-Some error
            Expect.isTrue child.Cancelled "pending child cleaned after earlier failure"
            None)
        Expect.isNone result "closed factory"
        Expect.isTrue (actual |> Option.exists(fun error -> obj.ReferenceEquals(error,fault))) "first cleanup fault"

    testCase "empty failed construction preserves existing scheduling ownership" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        use root=new Timeline(scheduler)
        let child=root.AddTimer(100u)
        let result:unit option=root.TryAddOwned(fun _ -> None)
        Expect.isNone result "no child"
        Expect.isFalse child.Cancelled "prior child retained"
        root.Seek 50.
        Expect.equal child.CurrentTime 50. "prior child still sampled"
]
