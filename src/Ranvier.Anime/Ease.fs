namespace Ranvier.Anime

open System

/// <summary>The placement of acceleration and deceleration within an easing interval.</summary>
type EaseDirection = In | Out | InOut | OutIn

/// <summary>Parameterized easing families. Numeric parameters must be finite.</summary>
type EaseFamily =
    | Power of exponent: float
    | Sine
    | Circular
    | Exponential
    | Bounce
    | Back of overshoot: float
    | Elastic of amplitude: float * period: float

/// <summary>A finite piecewise-linear value with implicit spacing or an explicit normalized position.</summary>
[<RequireQualifiedAccess; System.Diagnostics.DebuggerDisplay("{DebugText,nq}")>]
type LinearStop =
    | Value of value: float
    /// <summary>A value at normalized progress; first and last positions are ignored.</summary>
    | At of value: float * progress: float
    override this.ToString() =
        let text (value:float) = value.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
        match this with
        | LinearStop.Value value -> "Value " + text value
        | LinearStop.At(value, position) -> "At (" + text value + ", " + text position + ")"
    member private this.DebugText = this.ToString()

/// <summary>Anime.js easing formulas evaluated on normalized progress.</summary>
[<RequireQualifiedAccess>]
module Ease =
    let private finite name value =
        if not (Double.IsFinite value) then invalidArg name "Expected a finite value."

    let private progress time =
        finite "time" time

    /// <summary>Captures ordered stops; empty input is identity and a single value is constant.</summary>
    /// <remarks>Implicit positions use index spacing. Repeated or descending positions retain source arithmetic,
    /// including nonfinite results.</remarks>
    let linear (points:LinearStop array) =
        if isNull points then nullArg "points"
        let points = points |> Array.map(function LinearStop.Value value -> value, None | LinearStop.At(value, position) -> value, Some position)
        for value, position in points do
            finite "points" value
            position |> Option.iter (finite "points")
        if points.Length = 0 then fun time -> progress time; time
        else
            let last = points.Length - 1
            let length = max 2 points.Length
            let positions = Array.init length (fun index ->
                if index = 0 then 0.
                elif index = length - 1 then 1.
                else snd points[index] |> Option.defaultValue(float index / float last))
            let values = Array.init length (fun index -> fst points[min index last])
            fun time ->
                progress time
                let mutable index = 1
                while index < length && time > positions[index] do index <- index + 1
                if index = length then values[length - 1]
                else
                    values[index - 1] + (values[index] - values[index - 1]) *
                    (time - positions[index - 1]) / (positions[index] - positions[index - 1])

    /// <summary>Constructs an irregular easing from finite randomness and one [0,1) draw per interior stop.</summary>
    /// <remarks>Counts 0–2 yield the two endpoints. Draws occur only at construction; randomness may be signed or exceed 1.</remarks>
    let irregularWith (random:unit -> float) (count:uint) randomness =
        if isNull(box random) then nullArg "random"
        finite "randomness" randomness
        if count > uint Int32.MaxValue then invalidArg "count" "Count exceeds array capacity."
        let length = max 2 (int count)
        let values = Array.create length (LinearStop.Value 0.)
        values[length - 1] <- LinearStop.Value 1.
        let total = float count - 1.
        let mutable previous = 0.
        for index in 1 .. length - 2 do
            let draw = random()
            if not(Double.IsFinite draw) || draw < 0. || draw >= 1. then invalidArg "random" "Expected a draw in [0,1)."
            let spacing = float index / total
            let segmentEnd = float(index + 1) / total
            let variation = spacing + (segmentEnd - spacing) * draw
            let value = spacing * (1. - randomness) + variation * randomness
            previous <- Math.Clamp(value, previous, 1.)
            values[index] <- LinearStop.Value previous
        linear values

    /// <summary>Constructs an irregular easing using Random.Shared; counts and randomness follow irregularWith.</summary>
    let irregular count randomness = irregularWith (fun () -> Random.Shared.NextDouble()) count randomness

    let private bounce time =
        if time < -1. / 11. then invalidArg "time" "Bounce extrapolation must not fall below -1/11."
        let mutable b = 3
        let mutable power = Math.Pow(2., float b)
        while time < (power - 1.) / 11. do
            b <- b - 1
            power <- Math.Pow(2., float b)
        1. / Math.Pow(4., float (3 - b)) - 7.5625 * Math.Pow((power * 3. - 2.) / 22. - time, 2.)

    // Easing formulas adapted through Anime.js from Robert Penner, (c) 2001 Robert Penner.
    /// <summary>Creates an easing function accepting finite progress, including signed axis extrapolation; outputs may overshoot.</summary>
    /// <remarks>Bounce rejects transformed inputs below -1/11, where the upstream search cannot terminate.</remarks>
    let create family direction =
        let easeIn =
            match family with
            | Power exponent ->
                finite "exponent" exponent
                fun time -> Math.Pow(time, exponent)
            | Sine -> fun time -> 1. - Math.Cos(time * Math.PI / 2.)
            | Circular -> fun time -> 1. - Math.Sqrt(1. - time * time)
            | Exponential -> fun time -> if time = 0. then 0. else Math.Pow(2., 10. * time - 10.)
            | Bounce -> bounce
            | Back overshoot ->
                finite "overshoot" overshoot
                fun time -> (overshoot + 1.) * time * time * time - overshoot * time * time
            | Elastic (amplitude, period) ->
                finite "amplitude" amplitude
                finite "period" period
                let amplitude = Math.Clamp(amplitude, 1., 10.)
                let period = Math.Clamp(period, 1e-11, 2.)
                let shift = period / (Math.PI * 2.) * Math.Asin(1. / amplitude)
                let frequency = Math.PI * 2. / period
                fun time ->
                    if time = 0. || time = 1. then time
                    else -amplitude * Math.Pow(2., -10. * (1. - time)) * Math.Sin(((1. - time) - shift) * frequency)
        let evaluate =
            match direction with
            | In -> easeIn
            | Out -> fun time -> 1. - easeIn (1. - time)
            | InOut -> fun time -> if time < 0.5 then easeIn (time * 2.) / 2. else 1. - easeIn (time * -2. + 2.) / 2.
            | OutIn -> fun time -> if time < 0.5 then (1. - easeIn (1. - time * 2.)) / 2. else (easeIn (time * 2. - 1.) + 1.) / 2.
        fun time ->
            progress time
            evaluate time

    /// <summary>Resolves an Anime.js family name with default parameters; unknown names raise an argument error.</summary>
    let named (name: string) =
        if name = "linear" || name = "none" then create (Power 1.) In
        else
            let prefixes = ["inOut", InOut; "outIn", OutIn; "in", In; "out", Out]
            match prefixes |> List.tryFind (fun (prefix, _) -> not (isNull name) && name.StartsWith(prefix, StringComparison.Ordinal)) with
            | None -> invalidArg "name" "Unknown easing name."
            | Some (prefix, direction) ->
                let family =
                    match name.Substring prefix.Length with
                    | "" -> Power 1.68
                    | "Quad" -> Power 2.
                    | "Cubic" -> Power 3.
                    | "Quart" -> Power 4.
                    | "Quint" -> Power 5.
                    | "Sine" -> Sine
                    | "Circ" -> Circular
                    | "Expo" -> Exponential
                    | "Bounce" -> Bounce
                    | "Back" -> Back 1.7
                    | "Elastic" -> Elastic (1., 0.3)
                    | _ -> invalidArg "name" "Unknown easing name."
                create family direction

    /// <summary>Creates equal-width steps; zero steps is rejected and progress is clamped to the unit interval.</summary>
    let steps (count: uint) fromStart =
        if count = 0u then invalidArg "count" "At least one step is required."
        let count = float count
        fun time ->
            finite "time" time
            let scaled = Math.Clamp(time, 0., 1.) * count
            (if fromStart then Math.Ceiling scaled else Math.Floor scaled) * (1. / count)

    // Solver adapted through Anime.js from gre/bezier-easing, (c) 2014 Gaëtan Renaudeau.
    /// <summary>Creates a cubic Bezier easing with finite control points and finite input progress.</summary>
    let cubicBezier x1 y1 x2 y2 =
        finite "x1" x1
        finite "y1" y1
        finite "x2" x2
        finite "y2" y2
        let bezier time a1 a2 = (((1. - 3. * a2 + 3. * a1) * time + (3. * a2 - 6. * a1)) * time + 3. * a1) * time
        fun time ->
            progress time
            if time = 0. || time = 1. || (x1 = y1 && x2 = y2) then time
            else
                let mutable lower = 0.
                let mutable upper = 1.
                let mutable current = 0.
                let mutable iterations = 0
                let mutable searching = true
                while searching do
                    current <- lower + (upper - lower) / 2.
                    let difference = bezier current x1 x2 - time
                    if difference > 0. then upper <- current else lower <- current
                    iterations <- iterations + 1
                    searching <- abs difference > 0.0000001 && iterations < 100
                bezier current y1 y2
