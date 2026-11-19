namespace Ranvier.Anime

open System
open System.Collections.Frozen
open System.Collections.Generic
open Ranvier.Dom

module private CssSnapshotValues =
    let pending (struct(name,value,_,present):struct(string*string*string*bool)) = present && value="" && not(name.StartsWith("--",StringComparison.Ordinal))
    let serialized (values:struct(string*string*string*bool) array) (allowed:FrozenSet<string>) (read:unit -> string) =
        let mutable anyPending = false
        for i = 0 to values.Length - 1 do
            if pending values[i] then anyPending <- true
        if not anyPending then values
        else
            let missing=values |> Array.filter pending |> Array.map(fun struct(name,_,_,_) -> name) |> fun names -> HashSet<string>(names,StringComparer.Ordinal)
            let declarations=CssSerialization.declarations(read())
            let covered=HashSet<string>(StringComparer.Ordinal)
            for struct(name,value,_) in declarations do
                let leaves=CssPropertyMetadata.leafNamesReadOnly name
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
                    let leaves=CssPropertyMetadata.leafNamesReadOnly name
                    if leaves |> Array.exists covered.Contains then
                        if leaves |> Array.exists(fun leaf -> not(allowed.Contains leaf)) then
                            raise(NotSupportedException("Pending CSS shorthand overrides cross the captured property scope: "+name))
                        Some(struct(name,value,priority,true))
                    else None))

/// <summary>One inline property's affected longhands and original declaration order.</summary>
[<Sealed>]
type internal CssPropertySnapshot private
    (important:bool,values:struct(string*string*string*bool) array,order:Dictionary<string,int>,counterparts:FrozenSet<string>,capturedEmpty:bool) =
    /// <summary>Whether the original authored property had uniform important priority.</summary>
    member _.Important=important
    /// <summary>Whether the inline style declaration was empty when this snapshot was captured.</summary>
    member _.CapturedEmpty=capturedEmpty

    /// <summary>Restores affected leaves and preserves current logical counterpart values and original precedence.</summary>
    member this.Restore(style:CSSStyleDeclaration,current:unit -> bool) =
        this.RestoreGuarded(style, current, ValueNone)

    member internal _.RestoreGuarded(style:CSSStyleDeclaration,current:unit -> bool,allowFilter:struct(string array * struct(string * string) array) voption) =
        let mutable failure:exn option=None
        let attempt action =
            try action()
            with error -> if failure.IsNone then failure<-Some error
        let read action =
            if current() then
                let value=action()
                if current() then Some value else None
            else None
        let inline isAllowed property =
            match allowFilter with
            | ValueNone -> true
            | ValueSome struct(leaves, last) ->
                let propLeaves = CssPropertyMetadata.leafNamesReadOnly property
                let mutable ok = true
                let mutable j = 0
                while j < propLeaves.Length && ok do
                    let leaf = propLeaves[j]
                    let mutable idx = -1
                    let mutable k = 0
                    while k < leaves.Length && idx < 0 do
                        if String.Equals(leaves[k], leaf, StringComparison.Ordinal) then idx <- k
                        k <- k + 1
                    if idx >= 0 then
                        let struct(wVal, wPrio) = last[idx]
                        ok <- style.getPropertyValue leaf = wVal && style.getPropertyPriority leaf = wPrio
                    j <- j + 1
                ok
        if capturedEmpty && counterparts.Count = 0 then
            for i = 0 to values.Length - 1 do
                if current() then
                    let struct(name,_,_,_) = values[i]
                    attempt(fun () ->
                        if isAllowed name then style.removeProperty name |> ignore)
            failure |> Option.iter(fun error -> Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw())
        else
            let mutable liveValues : ResizeArray<struct(string*string*string*bool)> = null
            let mutable affected : HashSet<string> = null
            if counterparts.Count>0 then
                attempt(fun () ->
                    match read(fun () -> style.length) with
                    | Some length ->
                        let mutable index=0u
                        while current() && index<length do
                            match read(fun () -> style.item index) with
                            | Some name ->
                                for leaf in CssPropertyMetadata.leafNamesReadOnly name do
                                    if current() && counterparts.Contains leaf then
                                        if isNull affected then
                                            affected <- HashSet<string>(StringComparer.Ordinal)
                                            for vi = 0 to values.Length - 1 do
                                                let struct(vName,_,_,_) = values[vi]
                                                for vl in CssPropertyMetadata.leafNamesReadOnly vName do
                                                    affected.Add vl |> ignore
                                            liveValues <- ResizeArray<struct(string*string*string*bool)>()
                                        if affected.Add leaf then
                                            match read(fun () -> style.getPropertyValue leaf) with
                                            | Some value ->
                                                match read(fun () -> style.getPropertyPriority leaf) with
                                                | Some priority -> liveValues.Add(struct(leaf,value,priority,true))
                                                | None -> ()
                                            | None -> ()
                            | None -> ()
                            index<-index+1u
                    | None -> ())
            if capturedEmpty && isNull liveValues then
                for i = 0 to values.Length - 1 do
                    if current() then
                        let struct(name,_,_,_) = values[i]
                        attempt(fun () ->
                            if isAllowed name then style.removeProperty name |> ignore)
                failure |> Option.iter(fun error -> Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw())
            else
                let restore=ResizeArray<struct(string*string*string*bool)>(values)
                if not (isNull liveValues) && current() then
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
                            if isNull order then Int32.MaxValue
                            else
                                let rLeaves = CssPropertyMetadata.leafNamesReadOnly name
                                let mutable minPos = Int32.MaxValue
                                for li = 0 to rLeaves.Length - 1 do
                                    match order.TryGetValue rLeaves[li] with
                                    | true, v when v < minPos -> minPos <- v
                                    | _ -> ()
                                minPos
                        (position,index),value)
                    |> Array.sortBy fst
                for _,struct(name,value,priority,present) in ordered do
                    if current() then
                        attempt(fun () ->
                            if isAllowed name then
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
        let length=read(fun () -> style.length)
        if length = 0u then
            CssPropertySnapshot(false, CssPropertyMetadata.emptyValuesFor name, null, CssPropertyMetadata.counterpartSet name, true)
        else
            let leaves=CssPropertyMetadata.leafNamesReadOnly name
            let priority=read(fun () -> style.getPropertyPriority name)
            let order=Dictionary<string,int>(int length, StringComparer.Ordinal)
            let mutable index=0u
            while index<length do
                let itemName=read(fun () -> style.item index)
                for leaf in CssPropertyMetadata.leafNamesReadOnly itemName do order[leaf]<-int index
                index<-index+1u
            let values=leaves |> Array.map(fun leaf ->
                let value=read(fun () -> style.getPropertyValue leaf)
                let prio=read(fun () -> style.getPropertyPriority leaf)
                struct(leaf,value,prio,not(String.IsNullOrEmpty value) || order.ContainsKey leaf))
            let mutable anyPending = false
            for i = 0 to values.Length - 1 do
                if CssSnapshotValues.pending values[i] then anyPending <- true
            let values =
                if not anyPending then values
                else
                    let allowed = FrozenSet.ToFrozenSet(leaves, StringComparer.Ordinal)
                    CssSnapshotValues.serialized values allowed (fun () -> read(fun () -> style.cssText))
            CssPropertySnapshot((priority="important"),values,order,CssPropertyMetadata.counterpartSet name,false)
