namespace Ranvier.Anime

open System
open System.Collections.Frozen
open System.Collections.Generic
open System.Globalization
open System.Text.RegularExpressions
open Ranvier
open Ranvier.Dom

// Derived from Anime.js v4.5.0, MIT, Copyright (c) 2025 Julian Garnier.
// Baseline: 2c9cf8ea00329f6768c7d7902252ed977d75ce42, src/events/scroll.js.+
/// <summary>Container and target anchors, expressed in pixels, percentages, units or source relative syntax.</summary>
type ScrollThreshold =
    { Container: string
      Target: string }
    /// <summary>Target start crossing the container end.</summary>
    static member Enter = { Container = "end"; Target = "start" }
    /// <summary>Target end crossing the container start.</summary>
    static member Leave = { Container = "start"; Target = "end" }

/// <summary>Measured axis geometry after transforms have been normalized to container coordinates.</summary>
type ScrollGeometry =
    { Offset: float
      TargetSize: float
      ContainerSize: float
      ScrollSize: float }

module internal ScrollMath =
    let round digits (value:float) =
        let scale = Math.Pow(10., float digits)
        Math.Floor(value * scale + 0.5) / scale

    let anchors =
        [ "top",0.; "left",0.; "start",0.; "min",0.
          "bottom",1.; "right",1.; "end",1.; "max",1.; "center",0.5 ]
        |> dict |> fun values -> values.ToFrozenDictionary(StringComparer.Ordinal)

    let number = Regex(@"^([+-]?(?:\d*\.)?\d+(?:[eE][+-]?\d+)?)(.*)$", RegexOptions.CultureInvariant)
    let relative = Regex(@"(\+=|-=|\*=)", RegexOptions.CultureInvariant)

    let coordinate convertUnit size under over (text:string) =
        let convert (text:string) under over =
            match anchors.TryGetValue text with
            | true, fraction ->
                size * fraction + (if text = "max" && under < 0. then under elif text = "min" && over > 0. then over else 0.)
            | _ ->
                let matched = number.Match text
                if not matched.Success then invalidArg "threshold" ("Invalid scroll anchor: " + text)
                let value = Double.Parse(matched.Groups[1].Value, CultureInfo.InvariantCulture)
                match matched.Groups[2].Value with
                | "" | "px" -> value
                | "%" -> value / 100. * size
                | unitName -> convertUnit value unitName
        let apply op a b = match op with "+=" -> a+b | "-=" -> a-b | _ -> a*b
        let matched = relative.Match text
        let result =
            if matched.Success then
                let first = text.Substring(0, matched.Index)
                let last = text.Substring(matched.Index + 2)
                let a,b = convert first under over,convert last under over
                if first = "min" then max a (apply matched.Value (convert first 0. 0.) b)
                elif first = "max" then min a (apply matched.Value (convert first 0. 0.) b)
                else apply matched.Value a b
            else convert text under over
        round 0 result

    let bounds convertUnit geometry enter leave =
        let enterTarget = coordinate convertUnit geometry.TargetSize 0. 0. enter.Target
        let leaveTarget = coordinate convertUnit geometry.TargetSize 0. 0. leave.Target
        let under = enterTarget + geometry.Offset - geometry.ContainerSize
        let over = leaveTarget + geometry.Offset - (geometry.ScrollSize - geometry.ContainerSize)
        let enterContainer = coordinate convertUnit geometry.ContainerSize under over enter.Container
        let leaveContainer = coordinate convertUnit geometry.ContainerSize under over leave.Container
        let start = enterTarget + geometry.Offset - enterContainer
        let finish = leaveTarget + geometry.Offset - leaveContainer
        start,finish,max 0. (finish-start),[|enterTarget;leaveTarget;enterContainer;leaveContainer|]

    let progress scroll start distance =
        let value = (scroll-start)/distance
        if Double.IsPositiveInfinity value || Double.IsNaN value then 0.
        else round 6 (Math.Clamp(value,0.,1.))

/// <summary>Lifecycle notifications in source callback order.</summary>
[<RequireQualifiedAccess; Diagnostics.DebuggerDisplay("{ToString(),nq}")>]
type ScrollEvent =
    | Enter | Leave | EnterForward | LeaveForward | EnterBackward | LeaveBackward
    | Update | Resize | SyncComplete
    /// <summary>The invariant scroll event name.</summary>
    override this.ToString() =
        match this with
        | Enter -> "Enter" | Leave -> "Leave"
        | EnterForward -> "EnterForward" | LeaveForward -> "LeaveForward"
        | EnterBackward -> "EnterBackward" | LeaveBackward -> "LeaveBackward"
        | Update -> "Update" | Resize -> "Resize" | SyncComplete -> "SyncComplete"

