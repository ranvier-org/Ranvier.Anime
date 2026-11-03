namespace Ranvier.Anime

open System
open Ranvier.Dom
open Ranvier.Anime.ValueInterpolation

/// <summary>A paused retargetable property track with cached values, shared timing and guarded multi-target dispatch.</summary>
/// <remarks>Initialize explicitly. Precise duration is finite and nonnegative; cancellation is terminal.</remarks>
type internal AnimatableTrack(scheduler:FrameScheduler,kernels:AnimatableValue array,write:int -> Output -> unit,duration:float,
                              options:PlaybackOptions,initialEase:float -> float,modifier:float -> float,precision:int,
                              ?onBegin:Timer -> unit,?onPause:Timer -> unit,?onComplete:Timer -> unit) =
    let finite name value = if not(Double.IsFinite value) then invalidArg name "Expected finite input."
    do
        if isNull kernels || kernels.Length=0 then invalidArg "kernels" "Expected at least one captured target."
        if kernels |> Array.exists(fun value -> isNull(box value)) then invalidArg "kernels" "Captured targets are required."
        finite "duration" duration
        if duration<0. then invalidArg "duration" "Expected nonnegative milliseconds."
        if isNull(box initialEase) || isNull(box modifier) || isNull(box write) then invalidArg "callbacks" "Callbacks are required."
        if precision < -1 || precision>308 then invalidArg "precision" "Expected disabled rounding or at most 308 decimal places."
    let iterationDuration=PropertyNumbers.round 1e12 (if duration=0. then 1e-11 else duration)
    do if iterationDuration=0. then invalidArg "duration" "Positive duration rounds to zero at source timing precision."
    let mutable values=kernels |> Array.map(fun value -> value.Fork())
    let mutable ease=initialEase
    let mutable tweenTime=0.
    let ownerAllowsWrite=ScopeRegistration.captureWriteGuard()
    let clock=ScopeRegistration.run ignore (fun () ->
        new Timer(scheduler,iterationDuration,options=options,?onBegin=onBegin,?onPause=onPause,?onComplete=onComplete))
    let live () = if clock.Cancelled || not(ownerAllowsWrite()) then raise(ObjectDisposedException(nameof AnimatableTrack))
    let render (sample:PlaybackSample) mode muted =
        let timerCurrent=clock.CaptureWriteGuard()
        let current()=timerCurrent() && ownerAllowsWrite()
        let iteration=clock.IterationDuration
        let delta=sample.AbsoluteTime-clock.PreviousAbsoluteTime
        let force=mode=PlaybackTickMode.Force || (if sample.Backwards then -delta else delta)>=clock.TickThreshold
        let absolute=PropertyNumbers.round 1e12 (clock.Delay+sample.IterationTime)
        let start=PropertyNumbers.round 1e12 clock.Delay
        let eligible=force || ((tweenTime<>iteration || absolute<=start+iteration) && (tweenTime<>0. || absolute>=start))
        if eligible && current() then
            tweenTime<-Math.Clamp(sample.IterationTime,0.,iteration)
            let progress=ease(tweenTime/iteration)
            let mutable alive=current()
            for index in 0..values.Length-1 do
                if alive then
                    match values[index].Sample(progress,precision,modifier,current) with
                    | Some output when current() ->
                        if not clock.InternalRender then write index output
                        alive<-current()
                    | _ -> alive<-false
    do clock.ConfigureRenderer(render,?totalDuration=(if iterationDuration=1e-11 then Some 1e-11 else None),
                               onStretch=(fun scale -> tweenTime<-tweenTime*scale))
    let retarget (scalars:float array option) (components:float array option) newDuration newEase commit =
        live()
        newEase |> Option.iter(fun callback -> if isNull(box callback) then invalidArg "newEase" "An easing callback is required.")
        scalars |> Option.iter(fun targets ->
            if targets.Length<>values.Length then invalidArg "targets" "Expected one scalar destination per captured target."
            for target in targets do finite "value" target)
        components |> Option.iter(fun values -> for value in values do finite "components" value)
        for value in values do
            let componentTrack=value.Kind=ValueKind.Color || value.Kind=ValueKind.Complex
            if componentTrack<>components.IsSome then invalidArg "value" "Destination shape must match every captured property."
        let prepare operationCurrent =
            let inherited=clock.CaptureWriteGuard()
            let current () = operationCurrent() && inherited() && ownerAllowsWrite()
            let staged=values |> Array.map(fun value -> value.Fork())
            let mutable alive=current()
            for index in 0..staged.Length-1 do
                if alive then
                    alive <-
                        match scalars,components with
                        | Some targets,_ -> staged[index].RetargetScalar(targets[index],modifier,current)
                        | _,Some targets -> staged[index].RetargetComponents(targets,modifier,current)
                        | _ -> false
            if alive && current() then
                values<-staged
                newEase |> Option.iter(fun callback -> ease<-callback)
                tweenTime<-0.
                commit |> Option.iter(fun action -> action())
                true
            else false
        clock.Retarget(prepare,?newDuration=newDuration)
    /// <summary>The property's shared playback controller.</summary>
    member _.Clock=clock
    /// <summary>Number of captured targets.</summary>
    member _.Count=values.Length
    /// <summary>Renders the initial directional cache and property values with muted callbacks.</summary>
    member _.Initialize()=clock.Reset()
    /// <summary>Reads a cached scalar through its getter modifier; cached reads remain available after cancellation.</summary>
    member _.ScalarValue(index)=
        let expected=clock.Generation
        values[index].ScalarValue(modifier,fun () -> expected=clock.Generation)
    /// <summary>Returns an independent snapshot of cached color or compound components.</summary>
    member _.Components(index)=values[index].ComponentValues
    /// <summary>Returns a composed cached snapshot, applying only the scalar getter modifier.</summary>
    member _.Snapshot(index)=
        let expected=clock.Generation
        values[index].Snapshot(modifier,fun () -> expected=clock.Generation)
    /// <summary>The captured scalar/unit/color/compound category.</summary>
    member _.Kind(index)=values[index].Kind
    /// <summary>Retargets a finite scalar, retaining omitted duration and easing.</summary>
    member _.RetargetScalar(value,?newDuration:float,?newEase:float -> float)=
        retarget (Some(Array.create values.Length value)) None newDuration newEase None
    /// <summary>Atomically retargets one finite scalar per captured target and commits adapter metadata with accepted endpoints.</summary>
    member _.RetargetScalars(targets:float array,?newDuration:float,?newEase:float -> float,?onCommit:unit -> unit)=
        if isNull targets then nullArg "targets"
        retarget (Some(Array.copy targets)) None newDuration newEase onCommit
    /// <summary>Retargets supplied finite components, retaining omitted endpoints, duration and easing.</summary>
    member _.RetargetComponents(components:float array,?newDuration:float,?newEase:float -> float,?onCommit:unit -> unit)=
        if isNull components then nullArg "components"
        retarget None (Some(Array.copy components)) newDuration newEase onCommit
    /// <summary>Stops future dispatch; repeated cancellation is harmless.</summary>
    member _.Cancel()=clock.Cancel()
    interface IDisposable with member _.Dispose()=clock.Cancel()
