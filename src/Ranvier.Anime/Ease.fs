namespace Ranvier.Anime

open System

/// <summary>The placement of acceleration and deceleration within an easing interval.</summary>
[<Diagnostics.DebuggerDisplay("{ToString(),nq}")>]
type EaseDirection =
    | In | Out | InOut | OutIn
    /// <summary>The easing direction name.</summary>
    override this.ToString() = match this with In -> "In" | Out -> "Out" | InOut -> "InOut" | OutIn -> "OutIn"

/// <summary>Parameterized easing families. Numeric parameters must be finite.</summary>
[<Diagnostics.DebuggerDisplay("{ToString(),nq}")>]
type EaseFamily =
    | Power of exponent: float
    | Sine
    | Circular
    | Exponential
    | Bounce
    | Back of overshoot: float
    | Elastic of amplitude: float * period: float
    /// <summary>The family name and invariant numeric parameters.</summary>
    override this.ToString() =
        let text (value:float)=value.ToString("R",Globalization.CultureInfo.InvariantCulture)
        match this with
        | Power value -> "Power "+text value
        | Sine -> "Sine"
        | Circular -> "Circular"
        | Exponential -> "Exponential"
        | Bounce -> "Bounce"
        | Back value -> "Back "+text value
        | Elastic(amplitude,period) -> "Elastic ("+text amplitude+", "+text period+")"

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
            let mutable monotonic = true
            for i = 1 to length - 1 do
                if not (positions[i] >= positions[i - 1]) then monotonic <- false
            if monotonic && length > 8 then
                fun time ->
                    progress time
                    let mutable lo = 1
                    let mutable hi = length
                    while lo < hi do
                        let mid = int (uint (lo + hi) >>> 1)
                        if time > positions[mid] then lo <- mid + 1
                        else hi <- mid
                    if lo = length then values[length - 1]
                    else
                        values[lo - 1] + (values[lo] - values[lo - 1]) *
                        (time - positions[lo - 1]) / (positions[lo] - positions[lo - 1])
            else
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
        elif time >= 7. / 11. then
            let d = 1. - time
            1. - 7.5625 * d * d
        elif time >= 3. / 11. then
            let d = 10. / 22. - time
            0.25 - 7.5625 * d * d
        elif time >= 1. / 11. then
            let d = 4. / 22. - time
            0.0625 - 7.5625 * d * d
        elif time >= 0. then
            let d = 1. / 22. - time
            0.015625 - 7.5625 * d * d
        else
            let mutable b = 0
            let mutable power = 1.
            while time < (power - 1.) / 11. do
                b <- b - 1
                power <- power * 0.5
            let d = (power * 3. - 2.) / 22. - time
            1. / Math.Pow(4., float (3 - b)) - 7.5625 * d * d

    // Easing formulas adapted through Anime.js from Robert Penner, (c) 2001 Robert Penner.
    /// <summary>Creates an easing function accepting finite progress, including signed axis extrapolation; outputs may overshoot.</summary>
    /// <remarks>Bounce rejects transformed inputs below -1/11, where the upstream search cannot terminate.</remarks>
    let create family direction =
        let easeIn =
            match family with
            | Power exponent ->
                finite "exponent" exponent
                if exponent = 1. then id
                elif exponent = 2. then fun time -> time * time
                elif exponent = 3. then fun time -> time * time * time
                elif exponent = 4. then fun time -> let t2 = time * time in t2 * t2
                elif exponent = 5. then fun time -> let t2 = time * time in t2 * t2 * time
                else fun time -> Math.Pow(time, exponent)
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

    let private tryDirectionPrefix (s: string) : struct(int * EaseDirection) voption =
        if isNull s then ValueNone
        elif s.StartsWith("inOut", StringComparison.Ordinal) then ValueSome(struct(5, InOut))
        elif s.StartsWith("outIn", StringComparison.Ordinal) then ValueSome(struct(5, OutIn))
        elif s.StartsWith("in", StringComparison.Ordinal) then ValueSome(struct(2, In))
        elif s.StartsWith("out", StringComparison.Ordinal) then ValueSome(struct(3, Out))
        else ValueNone

    /// <summary>Resolves an Anime.js family name with default parameters; unknown names raise an argument error.</summary>
    let named (name: string) =
        if name = "linear" || name = "none" then create (Power 1.) In
        else
            match tryDirectionPrefix name with
            | ValueNone -> invalidArg "name" "Unknown easing name."
            | ValueSome struct(prefixLen, direction) ->
                let family =
                    match name.Substring prefixLen with
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

    /// <summary>Parses a named or parameterized Anime.js easing string, returning None for unknown or non-family syntax.</summary>
    let tryParseString (text: string) : (float -> float) option =
        if isNull text then None
        elif text = "linear" || text = "none" then Some(create (Power 1.) In)
        else
            let tryNumber (raw: string) fallback =
                let trimmed = raw.Trim()
                if String.IsNullOrEmpty trimmed then Some fallback
                else
                    match Double.TryParse(trimmed, Globalization.NumberStyles.Float, Globalization.CultureInfo.InvariantCulture) with
                    | true, value when Double.IsFinite value -> Some value
                    | _ -> None
            let paren = text.IndexOf '('
            if paren < 0 then
                match tryDirectionPrefix text with
                | ValueNone -> None
                | ValueSome struct(prefixLen, direction) ->
                    match text.Substring prefixLen with
                    | "" -> Some(create (Power 1.68) direction)
                    | "Quad" -> Some(create (Power 2.) direction)
                    | "Cubic" -> Some(create (Power 3.) direction)
                    | "Quart" -> Some(create (Power 4.) direction)
                    | "Quint" -> Some(create (Power 5.) direction)
                    | "Sine" -> Some(create Sine direction)
                    | "Circ" -> Some(create Circular direction)
                    | "Expo" -> Some(create Exponential direction)
                    | "Bounce" -> Some(create Bounce direction)
                    | "Back" -> Some(create (Back 1.7) direction)
                    | "Elastic" -> Some(create (Elastic (1., 0.3)) direction)
                    | _ -> None
            elif not (text.EndsWith(")", StringComparison.Ordinal)) || text.IndexOf('(', paren + 1) >= 0 then None
            else
                let head = text.Substring(0, paren)
                let body = text.Substring(paren + 1, text.Length - paren - 2)
                let parts = body.Split ','
                match tryDirectionPrefix head with
                | ValueNone -> None
                | ValueSome struct(prefixLen, direction) ->
                    match head.Substring prefixLen with
                    | "" ->
                        tryNumber parts[0] 1.68 |> Option.map (fun exp -> create (Power exp) direction)
                    | "Back" ->
                        tryNumber parts[0] 1.7 |> Option.map (fun overshoot -> create (Back overshoot) direction)
                    | "Elastic" ->
                        match tryNumber parts[0] 1. with
                        | None -> None
                        | Some amp ->
                            let periodOpt = if parts.Length > 1 then tryNumber parts[1] 0.3 else Some 0.3
                            periodOpt |> Option.map (fun per -> create (Elastic (amp, per)) direction)
                    | _ -> None

    /// <summary>Parses a named or parameterized Anime.js easing string, falling back to linear identity like source parseEase.</summary>
    let parse (text: string) : float -> float =
        if isNull text then nullArg "text"
        let deprecated = [| "steps("; "irregular("; "linear("; "cubicBezier(" |]
        if deprecated |> Array.exists (fun prefix -> text.StartsWith(prefix, StringComparison.Ordinal)) then
            create (Power 1.) In
        else
            tryParseString text |> Option.defaultWith (fun () -> create (Power 1.) In)

    /// <summary>Parses a named or parameterized Anime.js easing string, raising ArgumentException when unrecognized.</summary>
    let parseStrict (text: string) : float -> float =
        if isNull text then nullArg "text"
        match tryParseString text with
        | Some ease -> ease
        | None -> invalidArg "text" ("Unknown or unsupported easing string: " + text)

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
        let ax = 1. - 3. * x2 + 3. * x1
        let bx = 3. * x2 - 6. * x1
        let cx = 3. * x1
        let ay = 1. - 3. * y2 + 3. * y1
        let by = 3. * y2 - 6. * y1
        let cy = 3. * y1
        let inline evalX t = ((ax * t + bx) * t + cx) * t
        let inline evalY t = ((ay * t + by) * t + cy) * t
        let samples = Array.init 257 (fun k -> evalX (float k * (1. / 256.)))
        fun time ->
            progress time
            if time = 0. || time = 1. || (x1 = y1 && x2 = y2) then time
            else
                let mutable loIdx = 0
                let mutable hiIdx = 256
                let mutable midIdx = 128
                let mutable iterations = 0
                let mutable searching = true
                while searching && iterations < 8 do
                    midIdx <- (loIdx + hiIdx) >>> 1
                    let difference = samples[midIdx] - time
                    if difference > 0. then hiIdx <- midIdx else loIdx <- midIdx
                    iterations <- iterations + 1
                    searching <- abs difference > 0.0000001
                let mutable current = float midIdx * (1. / 256.)
                if searching then
                    let mutable lower = float loIdx * (1. / 256.)
                    let mutable upper = float hiIdx * (1. / 256.)
                    while searching do
                        current <- lower + (upper - lower) * 0.5
                        let difference = evalX current - time
                        if difference > 0. then upper <- current else lower <- current
                        iterations <- iterations + 1
                        searching <- abs difference > 0.0000001 && iterations < 100
                evalY current
