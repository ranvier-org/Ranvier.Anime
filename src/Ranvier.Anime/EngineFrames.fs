namespace Ranvier.Anime

open System
open System.Collections.Generic
open System.Runtime.CompilerServices
open Ranvier.Dom

/// <summary>Forwards source Clock FPS increases to the existing global playback-default cell.</summary>
module internal EngineDefaults =
    let mutable private frameRate=240.
    let mutable private changed:float -> unit=ignore
    /// <summary>Raises future constructor defaults while preserving current explicit factory options.</summary>
    let raiseFrameRate value=frameRate<-Math.Max(frameRate,value);changed value
    /// <summary>Attaches the existing playback-default cell after its declaration.</summary>
    let attach callback=changed<-callback;callback frameRate

/// <summary>The source Clock's tolerance, remainder and admitted-frame delta history.</summary>
type internal EngineClock(initialTime:float) =
    let mutable fps=240.
    let mutable lastTick=initialTime
    let mutable lastTime=initialTime
    let mutable delta=0.
    /// <summary>Finite frame ceiling, clamped to the source minimum.</summary>
    member _.Fps
        with get()=fps
        and set value=
            if not(Double.IsFinite value) then invalidArg "value" "Expected finite FPS."
            fps<-Math.Max(1e-11,value)
            EngineDefaults.raiseFrameRate fps
    /// <summary>The phase retained after the last admitted request.</summary>
    member _.LastTick=lastTick
    /// <summary>Elapsed host milliseconds between admitted engine updates.</summary>
    member _.Delta=delta
    /// <summary>Admits source jitter tolerance and carries full-frame remainders.</summary>
    member _.Request(time:float) =
        let duration=1000./fps
        let elapsed=time-lastTick
        if elapsed+Math.Min(duration*0.25,4.)<duration then false
        else
            lastTick<-if elapsed>=duration then time-elapsed%duration else time
            true
    /// <summary>Updates delta after admission; wake requests preserve delta history.</summary>
    member _.ComputeDelta(time:float)=delta<-time-lastTime;lastTime<-time

type private EngineFrameEntry() =
    member val Callback:(float -> unit) option=None with get,set
    member val Node:LinkedListNode<EngineFrameEntry> option=None with get,set
    member val Version=0L with get,set
    member val Closed=false with get,set
    member val ResetTime:unit -> unit=ignore with get,set
    member val Priority=1. with get,set

