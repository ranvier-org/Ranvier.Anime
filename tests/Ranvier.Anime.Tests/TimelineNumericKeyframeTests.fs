module Ranvier.Anime.Tests.TimelineNumericKeyframeTests

open System
open Expecto
open Ranvier
open Ranvier.Dom
open Ranvier.Anime

let private options={KeyframeAnimationOptions.Default with Ease=id;Precision=None}

let tests=testList "public owned numeric timeline keyframes" [
    testCase "reentrant public reset leaves cache-only construction context" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        use root=new Timeline(scheduler)
        let mutable value=10.
        let mutable armed=false
        let property=NumericProperty((fun () -> value),(fun next -> value<-next))
        let modifier next=
            if armed then armed<-false;root.Reset()
            next
        use first=root.AddKeyframes(property,Keyframe.values [|0.;100.|],duration=100u,options={options with Modifier=modifier})
        root.Seek 50.
        armed<-true
        Expect.throwsT<InvalidOperationException> (fun () ->
            root.AddKeyframes(property,Keyframe.values [|100.;200.|],duration=100u) |> ignore) "nested reset supersedes pending factory"
        Expect.equal (value,root.CurrentTime,first.Playback.CurrentTime) (0.,0.,0.) "ordinary reset writes and child zero time survive"
        Expect.isFalse root.Cancelled "root remains reusable"

    testCase "raw arrays and input providers share source tail and sampled overlap semantics" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        use root=new Timeline(scheduler)
        let mutable value=10.
        let property=NumericProperty((fun () -> value),(fun next -> value<-next))
        use first=root.AddKeyframes(property,Keyframe.values [|0.;100.;200.|],duration=200u,position=TimelinePosition.At 0.,options=options)
        let inputs=ResizeArray<float>()
        let frames=[|Keyframe<NumericProperty,float>.ToBy(fun c -> inputs.Add c.PreviousValue.Value;c.PreviousValue.Value+10.)
                     Keyframe<NumericProperty,float>.ToBy(fun c -> inputs.Add c.PreviousValue.Value;c.PreviousValue.Value+20.)|]
        use second=root.AddKeyframes(property,frames,duration=200u,position=TimelinePosition.At 75.,options=options)
        Expect.equal value 10. "constructors leave target unchanged"
        Expect.sequenceEqual inputs [200.;200.] "source pre-timing input tail"
        root.Seek 125.
        Expect.equal value 142.5 "sampled75 to computed210"
        root.Seek(50.,muteCallbacks=true)
        Expect.equal value 50. "earlier child backward recovery"
        root.Cancel()
        Expect.isTrue (first.Cancelled && second.Cancelled) "root owns child lifetimes"

    testCase "unsigned maximum and precise fractional duration retain exact timing" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        use root=new Timeline(scheduler)
        let mutable first=0.
        let mutable second=0.
        let a=NumericProperty((fun () -> first),(fun next -> first<-next))
        let b=NumericProperty((fun () -> second),(fun next -> second<-next))
        use ca=root.AddKeyframes(a,Keyframe.values [|0.;100.|],duration=UInt32.MaxValue,position=TimelinePosition.At 0.,options=options)
        use cb=root.AddKeyframesPrecise(b,Keyframe.values [|0.;100.|],0.5,position=TimelinePosition.At 0.,options=options)
        root.Seek 0.25
        Expect.equal second 50. "fractional midpoint"
        root.Seek(float UInt32.MaxValue/2.)
        Expect.equal first 50. "uint converted exactly to internal float"

    testCase "constant validation precedes parent pause and property capture" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let mutable pauses=0
        let mutable reads=0
        use root=new Timeline(scheduler,onPause=(fun _ -> pauses<-pauses+1))
        root.AddTimer(100u) |> ignore
        root.Resume()
        let property=NumericProperty((fun () -> reads<-reads+1;0.),ignore)
        Expect.throwsT<ArgumentException> (fun () ->
            root.AddKeyframesPrecise(property,[|Keyframe<NumericProperty,float>.To 100.|],-1.,options=options) |> ignore) "negative duration"
        Expect.equal (reads,pauses,root.Playing) (0,0,true) "invalid inputs have no effects"

    testCase "a seek from first original getter stops later targets and leaves no child entry" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        use root=new Timeline(scheduler)
        root.AddTimer(100u) |> ignore
        let mutable laterReads=0
        let first=NumericProperty((fun () -> root.Seek(25.,muteCallbacks=true);0.),ignore)
        let second=NumericProperty((fun () -> laterReads<-laterReads+1;0.),ignore)
        Expect.throwsT<InvalidOperationException> (fun () ->
            root.AddKeyframes([|first;second|],Keyframe.values [|0.;100.|],duration=100u,options=options) |> ignore) "superseded constructor"
        Expect.equal laterReads 0 "later original getters suppressed"
        Expect.equal (root.CurrentTime,root.IterationDuration,root.Cancelled) (25.,100.,false) "superseding root state retained"

    testCase "actual owner disposal inside an input provider stops later providers and dispatch" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        use graph=new Graph()
        use activation=graph.Activate()
        let owner=Api.createRoot id
        use root=new Timeline(scheduler)
        owner.Attach root
        let mutable later=0
        let mutable writes=0
        let property=NumericProperty((fun () -> 10.),(fun _ -> writes<-writes+1))
        let frames=[|Keyframe<NumericProperty,float>.ToBy(fun _ -> owner.Dispose();100.)
                     Keyframe<NumericProperty,float>.ToBy(fun _ -> later<-later+1;200.)|]
        Expect.throwsT<InvalidOperationException> (fun () -> root.AddKeyframes(property,frames,duration=200u,options=options) |> ignore) "cancelled construction"
        Expect.equal (later,writes) (0,0) "owner guard spans providers and publication"

    testCase "parent muting suppresses child lifecycle while retaining property dispatch" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let events=ResizeArray<string>()
        use root=new Timeline(scheduler,onRender=(fun _ -> events.Add "root"))
        let mutable value=10.
        let property=NumericProperty((fun () -> value),(fun next -> value<-next))
        let options={options with OnBegin=(fun _ -> events.Add "begin");OnBeforeUpdate=(fun _ -> events.Add "before")
                                  OnRender=(fun _ -> events.Add "render");OnUpdate=(fun _ -> events.Add "update")}
        use child=root.AddKeyframes(property,Keyframe.values [|0.;100.|],duration=100u,options=options)
        events.Clear()
        root.Seek(50.,muteCallbacks=true)
        Expect.equal value 50. "muted render still writes"
        Expect.isEmpty events "muting inherited by child and root"

    testCase "revert restores captured values and closes replacement registration" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        use root=new Timeline(scheduler)
        let mutable value=10.
        let property=NumericProperty((fun () -> value),(fun next -> value<-next))
        use child=root.AddKeyframes(property,Keyframe.values [|0.;100.|],duration=100u,options=options)
        root.Seek 50.
        child.Revert()
        Expect.equal value 10. "pre-construction snapshot"
        Expect.isTrue child.Cancelled "reverted child inert"
        root.Seek 75.
        Expect.equal value 10. "cancelled root entry cannot dispatch"
        use later=root.AddKeyframes(property,[|Keyframe<NumericProperty,float>.To 200.|],duration=100u,position=TimelinePosition.At 100.,options=options)
        root.Seek 150.
        Expect.equal value 105. "new child captures restored value after registry removal"

    testCase "empty frames skip getters while retaining the source epsilon controller" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        use root=new Timeline(scheduler)
        let property=NumericProperty((fun () -> failtest "empty original getter"),(fun _ -> failtest "empty write"))
        use child=root.AddKeyframes(property,[||],duration=100u,options=options)
        Expect.equal child.Playback.Duration 1e-11 "epsilon no-op"
        root.Complete()
        Expect.isTrue root.Completed "empty owned child can complete"

    testCase "a caller target-array mutation during capture cannot redirect dispatch" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        use root=new Timeline(scheduler)
        let mutable a=10.
        let mutable b=20.
        let mutable replacement=30.
        let other=NumericProperty((fun () -> replacement),(fun value -> replacement<-value))
        let targets=Array.zeroCreate<NumericProperty> 2
        targets[0]<-NumericProperty((fun () -> targets[1]<-other;a),(fun value -> a<-value))
        targets[1]<-NumericProperty((fun () -> b),(fun value -> b<-value))
        use child=root.AddKeyframes(targets,Keyframe.values [|0.;100.|],duration=100u,options=options)
        root.Seek 50.
        Expect.equal (a,b,replacement) (50.,50.,30.) "captured identities remain fixed"
]
