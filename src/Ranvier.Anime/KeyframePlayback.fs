namespace Ranvier.Anime

open System
open System.Collections.Generic
open Ranvier.Dom
open Ranvier.Anime.ValueInterpolation

module private KeyframePropComparer =
    let instance =
        { new IEqualityComparer<struct(int * string)> with
            member _.Equals(struct(ta, na), struct(tb, nb)) =
                ta = tb && StringComparer.Ordinal.Equals(na, nb)
            member _.GetHashCode(struct(t, n)) =
                HashCode.Combine(t, StringComparer.Ordinal.GetHashCode n) }

/// <summary>A paused compiled keyframe sequence using the shared timer, ordered guarded writes and per-frame history.</summary>
/// <remarks>Initialize explicitly. Shared siblings provide same-timeline replacement; precise geometry uses milliseconds.
/// Finite scalar interpolation preserves finite endpoints when their difference overflows.</remarks>
type internal KeyframePlayback(scheduler:FrameScheduler,program:FrameProgram,write:int -> string -> Output -> unit,
                               replace:bool,roundNumbers:bool,precision:int,options:PlaybackOptions,
                               ?onBegin:Timer -> unit,?onBeforeUpdate:Timer -> unit,?onRender:Timer -> unit,
                               ?onUpdate:Timer -> unit,?onLoop:Timer -> unit,?onPause:Timer -> unit,?onComplete:Timer -> unit,
                               ?siblings:FrameSibling array,?finiteScalar:bool,?writeScalar:int -> string -> float -> unit) =
    do
        if isNull(box program) then nullArg "program"
        if isNull(box write) then nullArg "write"
        if precision < -1 || precision>308 then invalidArg "precision" "Expected disabled rounding or at most 308 decimal places."
    let finiteScalar=defaultArg finiteScalar false
    let writeScalarOpt = match writeScalar with Some ws -> ValueSome ws | None -> ValueNone
    let hasSiblings = siblings.IsSome
    let ownerAllowsWrite=ScopeRegistration.captureWriteGuard()
    let definitions=Array.copy program.Tweens
    let frames=
        match siblings with
        | Some nodes ->
            if nodes.Length<>definitions.Length || nodes |> Array.exists isNull then invalidArg "siblings" "Expected one sibling node per compiled frame."
            Array.copy nodes
        | None ->
            let child=obj()
            definitions |> Array.map(fun frame -> FrameSibling(frame,child))
    let mutable offset=program.Offset
    do
        if replace && not hasSiblings then
            let groups=Dictionary<struct(int*string),ResizeArray<FrameSibling>>(KeyframePropComparer.instance)
            for frame in frames do frame.Start<-frame.Start+program.Delay
            for frame in frames do
                let definition=frame.Definition
                let key=struct(definition.Target,definition.Name)
                let nodes=
                    match groups.TryGetValue key with
                    | true,nodes -> nodes
                    | _ -> let nodes=ResizeArray<FrameSibling>() in groups.Add(key,nodes);nodes
                FrameSiblingComposition.select nodes frame.AbsoluteStart |> ignore
                FrameSiblingComposition.add nodes frame
            for index in 0..frames.Length-1 do frames[index].Start<-definitions[index].Start
    let effectiveOptions={options with Delay=program.Delay;Repeat=(if definitions.Length=0 then PlaybackRepeat.Count 0u else options.Repeat)}
    let mutable renderVersion=0L
    let clock=ScopeRegistration.run ignore (fun () -> new Timer(scheduler,program.IterationDuration,options=effectiveOptions,
                        ?onBegin=onBegin,?onBeforeUpdate=onBeforeUpdate,?onUpdate=onUpdate,
                        ?onLoop=onLoop,?onPause=onPause,?onComplete=onComplete))
    let sampleFrameOutput (definition:FrameTween) progress current : Output option =
        if finiteScalar then
            let raw = PropertyNumbers.interpolate definition.First.Number definition.Last.Number progress
            let value =
                if Object.ReferenceEquals(definition.Modifier, PropertyNumbers.identityFloat) then raw
                else definition.Modifier raw
            if current() then Some(ValueInterpolation.compose(definition.First,value,Array.Empty<float>())) else None
        else
            match ValueInterpolation.sampleOutput(roundNumbers,definition.First,definition.Last,progress,precision,definition.Modifier,current) with
            | null -> None
            | output -> Some output
    do
        if hasSiblings then
            let propGroups = Dictionary<struct(int * string), ResizeArray<FrameSibling>>(KeyframePropComparer.instance)
            for i = 0 to frames.Length - 1 do
                let f = frames[i]
                let key = struct(f.Definition.Target, f.Definition.Name)
                let list =
                    match propGroups.TryGetValue key with
                    | true, existing -> existing
                    | _ ->
                        let created = ResizeArray<FrameSibling>(4)
                        propGroups.Add(key, created)
                        created
                list.Add f
            let propSlices = Dictionary<struct(int * string), struct(FrameSibling array * bool)>(propGroups.Count, KeyframePropComparer.instance)
            for KeyValue(k, list) in propGroups do
                let arr = list.ToArray()
                let mutable sorted = true
                for cIdx = 1 to arr.Length - 1 do
                    if arr[cIdx].Start < arr[cIdx - 1].Start then sorted <- false
                propSlices.Add(k, struct(arr, sorted))
            for i = 0 to frames.Length - 1 do
                let frame = frames[i]
                let struct(slice, sorted) = propSlices[struct(frame.Definition.Target, frame.Definition.Name)]
                if slice.Length = 1 then
                    frame.SampleAt <-
                        Some(fun (f:FrameSibling) parentTime current ->
                            let time = clock.IterationTimeAt((parentTime - offset) * clock.Speed)
                            let progress = f.Definition.Ease(Math.Clamp((time - f.Start) / f.Update, 0., 1.))
                            if current() then sampleFrameOutput f.Definition progress current
                            else None)
                elif sorted && slice.Length > 4 then
                    frame.SampleAt <-
                        Some(fun (f:FrameSibling) parentTime current ->
                            let time = clock.IterationTimeAt((parentTime - offset) * clock.Speed)
                            let f0 = slice[0]
                            let selected =
                                if time < f0.Start then
                                    if f0.Start < f.Start then f0 else f
                                else
                                    let mutable lo = 0
                                    let mutable hi = slice.Length
                                    while lo < hi do
                                        let mid = int (uint (lo + hi) >>> 1)
                                        if slice[mid].Start <= time then lo <- mid + 1
                                        else hi <- mid
                                    slice[lo - 1]
                            let progress = selected.Definition.Ease(Math.Clamp((time - selected.Start) / selected.Update, 0., 1.))
                            if current() then sampleFrameOutput selected.Definition progress current
                            else None)
                else
                    frame.SampleAt <-
                        Some(fun (f:FrameSibling) parentTime current ->
                            let time = clock.IterationTimeAt((parentTime - offset) * clock.Speed)
                            let mutable selected = f
                            for cIdx = 0 to slice.Length - 1 do
                                let candidate = slice[cIdx]
                                if candidate.Start <= time || (selected.Start > time && candidate.Start < selected.Start) then
                                    selected <- candidate
                            let progress = selected.Definition.Ease(Math.Clamp((time - selected.Start) / selected.Update, 0., 1.))
                            if current() then sampleFrameOutput selected.Definition progress current
                            else None)
    let mutable renderToken = Int64.MinValue
    let current = Closure.keep (fun () -> clock.IsCurrent renderToken && ownerAllowsWrite())
    let render = PlaybackRenderer(fun (sample:PlaybackSample) mode muted ->
        let savedToken = renderToken
        renderToken <- clock.WriteToken
        try
            let delta=sample.AbsoluteTime-clock.PreviousAbsoluteTime
            let forced=mode=PlaybackTickMode.Force || (if sample.Backwards then -delta else delta)>=clock.TickThreshold
            let absolute=PropertyNumbers.round 1e12 (offset+clock.Delay+sample.IterationTime)
            let parentTime=if hasSiblings then PropertyNumbers.round 1e12 (offset+(clock.Delay+sample.AbsoluteTime)/clock.Speed) else 0.
            let mutable rendered=false
            let mutable alive=clock.IsCurrent renderToken && ownerAllowsWrite()
            for index in 0..frames.Length-1 do
                if alive then
                    let frame=frames[index]
                    let previous=frame.Previous
                    let next=frame.Next
                    let previousCross=hasSiblings && not(isNull previous) && not(obj.ReferenceEquals(previous.Child,frame.Child))
                    let nextSame=not(isNull next) && (not hasSiblings || obj.ReferenceEquals(next.Child,frame.Child))
                    let timedTakeover=hasSiblings && Double.IsFinite frame.OwnsUntil && not(isNull next) && not nextSame
                    let previousEnd=if isNull previous then 0. else previous.AbsoluteStart+previous.Change
                    let ending=frame.AbsoluteStart+frame.Change
                    let takeover=
                        if isNull next || next.Overridden then ending
                        elif nextSame then ending+next.Delay
                        elif next.AbsoluteStart<next.AbsoluteUpdateStart then next.AbsoluteStart
                        else next.AbsoluteUpdateStart
                    let history=(frame.Time<>frame.Change || absolute<=takeover ||
                                 (not(isNull previous) && not previousCross && (isNull next || not nextSame))) &&
                                (frame.Time<>0. || absolute>=frame.AbsoluteStart ||
                                 (previousCross && not frame.Definition.HasFrom && not previous.Overridden && absolute>=previousEnd) ||
                                 (nextSame && not next.Overridden && next.Time<>0. && sample.IterationTime<next.Start))
                    let owns =
                        if not hasSiblings then true
                        else
                            let laterAtBoundary=
                                sample.Backwards && not clock.InternalRender && not(isNull next) && not nextSame &&
                                not next.Overridden && next.Definition.HasFrom && parentTime=next.AbsoluteStart
                            parentTime>=frame.OwnsFrom && parentTime<frame.OwnsUntil && not laterAtBoundary
                    let eligible=owns && (forced || history || (timedTakeover && sample.Backwards)) && (isNull previous || previousCross || sample.IterationTime>=frame.Start) &&
                                 (not replace || (not frame.Overridden && (not frame.Overlapped || timedTakeover || absolute<=ending) &&
                                                  (isNull next || timedTakeover || next.Overridden || absolute<=takeover) &&
                                                  (isNull previous || previous.Overridden ||
                                                   (if not previousCross then absolute>=previousEnd+frame.Delay
                                                    else Double.IsFinite frame.OwnsFrom || absolute>=frame.AbsoluteStart || (not frame.Definition.HasFrom && absolute>=previousEnd)))))
                    if eligible then
                        frame.Time<-Math.Clamp(sample.IterationTime-frame.Start,0.,if timedTakeover then frame.Update else frame.Change)
                        let definition=definitions[index]
                        let rawRatio = frame.Time / frame.Update
                        let progress =
                            if Object.ReferenceEquals(definition.Ease, PropertyNumbers.identityFloat) then rawRatio
                            else definition.Ease rawRatio
                        alive <- clock.IsCurrent renderToken && ownerAllowsWrite()
                        if alive then
                            let kind = definition.First.Kind
                            if kind = ValueKind.Number || kind = ValueKind.Unit then
                                let struct(ok, scalarVal) =
                                    if finiteScalar then
                                        let raw = PropertyNumbers.interpolate definition.First.Number definition.Last.Number progress
                                        let v =
                                            if Object.ReferenceEquals(definition.Modifier, PropertyNumbers.identityFloat) then raw
                                            else definition.Modifier raw
                                        if clock.IsCurrent renderToken && ownerAllowsWrite() then struct(true, v) else struct(false, 0.)
                                    else
                                        ValueInterpolation.trySampleScalarValue(roundNumbers, kind, definition.First.Number, definition.Last.Number, progress, precision, definition.Modifier, current)
                                if ok && clock.IsCurrent renderToken && ownerAllowsWrite() then
                                    frame.SetScalarValue scalarVal
                                    if not clock.InternalRender && not clock.EffectivelyMuted then
                                        match writeScalarOpt with
                                        | ValueSome ws -> ws definition.Target definition.Name scalarVal
                                        | ValueNone -> write definition.Target definition.Name frame.Value
                                        renderVersion<-renderVersion+1L
                                        rendered<-true
                                    alive <- clock.IsCurrent renderToken && ownerAllowsWrite()
                                else alive<-false
                            else
                                match ValueInterpolation.sampleOutput(roundNumbers, definition.First, definition.Last, progress, precision, definition.Modifier, current) with
                                | null -> alive<-false
                                | output when clock.IsCurrent renderToken && ownerAllowsWrite() ->
                                    frame.Value<-output
                                    if not clock.InternalRender && not clock.EffectivelyMuted then
                                        write definition.Target definition.Name output
                                        renderVersion<-renderVersion+1L
                                        rendered<-true
                                    alive <- clock.IsCurrent renderToken && ownerAllowsWrite()
                                | _ -> alive<-false
                    elif frame.Time<>0. && not(isNull previous) && not previousCross && sample.IterationTime<frame.Start then frame.Time<-0.
            if alive && clock.IsCurrent renderToken && ownerAllowsWrite() && rendered && sample.AboveZero && not muted && not clock.EffectivelyMuted then onRender |> Option.iter(fun action -> action clock)
        finally renderToken <- savedToken)
    let stageStretch scale =
        let nextOffset=offset*scale
        if not(Double.IsFinite nextOffset) then invalidArg "scale" "Scaled offset must remain finite."
        nextOffset,frames |> Array.map(fun frame -> frame.Scaled scale)
    let stageZeroStretch scale nextOffset =
        if not(Double.IsFinite nextOffset) then invalidArg "scale" "Scaled offset must remain finite."
        frames |> Array.map(fun frame ->
            let next=FrameSibling(frame.Definition,frame.Child)
            frame.CopyTo(next,id)
            next.AbsoluteStart<-PropertyNumbers.round 1e12 (nextOffset+frame.Start)
            next.AbsoluteUpdateStart<-PropertyNumbers.round 1e12 (nextOffset+frame.Start-frame.Delay)
            next.AbsoluteEnd<-PropertyNumbers.round 1e12 (nextOffset+frame.Start+frame.Update)
            if Double.IsFinite frame.OwnsFrom then next.OwnsFrom<-PropertyNumbers.round 1e12 ((frame.OwnsFrom+1e-11)*scale-1e-11)
            if Double.IsFinite frame.OwnsUntil then next.OwnsUntil<-PropertyNumbers.round 1e12 ((frame.OwnsUntil+1e-11)*scale-1e-11)
            if not(Double.IsFinite next.AbsoluteStart && Double.IsFinite next.AbsoluteUpdateStart && Double.IsFinite next.AbsoluteEnd &&
                   (not(Double.IsFinite frame.OwnsFrom) || Double.IsFinite next.OwnsFrom) &&
                   (not(Double.IsFinite frame.OwnsUntil) || Double.IsFinite next.OwnsUntil)) then
                invalidArg "scale" "Scaled frame geometry must remain finite."
            next)
    do
        clock.Seek(0.,muteCallbacks=true)
        clock.ConfigureRenderer(render,sampleWhenMuted=true,?totalDuration=(if program.IterationDuration=1e-11 then Some 1e-11 else None),
            validateStretch=(fun scale -> stageStretch scale |> ignore),
            onStretch=(fun scale ->
                let nextOffset,staged=stageStretch scale
                for index in 0..frames.Length-1 do staged[index].CopyTo(frames[index],id)
                offset<-nextOffset),
            validateZeroStretch=(fun scale nextOffset -> stageZeroStretch scale nextOffset |> ignore),
            onZeroStretch=(fun scale nextOffset ->
                let staged=stageZeroStretch scale nextOffset
                for index in 0..frames.Length-1 do staged[index].CopyTo(frames[index],id)
                offset<-nextOffset))
    let mutable initialized=false
    /// <summary>The shared playback controller for this sequence.</summary>
    member _.Clock=clock
    /// <summary>Independent per-frame time snapshots for source behavior verification.</summary>
    member _.Times=frames |> Array.map _.Time
    /// <summary>Advances after each target write; cache-only and skipped samples leave it unchanged.</summary>
    member _.RenderVersion=renderVersion
    member _.RestoreHeld(previous,time,cacheOnly,parentCurrent)=
        let before=renderVersion
        let clockCurrent=clock.CaptureWriteGuard()
        let current()=clockCurrent() && parentCurrent() && ownerAllowsWrite()
        for frame in frames do
            let next=frame.Next
            if not(isNull next) && not(frame.Overridden || next.Overridden) && not(obj.ReferenceEquals(frame.Child,next.Child)) &&
               next.Definition.HasFrom && time>=frame.AbsoluteEnd && time<next.AbsoluteStart && previous>=next.AbsoluteStart && current() then
                match frame.SampleAt |> Option.bind(fun sample -> sample frame time current) with
                | Some value when current() ->
                    frame.Value<-value
                    if not cacheOnly && not clock.EffectivelyMuted then
                        write frame.Definition.Target frame.Definition.Name value
                        renderVersion<-renderVersion+1L
                | _ -> ()
        renderVersion<>before
    /// <summary>Independent snapshots of each tween's most recently sampled scalar/color/compound cache.</summary>
    member _.CurrentValues=frames |> Array.map(fun frame -> frame.Value.CopyComponents())
    /// <summary>Notifies standalone explicit-from construction once, then resets; owned child initialization only refreshes caches.</summary>
    member _.Initialize(?notifyConstruction:bool,?timelineChild:bool,?canWrite:unit -> bool)=
        if defaultArg timelineChild false && not initialized && not clock.Cancelled then
            clock.AssignToTimeline()
            clock.PrepareConstruction()
        let inherited=defaultArg canWrite (fun () -> true)
        let clockCurrent=clock.CaptureWriteGuard()
        let current()=clockCurrent() && inherited()
        try
            if current() && not initialized then
                initialized<-true
                if not clock.EffectivelyMuted && defaultArg notifyConstruction (not(defaultArg timelineChild false)) && (definitions |> Array.exists _.HasFrom) then
                    onRender |> Option.iter(fun action -> action clock)
            if current() then
                if defaultArg timelineChild false then clock.AttachToTimeline(internalRender=true,canWrite=inherited)
                else clock.Reset()
        with _ ->
            try clock.Cancel() with _ -> ()
            reraise()
    interface IDisposable with member _.Dispose()=clock.Cancel()