/// <summary>Shared source engine admission and cached-next root traversal for an existing host scheduler.</summary>
type internal EngineFrameQueue(scheduler:FrameScheduler) =
    let entries=LinkedList<EngineFrameEntry>()
    let clock=EngineClock(scheduler.Now())
    let mutable pending:IDisposable option=None
    let mutable requestGeneration=0L
    let mutable logicalRequest=false
    let mutable pumping=false
    let mutable paused=true
    let mutable speed=1.
    let mutable seconds=false
    let mutable suspended=false
    let mutable useDefaultMainLoop=true
    let mutable frameSpeed=1.
    let mutable frameFps=240.
    let cancelRequest () =
        requestGeneration<-requestGeneration+1L
        let previous=pending
        pending<-None
        previous |> Option.iter(fun lease -> lease.Dispose())
    let resetRoots () =
        let mutable cursor=entries.First
        while not(isNull cursor) do
            let next=cursor.Next
            cursor.Value.ResetTime()
            cursor<-next
    let update () =
        pumping<-true
        try
            let now=scheduler.Now()
            if clock.Request now then
                clock.ComputeDelta now
                frameSpeed<-speed
                frameFps<-clock.Fps
                let mutable cursor=entries.First
                while not(isNull cursor) do
                    let next=cursor.Next
                    let entry=cursor.Value
                    match entry.Callback with
                    | Some callback when not entry.Closed ->
                        entry.Callback<-None
                        callback now
                    | _ ->
                        entries.Remove cursor
                        entry.Node<-None
                    cursor<-next
        finally
            pumping<-false
    let rec schedule wake allowPump =
        if (not wake || useDefaultMainLoop) && entries.Count>0 && pending.IsNone && (allowPump || not pumping) && not suspended then
            if wake && not logicalRequest then clock.Request(scheduler.Now()) |> ignore
            logicalRequest<-true
            requestGeneration<-requestGeneration+1L
            let expected=requestGeneration
            pending<-Some(scheduler.Frame(fun _ ->
                if expected=requestGeneration then
                    pending<-None
                    if entries.Count>0 then
                        schedule false false
                        update()
                    else logicalRequest<-false))
    /// <summary>Enables host frame requests; manual Update remains available while disabled.</summary>
    member _.UseDefaultMainLoop with get()=useDefaultMainLoop and set value=useDefaultMainLoop<-value
    /// <summary>Samples the host's current time through the shared gate, including when the automatic loop is paused.</summary>
    member _.Update()=update()
    /// <summary>Source engine clock settings and last admitted phase.</summary>
    member _.Clock=clock
    /// <summary>Effective multiplier, including seconds mode's 0.001 scale.</summary>
    member _.EffectiveSpeed=speed
    /// <summary>The engine speed captured before the current cached-next traversal.</summary>
    member _.FrameSpeed=if pumping then frameSpeed else speed
    /// <summary>The engine frame ceiling captured before the current cached-next traversal.</summary>
    member _.FrameFps=if pumping then frameFps else clock.Fps
    /// <summary>The source forced-render gap in the selected engine time unit.</summary>
    member _.TickThreshold=if seconds then 0.2 else 200.
    /// <summary>Authored engine playback rate; existing roots reanchor at their current playhead.</summary>
    member _.Speed
        with get()=speed*(if seconds then 1000. else 1.)
        and set value=
            if not(Double.IsFinite value) || value<=0. then invalidArg "value" "Expected positive finite engine speed."
            let next=value*(if seconds then 0.001 else 1.)
            if speed<>next then speed<-next;resetRoots()
    /// <summary>Changes the scale of future elapsed samples without reanchoring active roots.</summary>
    member _.Seconds
        with get()=seconds
        and set value=
            if value<>seconds then
                seconds<-value
                speed<-speed*(if value then 0.001 else 1000.)
    /// <summary>Source pause flag; initial wake leaves this flag unchanged.</summary>
    member _.Paused=paused
    /// <summary>Stops the scheduled host request while retaining root order and playheads.</summary>
    member _.Pause() =
        if logicalRequest then
            paused<-true
            suspended<-true
            logicalRequest<-false
            cancelRequest()
    /// <summary>Resets active root anchors before scheduling after an engine pause.</summary>
    member _.Resume() =
        if paused then
            paused<-false
            suspended<-false
            resetRoots()
            schedule true true
    /// <summary>Creates a source root scheduler lease with stable insertion order and forwarded delayed work.</summary>
    member _.CreateScheduler() =
        let initialTick=clock.LastTick
        if not logicalRequest then clock.Request(scheduler.Now()) |> ignore
        let entry=EngineFrameEntry()
        let mutable disposed=false
        let work=HashSet<IDisposable>()
        let live ()=if disposed then raise(ObjectDisposedException(nameof FrameScheduler))
        let scheduleWork (schedule:(unit -> unit) -> IDisposable) callback =
            live()
            let mutable active=true
            let mutable registration:IDisposable option=None
            let mutable inner:IDisposable option=None
            let finish dispose =
                active<-false
                registration |> Option.iter(fun lease -> work.Remove lease |> ignore)
                registration<-None
                let previous=inner
                inner<-None
                if dispose then previous |> Option.iter(fun lease -> lease.Dispose())
            let lease={new IDisposable with member _.Dispose()=if active then finish true}
            registration<-Some lease
            work.Add lease |> ignore
            try
                let pending=schedule(fun () ->
                    if active then
                        finish false
                        if not disposed then callback())
                if active then inner<-Some pending else pending.Dispose()
                lease
            with _ -> finish true;reraise()
        let cancelWork () =
            let mutable failure:exn option=None
            for lease in work |> Seq.toArray do
                try lease.Dispose() with error -> if failure.IsNone then failure<-Some error
            work.Clear()
            failure |> Option.iter(fun error -> System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw())
        let adapter=
            { new FrameScheduler with
                member _.Now()=scheduler.Now()
                member _.Frame(callback) =
                    live()
                    entry.Version<-entry.Version+1L
                    let expected=entry.Version
                    entry.Callback<-Some callback
                    if entry.Node.IsNone then
                        let mutable previous=entries.Last
                        while not(isNull previous) && previous.Value.Priority>entry.Priority do previous<-previous.Previous
                        entry.Node<-Some(if isNull previous then entries.AddFirst entry else entries.AddAfter(previous,entry))
                    if not pumping then suspended<-false
                    schedule true false
                    {new IDisposable with member _.Dispose()=if expected=entry.Version then entry.Callback<-None}
                member _.Timeout(delay,callback)=scheduleWork (fun action -> scheduler.Timeout(delay,action)) callback
                member _.Microtask(callback)=scheduleWork scheduler.Microtask callback
                member _.Dispose()=
                    disposed<-true
                    entry.Closed<-true
                    entry.ResetTime<-ignore
                    entry.Version<-entry.Version+1L
                    entry.Callback<-None
                    cancelWork() }
        let terminate () =
            entry.Callback<-None
            cancelWork()
        adapter,(fun callback priority -> entry.ResetTime<-callback;entry.Priority<-priority),initialTick,terminate