/// <summary>Playback commands accepted by method synchronization.</summary>
[<RequireQualifiedAccess; Diagnostics.DebuggerDisplay("{ToString(),nq}")>]
type ScrollCommand =
    | Play | Pause | Resume | Reverse | Alternate | Restart | Reset | Complete
    /// <summary>The invariant playback command name.</summary>
    override this.ToString() =
        match this with
        | Play -> "Play" | Pause -> "Pause" | Resume -> "Resume" | Reverse -> "Reverse"
        | Alternate -> "Alternate" | Restart -> "Restart" | Reset -> "Reset" | Complete -> "Complete"

/// <summary>Typed access to an existing animation controller; the observer does not own its lifetime.</summary>
type ScrollLink =
    { Duration: unit -> float
      CurrentTime: unit -> float
      Progress: unit -> float
      Completed: unit -> bool
      Seek: float -> bool -> unit
      Command: ScrollCommand -> unit }
    /// <summary>Links an existing timer without creating another clock.</summary>
    static member OfTimer(timer:Timer) =
        { Duration=(fun () -> timer.Duration); CurrentTime=(fun () -> timer.CurrentTime)
          Progress=(fun () -> timer.Progress); Completed=(fun () -> timer.Completed)
          Seek=(fun time muted -> timer.Seek(time,muteCallbacks=muted))
          Command=(function
            | ScrollCommand.Play -> timer.Play() | ScrollCommand.Pause -> timer.Pause()
            | ScrollCommand.Resume -> timer.Resume() | ScrollCommand.Reverse -> timer.Reverse()
            | ScrollCommand.Alternate -> timer.Alternate() | ScrollCommand.Restart -> timer.Restart()
            | ScrollCommand.Reset -> timer.Reset() | ScrollCommand.Complete -> timer.Complete()) }
    /// <summary>Links a timeline through its existing public playback operations.</summary>
    static member OfTimeline(timeline:Timeline) =
        { Duration=(fun () -> timeline.Duration); CurrentTime=(fun () -> timeline.CurrentTime)
          Progress=(fun () -> timeline.Progress); Completed=(fun () -> timeline.Completed)
          Seek=(fun time muted -> timeline.Seek(time,muteCallbacks=muted))
          Command=(function
            | ScrollCommand.Play -> timeline.Play() | ScrollCommand.Resume -> timeline.Resume()
            | ScrollCommand.Pause -> timeline.Pause() | ScrollCommand.Reverse -> timeline.Reverse()
            | ScrollCommand.Alternate -> timeline.Alternate() | ScrollCommand.Restart -> timeline.Restart()
            | ScrollCommand.Reset -> timeline.Reset() | ScrollCommand.Complete -> timeline.Complete()) }

/// <summary>Disabled, smoothed, eased or typed method synchronization.</summary>
[<RequireQualifiedAccess>]
type ScrollSync =
    | Disabled
    | Smooth of amount:float
    | Ease of (float -> float)
    | Methods of enter:ScrollCommand option * leave:ScrollCommand option
    | DirectionalMethods of enterForward:ScrollCommand option * leaveForward:ScrollCommand option * enterBackward:ScrollCommand option * leaveBackward:ScrollCommand option

/// <summary>Refreshable threshold and direction settings.</summary>
type ScrollOptions =
    { Horizontal: bool
      Repeat: bool
      Enter: ScrollThreshold
      Leave: ScrollThreshold
      Sync: ScrollSync }
    /// <summary>Vertical repeated observation with play on entry and pause on exit.</summary>
    static member Default =
        { Horizontal=false; Repeat=true; Enter=ScrollThreshold.Enter; Leave=ScrollThreshold.Leave
          Sync=ScrollSync.Methods(Some ScrollCommand.Play,Some ScrollCommand.Pause) }

/// <summary>Native debug lifecycle callbacks installed by the container adapter.</summary>
type internal ScrollDebugCallbacks =
    { Request: unit -> unit
      Refresh: unit -> unit
      Sample: unit -> unit
      Remove: unit -> unit }

