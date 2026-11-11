namespace Ranvier.Anime

open System
open Ranvier.Dom

/// <summary>A paused timeline with shared repeat/direction controls, numeric property tracks and borrowed controllers.</summary>
/// <remarks>Use the scheduler's owning thread. Resume includes a 12 ms clock lead. Disposal is terminal and preserves child values.
/// Optional playback easing remaps every child from the directional iteration time; CurrentTime remains raw.
/// Callback snapshots expose mapped IterationCurrentTime. Internal construction renders retain raw time.</remarks>
type Timeline(scheduler: FrameScheduler, ?options: PlaybackOptions, ?playbackEase: (float -> float),
              ?onBegin: Timeline -> unit, ?onBeforeUpdate: Timeline -> unit,
              ?onRender: Timeline -> unit, ?onUpdate: Timeline -> unit,
              ?onLoop: Timeline -> unit, ?onPause: Timeline -> unit,
              ?onComplete: Timeline -> unit) as this =
    do playbackEase |> Option.iter(fun ease -> if isNull(box ease) then nullArg "playbackEase")
    let frameSiblings=FrameSiblingRegistry()
    let pending=System.Collections.Generic.List<IDisposable>()
    let children = System.Collections.Generic.List<TimelineEntry>()
    let propertySiblings = System.Collections.Generic.Dictionary<NumericProperty, System.Collections.Generic.List<NumericPropertyTrack>>()
    let borrowedProperties = System.Collections.Generic.Dictionary<obj, NumericProperty>(System.Collections.Generic.ReferenceEqualityComparer.Instance)
    let mutable borrowedGuard = fun () -> true
    let labels = System.Collections.Generic.Dictionary<string, float>(System.StringComparer.Ordinal)
    let mutable duration = 0.
    let mutable hasEntries = false
    let mutable sampledTime = 0.
    let mutable playbackIterationTime = 0.
    let mutable playbackRootGeneration = Int32.MinValue
    let mutable playbackClockGeneration = ValueNone
    let mutable preparePlaybackState: unit -> bool = fun () -> true
    let notify callback =
        if playbackEase.IsNone || preparePlaybackState() then callback |> Option.iter(fun callback -> callback this)
    let mutable cancelled = false
    let mutable reverting = false
    let restorations=System.Collections.Generic.List<(unit -> bool) -> unit>()
    let mutable generation = 0
    let mutable ancestorLive = ScopeRegistration.captureWriteGuard()
    let mutable internalRendering = false
    let mutable internalTime=0.
    let clock = ScopeRegistration.run ignore (fun () -> new Timer(scheduler, 0., ?options=options,
        onBegin=(fun _ -> notify onBegin),
        onBeforeUpdate=(fun _ -> notify onBeforeUpdate),
        onUpdate=(fun _ -> notify onUpdate),
        onLoop=(fun _ -> notify onLoop),
        onPause=(fun _ -> onPause |> Option.iter(fun callback -> callback this)),
        onComplete=(fun _ -> onComplete |> Option.iter(fun callback -> callback this))))
    let finite name value = if not(Double.IsFinite value) then invalidArg name "Expected a finite time."
    let ensureLive () = if cancelled then raise(ObjectDisposedException("Timeline"))
    let stopFrame () = generation <- generation + 1
    let cancel () =
        cancelled <- true
        restorations.Clear()
        stopFrame()
        let mutable firstError : Exception option = None
        try clock.Cancel() with error -> firstError <- Some error
        for entry in children.ToArray() do
            match entry with
            | TimelineEntry.OwnedTimer(_, timer) | TimelineEntry.OwnedAnimation(_,timer,_,_) ->
                try timer.Cancel()
                with error -> if firstError.IsNone then firstError <- Some error
            | _ -> ()
        let unfinished=pending.ToArray()
        pending.Clear()
        for child in unfinished do
            try child.Dispose()
            with error -> if firstError.IsNone then firstError<-Some error
        children.Clear()
        frameSiblings.Clear()
        propertySiblings.Clear()
        borrowedProperties.Clear()
        labels.Clear()
        firstError |> Option.iter(fun error -> System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw())
    do clock.OnFault <- cancel
    let canRender expected clockGeneration () =
        expected = generation && clockGeneration = clock.Generation && not cancelled && ancestorLive()
    let preparePlayback() =
        let expected=generation
        let clockGeneration=clock.Generation
        let current=canRender expected clockGeneration
        if current() then
            if playbackRootGeneration<>expected || playbackClockGeneration<>ValueSome clockGeneration then
                let raw=clock.Sample.IterationTime
                let mapped=
                    match playbackEase with
                    | None -> raw
                    | Some ease ->
                        let value=duration*ease(raw/duration)
                        if Double.IsNaN value || value=0. then 0. else value
                if current() then
                    playbackIterationTime<-mapped
                    playbackRootGeneration<-expected
                    playbackClockGeneration<-ValueSome clockGeneration
            current()
        else false
    do preparePlaybackState<-preparePlayback
    let resetTracks () =
        for entry in children do
            match entry with
            | TimelineEntry.Property track | TimelineEntry.Borrowed(track, _, _, _) -> track.ResetTimer()
            | TimelineEntry.OwnedTimer(_, timer) | TimelineEntry.OwnedAnimation(_,timer,_,_) -> timer.ResetChildFlags()
            | TimelineEntry.Callback(_, _, state) -> state.Completed <- false
    let renderMode ancestor muteCallbacks mode internalRender requestedTime =
        let force = mode = PlaybackTickMode.Force
        let expected = generation
        let clockGeneration = clock.Generation
        let previous = sampledTime
        let backwards = if internalRender && not internalRendering then requestedTime < previous else clock.Backwards
        let canWrite () = canRender expected clockGeneration () && ancestor()
        let prepared=playbackEase.IsNone || (canWrite() && preparePlayback())
        let time=if internalRender || playbackEase.IsNone then requestedTime else playbackIterationTime
        if prepared && canWrite() then sampledTime<-requestedTime
        let snapshots = children.ToArray()
        let snapshots = if backwards then Array.rev snapshots else snapshots
        let mutable allComplete = true
        let mutable rendered = false
        for entry in snapshots do
            let childTime = PropertyNumbers.round 1e12 ((time - entry.Offset) * entry.Speed)
            if canWrite() && not(backwards && childTime > entry.Duration) then
                match entry with
                | TimelineEntry.Property track ->
                    track.Sample(childTime, force, internalRender, muteCallbacks, backwards, clock.Began, canWrite, skipRender=(mode = PlaybackTickMode.Skip))
                    rendered <- rendered || track.Rendered
                    allComplete <- allComplete && track.Completed
                | TimelineEntry.OwnedTimer(_, child) | TimelineEntry.OwnedAnimation(_,child,_,_) ->
                    let version=match entry with TimelineEntry.OwnedAnimation(_,_,read,_) -> Some(read()) | _ -> None
                    let childBackwards=
                        match entry with TimelineEntry.OwnedTimer _ when internalRender -> requestedTime<previous | _ -> backwards
                    child.SampleChild(childTime, mode, muteCallbacks, clock.Fps, childBackwards, clock.Began, canWrite,internalRender=internalRender)
                    match entry,version with
                    | TimelineEntry.OwnedAnimation(_,_,read,_),Some before -> rendered<-rendered || (read()<>before && child.Sample.AboveZero)
                    | _ -> ()
                    allComplete <- allComplete && (child.Completed || child.Cancelled)
                | TimelineEntry.Borrowed(track, _, _, _) ->
                    let previousGuard = borrowedGuard
                    borrowedGuard <- canWrite
                    try
                        track.Sample(childTime, force, internalRender, muteCallbacks, backwards, clock.Began, canWrite,
                                     skipRender=(mode = PlaybackTickMode.Skip))
                        rendered <- rendered || track.Rendered
                        allComplete <- allComplete && track.Completed
                    finally borrowedGuard <- previousGuard
                | TimelineEntry.Callback(_, callback, state) when not internalRender ->
                    state.Time <- childTime
                    state.Backwards <- backwards
                    if not muteCallbacks &&
                       ((clock.Began && not backwards && childTime > 0. && not state.Completed) ||
                        (backwards && childTime <= 1e-11 && state.Completed)) then
                        state.Completed <- not backwards
                        callback()
                    allComplete <- allComplete && state.Completed
                | _ -> allComplete <- false
        if not internalRender && rendered && not muteCallbacks && canWrite() then
            onRender |> Option.iter(fun callback -> callback this)
        allComplete
    let prepareLoop (sample: PlaybackSample) _ muted =
        if not internalRendering then
            let canWrite = canRender generation clock.Generation
            for entry in children.ToArray() do
                if canWrite() then
                    if not sample.Backwards then
                        match entry with
                        | TimelineEntry.Property track | TimelineEntry.Borrowed(track, _, _, _) ->
                            if not track.Completed && not track.Backwards && track.CurrentTime < track.Duration then
                                track.Sample(duration, true, true, muted, false, clock.Began, canWrite)
                            if canWrite() then track.ResetTimer()
                        | TimelineEntry.OwnedTimer(_, child) | TimelineEntry.OwnedAnimation(_,child,_,_) ->
                            if not child.Cancelled && not child.Completed && not child.Backwards && child.Sample.AbsoluteTime < child.IterationDuration then
                                child.SampleChild(duration, PlaybackTickMode.Force, muted, infinity, false, clock.Began, canWrite,internalRender=true,applyFrameRate=false)
                            if canWrite() then child.ResetChildFlags()
                        | TimelineEntry.Callback(_, callback, state) ->
                            if not state.Completed && not state.Backwards && state.Time < 1e-11 then
                                state.Time <- duration
                                state.Backwards <- false
                                if not muted then
                                    state.Completed <- true
                                    callback()
                            if canWrite() then state.Completed <- false
                    else
                        match entry with
                        | TimelineEntry.OwnedTimer(offset, child) | TimelineEntry.OwnedAnimation(offset,child,_,_) when not muted &&
                            child.Duration <= 1e-11 && (offset + child.Delay = 0. || offset + child.Delay + child.Duration = duration) ->
                            child.NotifyBoundaryCompletion()
                        | TimelineEntry.Callback(offset, callback, _) when not muted && (offset = 0. || offset + 1e-11 = duration) ->
                            callback()
                        | _ -> ()
    do clock.ConfigureChildren(prepareLoop, fun sample mode muted ->
        renderMode (fun () -> true) muted mode internalRendering
            (if internalRendering then internalTime else sample.IterationTime))
    let prepareInternal time action =
        let previousMode=internalRendering
        let previousTime=internalTime
        internalRendering<-true
        internalTime<-time
        try action()
        finally internalRendering<-previousMode;internalTime<-previousTime
    let resize () = prepareInternal 0. (fun () -> clock.Resize duration)
    let externalControl action =
        let previousMode=internalRendering
        internalRendering<-false
        try action() finally internalRendering<-previousMode
    let seek ancestor muteCallbacks milliseconds =
        ensureLive()
        finite "milliseconds" milliseconds
        stopFrame()
        let previous = ancestorLive
        ancestorLive <- fun () -> previous() && ancestor()
        try externalControl(fun () -> clock.Seek(milliseconds, muteCallbacks=muteCallbacks))
        finally ancestorLive <- previous
    let addChild (entry: TimelineEntry) =
        let finish = entry.Offset + entry.Duration
        finite "position" finish
        stopFrame()
        let expected = generation
        match entry with
        | TimelineEntry.Borrowed(_, _, pause, _) -> pause()
        | _ -> ()
        if expected = generation && not cancelled then
            children.Add entry
            hasEntries <- true
            duration <- Math.Max(duration, finish)
            try
                resize()
                if expected = generation && not cancelled then resetTracks()
            with _ ->
                cancel()
                reraise()

    do ScopeRegistration.register this.Revert

    /// <summary>Constructs an owned animation; work superseded before publication is disposed and returns None.</summary>
    /// <remarks>Once published, the child handle is returned even when a reset callback supersedes initialization or cancels it.</remarks>
    member internal this.TryAddOwned(construct:TimelineChildContext -> TimelinePreparedChild<'T> option,
                                     ?position:TimelinePosition,?zeroDuration:bool) =
        ensureLive()
        if isNull(box construct) then nullArg "construct"
        let offset=this.ResolvePosition(?position=position)-(if defaultArg zeroDuration false then 1e-11 else 0.)
        stopFrame()
        let expected=generation
        let current()=expected=generation && not cancelled && ancestorLive() && ScopeRegistration.canConstruct()
        let attached=System.Collections.Generic.List<IDisposable>()
        let mutable published=false
        let attach (child:IDisposable) =
            if isNull(box child) then nullArg "child"
            if current() then
                pending.Add child
                attached.Add child
            else child.Dispose()
        try
            try
                clock.Pause()
                if current() && not hasEntries then clock.ConfigureRenderer((fun _ _ _ -> ()),totalDuration=0.)
                if current() then prepareInternal offset (fun () -> clock.Seek(offset,muteCallbacks=true))
                if not(current()) then None
                else
                    let context=TimelineChildContext(scheduler,offset,current,attach,frameSiblings)
                    match construct context with
                    | Some prepared when current() && not prepared.Clock.Cancelled ->
                        let finish=offset+prepared.Clock.Delay+prepared.Clock.Duration
                        finite "position" finish
                        if not(prepared.Commit()) || not(current()) then None
                        else
                            children.Add(TimelineEntry.OwnedAnimation(offset,prepared.Clock,prepared.RenderVersion,box prepared.Result))
                            restorations.Add prepared.Restore
                            published<-true
                            for child in attached do pending.Remove child |> ignore
                            hasEntries<-true
                            duration<-Math.Max(duration,finish)
                            resize()
                            if current() then resetTracks()
                            Some prepared.Result
                    | _ -> None
            with _ ->
                try cancel() with _ -> ()
                reraise()
        finally
            if not published then
                for child in attached do
                    pending.Remove child |> ignore
                    try child.Dispose() with _ -> ()

    member private this.Contains(candidate: Timeline) =
        Object.ReferenceEquals(this, candidate) ||
        children.Exists(function TimelineEntry.Borrowed(_, Some child, _, _) -> child.Contains candidate | _ -> false)

    /// <summary>Resolves a position against the current duration, labels and previous child.</summary>
    member _.ResolvePosition(?position: TimelinePosition) =
        ensureLive()
        let previous =
            if children.Count = 0 then None
            else let entry = children[children.Count - 1] in Some(entry.Offset, entry.Offset + entry.Duration)
        TimelinePositions.resolve duration previous
            (fun label -> match labels.TryGetValue label with true, value -> Some value | _ -> None) position

    /// <summary>Records a label at an explicit position or the current timeline end.</summary>
    member this.Label(name: string, ?position: TimelinePosition) =
        ensureLive()
        if isNull name then nullArg "name"
        labels[name] <- this.ResolvePosition(?position = position)
        this

    /// <summary>Adds an owned timer track and returns its observable controller; scheduling belongs to this timeline.</summary>
    /// <remarks>Parent cancellation disposes this child. Parent muting suppresses its callbacks. Positions precede child delay.</remarks>
    member this.AddTimer(milliseconds: float, ?position: TimelinePosition, ?options: PlaybackOptions,
                         ?onBegin: Timer -> unit, ?onBeforeUpdate: Timer -> unit, ?onUpdate: Timer -> unit,
                         ?onLoop: Timer -> unit, ?onPause: Timer -> unit, ?onComplete: Timer -> unit) =
        ensureLive()
        if Double.IsNaN milliseconds || milliseconds < 0. then invalidArg "milliseconds" "Expected nonnegative milliseconds."
        let offset = this.ResolvePosition(?position=position) - (if milliseconds <= 1e-11 then 1e-11 else 0.)
        let child = ScopeRegistration.run ignore (fun () ->
            new Timer(scheduler, milliseconds, ?options=options, ?onBegin=onBegin, ?onBeforeUpdate=onBeforeUpdate,
                      ?onUpdate=onUpdate, ?onLoop=onLoop, ?onPause=onPause, ?onComplete=onComplete))
        try
            child.AttachToTimeline()
            addChild(TimelineEntry.OwnedTimer(offset,child))
            if cancelled then child.Cancel()
            child
        with _ ->
            child.Cancel()
            reraise()
    /// <summary>Adds an owned timer using unsigned whole milliseconds.</summary>
    member this.AddTimer(milliseconds: uint, ?position: TimelinePosition, ?options: PlaybackOptions,
                         ?onBegin: Timer -> unit, ?onBeforeUpdate: Timer -> unit, ?onUpdate: Timer -> unit,
                         ?onLoop: Timer -> unit, ?onPause: Timer -> unit, ?onComplete: Timer -> unit) =
        this.AddTimer(float milliseconds, ?position=position, ?options=options, ?onBegin=onBegin,
                      ?onBeforeUpdate=onBeforeUpdate, ?onUpdate=onUpdate, ?onLoop=onLoop, ?onPause=onPause, ?onComplete=onComplete)

    member private this.Borrow(identity: obj, childDuration: float, pause: unit -> unit,
                               write: float -> (unit -> bool) -> unit, nested: Timeline option, position: TimelinePosition option) =
        ensureLive()
        finite "duration" childDuration
        let offset = this.ResolvePosition(?position=position) - (if childDuration <= 1e-11 then 1e-11 else 0.)
        stopFrame()
        let expected = generation
        let canWrite () = expected = generation && not cancelled
        pause()
        if canWrite() then
            let property =
                match borrowedProperties.TryGetValue identity with
                | true, property -> property
                | _ ->
                    let property = NumericProperty((fun () -> 0.), (fun value -> write value borrowedGuard))
                    borrowedProperties.Add(identity,property)
                    property
            let siblings =
                match propertySiblings.TryGetValue property with
                | true, siblings -> siblings
                | _ ->
                    let siblings = System.Collections.Generic.List<NumericPropertyTrack>()
                    propertySiblings.Add(property,siblings)
                    siblings
            let normalized = if childDuration = 0. then 1e-11 else childDuration
            match NumericPropertyTrack.Create(property,siblings,Some 0.,childDuration,offset,normalized,normalized,id,id,canWrite) with
            | Some track when canWrite() -> addChild(TimelineEntry.Borrowed(track,nested,ignore,identity))
            | _ -> ()
        this
    /// <summary>Pauses and borrows a numeric animation; parent muting preserves the child's callbacks.</summary>
    member this.Sync(child: NumericAnimation, ?position: TimelinePosition) =
        ensureLive()
        if isNull (box child) then nullArg "child"
        this.Borrow(child,child.Duration,child.Pause,(fun value guard -> child.SetTimeGuarded(value,guard)),None,position)
    /// <summary>Pauses and borrows a timer through its source current-time setter semantics.</summary>
    /// <remarks>Parent cancellation releases this timer without disposing it. Parent muting preserves its callbacks.</remarks>
    member internal this.BorrowExternal(identity: obj, childDuration: float, pause: unit -> unit, write: float -> (unit -> bool) -> unit, position: TimelinePosition option) =
        this.Borrow(identity, childDuration, pause, write, None, position)
    member this.Sync(child: Timer, ?position: TimelinePosition) =
        ensureLive()
        if isNull (box child) then nullArg "child"
        this.Borrow(child,child.Duration,child.Pause,(fun value guard -> child.SetTimeGuarded(value,guard)),None,position)
    /// <summary>Pauses and borrows a nested timeline, capturing its total duration and rejecting cycles.</summary>
    member this.Sync(child: Timeline, ?position: TimelinePosition) =
        ensureLive()
        if isNull (box child) then nullArg "child"
        if child.Contains this then invalidArg "child" "Timeline synchronization cannot contain a cycle."
        this.Borrow(child,child.Duration,child.Pause,(fun value guard -> child.SetTimeGuarded(value,guard)),Some child,position)

    /// <summary>Adds a numeric replace track sharing the supplied property's identity with sibling tracks.</summary>
    /// <remarks>
    /// Durations use source 12-place precision; positive values rounded to zero are rejected. Default easing is linear.
    /// Adding a track pauses and resets controller time without dispatching property writes. Originals are captured for Revert.
    /// </remarks>
    member this.Animate(property: NumericProperty, target: float, milliseconds: float, ?position: TimelinePosition,
                        ?ease: (float -> float), ?from: float, ?modifier: (float -> float)) =
        ensureLive()
        if isNull (box property) then nullArg "property"
        finite "target" target
        finite "milliseconds" milliseconds
        if milliseconds < 0. then invalidArg "milliseconds" "Duration must be nonnegative."
        from |> Option.iter (finite "from")
        let position = this.ResolvePosition(?position = position)
        let offset = position - (if milliseconds <= 1e-11 then 1e-11 else 0.)
        let updateDuration = PropertyNumbers.round 1e12 (if milliseconds = 0. then 1e-11 else milliseconds)
        if updateDuration = 0. then invalidArg "milliseconds" "Positive duration rounds to zero at source timing precision."
        let normalizedDuration = if Double.IsPositiveInfinity updateDuration then 1e12 else updateDuration
        finite "position" (offset + normalizedDuration)
        this.TryAddOwned((fun context ->
            let animation=new Ranvier.Anime.KeyframeAnimation(1)
            context.Attach animation
            let active=animation.Guard()
            let current()=active() && context.CanWrite()
            let mutable capture=(fun () -> current)
            let guarded name action value =
                let permission=capture()
                let result=action value
                if permission() && not(Double.IsFinite result) then invalidArg name "Expected a finite value."
                result
            let mutable originalValue:float option=None
            let captureOriginal()=
                match originalValue with
                | Some value -> value
                | None ->
                    let value=property.Value
                    if current() then
                        originalValue<-Some value
                        animation.SetRestore(fun permission -> if permission() then property.Value<-value)
                    value
            if from.IsSome then captureOriginal() |> ignore
            let frame={FrameInput.Default with From=from |> Option.map ValueInterpolation.Number;To=Some(ValueInterpolation.Number target)}
            let properties=[|{FrameProperty.Target=0;Name="value";Original=ValueInterpolation.Number 0.;Frames=[|frame|];ConvertUnit=None}|]
            let defaults={FrameDefaults.Default with Duration=milliseconds;Ease=guarded "ease" (defaultArg ease id);Modifier=guarded "modifier" (defaultArg modifier id)}
            let options={Ranvier.Anime.KeyframeAnimationOptions.Default with Ease=defaults.Ease;Modifier=defaults.Modifier;Precision=None}
            let original _=ValueInterpolation.Number(captureOriginal())
            let prepared=TimelineKeyframeInstallation.scalar(context,animation,defaults,properties,[|struct(box property,"value")|],
                (fun _ _ _ -> if current() then Some frame else None),original,(fun _ _ output -> property.Value<-output.Number),
                false,-1,options,current)
            if prepared.IsSome then capture<-(fun () -> animation.Playback.CaptureWriteGuard())
            prepared),position=TimelinePosition.At position,zeroDuration=(milliseconds<=1e-11)) |> ignore
        this

    /// <summary>Adds a numeric replace track from unsigned whole milliseconds, converted exactly to the precise duration path.</summary>
    member this.Animate(property: NumericProperty, target: float, milliseconds: uint, ?position: TimelinePosition,
                        ?ease: (float -> float), ?from: float, ?modifier: (float -> float)) =
        this.Animate(property, target, float milliseconds, ?position = position, ?ease = ease, ?from = from, ?modifier = modifier)

    /// <summary>Schedules an instantaneous numeric property replacement at a position.</summary>
    member this.Set(property: NumericProperty, value: float, ?position: TimelinePosition) =
        this.Animate(property, value, 0., ?position = position, ease = Ease.create (Power 2.) Out)

    /// <summary>Adds a callback track at an explicit position or the current timeline end.</summary>
    /// <remarks>Callbacks run on forward/backward crossings. State changes precede user code to make reentrant seeks finite.</remarks>
    member this.Call(callback: unit -> unit, ?position: TimelinePosition) =
        ensureLive()
        if isNull (box callback) then nullArg "callback"
        let offset = this.ResolvePosition(?position = position) - 1e-11
        addChild (TimelineEntry.Callback(offset, callback, TimelineCallState()))
        this

    /// <summary>Borrows a child group using stagger offsets based on the timeline end before insertion.</summary>
    member this.SyncStaggered(children: NumericAnimation array, value: StaggerValue, options: StaggerOptions) =
        ensureLive()
        if isNull children then nullArg "children"
        let originalStart =
            match options.Start, value with
            | Some start, _ when start <> 0. -> start
            | _, StaggerValue.Range(first, _) -> first
            | _ -> 0.
        let start = if options.Start.IsNone then duration else originalStart
        let offsets =
            Stagger.distributeAt (Some start) value options (uint children.Length)
            |> Array.map(fun offset ->
                finite "options" offset
                offset)
        Array.iter2(fun (child: NumericAnimation) offset -> this.Sync(child, TimelinePosition.At offset) |> ignore) children offsets
        this

    /// <summary>Named timeline positions recorded via Label.</summary>
    member _.Labels : Collections.Generic.IReadOnlyDictionary<string, float> = labels
    /// <summary>Total duration including repeats and gaps, excluding initial delay.</summary>
    member _.Duration = if hasEntries then clock.Duration else 0.
    /// <summary>Duration of one timeline iteration.</summary>
    member _.IterationDuration = duration
    /// <summary>Last sampled time with source bounds and four-decimal precision; setting seeks while preserving playback.</summary>
    member _.CurrentTime
        with get() = clock.CurrentTime
        and set value = finite "value" value; ensureLive(); stopFrame(); externalControl (fun () -> clock.CurrentTime <- value)
    /// <summary>Directional time within the current iteration; setting seeks within the current iteration.</summary>
    member _.IterationCurrentTime
        with get() =
            if playbackEase.IsNone then clock.IterationCurrentTime
            else Math.Clamp(PropertyNumbers.round 1e4 playbackIterationTime,0.,duration)
        and set value = finite "value" value; ensureLive(); stopFrame(); externalControl (fun () -> clock.IterationCurrentTime <- value)
    /// <summary>Directional progress within the current iteration; setting seeks within the current iteration.</summary>
    member _.IterationProgress
        with get() = clock.IterationProgress
        and set value = finite "value" value; ensureLive(); stopFrame(); externalControl (fun () -> clock.IterationProgress <- value)
    /// <summary>Current signed source iteration index.</summary>
    member _.CurrentIteration = clock.CurrentIteration
    /// <summary>Captures the shared clock's iteration, direction and scheduling anchor.</summary>
    member internal _.CaptureScopeTime() = clock.CaptureScopeTime()
    /// <summary>Restores iteration setters with child rendering, then the retained scheduling anchor.</summary>
    member internal _.RestoreScopeTime(snapshot: ScopeClockSnapshot) =
        ensureLive()
        stopFrame()
        externalControl(fun () -> clock.RestoreScopeTime snapshot)
    /// <summary>Whether a scheduled frame loop is active.</summary>
    member _.Playing = not clock.Paused
    /// <summary>Whether positive time has been sampled since reset.</summary>
    member _.Began = clock.Began
    /// <summary>Whether raw direction and iteration reversal produce backwards playback.</summary>
    member _.Backwards = clock.Backwards
    /// <summary>Whether the timeline and its direct tracks reached completion.</summary>
    member _.Completed = clock.Completed
    /// <summary>Whether the base playback direction is reversed; setting resumes in the requested direction.</summary>
    member this.Reversed
        with get() = clock.Reversed
        and set value = if value then this.Reverse() else this.Play()
    /// <summary>Whether terminal cancellation ended this timeline's lifetime.</summary>
    member _.Cancelled = cancelled
    /// <summary>Bounded overall progress; setting seeks proportionally across total duration.</summary>
    member _.Progress
        with get() = clock.Progress
        and set value = finite "value" value; ensureLive(); stopFrame(); externalControl (fun () -> clock.Progress <- value)
    /// <summary>Finite playback multiplier, clamped to at least 1e-11.</summary>
    member _.Speed
        with get() = clock.Speed
        and set value = finite "value" value; ensureLive(); stopFrame(); clock.Speed <- value
    /// <summary>Finite frame-rate ceiling, clamped to at least 1e-11.</summary>
    member _.Fps with get() = clock.Fps and set value = clock.Fps <- value
    /// <summary>Samples milliseconds after initial delay, optionally muting direct callbacks.</summary>
    member _.Seek(milliseconds: float, ?muteCallbacks: bool) =
        seek (fun () -> true) (defaultArg muteCallbacks false) milliseconds
    /// <summary>Samples a borrowed controller with every ancestor's write guard.</summary>
    member internal _.SeekGuarded(milliseconds: float, canWrite: unit -> bool) = seek canWrite false milliseconds
    /// <summary>Assigns borrowed current time with source setter callbacks and all ancestor write guards.</summary>
    member internal _.SetTimeGuarded(milliseconds: float, canWrite: unit -> bool) =
        ensureLive()
        finite "milliseconds" milliseconds
        stopFrame()
        let previous = ancestorLive
        ancestorLive <- canWrite
        try externalControl(fun () -> clock.SetTimeGuarded(milliseconds, ancestorLive)) finally ancestorLive <- previous
    /// <summary>Resumes the current direction with the source clock lead.</summary>
    member _.Resume() = ensureLive(); stopFrame(); externalControl clock.Resume
    /// <summary>Resumes forward playback, mirroring time when reversed.</summary>
    member _.Play() = ensureLive(); stopFrame(); externalControl clock.Play
    /// <summary>Resumes reversed playback, mirroring time when forward.</summary>
    member _.Reverse() = ensureLive(); stopFrame(); externalControl clock.Reverse
    /// <summary>Mirrors time and direction according to repeat parity.</summary>
    member _.Alternate() = ensureLive(); stopFrame(); externalControl clock.Alternate
    /// <summary>Stops scheduling and retains the last sampled time.</summary>
    member _.Pause() = stopFrame(); externalControl clock.Pause
    /// <summary>Rewinds with muted direct callbacks and resets direct track completion.</summary>
    member _.Reset() =
        ensureLive()
        stopFrame()
        let expected = generation
        externalControl clock.Reset
        if expected = generation && not cancelled then resetTracks()
    /// <summary>Resets then resumes playback.</summary>
    member this.Restart() =
        let expected = generation + 1
        this.Reset()
        if not cancelled && generation = expected then this.Resume()
    /// <summary>Samples the end, retaining the timeline's reusable native lifetime.</summary>
    member this.Complete(?muteCallbacks: bool) = this.Seek(clock.Duration, ?muteCallbacks=muteCallbacks)
    /// <summary>Stretches total timeline duration, scaling all child offsets, child durations and labels proportionally.</summary>
    member this.Stretch(milliseconds: float) =
        ensureLive()
        finite "milliseconds" milliseconds
        if milliseconds < 0. then invalidArg "milliseconds" "Expected nonnegative milliseconds."
        if hasEntries && clock.Duration > 0. then
            let currentDuration = clock.Duration
            let normalized = if milliseconds = 0. then 1e-11 else PropertyNumbers.round 1e12 milliseconds
            if currentDuration <> normalized then
                let scale = milliseconds / currentDuration
                stopFrame()
                for index in 0 .. children.Count - 1 do
                    match children[index] with
                    | TimelineEntry.Property track -> track.Stretch scale
                    | TimelineEntry.Borrowed(track, nested, pause, identity) ->
                        track.Stretch scale
                        children[index] <- TimelineEntry.Borrowed(track, nested, pause, identity)
                    | TimelineEntry.OwnedTimer(offset, child) ->
                        child.Stretch(child.Duration * scale)
                        children[index] <- TimelineEntry.OwnedTimer(offset * scale, child)
                    | TimelineEntry.OwnedAnimation(offset, child, renderVersion, instance) ->
                        child.Stretch(child.Duration * scale)
                        children[index] <- TimelineEntry.OwnedAnimation(offset * scale, child, renderVersion, instance)
                    | TimelineEntry.Callback(offset, callback, state) ->
                        state.Time <- state.Time * scale
                        children[index] <- TimelineEntry.Callback(offset * scale, callback, state)
                for key in Seq.toArray labels.Keys do
                    labels[key] <- labels[key] * scale
                clock.Stretch milliseconds
                duration <- clock.IterationDuration
        this
    /// <summary>Stretches total timeline duration using unsigned whole milliseconds.</summary>
    member this.Stretch(milliseconds: uint) = this.Stretch(float milliseconds)
    /// <summary>Removes a synced controller, owned child or property handle from this timeline and recalculates duration.</summary>
    member this.Remove(target: obj) =
        if not cancelled && not (isNull target) then
            let mutable removed = false
            let remaining = Collections.Generic.List<TimelineEntry>()
            for entry in children do
                let matches =
                    match entry with
                    | TimelineEntry.Property track -> Object.ReferenceEquals(box track.Property, target)
                    | TimelineEntry.OwnedTimer(_, child) -> Object.ReferenceEquals(box child, target)
                    | TimelineEntry.OwnedAnimation(_, child, _, instance) ->
                        Object.ReferenceEquals(box child, target) || Object.ReferenceEquals(instance, target)
                    | TimelineEntry.Borrowed(_, _, _, identity) -> Object.ReferenceEquals(identity, target)
                    | TimelineEntry.Callback _ -> false
                if matches then
                    removed <- true
                    match entry with
                    | TimelineEntry.OwnedTimer(_, child) | TimelineEntry.OwnedAnimation(_, child, _, _) ->
                        try child.Cancel() with _ -> ()
                    | TimelineEntry.Borrowed(_, _, _, identity) ->
                        borrowedProperties.Remove identity |> ignore
                    | _ -> ()
                else remaining.Add entry
            if removed then
                children.Clear()
                children.AddRange remaining
                if children.Count = 0 then
                    hasEntries <- false
                    duration <- 0.
                    cancel()
                else
                    let mutable maxEnd = 0.
                    for entry in children do
                        let finish = entry.Offset + entry.Duration
                        if finish > maxEnd then maxEnd <- finish
                    duration <- maxEnd
                    let iterations =
                        if Double.IsPositiveInfinity clock.IterationCount then infinity else clock.IterationCount
                    let total = PlaybackTiming(duration, iterations, clock.Delay, clock.LoopDelay).Duration
                    clock.ConfigureRenderer((fun _ _ _ -> ()), totalDuration = total)
        this
    /// <summary>Registers an optional completion callback and returns a task settling when this timeline completes.</summary>
    member this.Then(?callback: Timeline -> unit) : Threading.Tasks.Task<Timeline> =
        let invoke () = callback |> Option.iter (fun action -> action this)
        if clock.Completed then
            invoke()
            Threading.Tasks.Task.FromResult this
        elif cancelled || clock.Cancelled then
            Threading.Tasks.Task.FromCanceled<Timeline>(Threading.CancellationToken(true))
        else
            let source = Threading.Tasks.TaskCompletionSource<Timeline>()
            clock.Then(fun _ -> invoke()).ContinueWith((fun (task: Threading.Tasks.Task<Timer>) ->
                if task.IsCanceled then source.TrySetCanceled() |> ignore
                elif task.IsFaulted then source.TrySetException(task.Exception.InnerExceptions) |> ignore
                else source.TrySetResult this |> ignore
            ), Threading.Tasks.TaskContinuationOptions.ExecuteSynchronously) |> ignore
            source.Task
    /// <summary>Returns a task settling with this timeline upon completion.</summary>
    member this.AsTask() : Threading.Tasks.Task<Timeline> = this.Then()
    /// <summary>Rewinds, restores owned properties in reverse insertion order and cancels; repeated calls are harmless.</summary>
    /// <remarks>Borrowed controllers rewind and retain their independent lifetime. Explicit cancellation closes restoration.</remarks>
    member _.Revert() =
        if not cancelled && not reverting then
            reverting<-true
            stopFrame()
            let expected=generation
            let current()=not cancelled && generation=expected && ancestorLive()
            let actions=restorations.ToArray()
            let mutable failure:exn option=None
            let attempt action=try action() with error -> if failure.IsNone then failure<-Some error
            attempt clock.RewindForRevert
            for index=actions.Length-1 downto 0 do
                if current() then attempt(fun () -> actions[index] current)
            attempt cancel
            reverting<-false
            failure |> Option.iter(fun error -> System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw())
    /// <summary>Stops future writes and releases borrowed references.</summary>
    member _.Cancel() = cancel()
    interface ITickable with
        member this.Revert() = this.Revert() |> ignore
        member this.Completed = this.Completed
        member this.Cancelled = this.Cancelled
        member this.Pause() = this.Pause() |> ignore
        member this.Cancel() = this.Cancel()
        member this.Complete() = this.Complete() |> ignore
        member this.WhenCompleted(callback) = this.Then(fun _ -> callback ()) |> ignore
    interface IDisposable with member _.Dispose() = cancel()

