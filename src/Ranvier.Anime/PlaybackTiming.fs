namespace Ranvier.Anime

/// <summary>The source engine's skipped, automatic and forced sampling modes.</summary>
type internal PlaybackTickMode = Skip = 0 | Auto = 1 | Force = 2

/// <summary>One clock sample, separating raw time, public rounded getters and render eligibility.</summary>
[<Struct>]
type internal PlaybackSample(absoluteTime: float, previousTime: float, time: float, currentTime: float, iteration: int,
                             iterationTime: float, iterationCurrentTime: float, progress: float, iterationProgress: float,
                             backwards: bool, aboveZero: bool, atEnd: bool, shouldRender: bool) =
    /// <summary>Unclamped time after subtracting the initial delay, in milliseconds.</summary>
    member _.AbsoluteTime = absoluteTime
    /// <summary>The preceding raw time bounded to the timer's delay and duration.</summary>
    member _.PreviousTime = previousTime
    /// <summary>Current unrounded time bounded to the timer's delay and duration.</summary>
    member _.Time = time
    /// <summary>Current time rounded to the source's default four decimal places.</summary>
    member _.CurrentTime = currentTime
    /// <summary>The source iteration index, including its signed 32-bit truncation behavior.</summary>
    member _.Iteration = iteration
    /// <summary>Unclamped directional time inside the current iteration, in milliseconds.</summary>
    member _.IterationTime = iterationTime
    /// <summary>Directional iteration time rounded and bounded to the iteration duration.</summary>
    member _.IterationCurrentTime = iterationCurrentTime
    /// <summary>Overall progress rounded to ten decimal places and bounded to zero through one.</summary>
    member _.Progress = progress
    /// <summary>Iteration progress rounded and bounded as source, including undefined zero-duration ratios.</summary>
    member _.IterationProgress = iterationProgress
    /// <summary>Whether sampling direction and iteration reversal combine into backward playback.</summary>
    member _.Backwards = backwards
    /// <summary>Whether bounded current time is positive.</summary>
    member _.AboveZero = aboveZero
    /// <summary>Whether bounded current time reached total duration.</summary>
    member _.AtEnd = atEnd
    /// <summary>Whether source render gates admit this sample before callback muting or lifetime checks.</summary>
    member _.ShouldRender = shouldRender

