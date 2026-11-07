namespace Ranvier.Anime

open System
open System.Text
open System.Collections.Generic
open System.Collections.Frozen

/// <summary>Anime.js inline transform cache parsing and canonical function serialization.</summary>
/// <remarks>WebCore validates CSS. This adapter retains source function bodies, including nested expressions.</remarks>
/// <summary>The per-thread builder TransformCache.build reuses.</summary>
type internal BuildBuffer =
    [<ThreadStatic; DefaultValue>]
    static val mutable private shared: StringBuilder
    static member Shared with get()=BuildBuffer.shared and set value=BuildBuffer.shared<-value

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

    /// <summary>Builds source canonical order, grouping adjacent translation/scaling axes and preserving matrices and rotate3d.</summary>
    /// <remarks>Reuses one builder per thread: a follower writes its element's transform every frame.</remarks>
    let build (cache:IReadOnlyDictionary<string,string>) =
        let result=
            match BuildBuffer.Shared with
            | null ->
                let created=StringBuilder(128)
                BuildBuffer.Shared<-created
                created
            | shared -> shared.Clear()
        let has key=cache.ContainsKey key
        let append (key:string) (value:string)=result.Append(key).Append('(').Append(value).Append(") ") |> ignore
        let appendAxes (key:string) (x:string) (y:string) (z:string) =
            result.Append(key).Append('(').Append(x).Append(',').Append(y) |> ignore
            if not(isNull z) then result.Append(',').Append(z) |> ignore
            result.Append(") ") |> ignore
        let mutable index=0
        while index<order.Length do
            let key=order[index]
            let mutable grouped=false
            match cache.TryGetValue key with
            | true,value ->
                if key="translateX" && has "translateY" then
                    if has "translateZ" then appendAxes "translate3d" value cache["translateY"] cache["translateZ"];index<-index+2
                    else appendAxes "translate" value cache["translateY"] null;index<-index+1
                    grouped<-true
                elif key="scaleX" && not(has "scale") && has "scaleY" then
                    if has "scaleZ" then appendAxes "scale3d" value cache["scaleY"] cache["scaleZ"];index<-index+2
                    else appendAxes "scale" value cache["scaleY"] null;index<-index+1
                    grouped<-true
                else append key value
            | _ -> ()
            if not grouped && key="rotateZ" && has "rotate3d" then append "rotate3d" cache["rotate3d"]
            index<-index+1
        if has "matrix" then append "matrix" cache["matrix"]
        if has "matrix3d" then append "matrix3d" cache["matrix3d"]
        result.ToString()

