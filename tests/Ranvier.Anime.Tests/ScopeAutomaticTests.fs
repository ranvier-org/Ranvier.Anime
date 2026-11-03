module Ranvier.Anime.Tests.ScopeAutomaticTests

open System
open Expecto
open Ranvier
open Ranvier.Dom
open Ranvier.Anime

let private withOwner action=
    use graph=new Graph()
    use activation=graph.Activate()
    let owner=Api.createRoot id
    try Api.runWithOwner owner (fun () -> action owner)
    finally owner.Dispose()

let automaticTests=testList "automatic scope restoration" [
    testCase "standalone hidden controllers remain graph free outside a scope" <| fun _ ->
        Expect.isTrue (ValueOption.isNone Graph.TryCurrent) "no ambient graph for this regression"
        use scheduler=new ManualFrameScheduler()
        use group=new Animatable(scheduler)
        group.Numeric(NumericProperty((fun () -> 10.),ignore)) |> ignore
        use suppressed=ScopeRegistration.run ignore (fun () -> new NumericAnimation(scheduler,(fun () -> 10.),ignore,100.,100u))
        suppressed.Seek 50.
        Expect.equal group.Count 1 "standalone group works through hidden suppression"
    testCase "nested suppression retains a real scope's selected owner lifetime" <| fun _ ->
        withOwner(fun _ ->
            use scope=new Scope()
            let mutable guard=fun () -> false
            scope.Add(fun _ ->
                guard<-ScopeRegistration.run ignore ScopeRegistration.captureWriteGuard
                Expect.isTrue (guard()) "selected owner is initially live"
                None) |> ignore
            scope.Revert()
            Expect.isFalse (guard()) "suppression preserved actual scope ownership")
    for kind in ["timer";"numeric";"timeline";"keyframes";"animatable"] do
        testCase ("collects only outer "+kind) <| fun _ ->
            withOwner(fun _ ->
                use scheduler=new ManualFrameScheduler()
                let actions=ResizeArray<unit -> unit>()
                let property()=NumericProperty((fun () -> 10.),ignore)
                ScopeRegistration.run actions.Add (fun () ->
                    use resource:IDisposable=
                        match kind with
                        | "timer" -> new Timer(scheduler,100u)
                        | "numeric" -> new NumericAnimation(scheduler,(fun () -> 10.),ignore,100.,100u)
                        | "timeline" ->
                            let root=new Timeline(scheduler)
                            root.AddTimer(100u) |> ignore
                            root.Animate(property(),100.,100u) |> ignore
                            root.AddKeyframes(property(),Keyframe.values [|0.;200.|],duration=100u) |> ignore
                            root
                        | "keyframes" -> Keyframes.Numeric(scheduler,property(),Keyframe.values [|0.;100.|],duration=100u,
                                                options={KeyframeAnimationOptions.Default with Autoplay=false})
                        | _ ->
                            let group=new Animatable(scheduler)
                            group.Numeric(property()) |> ignore
                            group.Numeric(property()) |> ignore
                            group
                    Expect.equal actions.Count 1 "hidden controllers are suppressed"))
    testCase "keyframe construction stops before later getters after scope teardown" <| fun _ ->
        withOwner(fun _ ->
            use scheduler=new ManualFrameScheduler()
            use scope=new Scope()
            let mutable laterReads=0
            let first=NumericProperty((fun () -> scope.Revert();10.),ignore)
            let later=NumericProperty((fun () -> laterReads<-laterReads+1;20.),ignore)
            Expect.throws(fun () ->
                scope.Add(fun _ ->
                    Keyframes.Numeric(scheduler,[|first;later|],Keyframe.values [|0.;100.|],duration=100u) |> ignore
                    None) |> ignore) "scope teardown supersedes the pending sequence"
            Expect.equal laterReads 0 "later accessors are suppressed")
    testCase "refresh automatically restores before reconstructing numeric animation" <| fun _ ->
        withOwner(fun _ ->
            use scheduler=new ManualFrameScheduler()
            use scope=new Scope()
            let mutable value=10.
            let starts=ResizeArray<float>()
            scope.Add(fun _ ->
                starts.Add value
                let animation=new NumericAnimation(scheduler,(fun () -> value),(fun next -> value<-next),110.,100u)
                animation.Seek 50.
                None) |> ignore
            scope.Refresh() |> ignore
            Expect.sequenceEqual starts [10.;10.] "fresh construction follows restoration"
            Expect.equal value 60. "replacement animation rendered"
            scope.Revert()
            Expect.equal value 10. "last scope member restored")
    testCase "once owned numeric animation survives refresh through its persistent owner" <| fun _ ->
        withOwner(fun _ ->
            use scheduler=new ManualFrameScheduler()
            use scope=new Scope()
            let mutable value=10.
            let mutable animation:NumericAnimation option=None
            scope.Add(fun current ->
                current.AddOnce(fun _ ->
                    animation<-Some(new NumericAnimation(scheduler,(fun () -> value),(fun next -> value<-next),110.,100u) |> Animation.own)
                    None) |> ignore
                None) |> ignore
            scope.Refresh() |> ignore
            Expect.isFalse animation.Value.Cancelled "actual persistent owner survives"
            animation.Value.Seek 50.
            Expect.equal value 60. "retained owner permits writes"
            scope.Revert()
            Expect.equal value 10. "persistent restoration precedes cancellation"
            Expect.isTrue animation.Value.Cancelled "persistent member closes")
    testCase "reactive drives use the dynamically selected persistent owner" <| fun _ ->
        withOwner(fun _ ->
            use scheduler=new ManualFrameScheduler()
            use scope=new Scope()
            let handles=ResizeArray<Owner>()
            scope.Add(fun current ->
                handles.Add(Reactive.drive scheduler 100. (fun () -> 10.) ignore :?> Owner)
                current.AddOnce(fun _ -> None) |> ignore
                None) |> ignore
            scope.Refresh() |> ignore
            let retained=handles[1]
            scope.Refresh() |> ignore
            Expect.isFalse retained.IsDisposed "persistent registration retains its actual child owner"
            scope.Revert()
            Expect.isTrue (handles |> Seq.forall _.IsDisposed) "all subscription owners close")
    testCase "reactive scope restoration stops effects and restores the initial seed" <| fun _ ->
        withOwner(fun _ ->
            use scheduler=new ManualFrameScheduler()
            use scope=new Scope()
            let source=Api.createSignal 10.
            let mutable value=0.
            scope.Add(fun _ ->
                Reactive.drive scheduler 100. (fun () -> source.Value) (fun next -> value<-next) |> ignore
                None) |> ignore
            source.Value<-110.
            Graph.Current.Flush()
            scheduler.Advance 50.
            scheduler.RunFrame()
            Expect.isGreaterThan value 10. "reactive target was animated"
            scope.Revert()
            Expect.equal value 10. "source seed restored"
            source.Value<-210.
            Graph.Current.Flush()
            scheduler.Advance 500.
            scheduler.RunFrame()
            Expect.equal value 10. "disposed subscription schedules no new writes")
    testCase "reactive child cleanup errors remain visible on the containing scope" <| fun _ ->
        withOwner(fun _ ->
            use scheduler=new ManualFrameScheduler()
            use scope=new Scope()
            let marker=Exception("reactive cleanup")
            scope.Add(fun _ ->
                let child=Reactive.drive scheduler 100. (fun () -> 10.) ignore :?> Owner
                child.OnCleanup(Action(fun () -> raise marker))
                None) |> ignore
            Expect.throws scope.Revert "cleanup error is surfaced"
            Expect.isTrue (scope.Errors |> Seq.exists(fun error -> obj.ReferenceEquals(marker,error))) "recorded Owner errors are retained")
    testCase "reactive restoration retains a write fault before a child cleanup fault" <| fun _ ->
        withOwner(fun _ ->
            use scheduler=new ManualFrameScheduler()
            use scope=new Scope()
            let writeFault,cleanupFault=Exception("reactive restore"),Exception("reactive cleanup")
            let mutable armed=false
            let mutable child:Owner option=None
            scope.Add(fun _ ->
                let resource=Reactive.drive scheduler 100. (fun () -> 10.) (fun _ -> if armed then raise writeFault) :?> Owner
                child<-Some resource
                resource.OnCleanup(Action(fun () -> raise cleanupFault))
                None) |> ignore
            armed<-true
            Expect.throws scope.Revert "restore fault is surfaced"
            Expect.isTrue (obj.ReferenceEquals(scope.Errors[0],writeFault)) "first fault identity"
            Expect.isTrue (child.Value.ErrorsFrom 0 |> Seq.exists(fun error -> obj.ReferenceEquals(error,cleanupFault))) "cleanup still attempted")
    testCase "a restoration signal write and graph flush cannot retarget the retiring subscription" <| fun _ ->
        withOwner(fun _ ->
            use scheduler=new ManualFrameScheduler()
            use scope=new Scope()
            let source=Api.createSignal 10.
            let mutable armed=false
            let mutable writes=0
            let mutable child:Owner option=None
            scope.Add(fun _ ->
                let resource=Reactive.drive scheduler 100. (fun () -> source.Value) (fun value ->
                    if armed && value=10. then
                        writes<-writes+1
                        if writes=2 then
                            source.Value<-210.
                            Graph.Current.Flush()) :?> Owner
                child<-Some resource
                None) |> ignore
            armed<-true
            scope.Revert()
            Expect.equal writes 2 "rewind and original restoration complete"
            Expect.equal (child.Value.ErrorCount,scope.Errors.Count) (0,0) "retirement emits no callback fault"
            Expect.isTrue child.Value.IsDisposed "subscription owner closes")
    testCase "parent owner disposal restores automatically collected timeline and keyframes" <| fun _ ->
        withOwner(fun owner ->
            use scheduler=new ManualFrameScheduler()
            let scope=new Scope()
            let mutable a,b=10.,20.
            let mutable timeline:Timeline option=None
            scope.Add(fun _ ->
                let root=new Timeline(scheduler)
                timeline<-Some root
                root.Animate(NumericProperty((fun () -> a),(fun next -> a<-next)),100.,100u,from=0.) |> ignore
                root.AddKeyframes(NumericProperty((fun () -> b),(fun next -> b<-next)),Keyframe.values [|0.;200.|],duration=100u,position=TimelinePosition.At 0.) |> ignore
                root.Seek 50.
                None) |> ignore
            owner.Dispose()
            Expect.equal (a,b) (10.,20.) "outer restoration retains original captures"
            Expect.isTrue timeline.Value.Cancelled "child drivers close"
            Expect.equal owner.ErrorCount 0 "actual Ranvier cleanup succeeds")
]