type private EngineRoot(queue:EngineFrameQueue,configure:(unit -> unit) -> float -> unit,initialTick:float,terminate:unit -> unit) =
    member _.Queue=queue
    member _.InitialTick=initialTick
    member _.Terminate()=terminate()
    member _.Configure callback priority=configure callback priority

/// <summary>Host-keyed engine roots shared by timers, timelines and animatables.</summary>
module internal EngineFrames =
    let private queues=ConditionalWeakTable<FrameScheduler,EngineFrameQueue>()
    let private roots=ConditionalWeakTable<FrameScheduler,EngineRoot>()
    /// <summary>Resolves the engine of a raw host scheduler or an existing engine root adapter.</summary>
    let forScheduler scheduler =
        if isNull(box scheduler) then nullArg "scheduler"
        match roots.TryGetValue scheduler with
        | true,root -> root.Queue
        | _ -> queues.GetValue(scheduler,fun scheduler -> EngineFrameQueue scheduler)
    /// <summary>Retains existing root identity when a factory passes its adapter into a timer.</summary>
    let createRoot scheduler =
        if isNull(box scheduler) then nullArg "scheduler"
        match roots.TryGetValue scheduler with
        | true,_ -> scheduler
        | _ ->
            let queue=forScheduler scheduler
            let adapter,configure,initialTick,terminate=queue.CreateScheduler()
            roots.Add(adapter,EngineRoot(queue,configure,initialTick,terminate))
            adapter
    /// <summary>Captures the source Timer clock phase before an idle-engine construction wake request.</summary>
    let initialTick scheduler =
        match roots.TryGetValue scheduler with
        | true,root -> root.InitialTick
        | _ -> invalidArg "scheduler" "Expected an engine root adapter."
    /// <summary>Stops client sampling and delayed work while retaining the source engine's bounded cleanup frames and wake phase.</summary>
    let terminate scheduler =
        match roots.TryGetValue scheduler with
        | true,root -> root.Terminate()
        | _ -> ()
    /// <summary>Registers timer anchor reset and source priority before its first scheduled sample.</summary>
    let configure scheduler resetTime priority =
        match roots.TryGetValue scheduler with
        | true,root -> root.Configure resetTime priority
        | _ -> invalidArg "scheduler" "Expected an engine root adapter."
