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

    /// <summary>Parses source inline functions into an immutable cache, decomposing multi-axis translate/scale functions.</summary>
    let parse (inlineText:string) =
        let cache=Dictionary<string,string>(StringComparer.Ordinal)
        let mutable fullTranslate:string option=None
        let mutable position=0
        let length=inlineText.Length
        while position<length do
            while position<length && inlineText[position]=' ' do position<-position+1
            if position<length then
                let nameStart=position
                while position<length && inlineText[position]<>'(' do position<-position+1
                if position<length then
                    let name=inlineText.Substring(nameStart,position-nameStart)
                    let mutable depth=1
                    let valueStart=position+1
                    let mutable comma1= -1
                    let mutable comma2= -1
                    position<-position+1
                    while position<length && depth>0 do
                        match inlineText[position] with
                        | '(' -> depth<-depth+1
                        | ')' -> depth<-depth-1
                        | ',' when depth=1 -> if comma1= -1 then comma1<-position elif comma2= -1 then comma2<-position
                        | _ -> ()
                        position<-position+1
                    let valueEnd=position-1
                    let body=inlineText.Substring(valueStart,max 0 (valueEnd-valueStart))
                    let axis prefix =
                        if comma1= -1 then cache[if prefix="translate" then "translateX" else "scale"]<-body.Trim()
                        else
                            cache[prefix+"X"]<-inlineText.Substring(valueStart,comma1-valueStart).Trim()
                            if comma2= -1 then cache[prefix+"Y"]<-inlineText.Substring(comma1+1,valueEnd-comma1-1).Trim()
                            else
                                cache[prefix+"Y"]<-inlineText.Substring(comma1+1,comma2-comma1-1).Trim()
                                cache[prefix+"Z"]<-inlineText.Substring(comma2+1,valueEnd-comma2-1).Trim()
                    if name="translate" || name="translate3d" then
                        axis "translate"
                        fullTranslate<-Some body
                    elif name="scale" || name="scale3d" then axis "scale"
                    else cache[name]<-body
        struct(cache.ToFrozenDictionary(StringComparer.Ordinal),fullTranslate)

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
        let buffer=ScalarBuffer.Chars
        let mutable written=0
        if value<>0. && value.TryFormat(Span<char>(buffer),&written,"R".AsSpan(),CultureInfo.InvariantCulture)
           && ReadOnlySpan<char>(buffer,0,written).IndexOfAny('E','e')<0 then
            result.Append(ReadOnlySpan<char>(buffer,0,written)).Append(unitSuffix) |> ignore
        else result.Append(ValueInterpolation.number value).Append(unitSuffix) |> ignore

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

