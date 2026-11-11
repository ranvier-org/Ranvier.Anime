module Ranvier.Anime.Tests.TimelineSequenceContractTests

open System
open Expecto
open Ranvier
open Ranvier.Dom
open Ranvier.Anime

type private Cell(initial:float) as this =
    let mutable value=initial
    let mutable reads=0
    let writes=ResizeArray<float>()
    let handle=NumericProperty((fun () -> reads<-reads+1;this.BeforeRead();value),(fun next -> this.BeforeWrite next;value<-next;writes.Add next))
    member val BeforeRead:unit -> unit=ignore with get,set
    member val BeforeWrite:float -> unit=ignore with get,set
    member _.Handle=handle
    member _.Value=value
    member _.Reads=reads
    member _.Writes=writes

let private options={KeyframeAnimationOptions.Default with Ease=id;Precision=None}
let private rows values=KeyframeSequence<NumericProperty,float>.Rows [|KeyframeRow.values values|]

let tests=testList "owned sparse numeric timeline contracts" [
    testCase "sequence aliases and single-property factories replace the same handle" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        use root=new Timeline(scheduler)
        let cell=Cell(10.)
        use first=root.AddKeyframes(cell.Handle,Keyframe.values [|0.;100.;200.|],duration=200u,position=TimelinePosition.At 0.,options=options)
        use second=root.AddKeyframes([|"alias",cell.Handle|],rows [|"alias",300.|],duration=100u,position=TimelinePosition.At 75.,options=options)
        Expect.isEmpty cell.Writes "cache-only construction"
        root.Seek 125.
        Expect.equal cell.Value 187.5 "alias uses sampled predecessor75"
        root.Seek(50.,muteCallbacks=true)
        Expect.equal cell.Value 50. "original child recovers backward"
        root.Cancel()
        Expect.isTrue (first.Cancelled && second.Cancelled) "actual parent ownership"

    testCase "missing bindings and unequal target counts fail before parent pause or getters" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let mutable pauses=0
        use root=new Timeline(scheduler,onPause=(fun _ -> pauses<-pauses+1))
        root.AddTimer(100u) |> ignore
        root.Resume()
        let cell=Cell(0.)
        let sequence=rows [|"x",100.;"y",200.|]
        Expect.throwsT<ArgumentException> (fun () -> root.AddKeyframes([|"x",cell.Handle|],sequence) |> ignore) "missing y"
        Expect.throwsT<ArgumentException> (fun () -> root.AddKeyframes([|"x",[|cell.Handle|];"y",[|cell.Handle;cell.Handle|]|],sequence) |> ignore) "unequal counts"
        Expect.equal (pauses,cell.Reads) (0,0) "constant preflight"
        Expect.isTrue root.Playing "parent remains active"

    testCase "all handle arrays are captured before the first getter mutates caller storage" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        use root=new Timeline(scheduler)
        let first,second,wrong=Cell(10.),Cell(20.),Cell(999.)
        let handles=[|first.Handle;second.Handle|]
        first.BeforeRead<-fun () -> handles[1]<-wrong.Handle
        use child=root.AddKeyframes([|"x",handles|],rows [|"x",100.|],duration=100u,options=options)
        root.Seek 50.
        Expect.equal (first.Value,second.Value,wrong.Value) (55.,60.,999.) "private binding snapshot"
        child.Revert()
        Expect.equal (first.Value,second.Value) (10.,20.) "restoration uses captured handles"

    testCase "a first getter seek prevents later property capture and leaves no pending child" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        use root=new Timeline(scheduler)
        root.AddTimer(100u) |> ignore
        let x,y=Cell(10.),Cell(20.)
        let mutable armed=true
        x.BeforeRead<-fun () -> if armed then armed<-false;root.Seek 25.
        y.BeforeRead<-fun () -> failtest "later property read"
        Expect.throwsT<InvalidOperationException> (fun () -> root.AddKeyframes([|"x",x.Handle;"y",y.Handle|],rows [|"x",100.;"y",200.|],options=options) |> ignore) "superseded"
        Expect.equal (root.CurrentTime,root.IterationDuration,root.Cancelled) (25.,100.,false) "nested parent survives"
        Expect.isEmpty x.Writes "no pending dispatch"

    testCase "an empty sequence captures no values and completes as an owned epsilon child" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        use root=new Timeline(scheduler)
        let cell=Cell(10.)
        cell.BeforeRead<-fun () -> failtest "empty sequence read"
        use child=root.AddKeyframes([|"x",cell.Handle|],KeyframeSequence<NumericProperty,float>.Rows [||],duration=100u,options=options)
        Expect.equal child.Playback.Duration 1e-11 "empty source duration"
        root.Complete()
        Expect.isTrue root.Completed "root completion"
        Expect.isEmpty cell.Writes "no property dispatch"

    testCase "actual owner disposal during capture prevents later properties" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        use graph=new Graph()
        use activation=graph.Activate()
        let owner=Api.createRoot id
        use root=new Timeline(scheduler)
        owner.Attach root
        let x,y=Cell(10.),Cell(20.)
        x.BeforeRead<-owner.Dispose
        y.BeforeRead<-fun () -> failtest "later disposed read"
        Expect.throwsT<InvalidOperationException> (fun () -> root.AddKeyframes([|"x",x.Handle;"y",y.Handle|],rows [|"x",100.;"y",200.|],options=options) |> ignore) "disposed pending factory"
        Expect.isTrue root.Cancelled "actual ownership"

    testCase "restoration attempts later properties and preserves the first setter fault" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        use root=new Timeline(scheduler)
        let x,y=Cell(10.),Cell(20.)
        let sequence=KeyframeSequence<NumericProperty,float>.Offsets [|
            KeyframeOffset.at 0. [|"x",0.;"y",0.|]
            KeyframeOffset.at 1. [|"x",100.;"y",200.|] |]
        use child=root.AddKeyframes([|"x",x.Handle;"y",y.Handle|],sequence,duration=100u,options=options)
        root.Seek 50.
        let fault=InvalidOperationException("restore x")
        x.BeforeWrite<-fun value -> if value=10. then raise fault
        let mutable actual:exn option=None
        try child.Revert() with error -> actual<-Some error
        Expect.isTrue (actual |> Option.exists(fun error -> obj.ReferenceEquals(error,fault))) "fault identity"
        Expect.equal y.Value 20. "later property restored despite first fault"
        Expect.isTrue child.Cancelled "reverted child closed"

    testCase "fractional normalized offsets retain distinct property timing" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        use root=new Timeline(scheduler)
        let x,y=Cell(10.),Cell(20.)
        let sequence=KeyframeSequence<NumericProperty,float>.Offsets [|
            KeyframeOffset.at 0. [|"x",0.;"y",0.|]
            KeyframeOffset.at 0.5 [|"x",100.|]
            KeyframeOffset.at 1. [|"x",200.;"y",400.|] |]
        use child=root.AddKeyframesPrecise([|"x",x.Handle;"y",y.Handle|],sequence,0.5,options=options)
        root.Seek 0.125
        Expect.equal (x.Value,y.Value) (50.,100.) "per-property normalized midpoint"
]
