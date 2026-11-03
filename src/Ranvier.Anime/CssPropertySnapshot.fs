namespace Ranvier.Anime

open System
open System.Collections.Generic
open Ranvier.Dom

module private CssSnapshotValues =
    let pending (struct(name,value,_,present):struct(string*string*string*bool)) = present && value="" && not(name.StartsWith("--",StringComparison.Ordinal))
    let serialized (values:struct(string*string*string*bool) array) (allowed:HashSet<string>) (read:unit -> string) =
        let missing=values |> Array.filter pending |> Array.map(fun struct(name,_,_,_) -> name) |> fun names -> HashSet<string>(names,StringComparer.Ordinal)
        if missing.Count=0 then values
        else
            let declarations=CssSerialization.declarations(read())
            let covered=HashSet<string>(StringComparer.Ordinal)
            for struct(name,value,_) in declarations do
                let leaves=CssPropertyMetadata.leafNames name
                if leaves |> Array.exists missing.Contains then
                    if value="" then
                        raise(NotSupportedException("Native CSS serialization omitted a pending shorthand value: "+name))
                    if leaves |> Array.exists(fun leaf -> not(allowed.Contains leaf)) then
                        raise(NotSupportedException("Pending CSS shorthand crosses the captured property scope: "+name))
                    for leaf in leaves do covered.Add leaf |> ignore
            if missing |> Seq.exists(fun leaf -> not(covered.Contains leaf)) then
                raise(NotSupportedException("Native CSS serialization cannot reconstruct a pending inline value."))
            Array.append
                (values |> Array.filter(fun struct(name,_,_,_) -> not(covered.Contains name)))
                (declarations |> Array.choose(fun struct(name,value,priority) ->
                    let leaves=CssPropertyMetadata.leafNames name
                    if leaves |> Array.exists covered.Contains then
                        if leaves |> Array.exists(fun leaf -> not(allowed.Contains leaf)) then
                            raise(NotSupportedException("Pending CSS shorthand overrides cross the captured property scope: "+name))
                        Some(struct(name,value,priority,true))
                    else None))

/// <summary>One inline property's affected longhands and original declaration order.</summary>
[<Sealed>]
type internal CssPropertySnapshot private
    (important:bool,values:struct(string*string*string*bool) array,order:Dictionary<string,int>,counterparts:HashSet<string>) =
    /// <summary>Whether the original authored property had uniform important priority.</summary>
    member _.Important=important

    /// <summary>Restores affected leaves and preserves current logical counterpart values and original precedence.</summary>
    member _.Restore(style:CSSStyleDeclaration,current:unit -> bool) =
        let mutable failure:exn option=None
        let attempt action =
            try action()
            with error -> if failure.IsNone then failure<-Some error
        let read action =
            if current() then
                let value=action()
                if current() then Some value else None
            else None
        let restore=ResizeArray<struct(string*string*string*bool)>(values)
        let liveValues=ResizeArray<struct(string*string*string*bool)>()
        let affected=HashSet<string>(values |> Array.collect(fun struct(name,_,_,_) -> CssPropertyMetadata.leafNames name),StringComparer.Ordinal)
        if counterparts.Count>0 then
            attempt(fun () ->
                match read(fun () -> style.length) with
                | Some length ->
                    let mutable index=0u
                    while current() && index<length do
                        match read(fun () -> style.item index) with
                        | Some name ->
                            for leaf in CssPropertyMetadata.leafNames name do
                                if current() && counterparts.Contains leaf && affected.Add leaf then
                                    match read(fun () -> style.getPropertyValue leaf) with
                                    | Some value ->
                                        match read(fun () -> style.getPropertyPriority leaf) with
                                        | Some priority -> liveValues.Add(struct(leaf,value,priority,true))
                                        | None -> ()
                                    | None -> ()
                        | None -> ()
                        index<-index+1u
                | None -> ())
        if current() then
            attempt(fun () ->
                let serialized=CssSnapshotValues.serialized (liveValues.ToArray()) counterparts (fun () ->
                    match read(fun () -> style.cssText) with
                    | Some value -> value
                    | None -> "")
                if current() then restore.AddRange serialized)
        let ordered=
            restore.ToArray()
            |> Array.mapi(fun index value ->
                let struct(name,_,_,_)=value
                let position=
                    CssPropertyMetadata.leafNames name
                    |> Array.map(fun leaf -> match order.TryGetValue leaf with true,value -> value | _ -> Int32.MaxValue)
                    |> Array.min
                (position,index),value)
            |> Array.sortBy fst
        for _,struct(name,value,priority,present) in ordered do
            if current() then
                attempt(fun () ->
                    if present then style.setProperty(name,value,priority=priority)
                    else style.removeProperty name |> ignore)
        failure |> Option.iter(fun error -> Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw())

    /// <summary>Captures affected inline leaves before input effects; every style read checks ownership.</summary>
    static member Capture(style:CSSStyleDeclaration,name:string,ensureLive:unit -> unit) =
        let read action =
            ensureLive()
            let value=action()
            ensureLive()
            value
        let leaves=CssPropertyMetadata.leafNames name
        let priority=read(fun () -> style.getPropertyPriority name)
        let order=Dictionary<string,int>(StringComparer.Ordinal)
        let length=read(fun () -> style.length)
        let mutable index=0u
        while index<length do
            let name=read(fun () -> style.item index)
            for leaf in CssPropertyMetadata.leafNames name do order[leaf]<-int index
            index<-index+1u
        let values=leaves |> Array.map(fun name ->
            let value=read(fun () -> style.getPropertyValue name)
            let priority=read(fun () -> style.getPropertyPriority name)
            struct(name,value,priority,not(String.IsNullOrEmpty value) || order.ContainsKey name))
        let allowed=HashSet<string>(leaves,StringComparer.Ordinal)
        let values=CssSnapshotValues.serialized values allowed (fun () -> read(fun () -> style.cssText))
        CssPropertySnapshot((priority="important"),values,order,HashSet<string>(CssPropertyMetadata.counterparts leaves,StringComparer.Ordinal))
