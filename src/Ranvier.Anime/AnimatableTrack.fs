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
    // Guards are only checked while their call runs, so each path keeps one closure over a snapshot it saves and
    // restores: a nested render or retarget (user code seeking this clock) still invalidates the outer one.
    let mutable renderToken=Int64.MinValue
    let mutable readToken=Int64.MinValue
    let readCurrent=Closure.keep (fun () -> readToken=clock.Generation)
    let renderCurrent=Closure.keep (fun () -> clock.IsCurrent renderToken && ownerAllowsWrite())
    let render (sample:PlaybackSample) mode muted =
        let savedToken=renderToken
        renderToken<-clock.WriteToken
        try
            let iteration=clock.IterationDuration
            let delta=sample.AbsoluteTime-clock.PreviousAbsoluteTime
            let force=mode=PlaybackTickMode.Force || (if sample.Backwards then -delta else delta)>=clock.TickThreshold
            let absolute=PropertyNumbers.round 1e12 (clock.Delay+sample.IterationTime)
            let start=PropertyNumbers.round 1e12 clock.Delay
            let eligible=force || ((tweenTime<>iteration || absolute<=start+iteration) && (tweenTime<>0. || absolute>=start))
            if eligible && renderCurrent() then
                tweenTime<-Math.Clamp(sample.IterationTime,0.,iteration)
                let progress=ease(tweenTime/iteration)
                let mutable alive=renderCurrent()
                for index in 0..values.Length-1 do
                    if alive then
                        let output=values[index].TrySample(progress,precision,modifier,renderCurrent)
                        if not(isNull output) && renderCurrent() then
                            if not clock.InternalRender then write index output
                            alive<-renderCurrent()
                        else alive<-false
        finally renderToken<-savedToken
    do clock.ConfigureRenderer(render,?totalDuration=(if iterationDuration=1e-11 then Some 1e-11 else None),
                               onStretch=(fun scale -> tweenTime<-tweenTime*scale))
    // A retarget's arguments, read by the cached prepare closure: mode 0 is one scalar for every target, 1 one scalar
    // per target, 2 components.
    let mutable pendingMode=0
    let mutable pendingScalar=0.
    let mutable pendingTargets:float array=null
    let mutable pendingEase:float -> float=Unchecked.defaultof<_>
    let mutable pendingCommit:unit -> unit=Unchecked.defaultof<_>
    let mutable operationCurrent:unit -> bool=Unchecked.defaultof<_>
    let mutable prepareToken=Int64.MinValue
    let prepareCurrent=Closure.keep (fun () -> operationCurrent() && clock.IsCurrent prepareToken && ownerAllowsWrite())
    let mutable spare:AnimatableValue array=null
    let mutable staging=false
    let prepareCached=Closure.keep <| fun (current:unit -> bool) ->
        let savedCurrent=operationCurrent
        let savedToken=prepareToken
        operationCurrent<-current
        prepareToken<-clock.WriteToken
        // A nested retarget stages into fresh forks; the outermost reuses the spare array.
        let nested=staging
        let staged=
            if nested || isNull spare then values |> Array.map(fun value -> value.Fork())
            else
                for index in 0..values.Length-1 do spare[index].CopyFrom values[index]
                spare
        staging<-true
        try
            let mutable alive=prepareCurrent()
            for index in 0..staged.Length-1 do
                if alive then
                    alive <-
                        match pendingMode with
                        | 0 -> staged[index].RetargetScalar(pendingScalar,modifier,prepareCurrent)
                        | 1 -> staged[index].RetargetScalar(pendingTargets[index],modifier,prepareCurrent)
                        | _ -> staged[index].RetargetComponents(pendingTargets,modifier,prepareCurrent)
            if alive && prepareCurrent() then
                let previous=values
                values<-staged
                if not nested then spare<-previous
                if not(isNull(box pendingEase)) then ease<-pendingEase
                tweenTime<-0.
                if not(isNull(box pendingCommit)) then pendingCommit()
                true
            else false
        finally
            staging<-nested
            operationCurrent<-savedCurrent
            prepareToken<-savedToken
    let retarget mode scalar (targets:float array) (newDuration:float) (newEase:float -> float) (commit:unit -> unit) =
        live()
        if mode=0 then finite "value" scalar
        elif mode=1 then
            if targets.Length<>values.Length then invalidArg "targets" "Expected one scalar destination per captured target."
            for target in targets do finite "value" target
        else for value in targets do finite "components" value
        let components=mode=2
        for value in values do
            let componentTrack=value.Kind=ValueKind.Color || value.Kind=ValueKind.Complex
            if componentTrack<>components then invalidArg "value" "Destination shape must match every captured property."
        let savedMode=pendingMode
        let savedScalar=pendingScalar
        let savedTargets=pendingTargets
        let savedEase=pendingEase
        let savedCommit=pendingCommit
        pendingMode<-mode
        pendingScalar<-scalar
        pendingTargets<-targets
        pendingEase<-newEase
        pendingCommit<-commit
        try clock.RetargetPrecise(prepareCached,newDuration)
        finally
            pendingMode<-savedMode
            pendingScalar<-savedScalar
            pendingTargets<-savedTargets
            pendingEase<-savedEase
            pendingCommit<-savedCommit
    let easeOrNull (newEase:(float -> float) option) =
        match newEase with
        | Some callback ->
            if isNull(box callback) then invalidArg "newEase" "An easing callback is required."
            callback
        | None -> Unchecked.defaultof<_>
    let durationOrNaN (newDuration:float option) =
        match newDuration with
        | Some value ->
            if Double.IsNaN value then invalidArg "newDuration" "Expected finite nonnegative milliseconds."
            value
        | None -> nan
    /// <summary>The property's shared playback controller.</summary>
    member _.Clock=clock
    /// <summary>Number of captured targets.</summary>
    member _.Count=values.Length
    /// <summary>Renders the initial directional cache and property values with muted callbacks.</summary>
    member _.Initialize()=clock.Reset()
    /// <summary>Reads a cached scalar through its getter modifier; cached reads remain available after cancellation.</summary>
    member this.ScalarValue(index)=
        match this.TryScalarValue index with
        | ValueSome value -> Some value
        | ValueNone -> None
    /// <summary>Reads a cached scalar without allocating; ValueNone when a getter modifier superseded the read.</summary>
    member _.TryScalarValue(index)=
        let savedToken=readToken
        readToken<-clock.Generation
        try values[index].TryScalarValue(modifier,readCurrent)
        finally readToken<-savedToken
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
        let newEase=easeOrNull newEase
        retarget 0 value null (durationOrNaN newDuration) newEase Unchecked.defaultof<_>
    /// <summary>Retargets a finite scalar with a duration (NaN keeps it) and an ease (null keeps it), without option wrappers.</summary>
    member _.RetargetScalarPrecise(value,newDuration:float,newEase:float -> float)=
        retarget 0 value null newDuration newEase Unchecked.defaultof<_>
    /// <summary>Atomically retargets one finite scalar per captured target and commits adapter metadata with accepted endpoints.</summary>
    member _.RetargetScalars(targets:float array,?newDuration:float,?newEase:float -> float,?onCommit:unit -> unit)=
        if isNull targets then nullArg "targets"
        let newEase=easeOrNull newEase
        retarget 1 0. (Array.copy targets) (durationOrNaN newDuration) newEase (defaultArg onCommit Unchecked.defaultof<_>)
    /// <summary>Retargets supplied finite components, retaining omitted endpoints, duration and easing.</summary>
    member _.RetargetComponents(components:float array,?newDuration:float,?newEase:float -> float,?onCommit:unit -> unit)=
        if isNull components then nullArg "components"
        let newEase=easeOrNull newEase
        retarget 2 0. (Array.copy components) (durationOrNaN newDuration) newEase (defaultArg onCommit Unchecked.defaultof<_>)
    /// <summary>Stops future dispatch; repeated cancellation is harmless.</summary>
    member _.Cancel()=clock.Cancel()
    interface IDisposable with member _.Dispose()=clock.Cancel()
