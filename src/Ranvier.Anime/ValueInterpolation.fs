namespace Ranvier.Anime

/// <summary>Anime.js endpoint decomposition, homogenization and scalar/color/compound composition.</summary>
module internal ValueInterpolation =
    open System
    open System.Globalization
    open System.Runtime.CompilerServices
    open System.Runtime.InteropServices
    open System.Runtime.Intrinsics
    open Ranvier.Anime

    [<AbstractClass; Sealed>]
    type private ComposeScratchTls =
        [<ThreadStatic; DefaultValue>]
        static val mutable private chars: char array
        static member Acquire(minCapacity: int) =
            let mutable buf = ComposeScratchTls.chars
            if isNull buf || buf.Length < minCapacity then
                let cap = max 16384 (int (System.Numerics.BitOperations.RoundUpToPowerOf2(uint32 (max 16 minCapacity))))
                buf <- Array.zeroCreate<char> cap
                ComposeScratchTls.chars <- buf
            buf
        static member Grow(buf: byref<char array>, needed: int) =
            let mutable newCap = buf.Length * 2
            while newCap < needed do newCap <- newCap * 2
            let next = Array.zeroCreate<char> newCap
            Array.Copy(buf, 0, next, 0, buf.Length)
            buf <- next
            ComposeScratchTls.chars <- next
        static member Release(buf: char array) =
            if buf.Length > 131072 then
                ComposeScratchTls.chars <- Array.zeroCreate<char> 16384
    /// <summary>The source decomposition category.</summary>
    type ValueKind = Number = 0 | Unit = 1 | Color = 2 | Complex = 3
    /// <summary>A numeric or serialized endpoint before source decomposition.</summary>
    [<System.Diagnostics.DebuggerDisplay("{ToString(),nq}")>]
    type Literal =
        | Number of float
        | Text of string
        /// <summary>An invariant case/value diagnostic without structural formatting.</summary>
        override this.ToString() =
            match this with
            | Number value -> "Number "+value.ToString("R",CultureInfo.InvariantCulture)
            | Text value -> "Text "+(if isNull value then "<null>" else value)
    /// <summary>Source value kind, scalar, unit/operator and compound components/template.</summary>
    [<System.Diagnostics.DebuggerDisplay("{ToString(),nq}")>]
    type Data =
        { Kind:ValueKind; Number:float; Unit:string; Operator:char option; Components:float array; Strings:string array }
        /// <summary>An invariant value-kind/scalar diagnostic.</summary>
        override this.ToString() =
            "ValueData("+this.Kind.ToString()+", "+this.Number.ToString("R",CultureInfo.InvariantCulture)+")"
    let private finite name value = if not(Double.IsFinite value) then invalidArg name "Expected finite input."
    /// <summary>Formats source-style shortest numbers and exponent thresholds, including IEEE values.</summary>
    let number (value:float) =
        if value=0. then "0"
        elif Double.IsNaN value then "NaN"
        elif Double.IsPositiveInfinity value then "Infinity"
        elif Double.IsNegativeInfinity value then "-Infinity"
        else
            let raw=value.ToString("R",CultureInfo.InvariantCulture)
            if raw.IndexOf('E')<0 && raw.IndexOf('e')<0 then raw
            else
                let split=raw.Split('E','e')
                let exponent=Int32.Parse(split[1],CultureInfo.InvariantCulture)
                if abs value>=1e-6 && abs value<1e21 then
                    let negative=split[0].StartsWith "-"
                    let mantissa=split[0].TrimStart('-')
                    let dot=mantissa.IndexOf('.')
                    let digits=mantissa.Replace(".","")
                    let position=(if dot<0 then digits.Length else dot)+exponent
                    let result=
                        if position<=0 then "0."+String('0',-position)+digits
                        elif position>=digits.Length then digits+String('0',position-digits.Length)
                        else digits.Insert(position,".")
                    (if negative then "-" else "")+result
                else split[0]+"e"+(if exponent>=0 then "+" else "-")+string(abs exponent)
    let private numberText value=number value
    let inline private writeString (buf: byref<char array>) (pos: byref<int>) (s: string) =
        if not (String.IsNullOrEmpty s) then
            let needed = pos + s.Length
            if needed > buf.Length then ComposeScratchTls.Grow(&buf, needed)
            s.AsSpan().CopyTo(Span<char>(buf, pos, s.Length))
            pos <- needed

    let private writeNumber (buf: byref<char array>) (pos: byref<int>) (value: float) =
        if pos + 40 > buf.Length then ComposeScratchTls.Grow(&buf, pos + 40)
        if value = 0. then
            buf[pos] <- '0'
            pos <- pos + 1
        elif Double.IsFinite value then
            let mutable fastDone = false
            if abs value <= 1e9 then
                let n = int64 (Math.Round(value * 10000.))
                if float n / 10000. = value then
                    fastDone <- true
                    let mutable p = pos
                    let u =
                        if n < 0L then
                            buf[p] <- '-'
                            p <- p + 1
                            uint64 (-n)
                        else
                            uint64 n
                    let q = u / 10000UL
                    let r = int (u % 10000UL)
                    if q < 10UL then
                        buf[p] <- char (48 + int q)
                        p <- p + 1
                    else
                        let mutable written = 0
                        q.TryFormat(Span<char>(buf, p, 24), &written, ReadOnlySpan<char>.Empty, CultureInfo.InvariantCulture) |> ignore
                        p <- p + written
                    if r <> 0 then
                        buf[p] <- '.'
                        let d0 = r / 1000
                        let d1 = (r / 100) % 10
                        let d2 = (r / 10) % 10
                        let d3 = r % 10
                        buf[p + 1] <- char (48 + d0)
                        if d3 <> 0 then
                            buf[p + 2] <- char (48 + d1)
                            buf[p + 3] <- char (48 + d2)
                            buf[p + 4] <- char (48 + d3)
                            p <- p + 5
                        elif d2 <> 0 then
                            buf[p + 2] <- char (48 + d1)
                            buf[p + 3] <- char (48 + d2)
                            p <- p + 4
                        elif d1 <> 0 then
                            buf[p + 2] <- char (48 + d1)
                            p <- p + 3
                        else
                            p <- p + 2
                    pos <- p
            if not fastDone then
                let span = Span<char>(buf, pos, 36)
                let mutable written = 0
                if value.TryFormat(span, &written, "R", CultureInfo.InvariantCulture) &&
                   span.Slice(0, written).IndexOfAny('E', 'e') < 0 then
                    pos <- pos + written
                else
                    writeString &buf &pos (number value)
        else
            writeString &buf &pos (number value)
    /// <summary>Formats a scalar with an optional unit suffix in a single allocation.</summary>
    let formatNumberWithUnit (value: float) (unitSuffix: string) : string =
        if String.IsNullOrEmpty unitSuffix then
            let mutable buf = ComposeScratchTls.Acquire(48)
            let mutable pos = 0
            writeNumber &buf &pos value
            let s = String(ReadOnlySpan<char>(buf, 0, pos))
            ComposeScratchTls.Release buf
            s
        else
            let mutable buf = ComposeScratchTls.Acquire(48 + unitSuffix.Length)
            let mutable pos = 0
            writeNumber &buf &pos value
            writeString &buf &pos unitSuffix
            let s = String(ReadOnlySpan<char>(buf, 0, pos))
            ComposeScratchTls.Release buf
            s

    /// <summary>Formats two space-separated numbers in a single allocation (used by SVG Drawable).</summary>
    let formatPairSpace (a: float) (b: float) : string =
        let mutable buf = ComposeScratchTls.Acquire(96)
        let mutable pos = 0
        writeNumber &buf &pos a
        buf[pos] <- ' '
        pos <- pos + 1
        writeNumber &buf &pos b
        let s = String(ReadOnlySpan<char>(buf, 0, pos))
        ComposeScratchTls.Release buf
        s

    /// <summary>Formats a WAAPI CSS linear(...) easing string in a single scratch-buffer pass.</summary>
    let formatLinearStops (fn: float -> float) (count: int) : string =
        let mutable buf = ComposeScratchTls.Acquire((count + 1) * 12 + 16)
        let mutable pos = 7
        buf[0] <- 'l'; buf[1] <- 'i'; buf[2] <- 'n'; buf[3] <- 'e'; buf[4] <- 'a'; buf[5] <- 'r'; buf[6] <- '('
        let fCount = float count
        for i = 0 to count do
            if i > 0 then
                if pos + 2 > buf.Length then ComposeScratchTls.Grow(&buf, pos + 2)
                buf[pos] <- ','
                buf[pos + 1] <- ' '
                pos <- pos + 2
            let v = PropertyNumbers.round 1e4 (fn (float i / fCount))
            writeNumber &buf &pos v
        if pos + 1 > buf.Length then ComposeScratchTls.Grow(&buf, pos + 1)
        buf[pos] <- ')'
        pos <- pos + 1
        let s = String(ReadOnlySpan<char>(buf, 0, pos))
        ComposeScratchTls.Release buf
        s

    /// <summary>Formats Color or Complex component arrays directly into a CSS/object string without allocating an Output.</summary>
    let formatComponentsText (template: Data) (values: float array) (cssOpaqueRgb: bool) : string =
        if template.Kind = ValueKind.Color then
            if cssOpaqueRgb && values.Length = 4 && values[3] = 1. then
                let mutable buf = ComposeScratchTls.Acquire(64)
                let mutable pos = 4
                buf[0] <- 'r'; buf[1] <- 'g'; buf[2] <- 'b'; buf[3] <- '('
                writeNumber &buf &pos (float (int (PropertyNumbers.round 1. values[0])))
                buf[pos] <- ','; buf[pos + 1] <- ' '; pos <- pos + 2
                writeNumber &buf &pos (float (int (PropertyNumbers.round 1. values[1])))
                buf[pos] <- ','; buf[pos + 1] <- ' '; pos <- pos + 2
                writeNumber &buf &pos (float (int (PropertyNumbers.round 1. values[2])))
                buf[pos] <- ')'; pos <- pos + 1
                let s = String(ReadOnlySpan<char>(buf, 0, pos))
                ComposeScratchTls.Release buf
                s
            else
                let mutable buf = ComposeScratchTls.Acquire(64)
                let mutable pos = 5
                buf[0] <- 'r'; buf[1] <- 'g'; buf[2] <- 'b'; buf[3] <- 'a'; buf[4] <- '('
                writeNumber &buf &pos (PropertyNumbers.round 1. values[0])
                buf[pos] <- ','; pos <- pos + 1
                writeNumber &buf &pos (PropertyNumbers.round 1. values[1])
                buf[pos] <- ','; pos <- pos + 1
                writeNumber &buf &pos (PropertyNumbers.round 1. values[2])
                buf[pos] <- ','; pos <- pos + 1
                writeNumber &buf &pos values[3]
                buf[pos] <- ')'; pos <- pos + 1
                let s = String(ReadOnlySpan<char>(buf, 0, pos))
                ComposeScratchTls.Release buf
                s
        else
            let mutable buf = ComposeScratchTls.Acquire(values.Length * 12 + 64)
            let mutable pos = 0
            let strings = template.Strings
            writeString &buf &pos strings[0]
            for index = 0 to values.Length - 1 do
                writeNumber &buf &pos values[index]
                writeString &buf &pos strings[index + 1]
            let s = String(ReadOnlySpan<char>(buf, 0, pos))
            ComposeScratchTls.Release buf
            s

    /// <summary>A composed scalar or string with the sampled component values.</summary>
    /// <remarks>Text is composed on first read: a numeric writer reads <c>Number</c> and never formats.</remarks>
    [<Sealed; AllowNullLiteral; System.Diagnostics.DebuggerDisplay("{ToString(),nq}")>]
    type Output internal (kind:ValueKind, number:float, unitSuffix:string, components:float array, text:string) =
        let mutable text=text
        /// <summary>The source decomposition category of this output.</summary>
        member _.Kind=kind
        /// <summary>Whether the value is a unitless number, so <c>Number</c> is its scalar.</summary>
        member _.HasScalar=kind=ValueKind.Number
        /// <summary>The composed scalar; zero for color and compound values.</summary>
        member _.Number=number
        /// <summary>Whether the value is a number or a number with a unit, so its text is <c>Number</c> then <c>UnitSuffix</c>.</summary>
        member _.IsScalar=kind=ValueKind.Number || kind=ValueKind.Unit
        /// <summary>The unit a scalar's text ends with.</summary>
        member _.UnitSuffix=unitSuffix
        /// <summary>The scalar for unitless numbers, None otherwise.</summary>
        member _.Scalar=if kind=ValueKind.Number then Some number else None
        /// <summary>The composed CSS text, formatted on first read.</summary>
        member _.Text=
            if isNull text then text<-formatNumberWithUnit number unitSuffix
            text
        /// <summary>The sampled component values.</summary>
        member _.Components=components
        /// <summary>A copy with independent components.</summary>
        member _.CopyComponents()=Output(kind,number,unitSuffix,Array.copy components,text)
        /// <summary>The invariant scalar or composed text.</summary>
        override this.ToString() =
            if kind=ValueKind.Number then number.ToString("R",CultureInfo.InvariantCulture) else this.Text

    let private internUnit (u: ReadOnlySpan<char>) : string =
        match u.Length with
        | 1 ->
            if u[0] = '%' then "%"
            elif u[0] = 's' then "s"
            else u.ToString()
        | 2 ->
            if u[0] = 'p' && u[1] = 'x' then "px"
            elif u[0] = 'e' && u[1] = 'm' then "em"
            elif u[0] = 'v' && u[1] = 'w' then "vw"
            elif u[0] = 'v' && u[1] = 'h' then "vh"
            elif u[0] = 'm' && u[1] = 's' then "ms"
            elif u[0] = 'p' && u[1] = 't' then "pt"
            elif u[0] = 'f' && u[1] = 'r' then "fr"
            elif u[0] = 'c' && u[1] = 'h' then "ch"
            elif u[0] = 'e' && u[1] = 'x' then "ex"
            else u.ToString()
        | 3 ->
            if u.SequenceEqual("deg".AsSpan()) then "deg"
            elif u.SequenceEqual("rem".AsSpan()) then "rem"
            elif u.SequenceEqual("rad".AsSpan()) then "rad"
            elif u.SequenceEqual("dvh".AsSpan()) then "dvh"
            elif u.SequenceEqual("svh".AsSpan()) then "svh"
            elif u.SequenceEqual("lvh".AsSpan()) then "lvh"
            else u.ToString()
        | 4 ->
            if u.SequenceEqual("turn".AsSpan()) then "turn"
            elif u.SequenceEqual("grad".AsSpan()) then "grad"
            elif u.SequenceEqual("vmin".AsSpan()) then "vmin"
            elif u.SequenceEqual("vmax".AsSpan()) then "vmax"
            else u.ToString()
        | _ -> u.ToString()

    let private internTemplatePart (s: ReadOnlySpan<char>) : string =
        match s.Length with
        | 0 -> ""
        | 1 ->
            match s[0] with
            | ')' -> ")"
            | ' ' -> " "
            | ',' -> ","
            | '%' -> "%"
            | _ -> s.ToString()
        | 2 ->
            if s[0] = ',' && s[1] = ' ' then ", "
            elif s[0] = 'p' && s[1] = 'x' then "px"
            elif s[0] = ')' && s[1] = ' ' then ") "
            else s.ToString()
        | 3 ->
            if s.SequenceEqual("px ".AsSpan()) then "px "
            elif s.SequenceEqual("px,".AsSpan()) then "px,"
            elif s.SequenceEqual("px)".AsSpan()) then "px)"
            else s.ToString()
        | 4 ->
            if s.SequenceEqual("px, ".AsSpan()) then "px, "
            elif s.SequenceEqual("deg)".AsSpan()) then "deg)"
            else s.ToString()
        | _ -> s.ToString()

    let private tryNumberSpan (raw: ReadOnlySpan<char>) : float voption =
        let span = raw.Trim()
        if span.Length = 0 then ValueSome 0.
        else
            let c0 = span[0]
            if not ((c0 >= '0' && c0 <= '9') || c0 = '+' || c0 = '-' || c0 = '.' || c0 = 'I') then ValueNone
            elif span.Length > 2 && c0 = '0' &&
                 (let c1 = span[1] in c1 = 'x' || c1 = 'X' || c1 = 'b' || c1 = 'B' || c1 = 'o' || c1 = 'O') then
                let radix =
                    match Char.ToLowerInvariant span[1] with
                    | 'x' -> 16
                    | 'b' -> 2
                    | _ -> 8
                let mutable valid = true
                let mutable idx = 2
                if span.Length <= 18 then
                    let mutable acc = 0UL
                    let uRadix = uint64 radix
                    while idx < span.Length && valid do
                        let ch = span[idx]
                        let d =
                            if ch >= '0' && ch <= '9' then int ch - int '0'
                            elif ch >= 'a' && ch <= 'f' then int ch - int 'a' + 10
                            elif ch >= 'A' && ch <= 'F' then int ch - int 'A' + 10
                            else -1
                        if d < 0 || d >= radix then valid <- false
                        else acc <- acc * uRadix + uint64 d
                        idx <- idx + 1
                    if valid then ValueSome(float acc) else ValueNone
                else
                    let mutable value = System.Numerics.BigInteger.Zero
                    let bRadix = System.Numerics.BigInteger(radix)
                    while idx < span.Length && valid do
                        let ch = span[idx]
                        let d =
                            if ch >= '0' && ch <= '9' then int ch - int '0'
                            elif ch >= 'a' && ch <= 'f' then int ch - int 'a' + 10
                            elif ch >= 'A' && ch <= 'F' then int ch - int 'A' + 10
                            else -1
                        if d < 0 || d >= radix then valid <- false
                        else value <- value * bRadix + System.Numerics.BigInteger(d)
                        idx <- idx + 1
                    if valid then ValueSome(float value) else ValueNone
            else
                let mutable parsed = 0.
                if Double.TryParse(span, NumberStyles.Float, CultureInfo.InvariantCulture, &parsed) then
                    if Double.IsNaN parsed then ValueNone
                    elif Double.IsInfinity parsed &&
                         span.EndsWith("infinity".AsSpan(), StringComparison.OrdinalIgnoreCase) &&
                         not (span.SequenceEqual("Infinity".AsSpan())) &&
                         not (span.SequenceEqual("+Infinity".AsSpan())) &&
                         not (span.SequenceEqual("-Infinity".AsSpan())) then ValueNone
                    else ValueSome parsed
                else ValueNone

    let private parseSpanFinite (span: ReadOnlySpan<char>) : float =
        let value = Double.Parse(span, NumberStyles.Float, CultureInfo.InvariantCulture)
        finite "value" value
        value

    // Exact match of `^([-+]?[0-9]*\.?[0-9]+(?:[eE][-+]?[0-9]+)?)([A-Za-z]+|%)$` (when no ' ' in span).
    let private tryMatchUnitSpan (span: ReadOnlySpan<char>) : struct(float * string) voption =
        let len = span.Length
        if len < 2 || span.IndexOf ' ' >= 0 then ValueNone
        else
            let lastCh = span[len - 1]
            let uStart =
                if lastCh = '%' then len - 1
                elif Char.IsAsciiLetter lastCh then
                    let mutable k = len - 1
                    while k > 0 && Char.IsAsciiLetter span[k - 1] do k <- k - 1
                    k
                else len
            if uStart <= 0 || uStart >= len then ValueNone
            else
                let numSpan = span.Slice(0, uStart)
                let nLen = numSpan.Length
                let mutable p = 0
                if numSpan[0] = '+' || numSpan[0] = '-' then p <- 1
                let d1Start = p
                while p < nLen && numSpan[p] >= '0' && numSpan[p] <= '9' do p <- p + 1
                let d1 = p - d1Start
                let mutable ok = false
                if p < nLen && numSpan[p] = '.' then
                    p <- p + 1
                    let d2Start = p
                    while p < nLen && numSpan[p] >= '0' && numSpan[p] <= '9' do p <- p + 1
                    ok <- (p - d2Start) >= 1
                else
                    ok <- d1 >= 1
                if ok && p < nLen && (numSpan[p] = 'e' || numSpan[p] = 'E') then
                    p <- p + 1
                    if p < nLen && (numSpan[p] = '+' || numSpan[p] = '-') then p <- p + 1
                    let d3Start = p
                    while p < nLen && numSpan[p] >= '0' && numSpan[p] <= '9' do p <- p + 1
                    ok <- (p - d3Start) >= 1
                if ok && p = nLen then
                    let numVal = parseSpanFinite numSpan
                    ValueSome(struct(numVal, internUnit (span.Slice(uStart))))
                else ValueNone

    // Tries to match `[-+]?[0-9]*\.?[0-9]+(?:[eE][-+]?[0-9])?` (single-digit exponent!) starting at `startIdx`.
    // Returns the exclusive end index if matched, or -1 if no match starts at `startIdx`.
    let private tryMatchComplexNumberAt (span: ReadOnlySpan<char>, startIdx: int) : int =
        let len = span.Length
        let mutable p = startIdx
        if span[p] = '+' || span[p] = '-' then p <- p + 1
        let d1Start = p
        while p < len && span[p] >= '0' && span[p] <= '9' do p <- p + 1
        let d1 = p - d1Start
        let mutable matchedMantissa = false
        if p < len && span[p] = '.' && p + 1 < len && span[p + 1] >= '0' && span[p + 1] <= '9' then
            p <- p + 2
            while p < len && span[p] >= '0' && span[p] <= '9' do p <- p + 1
            matchedMantissa <- true
        elif d1 >= 1 then
            matchedMantissa <- true
        if not matchedMantissa then -1
        else
            if p < len && (span[p] = 'e' || span[p] = 'E') then
                let mutable ep = p + 1
                if ep < len && (span[ep] = '+' || span[ep] = '-') then ep <- ep + 1
                if ep < len && span[ep] >= '0' && span[ep] <= '9' then
                    p <- ep + 1
            p

    let private decomposeComplexSpan (span: ReadOnlySpan<char>) : struct(float array * string array) =
        let len = span.Length
        let mutable count = 0
        let mutable i = 0
        while i < len do
            let c = span[i]
            if (c >= '0' && c <= '9') || c = '+' || c = '-' || c = '.' then
                let mEnd = tryMatchComplexNumberAt(span, i)
                if mEnd > i then
                    count <- count + 1
                    i <- mEnd
                else i <- i + 1
            else i <- i + 1
        if count = 0 then
            struct(Array.Empty<float>(), [| span.ToString() |])
        else
            let comps = Array.zeroCreate<float> count
            let strs = Array.zeroCreate<string> (count + 1)
            let mutable cIdx = 0
            let mutable segStart = 0
            let mutable pos = 0
            while pos < len do
                let c = span[pos]
                if (c >= '0' && c <= '9') || c = '+' || c = '-' || c = '.' then
                    let mEnd = tryMatchComplexNumberAt(span, pos)
                    if mEnd > pos then
                        strs[cIdx] <- internTemplatePart (span.Slice(segStart, pos - segStart))
                        comps[cIdx] <- parseSpanFinite (span.Slice(pos, mEnd - pos))
                        cIdx <- cIdx + 1
                        segStart <- mEnd
                        pos <- mEnd
                    else pos <- pos + 1
                else pos <- pos + 1
            strs[cIdx] <- internTemplatePart (span.Slice(segStart, len - segStart))
            struct(comps, strs)

    /// <summary>Decomposes finite scalars and source unit/color/compound syntax without native CSS resolution.</summary>
    let decompose literal =
        let scalar number : Data = {Kind=ValueKind.Number;Number=number;Unit=null;Operator=None;Components=Array.Empty<float>();Strings=Array.Empty<string>()}
        match literal with
        | Number value -> finite "value" value; scalar value
        | Text text ->
            if String.IsNullOrWhiteSpace text then scalar 0.
            else
                let fullSpan = text.AsSpan()
                match tryNumberSpan fullSpan with
                | ValueSome value -> finite "value" value; scalar value
                | ValueNone ->
                    let op = if fullSpan.Length > 1 && fullSpan[1] = '=' then Some fullSpan[0] else None
                    let bodySpan = if op.IsSome then fullSpan.Slice(2) else fullSpan
                    match tryMatchUnitSpan bodySpan with
                    | ValueSome struct(numVal, unitStr) ->
                        { Kind = ValueKind.Unit; Number = numVal; Unit = unitStr; Operator = op; Components = Array.Empty<float>(); Strings = Array.Empty<string>() }
                    | ValueNone ->
                        if op.IsSome then
                            let value =
                                match tryNumberSpan bodySpan with
                                | ValueSome v -> v
                                | ValueNone -> invalidArg "value" "Expected a finite relative operand."
                            finite "value" value
                            { scalar value with Operator = op }
                        else
                            match CssColor.tryParseEndpoint text with
                            | Some color -> { scalar 0. with Kind = ValueKind.Color; Components = color }
                            | None ->
                                let struct(comps, strs) = decomposeComplexSpan bodySpan
                                { scalar 0. with Kind = ValueKind.Complex; Components = comps; Strings = strs }
    let private relative first last op = match op with '-' -> first-last | '+' -> first+last | _ -> first*last
    /// <summary>Normalizes decomposed relative endpoints, units and component templates using a captured original value.</summary>
    let normalizeData (convertUnit:(float -> string -> string -> float) option, original:Data, first:Data, last:Data) =
        let mutable first=first
        let mutable last=last
        match first.Operator with Some op -> first<-{first with Number=relative original.Number first.Number op} | None -> ()
        match last.Operator with Some op -> last<-{last with Number=relative first.Number last.Number op} | None -> ()
        if first.Kind<>last.Kind then
            if first.Kind=ValueKind.Complex || last.Kind=ValueKind.Complex then
                let complex=if first.Kind=ValueKind.Complex then first else last
                let convert (value:Data)={value with Kind=ValueKind.Complex;Components=Array.create complex.Components.Length value.Number;Strings=Array.copy complex.Strings}
                if first.Kind=ValueKind.Complex then last<-convert last else first<-convert first
            elif first.Kind=ValueKind.Unit || last.Kind=ValueKind.Unit then
                if first.Kind=ValueKind.Unit then last<-{last with Kind=ValueKind.Unit;Unit=first.Unit}
                else first<-{first with Kind=ValueKind.Unit;Unit=last.Unit}
            elif first.Kind=ValueKind.Color || last.Kind=ValueKind.Color then
                if first.Kind=ValueKind.Color then last<-{last with Kind=ValueKind.Color;Components=Array.zeroCreate 4}
                else first<-{first with Kind=ValueKind.Color;Components=Array.zeroCreate 4}
        if first.Unit<>last.Unit then
            match convertUnit with
            | None -> invalidArg "last" "Endpoint units require native conversion."
            | Some convert ->
                if not(String.IsNullOrEmpty last.Unit) then
                    let value=convert first.Number first.Unit last.Unit
                    finite "converted value" value
                    first<-{first with Number=value;Unit=last.Unit}
                else
                    let value=convert last.Number last.Unit first.Unit
                    finite "converted value" value
                    last<-{last with Number=value;Unit=first.Unit}
        if first.Components.Length<>last.Components.Length then
            let longest=if first.Components.Length>last.Components.Length then first else last
            let fill (value:Data)={value with Components=Array.init longest.Components.Length (fun i -> if i<value.Components.Length then value.Components[i] else 0.);Strings=Array.copy longest.Strings}
            if first.Components.Length>last.Components.Length then last<-fill last else first<-fill first
        first,last

    /// <summary>Normalizes relative endpoints, unit metadata and component templates during capture.</summary>
    let normalize (convertUnit:(float -> string -> string -> float) option) original first last =
        let first=decompose first
        let last=decompose last
        let original=if first.Operator.IsSome then decompose original else first
        normalizeData(convertUnit,original,first,last)

    /// <summary>Composes modified cached values using captured unit and component metadata.</summary>
    /// <remarks>The output retains the supplied component array.</remarks>
    let compose (template:Data, scalar:float, values:float array) : Output =
        if template.Kind=ValueKind.Number || template.Kind=ValueKind.Unit then
            Output(template.Kind,scalar,(if template.Kind=ValueKind.Unit then template.Unit else ""),Array.Empty<float>(),null)
        else
            Output(template.Kind,0.,null,values,formatComponentsText template values false)

    /// <summary>Samples scalar endpoints without allocating an <see cref="Output"/> instance.</summary>
    [<MethodImpl(MethodImplOptions.AggressiveInlining)>]
    let trySampleScalarValue (roundNumbers: bool, kind: ValueKind, fromNumber: float, toNumber: float,
                              progress: float, precision: int, modifier: float -> float, canWrite: unit -> bool) : struct(bool * float) =
        finite "progress" progress
        let precision = if progress = 0. || progress = 1. || (kind = ValueKind.Number && not roundNumbers) then -1 else precision
        let value = if progress = 1. then toNumber elif progress = 0. then fromNumber else fromNumber + (toNumber - fromNumber) * progress
        let value = if precision < 0 then value else PropertyNumbers.round (PropertyNumbers.pow10 precision) value
        if not (canWrite()) then struct(false, 0.)
        elif Object.ReferenceEquals(modifier, PropertyNumbers.identityFloat) then struct(true, value)
        else
            let result = modifier value
            if canWrite() then struct(true, result) else struct(false, 0.)

    let lerpComplexSimd (first: float array) (last: float array) (dst: float array) (progress: float) (precision: int) =
        let len = last.Length
        if progress = 0. then
            Array.Copy(first, 0, dst, 0, len)
        elif progress = 1. then
            Array.Copy(last, 0, dst, 0, len)
        elif precision < 0 then
            let mutable i = 0
            let rFirst = &MemoryMarshal.GetArrayDataReference(first)
            let rLast = &MemoryMarshal.GetArrayDataReference(last)
            let rDst = &MemoryMarshal.GetArrayDataReference(dst)
            if Vector512.IsHardwareAccelerated && len >= 8 then
                let vp = Vector512.Create(progress)
                while i <= len - 8 do
                    let va = Vector512.LoadUnsafe(&rFirst, unativeint i)
                    let vb = Vector512.LoadUnsafe(&rLast, unativeint i)
                    let vr = va + (vb - va) * vp
                    vr.StoreUnsafe(&rDst, unativeint i)
                    i <- i + 8
            elif Vector256.IsHardwareAccelerated && len >= 4 then
                let vp = Vector256.Create(progress)
                while i <= len - 4 do
                    let va = Vector256.LoadUnsafe(&rFirst, unativeint i)
                    let vb = Vector256.LoadUnsafe(&rLast, unativeint i)
                    let vr = va + (vb - va) * vp
                    vr.StoreUnsafe(&rDst, unativeint i)
                    i <- i + 4
            elif Vector128.IsHardwareAccelerated && len >= 2 then
                let vp = Vector128.Create(progress)
                while i <= len - 2 do
                    let va = Vector128.LoadUnsafe(&rFirst, unativeint i)
                    let vb = Vector128.LoadUnsafe(&rLast, unativeint i)
                    let vr = va + (vb - va) * vp
                    vr.StoreUnsafe(&rDst, unativeint i)
                    i <- i + 2
            while i < len do
                let a = first[i]
                dst[i] <- a + (last[i] - a) * progress
                i <- i + 1
        else
            let scale = PropertyNumbers.pow10 precision
            let mutable i = 0
            let rFirst = &MemoryMarshal.GetArrayDataReference(first)
            let rLast = &MemoryMarshal.GetArrayDataReference(last)
            let rDst = &MemoryMarshal.GetArrayDataReference(dst)
            if Vector512.IsHardwareAccelerated && len >= 8 then
                let vp = Vector512.Create(progress)
                let vScale = Vector512.Create(scale)
                let vHalf = Vector512.Create(0.5)
                while i <= len - 8 do
                    let va = Vector512.LoadUnsafe(&rFirst, unativeint i)
                    let vb = Vector512.LoadUnsafe(&rLast, unativeint i)
                    let vScaled = (va + (vb - va) * vp) * vScale
                    let vRound = Vector512.Round(vScaled, MidpointRounding.ToEven)
                    let vTie = Vector512.Equals(Vector512.Abs(vScaled - vRound), vHalf)
                    let vAdj = Vector512.ConditionalSelect(vTie, Vector512.Ceiling(vScaled), vRound)
                    (vAdj / vScale).StoreUnsafe(&rDst, unativeint i)
                    i <- i + 8
            elif Vector256.IsHardwareAccelerated && len >= 4 then
                let vp = Vector256.Create(progress)
                let vScale = Vector256.Create(scale)
                let vHalf = Vector256.Create(0.5)
                while i <= len - 4 do
                    let va = Vector256.LoadUnsafe(&rFirst, unativeint i)
                    let vb = Vector256.LoadUnsafe(&rLast, unativeint i)
                    let vScaled = (va + (vb - va) * vp) * vScale
                    let vRound = Vector256.Round(vScaled, MidpointRounding.ToEven)
                    let vTie = Vector256.Equals(Vector256.Abs(vScaled - vRound), vHalf)
                    let vAdj = Vector256.ConditionalSelect(vTie, Vector256.Ceiling(vScaled), vRound)
                    (vAdj / vScale).StoreUnsafe(&rDst, unativeint i)
                    i <- i + 4
            elif Vector128.IsHardwareAccelerated && len >= 2 then
                let vp = Vector128.Create(progress)
                let vScale = Vector128.Create(scale)
                let vHalf = Vector128.Create(0.5)
                while i <= len - 2 do
                    let va = Vector128.LoadUnsafe(&rFirst, unativeint i)
                    let vb = Vector128.LoadUnsafe(&rLast, unativeint i)
                    let vScaled = (va + (vb - va) * vp) * vScale
                    let vRound = Vector128.Round(vScaled, MidpointRounding.ToEven)
                    let vTie = Vector128.Equals(Vector128.Abs(vScaled - vRound), vHalf)
                    let vAdj = Vector128.ConditionalSelect(vTie, Vector128.Ceiling(vScaled), vRound)
                    (vAdj / vScale).StoreUnsafe(&rDst, unativeint i)
                    i <- i + 2
            while i < len do
                let a = first[i]
                dst[i] <- PropertyNumbers.round scale (a + (last[i] - a) * progress)
                i <- i + 1

    let inline private sampleColorRgba4 (first: float array) (last: float array) (progress: float) : Vector256<float> =
        let a3 = first[3]
        let b3 = last[3]
        let alpha = if progress = 1. then b3 elif progress = 0. then a3 else a3 + (b3 - a3) * progress
        if Vector256.IsHardwareAccelerated then
            let va = Vector256.LoadUnsafe(&MemoryMarshal.GetArrayDataReference(first))
            let vb = Vector256.LoadUnsafe(&MemoryMarshal.GetArrayDataReference(last))
            let vSq = Vector256.Max(Vector256<float>.Zero, va * va * Vector256.Create(1. - progress) + vb * vb * Vector256.Create(progress))
            Vector256.Sqrt(vSq).WithElement(3, alpha)
        else
            let oneMinusP = 1. - progress
            let inline sq i =
                let a = first[i]
                let b = last[i]
                Math.Sqrt(max 0. (a * a * oneMinusP + b * b * progress))
            Vector256.Create(sq 0, sq 1, sq 2, alpha)

    /// <summary>Samples compound or color components directly into <paramref name="dst"/> without allocating <see cref="Output"/> or formatting strings.</summary>
    let sampleComponentsInto (roundNumbers: bool, first: Data, last: Data, progress: float, precision: int,
                              modifier: float -> float, canWrite: unit -> bool, dst: float array) : bool =
        finite "progress" progress
        if not (canWrite()) then false
        else
            let precision = if progress = 0. || progress = 1. || (first.Kind = ValueKind.Number && not roundNumbers) then -1 else precision
            let isIdentity = Object.ReferenceEquals(modifier, PropertyNumbers.identityFloat)
            if first.Kind = ValueKind.Color && first.Components.Length = 4 && last.Components.Length = 4 then
                let vColor = sampleColorRgba4 first.Components last.Components progress
                if isIdentity then
                    vColor.StoreUnsafe(&MemoryMarshal.GetArrayDataReference(dst))
                    true
                else
                    let mutable alive = true
                    let mutable idx = 0
                    while idx < 4 && alive do
                        if not (canWrite()) then alive <- false
                        else
                            let result = modifier (vColor.GetElement idx)
                            if canWrite() then dst[idx] <- result else alive <- false
                        idx <- idx + 1
                    alive && canWrite()
            elif isIdentity then
                if first.Kind = ValueKind.Color then
                    let oneMinusP = 1. - progress
                    for index = 0 to last.Components.Length - 1 do
                        if index < 3 then
                            let a = first.Components[index]
                            let b = last.Components[index]
                            dst[index] <- Math.Sqrt(max 0. (a * a * oneMinusP + b * b * progress))
                        else
                            let a = first.Components[index]
                            let b = last.Components[index]
                            dst[index] <- if progress = 1. then b elif progress = 0. then a else a + (b - a) * progress
                    true
                else
                    lerpComplexSimd first.Components last.Components dst progress precision
                    true
            else
                let scale = if precision < 0 then 0. else PropertyNumbers.pow10 precision
                let inline roundVal value = if precision < 0 then value else PropertyNumbers.round scale value
                let inline lerp a b = if progress = 1. then b elif progress = 0. then a else a + (b - a) * progress
                let mutable alive = true
                let len = last.Components.Length
                let mutable index = 0
                while index < len && alive do
                    let value =
                        if first.Kind = ValueKind.Color && index < 3 then
                            let a, b = first.Components[index], last.Components[index]
                            Math.Sqrt(max 0. (a * a * (1. - progress) + b * b * progress))
                        elif first.Kind = ValueKind.Color then lerp first.Components[index] last.Components[index]
                        else roundVal (lerp first.Components[index] last.Components[index])
                    if not (canWrite()) then alive <- false
                    else
                        let result = modifier value
                        if canWrite() then dst[index] <- result else alive <- false
                    index <- index + 1
                alive && canWrite()

    /// <summary>Samples scalar endpoints held outside their template into an output, or null when the guard fails.</summary>
    /// <remarks>Allocates only the output; <c>template</c> supplies the kind and unit.</remarks>
    let sampleScalar (roundNumbers:bool, template:Data, fromNumber:float, toNumber:float, progress:float, precision:int,
                      modifier:float -> float, canWrite:unit -> bool) : Output =
        let struct(ok, result) = trySampleScalarValue(roundNumbers, template.Kind, fromNumber, toNumber, progress, precision, modifier, canWrite)
        if ok then compose(template, result, Array.Empty<float>()) else null

    /// <summary>Samples normalized endpoints into a composed output, or null when the guard fails around a modifier.</summary>
    /// <remarks>The unwrapped core of <c>sample</c>.</remarks>
    let sampleOutput (roundNumbers:bool, first:Data, last:Data, progress:float, precision:int, modifier:float -> float, canWrite:unit -> bool) : Output =
        if first.Kind=ValueKind.Number || first.Kind=ValueKind.Unit then
            sampleScalar(roundNumbers,first,first.Number,last.Number,progress,precision,modifier,canWrite)
        else
            finite "progress" progress
            if not(canWrite()) then null
            else
                let values = Array.zeroCreate last.Components.Length
                if sampleComponentsInto(roundNumbers, first, last, progress, precision, modifier, canWrite, values) then
                    compose(last, 0., values)
                else null

    /// <summary>Samples normalized endpoints and returns the modified scalar cache with the composed output.</summary>
    let sample (roundNumbers:bool, first:Data, last:Data, progress:float, precision:int, modifier:float -> float, canWrite:unit -> bool) =
        match sampleOutput(roundNumbers,first,last,progress,precision,modifier,canWrite) with
        | null -> None
        | output -> Some(struct(output.Number,output))

    let private prepareWith roundNumbers convertUnit original first last =
        let first,last=normalize convertUnit original first last
        fun progress precision modifier canWrite ->
            match sampleOutput(roundNumbers,first,last,progress,precision,modifier,canWrite) with
            | null -> None
            | output -> Some output

    /// <summary>Captures object endpoints and returns a guarded sampler; differing units require native conversion.</summary>
    let prepare original first last = prepareWith false None original first last

    /// <summary>Interpolates normalized SVG coordinates at the morph accessor's four decimal places.</summary>
    let morphCoordinates (first:float[]) (last:float[]) progress =
        if progress=0. then Array.copy first
        elif progress=1. then Array.copy last
        else
            let values = Array.zeroCreate<float> last.Length
            lerpComplexSimd first last values progress 4
            values

    /// <summary>Captures SVG endpoints for zero-Output string morphing.</summary>
    let prepareMorphText firstText lastText : (float -> (unit -> bool) -> string voption) =
        let first,last = normalize None (Text firstText) (Text firstText) (Text lastText)
        if first.Kind=ValueKind.Complex then
            let scratch = Array.zeroCreate<float> last.Components.Length
            fun progress current ->
                finite "progress" progress
                if not(current()) then ValueNone
                else
                    lerpComplexSimd first.Components last.Components scratch progress 4
                    if current() then ValueSome(formatComponentsText last scratch false) else ValueNone
        else
            fun progress current ->
                match sampleOutput(false,first,last,progress,4,PropertyNumbers.identityFloat,current) with
                | null -> ValueNone
                | output -> ValueSome output.Text

    /// <summary>Captures SVG endpoints for identity-only progress writes. Compound arithmetic has no callbacks.</summary>
    let prepareMorph firstText lastText =
        let first,last = normalize None (Text firstText) (Text firstText) (Text lastText)
        if first.Kind=ValueKind.Complex then
            let scratch = Array.zeroCreate<float> last.Components.Length
            fun progress current ->
                finite "progress" progress
                if not(current()) then None
                else
                    lerpComplexSimd first.Components last.Components scratch progress 4
                    if current() then Some(compose(last,0.,scratch)) else None
        else
            fun progress current ->
                match sampleOutput(false,first,last,progress,4,PropertyNumbers.identityFloat,current) with
                | null -> None
                | output -> Some output

    /// <summary>Captures CSS endpoints, rounding intermediate unitless scalars before modifiers as source CSS dispatch does.</summary>
    let prepareCss original first last = prepareWith true None original first last

    /// <summary>Converts differing scalar unit metadata after source relative operators and endpoint homogenization.</summary>
    let prepareCssWith convertUnit original first last = prepareWith true (Some convertUnit) original first last