and [<System.Diagnostics.DebuggerDisplay("{ToString(),nq}")>] private TimelineEntry =
    | Property of track: NumericPropertyTrack
    | OwnedTimer of offset: float * child: Timer
    | OwnedAnimation of offset:float * child:Timer * renderVersion:(unit -> int64) * instance:obj
    | Borrowed of track: NumericPropertyTrack * nested: Timeline option * pause: (unit -> unit) * identity:obj
    | Callback of offset: float * callback: (unit -> unit) * completed: TimelineCallState

    override this.ToString() =
        match this with
        | Property _ -> "Property"
        | OwnedTimer _ -> "OwnedTimer"
        | OwnedAnimation _ -> "OwnedAnimation"
        | Borrowed _ -> "Borrowed"
        | Callback _ -> "Callback"

    member this.Offset =
        match this with
        | Property track | Borrowed(track, _, _, _) -> track.Offset
        | OwnedTimer(offset, _) | OwnedAnimation(offset,_,_,_) | Callback(offset, _, _) -> offset

    member this.Speed =
        match this with
        | OwnedTimer(_, child) | OwnedAnimation(_,child,_,_) -> child.Speed
        | _ -> 1.

    member this.Duration =
        match this with
        | Property track | Borrowed(track, _, _, _) -> track.Duration
        | OwnedTimer(_, child) | OwnedAnimation(_,child,_,_) -> child.Delay + child.Duration
        | Callback _ -> 1e-11

and private TimelineCallState() =
    member val Completed = false with get, set
    member val Backwards = false with get, set
    member val Time = 0. with get, set

/// <summary>Optional Ranvier ownership for borrowed-child timelines.</summary>
[<RequireQualifiedAccess>]
module Timeline =
    /// <summary>Registers terminal cancellation with the current Ranvier owner.</summary>
    let own (timeline: Timeline) =
        ScopeRegistration.attach timeline
        timeline
