namespace Ranvier.Anime

open System
open System.Collections.Generic
open Ranvier.Dom
open Ranvier.Anime.ValueInterpolation

type private PlayingFrame(frame:FrameTween) =
    member val Start=frame.Start with get,set
    member val Delay=frame.Delay with get,set
    member val Update=frame.UpdateDuration with get,set
    member val Change=frame.UpdateDuration with get,set
    member val AbsoluteStart=frame.AbsoluteStart with get,set
    member val AbsoluteUpdateStart=frame.AbsoluteUpdateStart with get,set
    member val AbsoluteEnd=frame.AbsoluteEnd with get,set
    member val Time=0. with get,set
    member val Previous = -1 with get,set
    member val Next = -1 with get,set
    member val Overridden=false with get,set
    member val Overlapped=false with get,set
    member this.Scaled(scale:float) =
        let normalize value =
            if value<=1e-11 then 1e-11
            else let rounded=PropertyNumbers.round 1e11 value in if Double.IsPositiveInfinity rounded then 1e12 else rounded
        let next=PlayingFrame(frame)
        next.Start<-this.Start*scale
        next.Delay<-this.Delay*scale
        next.Update<-normalize(this.Update*scale)
        next.Change<-normalize(this.Change*scale)
        next.AbsoluteStart<-this.AbsoluteStart*scale
        next.AbsoluteUpdateStart<-this.AbsoluteUpdateStart*scale
        next.AbsoluteEnd<-this.AbsoluteEnd*scale
        next.Time<-this.Time*scale
        next.Previous<-this.Previous
        next.Next<-this.Next
        next.Overridden<-this.Overridden
        next.Overlapped<-this.Overlapped
        if [next.Start;next.Delay;next.AbsoluteStart;next.AbsoluteUpdateStart;next.AbsoluteEnd;next.Time] |> List.exists(fun value -> not(Double.IsFinite value)) then
            invalidArg "scale" "Scaled frame geometry must remain finite."
        next

