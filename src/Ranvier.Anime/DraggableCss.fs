namespace Ranvier.Anime

open System
open Ranvier.Dom
open Ranvier.Css
open Ranvier.Css.Dom

/// <summary>A typed native declaration mapping for one draggable numeric axis.</summary>
/// <remarks>The author must retain one property name. Revert restores only that property's original inline value, importance and absence.</remarks>
type DraggableCssAxis(element:HTMLElement,initial:float,author:float -> Declaration) as this =
    let ownerAllowsWrite=ScopeRegistration.captureWriteGuard()
    let declaration=author initial
    let name=Declaration.name declaration
    let style=(element:>ElementCSSInlineStyle).style
    let mutable state=0
    let snapshot=CssPropertySnapshot.Capture(style,name,fun () -> if state<>0 || not(ownerAllowsWrite()) then raise(ObjectDisposedException(nameof DraggableCssAxis)))
    let mutable value=initial
    let property=NumericProperty((fun()->value),(fun next ->
        if state<>0 then raise(ObjectDisposedException(nameof DraggableCssAxis))
        let declaration=author next
        if Declaration.name declaration<>name then invalidArg "author" "An axis must retain its captured property name."
        if state<>0 || not(ownerAllowsWrite()) then raise(ObjectDisposedException(nameof DraggableCssAxis))
        StyleDeclaration.apply style (if snapshot.Important then Declaration.important declaration else declaration)
        value<-next))
    /// <summary>Explicit numeric accessor consumed by the existing Animatable-based controller.</summary>
    member _.Property=property
    /// <summary>Captured native declaration property name.</summary>
    member _.Name=name
    /// <summary>Restores the captured inline property after its controller has stopped.</summary>
    member internal _.Restore(current:unit -> bool) =
        if state=0 then
            state<-1
            try snapshot.Restore(style,fun () -> state=1 && current() && ownerAllowsWrite()) finally state<-2
    /// <summary>Restores the captured inline property after its controller has stopped.</summary>
    member _.Revert()=this.Restore(fun()->true)
    /// <summary>Stops future writes while retaining the current inline declaration.</summary>
    member _.Cancel()=state<-2
    interface IDisposable with member _.Dispose()=state<-2

