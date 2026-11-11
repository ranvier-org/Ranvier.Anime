module Ranvier.Anime.NativeSmoke.EngineCoreNativeProbe

open System
open System.Collections.Generic
open System.Diagnostics
open System.Globalization
open Ranvier.Anime
open Ranvier.Dom
open Ranvier.View.WebCore

type private CountingFrames(inner:FrameScheduler) =
    let frames=HashSet<IDisposable>()
    let errors=ResizeArray<exn>()
    let mutable requested=0
    let mutable delivered=0
    let mutable peak=0
    let mutable disposed=false
    member _.Requested=requested
    member _.Delivered=delivered
    member _.Active=frames.Count
    member _.Peak=peak
    member _.Errors=errors
    interface FrameScheduler with
        member _.Now()=inner.Now()
        member _.Frame(callback)=
            if disposed then raise(ObjectDisposedException "CountingFrames")
            requested<-requested+1
            let mutable active=true
            let mutable physical:IDisposable option=None
            let mutable registration:IDisposable option=None
            let finish cancel =
                if active then
                    active<-false
                    registration |> Option.iter(fun lease -> frames.Remove lease |> ignore)
                    registration<-None
                    let previous=physical
                    physical<-None
                    if cancel then previous |> Option.iter(fun lease -> lease.Dispose())
            let lease={new IDisposable with member _.Dispose()=finish true}
            registration<-Some lease
            frames.Add lease |> ignore
            peak<-max peak frames.Count
            try
                let pending=inner.Frame(fun now ->
                    finish false
                    delivered<-delivered+1
                    try callback now with error -> errors.Add error)
                if active then physical<-Some pending else pending.Dispose()
                lease
            with _ ->
                finish true
                reraise()
        member _.Timeout(delay,callback)=inner.Timeout(delay,callback)
        member _.Microtask(callback)=inner.Microtask callback
        member _.Dispose()=
            if not disposed then
                disposed<-true
                let mutable failure:exn option=None
                for lease in frames |> Seq.toArray do
                    try lease.Dispose() with error -> if failure.IsNone then failure<-Some error
                frames.Clear()
                failure |> Option.iter(fun error -> Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw())

let private require name condition =
    if not condition then failwith("ENGINE_CORE_NATIVE "+name)
    Console.WriteLine("PASS engine core "+name)

let private pumpUntil (host:WebCoreHost) condition =
    let elapsed=Stopwatch.StartNew()
    while not(condition()) && elapsed.ElapsedMilliseconds<3000L do
        host.Cycle(milliseconds=1)
        host.Frame()
    require "real host reached bounded dispatch target" (condition())

let private pumpQuiet (host:WebCoreHost) =
    for _ in 1..12 do
        host.Cycle(milliseconds=2)
        host.Frame()