/// <summary>A paused compiled keyframe sequence using the shared timer, ordered guarded writes and per-frame history.</summary>
/// <remarks>Initialize explicitly. Replace arbitration is limited to this program; precise geometry uses milliseconds.</remarks>
type internal KeyframePlayback(scheduler:FrameScheduler,program:FrameProgram,write:int -> string -> Output -> unit,
                               replace:bool,roundNumbers:bool,precision:int,options:PlaybackOptions,
                               ?onBegin:Timer -> unit,?onBeforeUpdate:Timer -> unit,?onRender:Timer -> unit,
                               ?onUpdate:Timer -> unit,?onLoop:Timer -> unit,?onPause:Timer -> unit,?onComplete:Timer -> unit) =
    do
        if isNull(box program) then nullArg "program"
        if isNull(box write) then nullArg "write"
        if precision < -1 || precision>308 then invalidArg "precision" "Expected disabled rounding or at most 308 decimal places."
    let definitions=Array.copy program.Tweens
    let mutable frames=definitions |> Array.map PlayingFrame
    let mutable offset=program.Offset
    let overrideFrame index =
        let frame=frames[index]
        frame.Overlapped<-true
        frame.Overridden<-true
        frame.Change<-1e-11
        frame.Time<-1e-11
    do
        if replace then
            let tails=Dictionary<struct(int*string),int>()
            for index in 0..frames.Length-1 do
                let frame=frames[index]
                let definition=definitions[index]
                let key=struct(definition.Target,definition.Name)
                let mutable previous=match tails.TryGetValue key with true,value -> value | _ -> -1
                let mutable first=previous
                while first>=0 && frames[first].Previous>=0 do first<-frames[first].Previous
                let mutable sibling=first
                while sibling>=0 && frames[sibling].AbsoluteStart<=frame.AbsoluteStart do
                    sibling<-frames[sibling].Next
                    if sibling>=0 && frames[sibling].AbsoluteStart>=frame.AbsoluteStart then
                        while sibling>=0 do
                            overrideFrame sibling
                            sibling<-frames[sibling].Next
                while previous>=0 && (frames[previous].Overridden || frames[previous].AbsoluteStart>frame.AbsoluteStart) do
                    previous<-frames[previous].Previous
                let next=if previous>=0 then frames[previous].Next else first
                frame.Previous<-previous
                frame.Next<-next
                if previous>=0 then frames[previous].Next<-index
                if next>=0 then frames[next].Previous<-index else tails[key]<-index
                if previous>=0 then
                    let prior=frames[previous]
                    if prior.AbsoluteEnd>frame.AbsoluteUpdateStart then
                        let rawStart=prior.Start+program.Delay
                        let offset=prior.AbsoluteEnd-(rawStart+prior.Update)
                        let changed=PropertyNumbers.round 1e12 (frame.AbsoluteUpdateStart-offset-rawStart)
                        prior.Change<-changed
                        prior.Time<-changed
                        prior.Overlapped<-true
                        if changed<1e-11 then overrideFrame previous
    let effectiveOptions={options with Delay=program.Delay;Repeat=(if definitions.Length=0 then PlaybackRepeat.Count 0u else options.Repeat)}
    let clock=new Timer(scheduler,program.IterationDuration,options=effectiveOptions,
                        ?onBegin=onBegin,?onBeforeUpdate=onBeforeUpdate,?onUpdate=onUpdate,
                        ?onLoop=onLoop,?onPause=onPause,?onComplete=onComplete)
    let render (sample:PlaybackSample) mode muted =
        let current=clock.CaptureWriteGuard()
        let delta=sample.AbsoluteTime-clock.PreviousAbsoluteTime
        let forced=mode=PlaybackTickMode.Force || (if sample.Backwards then -delta else delta)>=200.
        let absolute=PropertyNumbers.round 1e12 (offset+clock.Delay+sample.IterationTime)
        let mutable rendered=false
        let mutable alive=current()
        for index in 0..frames.Length-1 do
            if alive then
                let frame=frames[index]
                let previous=frame.Previous
                let next=frame.Next
                let ending=frame.AbsoluteStart+frame.Change
                let takeover=if next<0 || frames[next].Overridden then ending else ending+frames[next].Delay
                let history=(frame.Time<>frame.Change || absolute<=takeover || (previous>=0 && next<0)) &&
                            (frame.Time<>0. || absolute>=frame.AbsoluteStart ||
                             (next>=0 && not frames[next].Overridden && frames[next].Time<>0. && sample.IterationTime<frames[next].Start))
                let eligible=(forced || history) && (previous<0 || sample.IterationTime>=frame.Start) &&
                             (not replace || (not frame.Overridden && (not frame.Overlapped || absolute<=ending) &&
                                              (next<0 || frames[next].Overridden || absolute<=takeover) &&
                                              (previous<0 || frames[previous].Overridden || absolute>=frames[previous].AbsoluteStart+frames[previous].Change+frame.Delay)))
                if eligible then
                    frame.Time<-Math.Clamp(sample.IterationTime-frame.Start,0.,frame.Change)
                    let definition=definitions[index]
                    let progress=definition.Ease(frame.Time/frame.Update)
                    alive<-current()
                    if alive then
                        match ValueInterpolation.sample(roundNumbers,definition.First,definition.Last,progress,precision,definition.Modifier,current) with
                        | Some struct(_,output) when current() ->
                            if not clock.InternalRender then
                                write definition.Target definition.Name output
                                rendered<-true
                            alive<-current()
                        | _ -> alive<-false
                elif frame.Time<>0. && previous>=0 && sample.IterationTime<frame.Start then frame.Time<-0.
        if alive && current() && rendered && sample.AboveZero && not muted then onRender |> Option.iter(fun action -> action clock)
    do
        clock.Seek(0.,muteCallbacks=true)
        clock.ConfigureRenderer(render,?totalDuration=(if program.IterationDuration=1e-11 then Some 1e-11 else None),
            onStretch=(fun scale ->
                let nextOffset=offset*scale
                if not(Double.IsFinite nextOffset) then invalidArg "scale" "Scaled offset must remain finite."
                let staged=frames |> Array.map(fun frame -> frame.Scaled scale)
                frames<-staged
                offset<-nextOffset))
    let mutable initialized=false
    /// <summary>The shared playback controller for this sequence.</summary>
    member _.Clock=clock
    /// <summary>Independent per-frame time snapshots for source behavior verification.</summary>
    member _.Times=frames |> Array.map _.Time
    /// <summary>Notifies explicit-from construction once, then renders initial directional values with muted reset callbacks.</summary>
    member _.Initialize(?notifyConstruction:bool)=
        let current=clock.CaptureWriteGuard()
        try
            if current() && not initialized then
                initialized<-true
                if defaultArg notifyConstruction true && (definitions |> Array.exists _.HasFrom) then onRender |> Option.iter(fun action -> action clock)
            if current() then clock.Reset()
        with _ ->
            try clock.Cancel() with _ -> ()
            reraise()
    interface IDisposable with member _.Dispose()=clock.Cancel()
