namespace Ranvier.Anime

/// <summary>Scalar arithmetic preserving source rounding and finite-endpoint interpolation.</summary>
module internal PropertyNumbers =
    /// <summary>Rounds scaled values with JavaScript's positive half ties.</summary>
    let round (factor: float) (value: float) =
        let scaled = value * factor
        let rounded = System.Math.Round(scaled, System.MidpointRounding.ToEven)
        (if abs(scaled - rounded) = 0.5 then System.Math.Ceiling scaled else rounded) / factor
    /// <summary>Interpolates finite endpoints without overflowing their difference.</summary>
    let interpolate first last progress =
        if progress = 0. then first
        elif progress = 1. then last
        else
            let delta = last - first
            if System.Double.IsFinite delta then first + delta * progress
            else first * (1. - progress) + last * progress

/// <summary>A scalar replace sibling retaining source handoff, cache and override state.</summary>
[<Sealed>]
type internal NumericPropertyTrack private (property: NumericProperty, first: float, target: float, offset: float,
                                duration: float, updateDuration: float, ease: float -> float, modifier: float -> float, hasFrom: bool, cached: float) =
    let mutable previous: NumericPropertyTrack option = None
    let mutable next: NumericPropertyTrack option = None
    let mutable overridden = false
    let mutable overlapped = false
    let mutable changeDuration = updateDuration
    let mutable tweenTime = 0.
    let mutable parentTime = 0.
    let mutable completed = false
    let mutable backwardsValue = false
    let mutable rendered = false
    let mutable cachedValue = cached
    let absoluteStart = PropertyNumbers.round 1e12 offset
    let absoluteEnd = PropertyNumbers.round 1e12 (offset + updateDuration)
    /// <summary>The raw timeline offset in milliseconds.</summary>
    member _.Offset = offset
    /// <summary>The normalized child duration in milliseconds.</summary>
    member _.Duration = duration
    /// <summary>Whether the most recent sample completed this track.</summary>
    member _.Completed = completed
    /// <summary>Raw time at the preceding sample.</summary>
    member _.CurrentTime = parentTime
    /// <summary>Parent-directed playback direction at the preceding sample.</summary>
    member _.Backwards = backwardsValue
    /// <summary>Whether the most recent sample dispatched a value at positive child time.</summary>
    member _.Rendered = rendered
    member private _.Start = absoluteStart
    member private _.End = absoluteEnd
    member private _.Cached = cachedValue
    member private _.Change with get() = changeDuration and set value = changeDuration <- value
    member private _.Time with get() = tweenTime and set value = tweenTime <- value
    member private _.Previous with get() = previous and set value = previous <- value
    member private _.Next with get() = next and set value = next <- value
    member private _.Overridden = overridden
    member private _.Overlap with set value = overlapped <- value
    member private _.Override() =
        overlapped <- true
        overridden <- true
        changeDuration <- 1e-11
        tweenTime <- 1e-11
    /// <summary>Resets completion while preserving timer and tween sample history.</summary>
    member _.ResetTimer() = completed <- false
    /// <summary>Samples a scalar sibling, caching internal renders and guarding dispatched writes after user callbacks.</summary>
    member _.Sample(localTime: float, force: bool, internalRender: bool, muted: bool,
                    backwards: bool, parentBegan: bool, canWrite: unit -> bool, ?skipRender: bool) =
        let skipRender = defaultArg skipRender false
        rendered <- false
        backwardsValue <- backwards
        let prior = System.Math.Clamp(parentTime, 0., duration)
        let delta = localTime - parentTime
        let current = System.Math.Clamp(localTime, 0., duration)
        parentTime <- localTime
        let atEnd = current >= duration
        let setter = duration <= 1e-11
        let iteration = if atEnd then duration else localTime
        let eligible =
            force || (not skipRender && ((localTime >= 0. && localTime <= duration) ||
            (localTime <= 0. && prior > 0.) || (localTime >= duration && prior <> duration))) ||
            (iteration >= duration && prior <> duration) || (iteration <= 0. && prior > 0. && not atEnd) ||
            (localTime <= prior && prior = duration && completed) || (atEnd && not completed && setter)
        if eligible && canWrite() then
            let absolute = PropertyNumbers.round 1e12 (offset + iteration)
            let previousEnd = previous |> Option.map(fun track -> track.Start + track.Change) |> Option.defaultValue 0.
            let ownEnd = absoluteStart + changeDuration
            let nextTakeover = next |> Option.filter(fun track -> not track.Overridden) |> Option.map _.Start |> Option.defaultValue ownEnd
            let previousAlive = previous |> Option.exists(fun track -> not track.Overridden)
            let canEvaluate =
                (force || abs delta >= 200. ||
                 ((tweenTime <> changeDuration || absolute <= nextTakeover) &&
                  (tweenTime <> 0. || absolute >= absoluteStart ||
                   (previousAlive && not hasFrom && absolute >= previousEnd)))) &&
                not overridden && (not overlapped || absolute <= ownEnd) &&
                (next |> Option.forall(fun track -> track.Overridden || absolute <= nextTakeover)) &&
                (not previousAlive || absolute >= absoluteStart || (not hasFrom && absolute >= previousEnd))
            if canEvaluate then
                tweenTime <- System.Math.Clamp(iteration, 0., changeDuration)
                let progress = ease (tweenTime / updateDuration)
                if canWrite() then
                    if not(System.Double.IsFinite progress) then invalidArg "ease" "Expected finite progress."
                    let value = modifier (PropertyNumbers.interpolate first target progress)
                    if canWrite() then
                        if not(System.Double.IsFinite value) then invalidArg "modifier" "Expected a finite value."
                        if internalRender then cachedValue <- value
                        else
                            property.Value <- value
                            if canWrite() then rendered <- current > 0.
        if canWrite() then
            if setter then
                if not muted && ((parentBegan && not backwards && localTime > 0. && not completed) ||
                                 (backwards && localTime <= 1e-11 && completed)) then
                    completed <- not backwards
            else completed <- current > 0. && atEnd
    /// <summary>Captures a start value and composes a new sibling when construction has not been superseded.</summary>
    static member Create(property: NumericProperty, siblings: System.Collections.Generic.List<NumericPropertyTrack>,
                         fromValue: float option, target: float, offset: float, duration: float, updateDuration: float,
                         ease: float -> float, modifier: float -> float, canWrite: unit -> bool) =
        let start = PropertyNumbers.round 1e12 offset
        let ordered = siblings.ToArray()
        let mutable previous = None
        let mutable index = 0
        let mutable stop = false
        let mutable overrideFrom = ordered.Length
        while index < ordered.Length && not stop && ordered[index].Start <= start do
            if not ordered[index].Overridden then previous <- Some ordered[index]
            index <- index + 1
            if index < ordered.Length && ordered[index].Start >= start then
                overrideFrom <- index
                stop <- true
        let first =
            match fromValue, previous with
            | Some value, _ -> value
            | None, Some track -> track.Cached
            | _ -> property.Value
        if not(canWrite()) then None
        else
            let cached = modifier target
            if not(canWrite()) then None
            else
                if not(System.Double.IsFinite first && System.Double.IsFinite cached) then invalidArg "property" "Expected finite property values."
                for index in overrideFrom .. ordered.Length - 1 do ordered[index].Override()
                siblings.RemoveAll(fun track -> track.Overridden) |> ignore
                let track = NumericPropertyTrack(property, first, target, offset, duration, updateDuration, ease, modifier, fromValue.IsSome, cached)
                let mutable insert = 0
                while insert < siblings.Count && siblings[insert].Start <= start do insert <- insert + 1
                let prior = if insert = 0 then None else Some siblings[insert - 1]
                match prior with
                | Some previous when previous.End > start ->
                    let change = PropertyNumbers.round 1e12 (start - previous.Start)
                    previous.Change <- change
                    previous.Time <- change
                    previous.Overlap <- true
                    if change < 1e-11 then previous.Override()
                | _ -> ()
                siblings.Insert(insert, track)
                siblings.RemoveAll(fun item -> item.Overridden) |> ignore
                for index in 0 .. siblings.Count - 1 do
                    siblings[index].Previous <- if index = 0 then None else Some siblings[index - 1]
                    siblings[index].Next <- if index + 1 = siblings.Count then None else Some siblings[index + 1]
                Some track
