namespace Ranvier.Anime

open Ranvier.Dom

/// <summary>Expanded inline snapshot that restores leaves still equal to the layout's last write.</summary>
type internal LayoutStyleSnapshot(style:CSSStyleDeclaration,name:string) =
    let snapshot=CssPropertySnapshot.Capture(style,name,ignore)
    let leaves=CssPropertyMetadata.leafNamesReadOnly name
    let last=Array.zeroCreate<struct(string * string)> leaves.Length
    let readInto() =
        for i=0 to leaves.Length-1 do
            let leaf=leaves[i]
            last[i]<-struct(style.getPropertyValue leaf,style.getPropertyPriority leaf)
    do readInto()
    let findLeafIndex (leaf:string) =
        let mutable idx = -1
        let mutable i = 0
        while i < leaves.Length && idx < 0 do
            if leaves[i] = leaf then idx <- i
            i <- i + 1
        idx
    /// <summary>Records actual normalized leaf values and priorities after a layout write.</summary>
    member _.MarkWritten()=readInto()
    /// <summary>Restores original leaf values, priorities and order while retaining concurrent leaf and logical edits.</summary>
    member _.Restore() =
        let allowed property =
            let propLeaves=CssPropertyMetadata.leafNamesReadOnly property
            let mutable ok=true
            let mutable j=0
            while j < propLeaves.Length && ok do
                let leaf=propLeaves[j]
                let idx=findLeafIndex leaf
                if idx >= 0 then
                    let struct(value,priority)=last[idx]
                    ok <- style.getPropertyValue leaf=value && style.getPropertyPriority leaf=priority
                j <- j + 1
            ok
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