/// <summary>Deterministic source scroll transitions; refresh supplies current layout and authored settings.</summary>
type ScrollObserver(readScroll:unit -> float * bool, measure:unit -> ScrollGeometry,
                    ?options:unit -> ScrollOptions, ?convertUnit:float -> string -> float,
                    ?onEvent:ScrollEvent -> ScrollObserver -> unit, ?wake:unit -> unit,
                    ?onRevert:unit -> unit) as this =
    let options = defaultArg options (fun () -> ScrollOptions.Default)
    let convertUnit = defaultArg convertUnit (fun _ unitName -> invalidArg "threshold" ("No unit context for " + unitName))
    let onEvent = defaultArg onEvent (fun _ _ -> ())
    let wake = defaultArg wake ignore
    let onRevert = defaultArg onRevert ignore
    let mutable settings = ScrollOptions.Default
    let mutable linked:ScrollLink option = None
    let mutable geometry = Unchecked.defaultof<ScrollGeometry>
    let mutable start,finish,distance = 0.,0.,0.
    let mutable coords = [||]
    let mutable ready,reverted,began,completed,inView,forceEnter,hasEntered = false,false,false,false,false,false,false
    let mutable previousProgress = 0.
    let mutable generation = 0L
    let mutable debug:ScrollDebugCallbacks option = None
    let emit event =
        if not reverted then
            try onEvent event this
            with _ ->
                try this.Revert() with _ -> ()
                reraise()
    let command value = value |> Option.iter(fun value -> linked |> Option.iter(fun linked -> linked.Command value))
    let syncEvent event =
        if not reverted then
            match settings.Sync,event with
            | ScrollSync.Methods(enter,_),ScrollEvent.Enter -> command enter
            | ScrollSync.Methods(_,leave),ScrollEvent.Leave -> command leave
            | ScrollSync.DirectionalMethods(a,_,_,_),ScrollEvent.EnterForward -> command a
            | ScrollSync.DirectionalMethods(_,a,_,_),ScrollEvent.LeaveForward -> command a
            | ScrollSync.DirectionalMethods(_,_,a,_),ScrollEvent.EnterBackward -> command a
            | ScrollSync.DirectionalMethods(_,_,_,a),ScrollEvent.LeaveBackward -> command a
            | _ -> ()
    let notify event = syncEvent event; emit event

    /// <summary>Pauses and retains a borrowed playback controller.</summary>
    member _.Link(value:ScrollLink) =
        if reverted then raise(ObjectDisposedException(nameof ScrollObserver))
        value.Command ScrollCommand.Pause
        linked <- Some value
        this
    /// <summary>Recomputes thresholds after a muted measurement at the linked controller's zero playhead.</summary>
    member _.Refresh() =
        if reverted then raise(ObjectDisposedException(nameof ScrollObserver))
        generation <- generation + 1L
        let expected = generation
        settings <- options()
        match settings.Sync with
        | ScrollSync.Smooth amount when not(Double.IsFinite amount) -> invalidArg "Sync" "Smoothing must be finite."
        | _ -> ()
        debug |> Option.iter(fun debug -> debug.Remove())
        let time = linked |> Option.map(fun linked -> linked.CurrentTime())
        try
            linked |> Option.iter(fun linked -> linked.Seek 0. true)
            if expected = generation && not reverted then
                geometry <- measure()
                let a,b,d,c = ScrollMath.bounds convertUnit geometry settings.Enter settings.Leave
                start <- a; finish <- b; distance <- d; coords <- c
                ready <- true
        finally
            match linked,time with
            | Some linked,Some time when expected = generation && not reverted -> linked.Seek time true
            | _ -> ()
        if expected = generation && not reverted then
            debug |> Option.iter(fun debug -> debug.Refresh())
            this.HandleScroll()
    /// <summary>Samples current scroll position, preserving edge entry and directional callback order.</summary>
    member _.HandleScroll() =
        if ready && not reverted then
            generation <- generation + 1L
            let expected = generation
            let live () = expected = generation && not reverted
            let scroll,backward = readScroll()
            let before,after = scroll <= start,scroll >= finish
            let inside = not before && not after
            let forced = not hasEntered && (scroll = start || scroll = finish)
            let mutable p = ScrollMath.progress scroll start distance
            let mutable updated,syncCompleted = false,false
            let smooth = match settings.Sync with ScrollSync.Smooth amount -> Some amount | _ -> None
            let seek = linked.IsSome && (match settings.Sync with ScrollSync.Smooth amount -> amount <> 0. | ScrollSync.Ease _ -> true | _ -> false)
            let sync = match settings.Sync with ScrollSync.Disabled | ScrollSync.Smooth 0. -> false | _ -> true
            debug |> Option.iter(fun debug -> debug.Sample())
            if before && began then began <- false
            if p > 0. && not began then began <- true
            if seek then
                let lp = linked.Value.Progress()
                match settings.Sync with
                | ScrollSync.Smooth amount when amount <> 0. && amount < 1. ->
                    let snap = if lp < p && p = 1. then 0.0001 elif lp > p && p = 0. then -0.0001 else 0.
                    p <- ScrollMath.round 6 (lp + (p-lp)*(0.01+(0.2-0.01)*amount)+snap)
                | ScrollSync.Ease ease -> p <- ease p
                | _ -> ()
                updated <- p <> previousProgress
                syncCompleted <- lp = 1.
                if updated && not syncCompleted && smooth.IsSome && smooth.Value <> 0. && lp <> 0. && live() then wake()
            if live() && ((inside && not inView) || (forced && not forceEnter && not hasEntered)) then
                if inside then inView <- true
                if not forceEnter || not hasEntered then
                    notify ScrollEvent.Enter
                    if live() then notify (if backward then ScrollEvent.EnterBackward else ScrollEvent.EnterForward)
                    if live() then hasEntered <- true; if forced then forceEnter <- true
                elif inside then forceEnter <- false
            if inside || (not inside && inView) then updated <- true
            if updated && live() then
                if seek then linked.Value.Seek (linked.Value.Duration()*p) false
                if live() then emit ScrollEvent.Update
            if not inside && inView && live() then
                inView <- false
                notify ScrollEvent.Leave
                if live() then notify (if backward then ScrollEvent.LeaveBackward else ScrollEvent.LeaveForward)
                if sync && (smooth.IsNone || smooth.Value = 0.) then syncCompleted <- true
            if p >= 1. && began && not completed && ((sync && syncCompleted) || not sync) && live() then
                if sync then emit ScrollEvent.SyncComplete
                if live() then
                    completed <- true
                    if not settings.Repeat && (linked.IsNone || linked.Value.Completed()) then this.Revert()
            if p < 1. && completed && live() then completed <- false
            if live() then previousProgress <- p
    /// <summary>Refreshes measurements and emits the resize notification after the refresh sample.</summary>
    member _.Resize() = this.Refresh(); emit ScrollEvent.Resize
    /// <summary>Ends observation without cancelling the borrowed animation.</summary>
    member _.Revert() =
        if not reverted then
            generation <- generation + 1L
            reverted <- true
            ready <- false
            try debug |> Option.iter(fun debug -> debug.Remove())
            finally onRevert()
    /// <summary>Installs container-owned DOM debug operations.</summary>
    member internal _.AttachDebug(callbacks:ScrollDebugCallbacks) = debug <- Some callbacks
    /// <summary>Displays resolved entry and exit anchors until observation ends.</summary>
    member _.Debug() =
        if reverted then raise(ObjectDisposedException(nameof ScrollObserver))
        debug |> Option.iter(fun debug -> debug.Request())
        this
    /// <summary>Removes the current debug overlay; a later refresh redraws enabled debugging.</summary>
    member _.RemoveDebug() = debug |> Option.iter(fun debug -> debug.Remove()); this
    /// <summary>Attaches cleanup to the current Ranvier owner.</summary>
    member _.Own() = Api.getOwner().Attach(this :> IDisposable); this
    /// <summary>Current bounded source scroll progress.</summary>
    member _.Progress = let scroll,_ = readScroll() in ScrollMath.progress scroll start distance
    /// <summary>Whether the scroll coordinate lies strictly inside the bounds.</summary>
    member _.IsInView = inView
    /// <summary>Whether positive scroll progress has begun.</summary>
    member _.Began = began
    /// <summary>Whether the current observation has reached completion.</summary>
    member _.Completed = completed
    /// <summary>Whether geometry is initialized.</summary>
    member _.Ready = ready
    /// <summary>Whether cleanup has ended this observer.</summary>
    member _.Reverted = reverted
    /// <summary>Target offset in container coordinates.</summary>
    member _.Offset = if ready then geometry.Offset else 0.
    /// <summary>Entry coordinate in container scroll space.</summary>
    member _.OffsetStart = start
    /// <summary>Exit coordinate in container scroll space.</summary>
    member _.OffsetEnd = finish
    /// <summary>Nonnegative distance between thresholds.</summary>
    member _.Distance = distance
    /// <summary>Independent snapshot of resolved target and container anchors.</summary>
    member _.Coordinates = Array.copy coords
    interface IDisposable with member _.Dispose() = this.Revert()
