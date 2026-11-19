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
    open System

    let private whitespace = "\u0009\u000A\u000B\u000C\u000D\u0020\u00A0\u1680\u2000\u2001\u2002\u2003\u2004\u2005\u2006\u2007\u2008\u2009\u200A\u2028\u2029\u202F\u205F\u3000\uFEFF".ToCharArray()

    let private tryNumberSpan (raw: ReadOnlySpan<char>) : float voption =
        let span = raw.Trim(ReadOnlySpan<char>(whitespace))
        if span.Length = 0 then ValueSome 0.
        elif span.Length > 2 && span[0] = '0' &&
             (let c = span[1] in c = 'x' || c = 'X' || c = 'b' || c = 'B' || c = 'o' || c = 'O') then
            let radix =
                match Char.ToLowerInvariant span[1] with
                | 'x' -> 16
                | 'b' -> 2
                | _ -> 8
            let mutable valid = true
            let mutable value = Numerics.BigInteger.Zero
            let mutable idx = 2
            while idx < span.Length && valid do
                let ch = span[idx]
                let digit =
                    if ch >= '0' && ch <= '9' then int ch - int '0'
                    elif ch >= 'a' && ch <= 'f' then int ch - int 'a' + 10
                    elif ch >= 'A' && ch <= 'F' then int ch - int 'A' + 10
                    else -1
                if digit < 0 || digit >= radix then valid <- false
                else value <- value * bigint radix + bigint digit
                idx <- idx + 1
            if valid then ValueSome(float value) else ValueNone
        else
            // Quick reject: if first char isn't digit, '+', '-', '.', or 'I', Double.TryParse won't succeed as valid JS number
            let c0 = span[0]
            if not ((c0 >= '0' && c0 <= '9') || c0 = '+' || c0 = '-' || c0 = '.' || c0 = 'I') then ValueNone
            else
                match Double.TryParse(span, Globalization.NumberStyles.Float, Globalization.CultureInfo.InvariantCulture) with
                | true, value when not(Double.IsNaN value) &&
                    (not(Double.IsInfinity value) ||
                     not(span.EndsWith("infinity".AsSpan(), StringComparison.OrdinalIgnoreCase)) ||
                     span.EndsWith("Infinity".AsSpan(), StringComparison.Ordinal)) -> ValueSome value
                | _ -> ValueNone

    let private findOperatorSpan (span: ReadOnlySpan<char>) : struct(int * char) voption =
        let mutable idx = 0
        let limit = span.Length - 1
        let mutable found = ValueNone
        while idx < limit && found.IsNone do
            if span[idx + 1] = '=' then
                let c = span[idx]
                if c = '*' || c = '+' || c = '-' then
                    found <- ValueSome(struct(idx, c))
            idx <- idx + 1
        found

    /// <summary>Resolves source position syntax against a duration, previous start/end and label dictionary without allocations.</summary>
    let resolve duration (previous: struct(float * float) voption) (labels: Collections.Generic.Dictionary<string, float>) position =
        let duration = if duration = 1e-11 then 0. else duration
        let result =
            match position with
            | None -> duration
            | Some(TimelinePosition.At time) -> time
            | Some(TimelinePosition.Expression text) ->
                if isNull text then nullArg "position"
                let span = text.AsSpan()
                match tryNumberSpan span with
                | ValueSome time -> time
                | ValueNone ->
                    match findOperatorSpan span with
                    | ValueSome(struct(opIdx, opChar)) ->
                        let prefix = span.Slice(0, opIdx)
                        let amountSpan = span.Slice(opIdx + 2)
                        let anchor =
                            if prefix.Length = 2 && prefix[0] = '<' && prefix[1] = '<' then
                                match previous with ValueSome struct(s, _) -> s | ValueNone -> 0.
                            elif prefix.Length = 1 && prefix[0] = '<' then
                                match previous with ValueSome struct(_, e) -> e | ValueNone -> invalidArg "position" "No previous child exists."
                            elif prefix.Length = 0 then
                                duration
                            else
                                let lookup = labels.GetAlternateLookup<ReadOnlySpan<char>>()
                                let mutable labelVal = 0.
                                if lookup.TryGetValue(prefix, &labelVal) then labelVal
                                else invalidArg "position" "Relative label does not exist."
                        let amount =
                            match tryNumberSpan amountSpan with
                            | ValueSome v -> v
                            | ValueNone -> invalidArg "position" "Invalid relative amount."
                        match opChar with '+' -> anchor + amount | '-' -> anchor - amount | _ -> anchor * amount
                    | ValueNone ->
                        if span.Length = 2 && span[0] = '<' && span[1] = '<' then
                            match previous with ValueSome struct(s, _) -> s | ValueNone -> 0.
                        elif span.Length = 1 && span[0] = '<' then
                            match previous with ValueSome struct(_, e) -> e | ValueNone -> invalidArg "position" "No previous child exists."
                        else
                            let lookup = labels.GetAlternateLookup<ReadOnlySpan<char>>()
                            let mutable labelVal = 0.
                            if lookup.TryGetValue(span, &labelVal) then labelVal
                            else duration
        if not(Double.IsFinite result) then invalidArg "position" "Position must resolve to a finite time."
        result
