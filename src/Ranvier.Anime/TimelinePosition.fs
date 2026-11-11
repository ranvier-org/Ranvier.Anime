namespace Ranvier.Anime

/// <summary>A finite absolute millisecond position or an Anime.js label/relative position expression.</summary>
[<RequireQualifiedAccess; System.Diagnostics.DebuggerDisplay("{ToString(),nq}")>]
type TimelinePosition =
    | At of float
    | Expression of string
    /// <summary>An invariant position diagnostic preserving the case name and authored value.</summary>
    override this.ToString() =
        match this with
        | TimelinePosition.At time -> "At " + time.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
        | TimelinePosition.Expression text -> "Expression " + (if isNull text then "<null>" else text)

module internal TimelinePositions =
    let private whitespace = "\u0009\u000A\u000B\u000C\u000D\u0020\u00A0\u1680\u2000\u2001\u2002\u2003\u2004\u2005\u2006\u2007\u2008\u2009\u200A\u2028\u2029\u202F\u205F\u3000\uFEFF".ToCharArray()

    let private number (text: string) =
        let text = text.Trim(whitespace)
        if text = "" then Some 0.
        else
            try
                if text.Length > 2 && text[0] = '0' && "xXbBoO".Contains(text[1]) then
                    let radix = match System.Char.ToLowerInvariant text[1] with 'x' -> 16 | 'b' -> 2 | _ -> 8
                    let value = text.Substring(2) |> Seq.fold(fun value digit ->
                        let digit = if System.Char.IsDigit digit then int digit - int '0' else int (System.Char.ToLowerInvariant digit) - int 'a' + 10
                        if digit < 0 || digit >= radix then raise(System.FormatException())
                        value * bigint radix + bigint digit) System.Numerics.BigInteger.Zero
                    Some(float value)
                else
                    match System.Double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture) with
                    | true, value when not(System.Double.IsNaN value) &&
                        (not(System.Double.IsInfinity value) || not(text.EndsWith("infinity", System.StringComparison.OrdinalIgnoreCase)) ||
                         text.EndsWith("Infinity", System.StringComparison.Ordinal)) -> Some value
                    | _ -> None
            with :? System.FormatException -> None

    /// <summary>Resolves source position syntax against a duration, previous start/end and label lookup.</summary>
    let resolve duration (previous: (float * float) option) (tryLabel: string -> float option) position =
        let duration = if duration = 1e-11 then 0. else duration
        let result =
            match position with
            | None -> duration
            | Some(TimelinePosition.At time) -> time
            | Some(TimelinePosition.Expression text) ->
                if isNull text then nullArg "position"
                match number text with
                | Some time -> time
                | None ->
                    let sibling =
                        if text.StartsWith("<", System.StringComparison.Ordinal) then
                            if text.StartsWith("<<", System.StringComparison.Ordinal) then
                                Some(previous |> Option.map fst |> Option.defaultValue 0.)
                            else Some(previous |> Option.map snd |> Option.defaultWith(fun () -> invalidArg "position" "No previous child exists."))
                        else None
                    let relative = System.Text.RegularExpressions.Regex.Match(text, @"(\*=|\+=|-=)")
                    if relative.Success then
                        let parts = text.Split([|relative.Value|], System.StringSplitOptions.None)
                        let anchor =
                            sibling |> Option.defaultWith(fun () ->
                                if parts[0] = "" then duration
                                else tryLabel parts[0] |> Option.defaultWith(fun () -> invalidArg "position" "Relative label does not exist."))
                        let amount = number parts[1] |> Option.defaultWith(fun () -> invalidArg "position" "Invalid relative amount.")
                        match relative.Value[0] with '+' -> anchor + amount | '-' -> anchor - amount | _ -> anchor * amount
                    else sibling |> Option.defaultWith(fun () -> tryLabel text |> Option.defaultValue duration)
        if not(System.Double.IsFinite result) then invalidArg "position" "Position must resolve to a finite time."
        result