/// <summary>Standalone timer geometry shared by playback controllers; input time includes the initial delay.</summary>
[<Sealed>]
type internal PlaybackTiming(iterationDuration: float, iterationCount: float, delay: float, loopDelay: float, ?totalDuration: float) as this =
    do
        if System.Double.IsNaN iterationDuration || iterationDuration < 0. then
            invalidArg "iterationDuration" "Expected nonnegative milliseconds or positive infinity."
        if System.Double.IsNaN iterationCount || iterationCount < 1. ||
           (System.Double.IsFinite iterationCount && System.Math.Truncate iterationCount <> iterationCount) then
            invalidArg "iterationCount" "Expected a positive whole iteration count or positive infinity."
        if not(System.Double.IsFinite delay) then invalidArg "delay" "Expected finite milliseconds."
        if not(System.Double.IsFinite loopDelay) then invalidArg "loopDelay" "Expected finite milliseconds."
    let duration =
        let total = defaultArg totalDuration ((iterationDuration + loopDelay) * iterationCount - loopDelay)
        if System.Double.IsNaN total || (total = 0. && totalDuration.IsNone) then 1e-11
        elif System.Double.IsPositiveInfinity total then 1e12
        elif System.Double.IsNegativeInfinity total then -1e12
        else total
    let integer32 value =
        if not(System.Double.IsFinite value) || value = 0. then 0
        else
            let residue = System.Math.Truncate value % 4294967296.
            int (if residue >= 2147483648. then residue - 4294967296.
                 elif residue < -2147483648. then residue + 4294967296.
                 else residue)
    /// <summary>Total duration after repeat gaps and the source's infinity/zero normalization.</summary>
    member _.Duration = duration
    /// <summary>Iteration duration retained independently of total duration after stretching.</summary>
    member _.IterationDuration = iterationDuration
    /// <summary>Initial delay in milliseconds.</summary>
    member _.Delay = delay
    /// <summary>Gap between repeated iterations in milliseconds.</summary>
    member _.LoopDelay = loopDelay
    /// <summary>Prepares source stretch geometry; finite nonnegative input and finite scaled delays are required.</summary>
    member _.Stretch(value:float) =
        if not(System.Double.IsFinite value) || value<0. then invalidArg "value" "Expected finite nonnegative milliseconds."
        let normalize value =
            if value<=1e-11 then 1e-11
            else
                let rounded=PropertyNumbers.round 1e11 value
                if System.Double.IsPositiveInfinity rounded then 1e12
                elif System.Double.IsNegativeInfinity rounded then -1e12
                else rounded
        let total=normalize value
        if total=duration then this
        else
            let scale=value/duration
            let iteration=if value<=1e-11 then 1e-11 else normalize(iterationDuration*scale)
            PlaybackTiming(iteration,iterationCount,delay*scale,loopDelay*scale,totalDuration=total)
    /// <summary>Refreshes bounded getters after a geometry change while retaining raw playhead and lifecycle samples.</summary>
    member _.Rebound(sample:PlaybackSample) =
        let lower = -delay
        let rounded=PropertyNumbers.round 1e4 sample.AbsoluteTime
        let current=
            if lower>duration then (if rounded<lower then lower else duration)
            else System.Math.Clamp(rounded,lower,duration)
        let iteration=System.Math.Clamp(PropertyNumbers.round 1e4 sample.IterationTime,0.,iterationDuration)
        let progress=System.Math.Clamp(PropertyNumbers.round 1e10 (sample.AbsoluteTime/duration),0.,1.)
        let iterationProgress=System.Math.Clamp(PropertyNumbers.round 1e10 (sample.IterationTime/iterationDuration),0.,1.)
        PlaybackSample(sample.AbsoluteTime,sample.PreviousTime,sample.Time,current,sample.Iteration,sample.IterationTime,
                       iteration,progress,iterationProgress,sample.Backwards,sample.AboveZero,sample.AtEnd,sample.ShouldRender)
    /// <summary>Samples raw time without mutating callback, scheduler or ownership state.</summary>
    member _.Sample(previousAbsoluteTime: float, time: float, reversed: bool, alternate: bool, completed: bool,
                    mode: PlaybackTickMode, ?parentBackwards: bool, ?child: bool) : PlaybackSample =
        if System.Double.IsNaN previousAbsoluteTime then invalidArg "previousAbsoluteTime" "Expected a defined clock time."
        if System.Double.IsNaN time then invalidArg "time" "Expected a defined clock time."
        let absolute = time - delay
        let lower = -delay
        let previous =
            if lower > duration then (if previousAbsoluteTime < lower then lower else duration)
            else System.Math.Clamp(previousAbsoluteTime, lower, duration)
        let bounded =
            if lower > duration then (if absolute < lower then lower else duration)
            else System.Math.Clamp(absolute, lower, duration)
        let aboveZero, atEnd = bounded > 0., bounded >= duration
        let index, iterationElapsed =
            if iterationCount > 1. then
                let period = iterationDuration + (if atEnd then 0. else loopDelay)
                let rawIndex = integer32 (bounded / period)
                let index = int(System.Math.Clamp(float rawIndex, 0., iterationCount)) - (if atEnd then 1 else 0)
                let elapsed = bounded - float rawIndex * period
                index, (if System.Double.IsNaN elapsed || elapsed = 0. then 0. else elapsed)
            else 0, absolute
        let isReversed = ((if reversed then 1 else 0) ^^^ (if alternate then index % 2 else 0)) <> 0
        let iterationTime =
            if atEnd then (if isReversed then 0. else duration)
            elif isReversed then iterationDuration - iterationElapsed
            else iterationElapsed
        let backwards = (defaultArg parentBackwards (absolute < previousAbsoluteTime)) <> isReversed
        let endTime = delay + iterationDuration
        let shouldRender =
            mode = PlaybackTickMode.Force ||
            (mode = PlaybackTickMode.Auto && ((time >= (if defaultArg child false && delay > 0. then 0. else delay) && time <= endTime) ||
                                            (time <= delay && previous > delay) ||
                                            (time >= endTime && previous <> duration))) ||
            (iterationTime >= endTime && previous <> duration) ||
            (iterationTime <= delay && previous > 0. && not atEnd) ||
            (time <= previous && previous = duration && completed) ||
            (atEnd && not completed && duration <= 1e-11)
        let rounded = PropertyNumbers.round 1e4 absolute
        let currentTime =
            if lower > duration then (if rounded < lower then lower else duration)
            else System.Math.Clamp(rounded, lower, duration)
        let iterationCurrent = System.Math.Clamp(PropertyNumbers.round 1e4 iterationTime, 0., iterationDuration)
        let progress = System.Math.Clamp(PropertyNumbers.round 1e10 (absolute / duration), 0., 1.)
        let iterationProgress = System.Math.Clamp(PropertyNumbers.round 1e10 (iterationTime / iterationDuration), 0., 1.)
        PlaybackSample(absolute, previous, bounded, currentTime, index, iterationTime, iterationCurrent, progress,
                       iterationProgress, backwards, aboveZero, atEnd, shouldRender)
