namespace Ranvier.Anime

open System
open System.Collections.Generic
open System.Collections.Frozen
open System.Globalization
open Ranvier.Dom

/// <summary>Scalar source transform names and their default units.</summary>
module internal DraggableTransformValue =
    let private units =
        [ "perspective","px";"translateX","px";"translateY","px";"translateZ","px"
          "rotate","deg";"rotateX","deg";"rotateY","deg";"rotateZ","deg"
          "scale","";"scaleX","";"scaleY","";"scaleZ","";"skew","deg";"skewX","deg";"skewY","deg" ]
        |> dict
        |> fun values -> values.ToFrozenDictionary(StringComparer.Ordinal)
    /// <summary>Reads one source scalar transform and retains its authored unit or property default.</summary>
    let scalar cache full name =
        let fallback=match units.TryGetValue name with true,value->value | _->invalidArg "name" "Expected a scalar source transform property."
        let text=TransformCache.read cache full name
        let data=ValueInterpolation.decompose(ValueInterpolation.Text text)
        if data.Kind<>ValueInterpolation.ValueKind.Number && data.Kind<>ValueInterpolation.ValueKind.Unit then
            raise(NotSupportedException("A draggable transform requires a scalar inline value: "+text))
        struct(data.Number,if String.IsNullOrEmpty data.Unit then fallback else data.Unit)

/// <summary>One captured inline transform cache with stable source scalar property identities.</summary>
/// <remarks>During an engine frame the axes' writes are staged and the transform is set once when the frame ends.</remarks>
type internal DraggableTransformTarget(element:HTMLElement) as this =
    let ownerAllowsWrite=ScopeRegistration.captureWriteGuard()
    let style=(element:>ElementCSSInlineStyle).style
    let mutable state=0
    let live ()=if state<>0 || not(ownerAllowsWrite()) then raise(ObjectDisposedException(nameof DraggableTransformTarget))
    let snapshot=CssPropertySnapshot.Capture(style,"transform",live)
    let struct(initial,full)=TransformCache.parse(style.getPropertyValue "transform")
    let cache=Dictionary<string,string>(initial)
    let properties=Dictionary<string,NumericProperty>(StringComparer.Ordinal)
    let mutable written:string=null
    let mutable pending=false
    let priority=TransformCache.priorityOption(if snapshot.Important then "important" else "")
    let apply () =
        pending<-false
        let text=TransformCache.buildDeclaration cache null
        style.setProperty("transform",text,?priority=priority)
        written<-style.getPropertyValue "transform"
    let property name =
        match properties.TryGetValue name with
        | true,value -> value
        | _ ->
            let struct(initialValue,unit)=DraggableTransformValue.scalar initial full name
            let mutable current=initialValue
            let write (value:float) =
                live()
                // The element's other transform components belong to whoever wrote them last, such as an animation
                // started after this target; anime's draggable shares the element's transform cache with them.
                // An unchanged declaration is the one this target wrote last, so the cache already matches it. A write
                // already staged this frame was checked by the first axis.
                if not pending then
                    let inlineText=style.getPropertyValue "transform"
                    if not(String.Equals(inlineText,written,StringComparison.Ordinal)) then
                        let struct(inlineNow,_)=TransformCache.parse inlineText
                        let owned=[| for KeyValue(key,text) in cache do if properties.ContainsKey key then struct(key,text) |]
                        cache.Clear()
                        for KeyValue(key,text) in inlineNow do cache[key]<-text
                        for struct(key,text) in owned do cache[key]<-text
                cache[name]<-TransformCache.numberText value unit
                current<-value
                if DeferredWrites.Deferring then
                    if not pending then
                        pending<-true
                        DeferredWrites.Add this
                else apply()
            let value=NumericProperty((fun()->current),write)
            properties.Add(name,value)
            value
    /// <summary>Returns the shared identity of a scalar source transform property.</summary>
    member _.Property(name)=property name
    /// <summary>Source translateX magnitude in its captured scalar unit.</summary>
    member _.X=property "translateX"
    /// <summary>Source translateY magnitude in its captured scalar unit.</summary>
    member _.Y=property "translateY"
    /// <summary>Restores only the captured transform property while the facade permits writes.</summary>
    member _.Restore(current:unit -> bool) =
        if state=0 then
            pending<-false
            state<-1
            try snapshot.Restore(style,fun()->state=1 && current() && ownerAllowsWrite()) finally state<-2
    /// <summary>Ends axis writes while retaining the current inline transform.</summary>
    member _.Cancel() =
        if pending && state=0 && ownerAllowsWrite() then apply()
        pending<-false
        state<-2
    interface IDeferredWrite with
        member _.Flush() = if pending && state=0 && ownerAllowsWrite() then apply() else pending<-false

