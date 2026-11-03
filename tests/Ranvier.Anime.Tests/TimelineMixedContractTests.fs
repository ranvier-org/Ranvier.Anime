module Ranvier.Anime.Tests.TimelineMixedContractTests

open System
open Expecto
open Ranvier
open Ranvier.Dom
open Ranvier.Anime

let private options={KeyframeAnimationOptions.Default with Ease=id;Precision=None}

let tests=testList "mixed scalar and keyframe contracts" [
    for backwards in [false;true] do
        testCase (sprintf "scalar finite endpoints preserve overflow-resistant interpolation %b" backwards) <| fun _ ->
            use scheduler=new ManualFrameScheduler()
            use root=new Timeline(scheduler)
            let first,last=if backwards then 1e308,-1e308 else -1e308,1e308
            let mutable value=first
            let property=NumericProperty((fun () -> value),(fun next -> value<-next))
            root.Animate(property,last,100u) |> ignore
            root.Seek 50.
            Expect.equal value 0. "finite midpoint when the difference overflows"
            root.Complete()
            Expect.equal value last "exact endpoint"

    testCase "scalar explicit source captures its original for restoration" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        use root=new Timeline(scheduler)
        let mutable value=7.
        let mutable reads=0
        let property=NumericProperty((fun () -> reads<-reads+1;value),(fun next -> value<-next))
        let result=root.Animate(property,110.,100u,from=10.)
        Expect.isTrue (obj.ReferenceEquals(root,result)) "fluent scalar result"
        Expect.equal value 7. "cache-only construction"
        Expect.equal reads 1 "original capture also applies to explicit sources"
        root.Seek 50.
        Expect.equal value 60. "explicit scalar endpoints"
        root.Revert()
        Expect.equal value 7. "original restored independently of explicit source"

    testCase "a scalar getter seek aborts pending publication and retains the fluent root" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        use root=new Timeline(scheduler)
        root.AddTimer(100u) |> ignore
        let mutable armed=true
        let mutable modifiers=0
        let property=NumericProperty((fun () -> if armed then armed<-false;root.Seek 25.;10. else 10.),ignore)
        let result=root.Animate(property,200.,200u,modifier=(fun value -> modifiers<-modifiers+1;value))
        Expect.isTrue (obj.ReferenceEquals(root,result)) "superseded scalar call returns the root"
        Expect.equal (root.CurrentTime,root.IterationDuration,root.Cancelled,modifiers) (25.,100.,false,0) "nested seek survives without a phantom child"

    testCase "cancellation inside scalar construction suppresses invalid modifier validation" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        use root=new Timeline(scheduler)
        let mutable value=10.
        let property=NumericProperty((fun () -> value),(fun next -> value<-next))
        let result=root.Animate(property,100.,100u,modifier=(fun _ -> root.Cancel();Double.NaN))
        Expect.isTrue (obj.ReferenceEquals(root,result) && root.Cancelled) "cancelled fluent construction"
        Expect.equal value 10. "no constructor dispatch"

    testCase "scalar easing cancellation suppresses invalid progress and later keyframe writes" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        use root=new Timeline(scheduler)
        let mutable x,y,armed=10.,20.,false
        let a=NumericProperty((fun () -> x),(fun next -> x<-next))
        let b=NumericProperty((fun () -> y),(fun next -> y<-next))
        root.Animate(a,100.,100u,ease=(fun progress -> if armed then root.Cancel();Double.NaN else progress)) |> ignore
        use child=root.AddKeyframes(b,Keyframe.values [|0.;200.|],duration=100u,position=TimelinePosition.At 0.,options=options)
        armed<-true
        root.Seek 50.
        Expect.equal (x,y,root.Cancelled,child.Cancelled) (10.,20.,true,true) "ancestor guard spans both driver families"

    testCase "scalar modifier cancellation suppresses invalid output" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        use root=new Timeline(scheduler)
        let mutable value=7.
        let mutable armed=false
        let property=NumericProperty((fun () -> value),(fun next -> value<-next))
        root.Animate(property,100.,100u,modifier=(fun next -> if armed then root.Cancel();Double.NaN else next)) |> ignore
        armed<-true
        root.Seek 50.
        Expect.equal (value,root.Cancelled) (7.,true) "cancelled output is never validated or dispatched"

    testCase "live scalar nonfinite modifier output still faults and cancels the root" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        use root=new Timeline(scheduler)
        let mutable value=10.
        let mutable armed=false
        let property=NumericProperty((fun () -> value),(fun next -> value<-next))
        root.Animate(property,100.,100u,modifier=(fun next -> if armed then Double.PositiveInfinity else next)) |> ignore
        armed<-true
        Expect.throwsT<ArgumentException> (fun () -> root.Seek 50.) "existing finite scalar policy"
        Expect.equal (value,root.Cancelled) (10.,true) "first fault closes the root"

    testCase "scalar constructor faults retain identity and close existing owned keyframe children" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        use root=new Timeline(scheduler)
        let mutable value=10.
        let property=NumericProperty((fun () -> value),(fun next -> value<-next))
        use child=root.AddKeyframes(property,Keyframe.values [|0.;100.|],duration=100u,options=options)
        let fault=InvalidOperationException("mixed scalar constructor")
        let mutable actual:exn option=None
        try root.Animate(property,200.,100u,modifier=(fun _ -> raise fault)) |> ignore
        with error -> actual<-Some error
        Expect.isTrue (actual |> Option.exists(fun error -> obj.ReferenceEquals(error,fault))) "original exception"
        Expect.isTrue (root.Cancelled && child.Cancelled) "shared owned cleanup"
        Expect.equal value 10. "cache-only failed construction"

    testCase "different numeric handles retain independent replacement identities" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        use root=new Timeline(scheduler)
        let mutable value=10.
        let a=NumericProperty((fun () -> value),(fun next -> value<-next))
        let b=NumericProperty((fun () -> value),(fun next -> value<-next))
        root.Animate(a,100.,100u,from=0.,position=TimelinePosition.At 0.) |> ignore
        use child=root.AddKeyframes(b,[|Keyframe.toValue 300.|],duration=100u,position=TimelinePosition.At 50.,options=options)
        root.Seek 125.
        Expect.equal value 227.5 "backing storage does not merge distinct handles"

    testCase "a scalar modifier seek supersedes later keyframe writes and outer callbacks" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let mutable renders=0
        use root=new Timeline(scheduler,onRender=(fun _ -> renders<-renders+1))
        let mutable x,y,armed=0.,0.,false
        let a=NumericProperty((fun () -> x),(fun next -> x<-next))
        let b=NumericProperty((fun () -> y),(fun next -> y<-next))
        root.Animate(a,100.,100u,from=0.,position=TimelinePosition.At 0.,modifier=(fun value ->
            if armed then armed<-false;root.Seek(75.,muteCallbacks=true)
            value)) |> ignore
        use child=root.AddKeyframes(b,Keyframe.values [|0.;200.|],duration=100u,position=TimelinePosition.At 0.,options=options)
        armed<-true
        root.Seek 50.
        Expect.equal (x,y,root.CurrentTime,renders) (75.,150.,75.,0) "nested muted sample wins"

    testCase "actual ownership stops scheduled scalar and keyframe drivers together" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        use graph=new Graph()
        use activation=graph.Activate()
        let owner=Api.createRoot id
        use root=new Timeline(scheduler)
        owner.Attach root
        let mutable writes=0
        let a=NumericProperty((fun () -> 0.),(fun _ -> writes<-writes+1))
        let b=NumericProperty((fun () -> 0.),(fun _ -> writes<-writes+1))
        root.Animate(a,100.,100u,from=0.,position=TimelinePosition.At 0.) |> ignore
        use child=root.AddKeyframes(b,Keyframe.values [|0.;200.|],duration=100u,position=TimelinePosition.At 0.,options=options)
        root.Resume()
        scheduler.RunFrame()
        scheduler.Advance 20.
        scheduler.RunFrame()
        Expect.isGreaterThan writes 0 "scheduled drivers were active"
        owner.Dispose()
        let before=writes
        scheduler.Advance 200.
        scheduler.RunFrame()
        Expect.equal writes before "no independent child schedule survives ownership"
        Expect.isTrue (root.Cancelled && child.Cancelled) "actual owner closes both"
]
