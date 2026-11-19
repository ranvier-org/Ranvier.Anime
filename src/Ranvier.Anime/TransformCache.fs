namespace Ranvier.Anime

open System
open System.Text
open System.Collections.Generic
open System.Collections.Frozen
open System.Globalization

/// <summary>Anime.js inline transform cache parsing and canonical function serialization.</summary>
/// <remarks>WebCore validates CSS. This adapter retains source function bodies, including nested expressions.</remarks>
/// <summary>The per-thread builder TransformCache.build reuses.</summary>
type internal BuildBuffer =
    [<ThreadStatic; DefaultValue>]
    static val mutable private shared: StringBuilder
    static member Shared with get()=BuildBuffer.shared and set value=BuildBuffer.shared<-value

/// <summary>The per-thread buffer scalar transform values are formatted in.</summary>
type internal ScalarBuffer =
    [<ThreadStatic; DefaultValue>]
    static val mutable private chars: char array
    static member Chars =
        if isNull ScalarBuffer.chars then ScalarBuffer.chars <- Array.zeroCreate 64
        ScalarBuffer.chars

[<RequireQualifiedAccess>]
module TransformCache =
    let private order =
        [|"perspective";"translateX";"translateY";"translateZ";"rotate";"rotateX";"rotateY";"rotateZ"
          "scale";"scaleX";"scaleY";"scaleZ";"skew";"skewX";"skewY"|]

    let private internCanonicalName (span: ReadOnlySpan<char>) : string =
        match span.Length with
        | 4 ->
            if span.SequenceEqual("skew".AsSpan()) then "skew"
            else span.ToString()
        | 5 ->
            if span.SequenceEqual("scale".AsSpan()) then "scale"
            elif span.SequenceEqual("skewX".AsSpan()) then "skewX"
            elif span.SequenceEqual("skewY".AsSpan()) then "skewY"
            else span.ToString()
        | 6 ->
            if span.SequenceEqual("rotate".AsSpan()) then "rotate"
            elif span.SequenceEqual("scaleX".AsSpan()) then "scaleX"
            elif span.SequenceEqual("scaleY".AsSpan()) then "scaleY"
            elif span.SequenceEqual("scaleZ".AsSpan()) then "scaleZ"
            elif span.SequenceEqual("matrix".AsSpan()) then "matrix"
            else span.ToString()
        | 7 ->
            if span.SequenceEqual("rotateX".AsSpan()) then "rotateX"
            elif span.SequenceEqual("rotateY".AsSpan()) then "rotateY"
            elif span.SequenceEqual("rotateZ".AsSpan()) then "rotateZ"
            elif span.SequenceEqual("scale3d".AsSpan()) then "scale3d"
            else span.ToString()
        | 8 ->
            if span.SequenceEqual("rotate3d".AsSpan()) then "rotate3d"
            elif span.SequenceEqual("matrix3d".AsSpan()) then "matrix3d"
            else span.ToString()
        | 9 ->
            if span.SequenceEqual("translate".AsSpan()) then "translate"
            else span.ToString()
        | 10 ->
            if span.SequenceEqual("translateX".AsSpan()) then "translateX"
            elif span.SequenceEqual("translateY".AsSpan()) then "translateY"
            elif span.SequenceEqual("translateZ".AsSpan()) then "translateZ"
            else span.ToString()
        | 11 ->
            if span.SequenceEqual("perspective".AsSpan()) then "perspective"
            elif span.SequenceEqual("translate3d".AsSpan()) then "translate3d"
            else span.ToString()
        | _ -> span.ToString()

    let private storeAxis (span: ReadOnlySpan<char>) (bodySpan: ReadOnlySpan<char>) (valueStart: int) (valueEnd: int) (comma1: int) (comma2: int) (isTranslate: bool) (cache: Dictionary<string, string>) =
        if comma1 = -1 then
            cache[if isTranslate then "translateX" else "scale"] <- bodySpan.Trim().ToString()
        else
            cache[if isTranslate then "translateX" else "scaleX"] <- span.Slice(valueStart, comma1 - valueStart).Trim().ToString()
            if comma2 = -1 then
                cache[if isTranslate then "translateY" else "scaleY"] <- span.Slice(comma1 + 1, valueEnd - comma1 - 1).Trim().ToString()
            else
                cache[if isTranslate then "translateY" else "scaleY"] <- span.Slice(comma1 + 1, comma2 - comma1 - 1).Trim().ToString()
                cache[if isTranslate then "translateZ" else "scaleZ"] <- span.Slice(comma2 + 1, valueEnd - comma2 - 1).Trim().ToString()

    /// <summary>Parses inline transform functions directly into <paramref name="cache"/> without constructing a <see cref="FrozenDictionary{TKey,TValue}"/>.</summary>
    let internal parseInto (inlineText:string) (cache:Dictionary<string,string>) : string option =
        let mutable fullTranslate:string option=None
        let span = inlineText.AsSpan()
        let length = span.Length
        let mutable position = 0
        while position < length do
            while position < length && Char.IsWhiteSpace span[position] do position <- position + 1
            if position < length then
                let nameStart = position
                while position < length && span[position] <> '(' do position <- position + 1
                if position < length then
                    let nameSpan = span.Slice(nameStart, position - nameStart).Trim()
                    let name = internCanonicalName nameSpan
                    let mutable depth = 1
                    let valueStart = position + 1
                    let mutable comma1 = -1
                    let mutable comma2 = -1
                    position <- position + 1
                    while position < length && depth > 0 do
                        match span[position] with
                        | '(' -> depth <- depth + 1
                        | ')' -> depth <- depth - 1
                        | ',' when depth = 1 -> if comma1 = -1 then comma1 <- position elif comma2 = -1 then comma2 <- position
                        | _ -> ()
                        position <- position + 1
                    let valueEnd = if depth = 0 then position - 1 else position
                    let bodySpan = span.Slice(valueStart, max 0 (valueEnd - valueStart))
                    if Object.ReferenceEquals(name, "translate") || Object.ReferenceEquals(name, "translate3d") || name = "translate" || name = "translate3d" then
                        storeAxis span bodySpan valueStart valueEnd comma1 comma2 true cache
                        fullTranslate <- Some (bodySpan.ToString())
                    elif Object.ReferenceEquals(name, "scale") || Object.ReferenceEquals(name, "scale3d") || name = "scale" || name = "scale3d" then
                        storeAxis span bodySpan valueStart valueEnd comma1 comma2 false cache
                    else
                        cache[name] <- bodySpan.ToString()
        fullTranslate

    /// <summary>Parses source inline functions into an immutable cache, decomposing multi-axis translate/scale functions.</summary>
    let parse (inlineText:string) =
        let cache = Dictionary<string,string>(8, StringComparer.Ordinal)
        let fullTranslate = parseInto inlineText cache
        struct(cache.ToFrozenDictionary(StringComparer.Ordinal), fullTranslate)

    /// <summary>Returns a cached transform value or the source's scale/angle/length defaults.</summary>
    let read (cache:IReadOnlyDictionary<string,string>) (fullTranslate:string option) (property:string) =
        if property="translate3d" && fullTranslate.IsSome then fullTranslate.Value
        else
            match cache.TryGetValue property with
            | true,value -> value
            | _ when property="translate3d" -> "0px, 0px, 0px"
            | _ when property="rotate3d" -> "0, 0, 0, 0deg"
            | _ when property.StartsWith("scale",StringComparison.Ordinal) -> "1"
            | _ when property.StartsWith("rotate",StringComparison.Ordinal) || property.StartsWith("skew",StringComparison.Ordinal) -> "0deg"
            | _ -> "0px"

    let private noPriority = Some ""
    let private important = Some "important"
    /// <summary>A priority as <c>setProperty</c>'s option, shared for the two priorities a transform is written with.</summary>
    let internal priorityOption (priority:string) =
        if isNull priority || priority.Length=0 then noPriority
        elif priority="important" then important
        else Some priority

    /// <summary>The cache value of a component held as a number; compared by reference, never by text.</summary>
    let internal numeric : string = String([|'\u0000'|])

    /// <summary>Appends a number as <c>ValueInterpolation.number</c> formats it, then its unit.</summary>
    let private appendNumber (result:StringBuilder) (value:float) (unitSuffix:string) =
        if value = 0. then
            result.Append('0').Append(unitSuffix) |> ignore
        elif Double.IsFinite value then
            let buffer = ScalarBuffer.Chars
            let mutable fastDone = false
            if abs value <= 1e9 then
                let n = int64 (Math.Round(value * 10000.))
                if float n / 10000. = value then
                    fastDone <- true
                    let mutable p = 0
                    let u =
                        if n < 0L then
                            buffer[0] <- '-'
                            p <- 1
                            uint64 (-n)
                        else
                            uint64 n
                    let q = u / 10000UL
                    let r = int (u % 10000UL)
                    if q < 10UL then
                        buffer[p] <- char (48 + int q)
                        p <- p + 1
                    else
                        let mutable written = 0
                        q.TryFormat(Span<char>(buffer, p, 24), &written, ReadOnlySpan<char>.Empty, CultureInfo.InvariantCulture) |> ignore
                        p <- p + written
                    if r <> 0 then
                        buffer[p] <- '.'
                        let d0 = r / 1000
                        let d1 = (r / 100) % 10
                        let d2 = (r / 10) % 10
                        let d3 = r % 10
                        buffer[p + 1] <- char (48 + d0)
                        if d3 <> 0 then
                            buffer[p + 2] <- char (48 + d1); buffer[p + 3] <- char (48 + d2); buffer[p + 4] <- char (48 + d3); p <- p + 5
                        elif d2 <> 0 then
                            buffer[p + 2] <- char (48 + d1); buffer[p + 3] <- char (48 + d2); p <- p + 4
                        elif d1 <> 0 then
                            buffer[p + 2] <- char (48 + d1); p <- p + 3
                        else
                            p <- p + 2
                    result.Append(ReadOnlySpan<char>(buffer, 0, p)).Append(unitSuffix) |> ignore
            if not fastDone then
                let mutable written = 0
                if value.TryFormat(Span<char>(buffer), &written, "R".AsSpan(), CultureInfo.InvariantCulture)
                   && ReadOnlySpan<char>(buffer, 0, written).IndexOfAny('E', 'e') < 0 then
                    result.Append(ReadOnlySpan<char>(buffer, 0, written)).Append(unitSuffix) |> ignore
                else
                    result.Append(ValueInterpolation.number value).Append(unitSuffix) |> ignore
        else
            result.Append(ValueInterpolation.number value).Append(unitSuffix) |> ignore

    /// <summary>The text of a numeric component, as <c>build</c> writes it.</summary>
    let internal numberText (value:float) (unitSuffix:string) =
        let buffer=ScalarBuffer.Chars
        let mutable written=0
        if value<>0. && value.TryFormat(Span<char>(buffer),&written,"R".AsSpan(),CultureInfo.InvariantCulture)
           && ReadOnlySpan<char>(buffer,0,written).IndexOfAny('E','e')<0 && written+unitSuffix.Length<=buffer.Length then
            unitSuffix.CopyTo(Span<char>(buffer,written,unitSuffix.Length))
            String(buffer,0,written+unitSuffix.Length)
        else ValueInterpolation.number value+unitSuffix

    let private appendValue (result:StringBuilder) (numbers:Dictionary<string,struct(float*string)>) (key:string) (value:string) =
        if Object.ReferenceEquals(value,numeric) then
            let struct(number,unitSuffix)=numbers[key]
            appendNumber result number unitSuffix
        else result.Append(value) |> ignore

    let private append result numbers (key:string) (value:string) =
        (result:StringBuilder).Append(key).Append('(') |> ignore
        appendValue result numbers key value
        result.Append(") ") |> ignore

    let private appendAxes result numbers (name:string) (translate:bool) (cache:IReadOnlyDictionary<string,string>) (x:string) (withZ:bool) =
        (result:StringBuilder).Append(name).Append('(') |> ignore
        appendValue result numbers (if translate then "translateX" else "scaleX") x
        result.Append(',') |> ignore
        let yKey=if translate then "translateY" else "scaleY"
        appendValue result numbers yKey cache[yKey]
        if withZ then
            let zKey=if translate then "translateZ" else "scaleZ"
            result.Append(',') |> ignore
            appendValue result numbers zKey cache[zKey]
        result.Append(") ") |> ignore

    /// <summary>Fills the thread's builder in source canonical order; numeric components read from <c>numbers</c>.</summary>
    let private fill (cache:IReadOnlyDictionary<string,string>) (numbers:Dictionary<string,struct(float*string)>) =
        let result=
            match BuildBuffer.Shared with
            | null ->
                let created=StringBuilder(128)
                BuildBuffer.Shared<-created
                created
            | shared -> shared.Clear()
        let mutable index=0
        while index<order.Length do
            let key=order[index]
            let mutable grouped=false
            match cache.TryGetValue key with
            | true,value ->
                if key="translateX" && cache.ContainsKey "translateY" then
                    if cache.ContainsKey "translateZ" then appendAxes result numbers "translate3d" true cache value true;index<-index+2
                    else appendAxes result numbers "translate" true cache value false;index<-index+1
                    grouped<-true
                elif key="scaleX" && not(cache.ContainsKey "scale") && cache.ContainsKey "scaleY" then
                    if cache.ContainsKey "scaleZ" then appendAxes result numbers "scale3d" false cache value true;index<-index+2
                    else appendAxes result numbers "scale" false cache value false;index<-index+1
                    grouped<-true
                else append result numbers key value
            | _ -> ()
            if not grouped && key="rotateZ" && cache.ContainsKey "rotate3d" then append result numbers "rotate3d" cache["rotate3d"]
            index<-index+1
        match cache.TryGetValue "matrix" with
        | true,value -> append result numbers "matrix" value
        | _ -> ()
        match cache.TryGetValue "matrix3d" with
        | true,value -> append result numbers "matrix3d" value
        | _ -> ()
        result

    /// <summary>Builds source canonical order, grouping adjacent translation/scaling axes and preserving matrices and rotate3d.</summary>
    /// <remarks>Reuses one builder per thread: a follower writes its element's transform every frame.</remarks>
    let build (cache:IReadOnlyDictionary<string,string>) = (fill cache null).ToString()

    /// <summary>
    /// <c>build</c> without its trailing space, reading components held as <c>numeric</c> from <c>numbers</c>: the text
    /// an element's inline transform is set to.
    /// </summary>
    let internal buildDeclaration (cache:IReadOnlyDictionary<string,string>) (numbers:Dictionary<string,struct(float*string)>) =
        let result=fill cache numbers
        let mutable length=result.Length
        while length>0 && result[length-1]=' ' do length<-length-1
        result.ToString(0,length)

