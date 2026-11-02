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

/// <summary>Anime.js easing formulas evaluated on normalized progress.</summary>
[<RequireQualifiedAccess>]
module Ease =
    let private finite name value =
        if not (Double.IsFinite value) then invalidArg name "Expected a finite value."

    let private progress time =
        finite "time" time
        if time < 0. || time > 1. then invalidArg "time" "Progress must be between zero and one."

    let private bounce time =
        let mutable b = 3
        let mutable power = Math.Pow(2., float b)
        while b > 0 && time < (power - 1.) / 11. do
            b <- b - 1
            power <- Math.Pow(2., float b)
        1. / Math.Pow(4., float (3 - b)) - 7.5625 * Math.Pow((power * 3. - 2.) / 22. - time, 2.)

    // Easing formulas adapted through Anime.js from Robert Penner, (c) 2001 Robert Penner.
    /// <summary>Creates an easing function accepting progress from zero through one; outputs may overshoot.</summary>
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
    /// <summary>Creates a cubic Bezier easing with finite control points and normalized input progress.</summary>
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
