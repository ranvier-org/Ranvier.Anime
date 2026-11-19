namespace Ranvier.Anime

open System

module private SpringMath =
    let inline finite name value =
        if not (System.Double.IsFinite value) then invalidArg name "Expected a finite value."

    let inline parameter name fallback supplied =
        let value = defaultArg supplied fallback
        finite name value
        value

    let inline round0 value =
        System.Math.Floor(value + 0.5)

    let inline round3 value =
        System.Math.Floor(value * 1000. + 0.5) * 0.001

    let inline scaledExp factor exponent =
        if factor = 0. then 0. else factor * System.Math.Exp exponent

    let inline clampFinite value =
        if System.Double.IsPositiveInfinity value then System.Double.MaxValue
        elif System.Double.IsNegativeInfinity value then -System.Double.MaxValue
        elif System.Double.IsNaN value then 1.
        else value

    [<Struct>]
    type SolverState =
        val mutable Mass: float
        val mutable Stiffness: float
        val mutable Damping: float
        val mutable Velocity: float
        val mutable Bounce: float
        val mutable Duration: float
        val mutable NaturalFrequency: float
        val mutable DampingRatio: float
        val mutable NegZetaW0: float
        val mutable DampedFrequency: float
        val mutable Coefficient: float
        val mutable OverdampedSlowRate: float
        val mutable OverdampedFastRate: float
        val mutable SolverDuration: float
        val mutable NaturalSettlingDuration: float
        val mutable SettlingDuration: float

    let inline solve (s: inref<SolverState>) (time: float) =
        let displacement =
            if s.DampingRatio < 1. then
                let env = System.Math.Exp(s.NegZetaW0 * time)
                let wt = s.DampedFrequency * time
                let osc = System.Math.Cos wt + (if s.Coefficient = 0. then 0. else s.Coefficient * System.Math.Sin wt)
                if osc = 0. then 0. else env * osc
            elif s.DampingRatio = 1. then
                scaledExp (1. + s.Coefficient * time) (-time * s.NaturalFrequency)
            else
                (scaledExp (1. + s.Coefficient) (s.OverdampedSlowRate * time) +
                 scaledExp (1. - s.Coefficient) (s.OverdampedFastRate * time)) * 0.5
        clampFinite (1. - displacement)

    let physicalFromPerceived (s: byref<SolverState>) =
        let seconds = s.Duration * 0.001
        s.Mass <- 1.
        s.Velocity <- 0.
        let twoPiOverSec = (2. * System.Math.PI) / seconds
        s.Stiffness <- System.Math.Clamp(twoPiOverSec * twoPiOverSec, 1e-11, 10000.) |> round3
        let rawDamping =
            if s.Bounce >= 0. then ((1. - s.Bounce) * 4. * System.Math.PI) / seconds
            else (4. * System.Math.PI) / (seconds * (1. + s.Bounce))
        s.Damping <- System.Math.Clamp(rawDamping, 1e-11, 300.) |> round3

    let perceivedFromPhysical (s: byref<SolverState>) =
        let sqrtStiff = System.Math.Sqrt s.Stiffness
        let seconds = (2. * System.Math.PI) / sqrtStiff
        let ratio = s.Damping / (2. * sqrtStiff)
        let rawBounce =
            if ratio <= 1. then 1. - (s.Damping * seconds) / (4. * System.Math.PI)
            else (4. * System.Math.PI) / (s.Damping * seconds) - 1.
        s.Bounce <- System.Math.Clamp(rawBounce, -1., 1.) |> round3
        s.Duration <- System.Math.Clamp(seconds * 1000., 10., 10000.) |> round3

    let compute (s: byref<SolverState>) =
        let w0 = System.Math.Clamp(System.Math.Sqrt(s.Stiffness / s.Mass), 1e-11, 1000.)
        let zeta = s.Damping / (2. * System.Math.Sqrt(s.Stiffness * s.Mass))
        let zetaW0 = zeta * w0
        let negZetaW0 = -zetaW0
        s.NaturalFrequency <- w0
        s.DampingRatio <- zeta
        s.NegZetaW0 <- negZetaW0
        let mutable time = 0.
        let mutable restSteps = 0
        let mutable iterations = 0
        let mutable solvDur = 0.
        if zeta < 1. then
            let wd = w0 * System.Math.Sqrt(1. - zeta * zeta)
            let coeff = (zetaW0 - s.Velocity) / wd
            s.DampedFrequency <- wd
            s.Coefficient <- coeff
            while restSteps <= 10 && iterations <= 3000 do
                let env = System.Math.Exp(negZetaW0 * time)
                let wt = wd * time
                let osc = System.Math.Cos wt + (if coeff = 0. then 0. else coeff * System.Math.Sin wt)
                let disp = if osc = 0. then 0. else env * osc
                if abs(1. - clampFinite (1. - disp)) < 0.0005 then restSteps <- restSteps + 1 else restSteps <- 0
                solvDur <- time
                time <- time + 0.02
                iterations <- iterations + 1
        elif zeta = 1. then
            let coeff = -s.Velocity + w0
            s.DampedFrequency <- 0.
            s.Coefficient <- coeff
            while restSteps <= 10 && iterations <= 3000 do
                let disp = scaledExp (1. + coeff * time) (-time * w0)
                if abs(1. - clampFinite (1. - disp)) < 0.0005 then restSteps <- restSteps + 1 else restSteps <- 0
                solvDur <- time
                time <- time + 0.02
                iterations <- iterations + 1
        else
            let wd = w0 * System.Math.Sqrt(zeta * zeta - 1.)
            let coeff = (zetaW0 - s.Velocity) / wd
            let sum = zetaW0 + wd
            let fastRate = -sum
            let slowRate = if sum > 0. then -(w0 * w0) / sum else 0.
            s.DampedFrequency <- wd
            s.Coefficient <- coeff
            s.OverdampedFastRate <- fastRate
            s.OverdampedSlowRate <- slowRate
            let cSlow = 1. + coeff
            let cFast = 1. - coeff
            while restSteps <= 10 && iterations <= 3000 do
                let disp = (scaledExp cSlow (slowRate * time) + scaledExp cFast (fastRate * time)) * 0.5
                if abs(1. - clampFinite (1. - disp)) < 0.0005 then restSteps <- restSteps + 1 else restSteps <- 0
                solvDur <- time
                time <- time + 0.02
                iterations <- iterations + 1
        s.SolverDuration <- solvDur
        let natSettling = round0 (solvDur * 1000.)
        s.NaturalSettlingDuration <- natSettling
        s.SettlingDuration <- natSettling

