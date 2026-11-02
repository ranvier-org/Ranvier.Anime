namespace Ranvier.Anime

open System

/// <summary>A stateful Anime.js spring easing with physical or perceived-duration parameters.</summary>
/// <remarks>Duration values are milliseconds; Solve accepts seconds. Supplying bounce or duration resets mass to one and velocity to zero.</remarks>
type Spring(?mass: float, ?stiffness: float, ?damping: float, ?velocity: float, ?bounce: float, ?duration: float,
            ?onComplete: (unit -> unit)) =
    let finite name value =
        if not (Double.IsFinite value) then invalidArg name "Expected a finite value."
    let parameter name fallback supplied =
        let value = defaultArg supplied fallback
        finite name value
        value
    let round digits value =
        let factor = Math.Pow(10., float digits)
        Math.Floor(value * factor + 0.5) / factor
    let mutable massValue = Math.Clamp(parameter "mass" 1. mass, 1., 10000.)
    let mutable stiffnessValue = Math.Clamp(parameter "stiffness" 100. stiffness, 1e-11, 10000.)
    let mutable dampingValue = Math.Clamp(parameter "damping" 10. damping, 1e-11, 10000.)
    let mutable velocityValue = Math.Clamp(parameter "velocity" 0. velocity, -10000., 10000.)
    let mutable bounceValue = Math.Clamp(parameter "bounce" 0.5 bounce, -1., 1.)
    let mutable durationValue = Math.Clamp(parameter "duration" 628. duration, 10., 10000.)
    let onComplete = defaultArg onComplete ignore
    let mutable naturalFrequency = 0.
    let mutable dampingRatio = 0.
    let mutable dampedFrequency = 0.
    let mutable coefficient = 0.
    let mutable solverDuration = 0.
    let mutable settlingDuration = 0.
    let mutable completed = false

    // Solver adapted through Anime.js from WebKit spring.js, (c) 2016 WebKit - Apple Inc.
    let solve time =
        let displacement =
            if dampingRatio < 1. then
                Math.Exp(-time * dampingRatio * naturalFrequency) *
                    (Math.Cos(dampedFrequency * time) + coefficient * Math.Sin(dampedFrequency * time))
            elif dampingRatio = 1. then
                (1. + coefficient * time) * Math.Exp(-time * naturalFrequency)
            else
                ((1. + coefficient) * Math.Exp((-dampingRatio * naturalFrequency + dampedFrequency) * time) +
                 (1. - coefficient) * Math.Exp((-dampingRatio * naturalFrequency - dampedFrequency) * time)) / 2.
        1. - displacement

    let physicalFromPerceived () =
        let seconds = durationValue / 1000.
        massValue <- 1.
        velocityValue <- 0.
        stiffnessValue <- Math.Pow((2. * Math.PI) / seconds, 2.)
        dampingValue <-
            if bounceValue >= 0. then ((1. - bounceValue) * 4. * Math.PI) / seconds
            else (4. * Math.PI) / (seconds * (1. + bounceValue))
        stiffnessValue <- Math.Clamp(stiffnessValue, 1e-11, 10000.) |> round 3
        dampingValue <- Math.Clamp(dampingValue, 1e-11, 300.) |> round 3

    let perceivedFromPhysical () =
        let seconds = (2. * Math.PI) / Math.Sqrt stiffnessValue
        durationValue <- seconds * 1000.
        let ratio = dampingValue / (2. * Math.Sqrt stiffnessValue)
        bounceValue <-
            if ratio <= 1. then 1. - (dampingValue * seconds) / (4. * Math.PI)
            else (4. * Math.PI) / (dampingValue * seconds) - 1.
        bounceValue <- Math.Clamp(bounceValue, -1., 1.) |> round 3
        durationValue <- Math.Clamp(durationValue, 10., 10000.) |> round 3

    let compute () =
        naturalFrequency <- Math.Clamp(Math.Sqrt(stiffnessValue / massValue), 1e-11, 1000.)
        dampingRatio <- dampingValue / (2. * Math.Sqrt(stiffnessValue * massValue))
        if dampingRatio < 1. then
            dampedFrequency <- naturalFrequency * Math.Sqrt(1. - dampingRatio * dampingRatio)
            coefficient <- (dampingRatio * naturalFrequency - velocityValue) / dampedFrequency
        elif dampingRatio = 1. then
            dampedFrequency <- 0.
            coefficient <- -velocityValue + naturalFrequency
        else
            dampedFrequency <- naturalFrequency * Math.Sqrt(dampingRatio * dampingRatio - 1.)
            coefficient <- (dampingRatio * naturalFrequency - velocityValue) / dampedFrequency
        let mutable time = 0.
        let mutable restSteps = 0
        let mutable iterations = 0
        while restSteps <= 10 && iterations <= 3000 do
            if abs(1. - solve time) < 0.0005 then restSteps <- restSteps + 1 else restSteps <- 0
            solverDuration <- time
            time <- time + 0.02
            iterations <- iterations + 1
        settlingDuration <- round 0 (solverDuration * 1000.)

    do
        if bounce.IsSome || duration.IsSome then physicalFromPerceived()
        compute()

    /// <summary>Mass clamped to one through 10000; setting it recomputes the solver.</summary>
    member _.Mass
        with get() = massValue
        and set value =
            finite "value" value
            massValue <- Math.Clamp(value, 1., 10000.)
            compute()

    /// <summary>Stiffness clamped to 1e-11 through 10000; setting it also updates bounce and perceived duration.</summary>
    member _.Stiffness
        with get() = stiffnessValue
        and set value =
            finite "value" value
            stiffnessValue <- Math.Clamp(value, 1e-11, 10000.)
            perceivedFromPhysical()
            compute()

    /// <summary>Setting damping clamps to 1e-11 through 10000 and updates bounce and perceived duration.</summary>
    member _.Damping
        with get() = dampingValue
        and set value =
            finite "value" value
            dampingValue <- Math.Clamp(value, 1e-11, 10000.)
            perceivedFromPhysical()
            compute()

    /// <summary>Initial velocity clamped to negative 10000 through 10000; setting it recomputes the solver.</summary>
    member _.Velocity
        with get() = velocityValue
        and set value =
            finite "value" value
            velocityValue <- Math.Clamp(value, -10000., 10000.)
            compute()

    /// <summary>Bounce clamped to negative one through one; setting it derives physical parameters and resets mass and velocity.</summary>
    member _.Bounce
        with get() = bounceValue
        and set value =
            finite "value" value
            bounceValue <- Math.Clamp(value, -1., 1.)
            physicalFromPerceived()
            compute()

    /// <summary>Perceived duration in milliseconds, clamped to 10 through 10000; setting it derives physical parameters.</summary>
    member _.Duration
        with get() = durationValue
        and set value =
            finite "value" value
            durationValue <- Math.Clamp(value, 10., 10000.)
            physicalFromPerceived()
            compute()

    /// <summary>Physical time in seconds used to normalize this spring's easing curve.</summary>
    member _.SolverDuration = solverDuration

    /// <summary>Calculated settling time in milliseconds, used as an animation's actual duration.</summary>
    member _.SettlingDuration = settlingDuration

    /// <summary>Whether the last easing sample reached perceived duration; a backward sample can reset it.</summary>
    member _.Completed = completed

    /// <summary>Evaluates displacement at a finite physical time in seconds.</summary>
    member _.Solve(time: float) =
        finite "time" time
        solve time

    /// <summary>Evaluates finite progress, including signed axis extrapolation, and notifies completion when perceived duration is crossed.</summary>
    /// <remarks>Calling below the perceived threshold resets completion, allowing a later forward crossing to notify again.</remarks>
    member _.Ease(time: float) =
        finite "time" time
        let currentTime = time * settlingDuration
        let perceivedTime = durationValue
        if currentTime >= perceivedTime && not completed then
            completed <- true
            onComplete()
        if currentTime < perceivedTime && completed then completed <- false
        if time = 0. || time = 1. then time else solve (time * solverDuration)

/// <summary>Convenience constructors for spring authoring.</summary>
[<RequireQualifiedAccess>]
module Spring =
    /// <summary>Creates a perceived-mode spring from unsigned whole milliseconds.</summary>
    let perceivedMilliseconds (duration: uint) bounce =
        new Spring(duration = float duration, bounce = bounce)
