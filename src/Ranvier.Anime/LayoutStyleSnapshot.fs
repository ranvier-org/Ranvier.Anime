namespace Ranvier.Anime

open Ranvier.Dom

/// <summary>Expanded inline snapshot that restores leaves still equal to the layout's last write.</summary>
type internal LayoutStyleSnapshot(style:CSSStyleDeclaration,name:string) =
    static let alwaysTrue = fun () -> true
    let snapshot=CssPropertySnapshot.Capture(style,name,ignore)
    let leaves=CssPropertyMetadata.leafNamesReadOnly name
    let last=Array.zeroCreate<struct(string * string)> leaves.Length
    let readInto() =
        for i=0 to leaves.Length-1 do
            let leaf=leaves[i]
            last[i]<-struct(style.getPropertyValue leaf,style.getPropertyPriority leaf)
    do
        if snapshot.CapturedEmpty then
            for i = 0 to last.Length - 1 do
                last[i] <- struct("", "")
        else
            readInto()
    /// <summary>Records actual normalized leaf values and priorities after a layout write.</summary>
    member _.MarkWritten()=readInto()
    /// <summary>Restores original leaf values, priorities and order while retaining concurrent leaf and logical edits.</summary>
    member _.Restore() =
        snapshot.RestoreGuarded(style, alwaysTrue, ValueSome(struct(leaves, last)))