/// <summary>A stateful Anime.js spring easing with physical or perceived-duration parameters.</summary>
/// <remarks>Duration values are milliseconds; Solve accepts seconds. Supplying bounce or duration resets mass to one and velocity to zero.</remarks>
type Spring(?mass: float, ?stiffness: float, ?damping: float, ?velocity: float, ?bounce: float, ?duration: float,
            ?onComplete: (unit -> unit)) =
    let mutable state =
        let mutable s = Unchecked.defaultof<SpringMath.SolverState>
        s.Mass <- Math.Clamp(SpringMath.parameter "mass" 1. mass, 1., 10000.)
        s.Stiffness <- Math.Clamp(SpringMath.parameter "stiffness" 100. stiffness, 1e-11, 10000.)
        s.Damping <- Math.Clamp(SpringMath.parameter "damping" 10. damping, 1e-11, 10000.)
        s.Velocity <- Math.Clamp(SpringMath.parameter "velocity" 0. velocity, -10000., 10000.)
        s.Bounce <- Math.Clamp(SpringMath.parameter "bounce" 0.5 bounce, -1., 1.)
        s.Duration <- Math.Clamp(SpringMath.parameter "duration" 628. duration, 10., 10000.)
        match onComplete with
        | Some cb when isNull (box cb) -> nullArg "onComplete"
        | _ -> ()
        if bounce.IsSome || duration.IsSome then SpringMath.physicalFromPerceived &s
        SpringMath.compute &s
        s
    let onComplete = defaultArg onComplete ignore
    let mutable completed = false

    /// <summary>Mass clamped to one through 10000; setting it recomputes the solver.</summary>
    member _.Mass
        with get() = state.Mass
        and set value =
            SpringMath.finite "value" value
            state.Mass <- Math.Clamp(value, 1., 10000.)
            SpringMath.compute &state

    /// <summary>Stiffness clamped to 1e-11 through 10000; setting it also updates bounce and perceived duration.</summary>
    member _.Stiffness
        with get() = state.Stiffness
        and set value =
            SpringMath.finite "value" value
            state.Stiffness <- Math.Clamp(value, 1e-11, 10000.)
            SpringMath.perceivedFromPhysical &state
            SpringMath.compute &state

    /// <summary>Setting damping clamps to 1e-11 through 10000 and updates bounce and perceived duration.</summary>
    member _.Damping
        with get() = state.Damping
        and set value =
            SpringMath.finite "value" value
            state.Damping <- Math.Clamp(value, 1e-11, 10000.)
            SpringMath.perceivedFromPhysical &state
            SpringMath.compute &state

    /// <summary>Initial velocity clamped to negative 10000 through 10000; setting it recomputes the solver.</summary>
    member _.Velocity
        with get() = state.Velocity
        and set value =
            SpringMath.finite "value" value
            state.Velocity <- Math.Clamp(value, -10000., 10000.)
            SpringMath.compute &state

    /// <summary>Bounce clamped to negative one through one; setting it derives physical parameters and resets mass and velocity.</summary>
    member _.Bounce
        with get() = state.Bounce
        and set value =
            SpringMath.finite "value" value
            state.Bounce <- Math.Clamp(value, -1., 1.)
            SpringMath.physicalFromPerceived &state
            SpringMath.compute &state

    /// <summary>Perceived duration in milliseconds, clamped to 10 through 10000; setting it derives physical parameters.</summary>
    member _.Duration
        with get() = state.Duration
        and set value =
            SpringMath.finite "value" value
            state.Duration <- Math.Clamp(value, 10., 10000.)
            SpringMath.physicalFromPerceived &state
            SpringMath.compute &state

    /// <summary>Physical time in seconds used to normalize this spring's easing curve.</summary>
    member _.SolverDuration = state.SolverDuration

    /// <summary>Playback duration in milliseconds. Changing a spring parameter restores the calculated settling time.</summary>
    member _.SettlingDuration = state.SettlingDuration

    /// <summary>Shortest stretched playback duration in milliseconds.</summary>
    member _.MinSettlingDuration = 1.

    /// <summary>Sets the playback duration in milliseconds and returns this spring. The normalized easing curve and completion point are preserved.</summary>
    /// <remarks>Durations must be finite and nonnegative. Values below MinSettlingDuration are rounded up.</remarks>
    member this.Stretch(duration: float) =
        SpringMath.finite "duration" duration
        if duration < 0. then invalidArg "duration" "Expected a nonnegative duration."
        state.SettlingDuration <- max this.MinSettlingDuration duration
        this

    /// <summary>Whether the last easing sample reached perceived duration; a backward sample can reset it.</summary>
    member _.Completed = completed

    /// <summary>Evaluates displacement at a finite physical time in seconds.</summary>
    member _.Solve(time: float) =
        SpringMath.finite "time" time
        SpringMath.solve &state time

    /// <summary>Evaluates finite progress, including signed axis extrapolation, and notifies completion when perceived duration is crossed.</summary>
    /// <remarks>Calling below the perceived threshold resets completion, allowing a later forward crossing to notify again.</remarks>
    member _.Ease(time: float) =
        SpringMath.finite "time" time
        let currentTime = time * state.NaturalSettlingDuration
        let perceivedTime = state.Duration
        if currentTime >= perceivedTime && not completed then
            completed <- true
            onComplete()
        if currentTime < perceivedTime && completed then completed <- false
        if time = 0. || time = 1. then time else SpringMath.solve &state (time * state.SolverDuration)

/// <summary>Convenience constructors for spring authoring.</summary>
[<RequireQualifiedAccess>]
module Spring =
    /// <summary>Creates a perceived-mode spring from unsigned whole milliseconds.</summary>
    let perceivedMilliseconds (duration: uint) bounce =
        new Spring(duration = float duration, bounce = bounce)
