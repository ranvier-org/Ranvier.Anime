namespace Ranvier.Anime

open System
open System.Collections.Generic

/// <summary>Lossless declaration boundaries in native CSSOM serialization; values remain native CSS text.</summary>
module internal CssSerialization =
    let private tokens (text:string) =
        let result=ResizeArray<int*char>()
        let mutable index=0
        let mutable quote='\000'
        let mutable depth=0
        while index<text.Length do
            let value=text[index]
            if value='\\' then index<-index+2
            elif quote<>'\000' then
                if value=quote then quote<-'\000'
                index<-index+1
            elif value='/' && index+1<text.Length && text[index+1]='*' then
                let ending=text.IndexOf("*/",index+2,StringComparison.Ordinal)
                index<-if ending<0 then text.Length else ending+2
            elif value='\'' || value='"' then
                quote<-value
                index<-index+1
            else
                if value='(' || value='[' || value='{' then depth<-depth+1
                elif value=')' || value=']' || value='}' then depth<-Math.Clamp(depth-1,0,Int32.MaxValue)
                elif depth=0 && (value=';' || value=':' || value='!') then result.Add(index,value)
                index<-index+1
        result.ToArray()

    /// <summary>Splits serialized declarations, retaining raw values and top-level important priority.</summary>
    let declarations (text:string) =
        let tokens=tokens text
        let endings=Array.append (tokens |> Array.choose(fun (index,value) -> if value=';' then Some index else None)) [|text.Length|]
        let result=ResizeArray<struct(string*string*string)>()
        let mutable start=0
        for ending in endings do
            match tokens |> Array.tryFind(fun (index,value) -> index>=start && index<ending && value=':') with
            | Some(colon,_) ->
                let name=text.Substring(start,colon-start).Trim()
                let mutable valueEnd=ending
                let mutable priority=""
                match tokens |> Array.tryFindBack(fun (index,value) -> index>colon && index<ending && value='!') with
                | Some(bang,_) when text.Substring(bang+1,ending-bang-1).Trim().Equals("important",StringComparison.OrdinalIgnoreCase) ->
                    priority<-"important"
                    valueEnd<-bang
                | _ -> ()
                let value=text.Substring(colon+1,valueEnd-colon-1).Trim()
                if name.Length>0 then result.Add(struct(name,value,priority))
            | None -> ()
            start<-ending+1
        result.ToArray()