/// <summary>Checks shared engine admission, in-dispatch terminal cleanup and host reuse through the actual native frame service.</summary>
/// <remarks>Pass the Program's shared Frames.forHost scheduler. Expected callback faults are captured after engine cleanup.</remarks>
let run (host:WebCoreHost) (borrowed:FrameScheduler) =
    let repeated=Frames.forHost host
    try require "forHost creates distinct wrappers; caller shares one scheduler" (not(Object.ReferenceEquals(repeated,borrowed)))
    finally repeated.Dispose()
    use counted=new CountingFrames(borrowed)
    let scheduler=counted:>FrameScheduler
    let element=host.Document.createElement("div") :?> HTMLElement
    let style=(element:>ElementCSSInlineStyle).style
    style.setProperty("width","0px")
    style.setProperty("height","1px")
    host.Document.body.appendChild element |> ignore
    try
        let mutable value=0.
        let mutable writes=0
        let mutable tracking=false
        let mutable firstPropertyFrame:int option=None
        let mutable firstTimerFrame:int option=None
        let mutable updates=0
        let mutable groupUpdates=0
        let mutable armCancel=false
        let mutable cancelledInside=false
        let mutable groupRef:Animatable option=None
        use timer=new Timer(scheduler,1000000u,
            onBeforeUpdate=(fun timer ->
                if armCancel then
                    cancelledInside<-true
                    timer.Cancel()
                    groupRef.Value.Cancel()),
            onUpdate=(fun _ ->
                updates<-updates+1
                if firstTimerFrame.IsNone then firstTimerFrame<-Some counted.Delivered))
        use group=new Animatable(scheduler,onUpdate=(fun _ -> groupUpdates<-groupUpdates+1))
        groupRef<-Some group
        let property=group.Numeric(NumericProperty((fun()->value),(fun next ->
            value<-next
            style.setProperty("width",next.ToString("G17",CultureInfo.InvariantCulture)+"px")
            if tracking then
                writes<-writes+1
                if next>0. && firstPropertyFrame.IsNone then firstPropertyFrame<-Some counted.Delivered)),duration=1000000u)
        tracking<-true
        timer.Resume()
        property.To(1000000.)
        require "independent Timer and Animatable request one native frame" (counted.Requested=1 && counted.Active=1 && counted.Peak=1)
        pumpUntil host (fun()->firstTimerFrame.IsSome && firstPropertyFrame.IsSome)
        require "independent roots sample the same actual native dispatch" (firstTimerFrame=firstPropertyFrame)
        require "native CSS writes follow admitted animatable playback" (value>0. && element.getBoundingClientRect().width>0.)
        require "shared engine retains only its prequeued successor" (counted.Active=1 && counted.Peak=1 && counted.Errors.Count=0)
        armCancel<-true
        pumpUntil host (fun()->cancelledInside)
        require "cancelled clients retain only shared source cleanup" (timer.Cancelled && group.Cancelled && counted.Active=1 && counted.Peak=1)
        let retained=value,writes,updates,groupUpdates,style.getPropertyValue "width"
        pumpUntil host (fun()->counted.Active=0)
        pumpQuiet host
        require "bounded engine cleanup leaves no cancelled-client writes or callbacks"
            ((value,writes,updates,groupUpdates,style.getPropertyValue "width")=retained && counted.Active=0 && counted.Errors.Count=0)

        let primary=InvalidOperationException "native engine primary callback fault"
        let mutable faultWrites=0
        let mutable faultUpdates=0
        let mutable faultGroupRef:Animatable option=None
        use faultTimer=new Timer(scheduler,1000000u,
            onBeforeUpdate=(fun _ -> faultGroupRef.Value.Cancel();raise primary),
            onUpdate=(fun _ -> faultUpdates<-faultUpdates+1))
        use faultGroup=new Animatable(scheduler)
        faultGroupRef<-Some faultGroup
        let mutable faultValue=0.
        let faultProperty=faultGroup.Numeric(NumericProperty((fun()->faultValue),(fun next -> faultValue<-next;faultWrites<-faultWrites+1)),duration=1000000u)
        let initialFaultWrites=faultWrites
        faultTimer.Resume();faultProperty.To(100.)
        pumpUntil host (fun()->counted.Errors.Count>0)
        require "real frame callback fault retains the exact primary exception"
            (counted.Errors.Count=1 && Object.ReferenceEquals(counted.Errors[0],primary))
        require "fault cleanup cancels clients while preserving shared source cleanup"
            (faultTimer.Cancelled && faultGroup.Cancelled && counted.Active=1 && counted.Peak=1 && faultUpdates=0 && faultWrites=initialFaultWrites)
        let retainedFault=faultValue,faultWrites,faultUpdates
        pumpUntil host (fun()->counted.Active=0)
        pumpQuiet host
        require "faulted native roots leave no late writes callbacks or new errors"
            ((faultValue,faultWrites,faultUpdates)=retainedFault && counted.Active=0 && counted.Errors.Count=1)

        let mutable successorUpdates=0
        use successor=new Timer(scheduler,1000000u,onUpdate=(fun _ -> successorUpdates<-successorUpdates+1))
        successor.Resume()
        pumpUntil host (fun()->successorUpdates>0)
        successor.Cancel()
        pumpUntil host (fun()->counted.Active=0)
        require "successor reuses the borrowed scheduler and shared cleanup drains" (successorUpdates>0 && counted.Active=0)
        scheduler.Dispose()
        let mutable directFrames=0
        use direct=borrowed.Frame(fun _ -> directFrames<-directFrames+1)
        pumpUntil host (fun()->directFrames=1)
        require "forwarder disposal preserves the caller's actual native host and scheduler" (directFrames=1 && host.Errors.Count=0)
    finally
        let parent=(element:>Node).parentNode
        if not(isNull(box parent)) then parent.removeChild(element:>Node) |> ignore
