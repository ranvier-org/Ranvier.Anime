namespace Ranvier.Anime

open System.Collections.Generic
open System.Collections.Frozen
open Ranvier.Dom

/// <summary>Expanded inline snapshot that restores leaves still equal to the layout's last write.</summary>
type internal LayoutStyleSnapshot(style:CSSStyleDeclaration,name:string) =
    let snapshot=CssPropertySnapshot.Capture(style,name,ignore)
    let leaves=CssPropertyMetadata.leafNames name
    let read() =
        leaves |> Array.map(fun leaf -> KeyValuePair(leaf,struct(style.getPropertyValue leaf,style.getPropertyPriority leaf)))
        |> FrozenDictionary.ToFrozenDictionary
    let mutable last=read()
    /// <summary>Records actual normalized leaf values and priorities after a layout write.</summary>
    member _.MarkWritten()=last<-read()
    /// <summary>Restores original leaf values, priorities and order while retaining concurrent leaf and logical edits.</summary>
    member _.Restore() =
        let allowed property =
            CssPropertyMetadata.leafNames property |> Array.forall(fun leaf ->
                match last.TryGetValue leaf with
                | true,struct(value,priority) -> style.getPropertyValue leaf=value && style.getPropertyPriority leaf=priority
                | _ -> true)
        let guarded=
            { new CSSStyleDeclaration with
                member _.getPropertyValue property=style.getPropertyValue property
                member _.getPropertyPriority property=style.getPropertyPriority property
                member _.setProperty(property,value,priority)=if allowed property then style.setProperty(property,value,?priority=priority)
                member _.removeProperty property=if allowed property then style.removeProperty property else style.getPropertyValue property
                member _.item index=style.item index
                member _.length=style.length
                member _.cssText with get()=style.cssText and set value=style.cssText<-value
                member _.parentRule=style.parentRule
                member _.cssRules=style.cssRules }
        snapshot.Restore(guarded,(fun () -> true))

