namespace Ranvier.Anime

open System
open System.Collections.Generic
open Ranvier.Dom
open Ranvier.Anime.ValueInterpolation

/// <summary>A paused compiled keyframe sequence using the shared timer, ordered guarded writes and per-frame history.</summary>
/// <remarks>Initialize explicitly. Shared siblings provide same-timeline replacement; precise geometry uses milliseconds.
/// Finite scalar interpolation preserves finite endpoints when their difference overflows.</remarks>
type internal KeyframePlayback(scheduler:FrameScheduler,program:FrameProgram,write:int -> string -> Output -> unit,
                               replace:bool,roundNumbers:bool,precision:int,options:PlaybackOptions,
                               ?onBegin:Timer -> unit,?onBeforeUpdate:Timer -> unit,?onRender:Timer -> unit,
                               ?onUpdate:Timer -> unit,?onLoop:Timer -> unit,?onPause:Timer -> unit,?onComplete:Timer -> unit,
                               ?siblings:FrameSibling array,?finiteScalar:bool) =
    do
        if isNull(box program) then nullArg "program"
        if isNull(box write) then nullArg "write"
        if precision < -1 || precision>308 then invalidArg "precision" "Expected disabled rounding or at most 308 decimal places."
    let finiteScalar=defaultArg finiteScalar false
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
        if replace && siblings.IsNone then
            let groups=Dictionary<struct(int*string),ResizeArray<FrameSibling>>()
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
    let render (sample:PlaybackSample) mode muted =
        let timerCurrent=clock.CaptureWriteGuard()
        let current()=timerCurrent() && ownerAllowsWrite()
        let delta=sample.AbsoluteTime-clock.PreviousAbsoluteTime
        let forced=mode=PlaybackTickMode.Force || (if sample.Backwards then -delta else delta)>=clock.TickThreshold
        let absolute=PropertyNumbers.round 1e12 (offset+clock.Delay+sample.IterationTime)
        let mutable rendered=false
        let mutable alive=current()
        for index in 0..frames.Length-1 do
            if alive then
                let frame=frames[index]
                let previous=frame.Previous
                let next=frame.Next
                let previousCross=not(isNull previous) && not(obj.ReferenceEquals(previous.Child,frame.Child))
                let nextSame=not(isNull next) && obj.ReferenceEquals(next.Child,frame.Child)
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
                let eligible=(forced || history) && (isNull previous || previousCross || sample.IterationTime>=frame.Start) &&
                             (not replace || (not frame.Overridden && (not frame.Overlapped || absolute<=ending) &&
                                              (isNull next || next.Overridden || absolute<=takeover) &&
                                              (isNull previous || previous.Overridden ||
                                               (if not previousCross then absolute>=previousEnd+frame.Delay
                                                else absolute>=frame.AbsoluteStart || (not frame.Definition.HasFrom && absolute>=previousEnd)))))
                if eligible then
                    frame.Time<-Math.Clamp(sample.IterationTime-frame.Start,0.,frame.Change)
                    let definition=definitions[index]
                    let progress=definition.Ease(frame.Time/frame.Update)
                    alive<-current()
                    if alive then
                        let output=
                            if finiteScalar then
                                let value=definition.Modifier(PropertyNumbers.interpolate definition.First.Number definition.Last.Number progress)
                                if current() then Some(struct(value,ValueInterpolation.compose(definition.First,value,[||]))) else None
                            else ValueInterpolation.sample(roundNumbers,definition.First,definition.Last,progress,precision,definition.Modifier,current)
                        match output with
                        | Some struct(_,output) when current() ->
                            frame.Value<-output
                            if not clock.InternalRender then
                                write definition.Target definition.Name output
                                renderVersion<-renderVersion+1L
                                rendered<-true
                            alive<-current()
                        | _ -> alive<-false
                elif frame.Time<>0. && not(isNull previous) && not previousCross && sample.IterationTime<frame.Start then frame.Time<-0.
        if alive && current() && rendered && sample.AboveZero && not muted then onRender |> Option.iter(fun action -> action clock)
    do
        clock.Seek(0.,muteCallbacks=true)
        clock.ConfigureRenderer(render,?totalDuration=(if program.IterationDuration=1e-11 then Some 1e-11 else None),
            onStretch=(fun scale ->
                let nextOffset=offset*scale
                if not(Double.IsFinite nextOffset) then invalidArg "scale" "Scaled offset must remain finite."
                let staged=frames |> Array.map(fun frame -> frame.Scaled scale)
                for index in 0..frames.Length-1 do staged[index].CopyTo(frames[index],id)
                offset<-nextOffset))
    let mutable initialized=false
    /// <summary>The shared playback controller for this sequence.</summary>
    member _.Clock=clock
    /// <summary>Independent per-frame time snapshots for source behavior verification.</summary>
    member _.Times=frames |> Array.map _.Time
    /// <summary>Advances after each target write; cache-only and skipped samples leave it unchanged.</summary>
    member _.RenderVersion=renderVersion
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
                if defaultArg notifyConstruction (not(defaultArg timelineChild false)) && (definitions |> Array.exists _.HasFrom) then
                    onRender |> Option.iter(fun action -> action clock)
            if current() then
                if defaultArg timelineChild false then clock.AttachToTimeline(internalRender=true,canWrite=inherited)
                else clock.Reset()
        with _ ->
            try clock.Cancel() with _ -> ()
            reraise()
    interface IDisposable with member _.Dispose()=clock.Cancel()
