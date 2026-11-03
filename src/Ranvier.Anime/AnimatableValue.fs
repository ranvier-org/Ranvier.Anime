namespace Ranvier.Anime

open System
open Ranvier.Anime.ValueInterpolation

/// <summary>Cached scalar or component endpoints for guarded animatable retargeting on the scheduler thread.</summary>
/// <remarks>Sampling commits atomically after modifiers. Component getters return independent snapshots.</remarks>
type internal AnimatableValue private (initialFirst:Data,initialLast:Data,roundNumbers:bool,number:float,components:float array) =
    let mutable first=initialFirst
    let mutable last=initialLast
    let mutable cachedNumber=number
    let mutable cachedComponents=components
    let mutable generation=0L
    let scalar=first.Kind=ValueKind.Number || first.Kind=ValueKind.Unit
    let finite name value = if not(Double.IsFinite value) then invalidArg name "Expected finite input."
    let capture canWrite =
        let expected=generation
        fun () -> expected=generation && canWrite()
    let modify (modifier:float -> float) canWrite value =
        if not(canWrite()) then None
        else
            let result=modifier value
            if canWrite() then Some result else None
    /// <summary>Captures normalized endpoints and the original unmodified cache.</summary>
    new(original:Literal,unit:string option,roundNumbers:bool,convertUnit:(float -> string -> string -> float) option) =
        let first,last=normalize convertUnit original original (Text("+=0"+defaultArg unit ""))
        AnimatableValue(first,last,roundNumbers,first.Number,Array.copy first.Components)
    /// <summary>Copies endpoints and cached values for atomic multi-target preparation without repeating context conversion.</summary>
    member _.Fork() = AnimatableValue(first,last,roundNumbers,cachedNumber,Array.copy cachedComponents)
    /// <summary>The normalized endpoint category retained across retargets.</summary>
    member _.Kind=first.Kind
    /// <summary>The cached modified scalar before the getter applies its modifier.</summary>
    member _.CachedScalar=cachedNumber
    /// <summary>An independent copy of cached color or compound components.</summary>
    member _.ComponentValues=Array.copy cachedComponents
    /// <summary>Applies the getter modifier to the cached scalar while the operation remains current.</summary>
    member _.ScalarValue(modifier,canWrite) =
        if not scalar then invalidOp "The property has component values."
        modify modifier (capture canWrite) cachedNumber
    /// <summary>Composes an independent cached snapshot.</summary>
    /// <remarks>Scalar getters apply their modifier again; component getters retain cached values.</remarks>
    member _.Snapshot(modifier,canWrite) =
        let current=capture canWrite
        if scalar then
            modify modifier current cachedNumber |> Option.map(fun value -> compose(first,value,[||]))
        elif current() then Some(compose(last,0.,Array.copy cachedComponents))
        else None
    /// <summary>Samples cached endpoints; superseded modifiers discard the whole pending cache update.</summary>
    member _.Sample(progress,precision,modifier,canWrite) =
        let current=capture canWrite
        match sample(roundNumbers,first,last,progress,precision,modifier,current) with
        | Some(struct(number,output)) when current() ->
            cachedNumber<-number
            cachedComponents<-Array.copy output.Components
            generation<-generation+1L
            Some output
        | _ -> None
    /// <summary>Stages a finite scalar destination from the modified cache; sampling remains explicit.</summary>
    member _.RetargetScalar(value,modifier,canWrite) =
        finite "value" value
        if not scalar then invalidArg "value" "The property requires component destinations."
        let current=capture canWrite
        match modify modifier current cachedNumber with
        | Some valueFrom when current() ->
            first<-{first with Number=valueFrom}
            last<-{last with Number=value}
            generation<-generation+1L
            true
        | _ -> false
    /// <summary>Updates supplied finite component destinations; omitted endpoints persist and surplus values are ignored.</summary>
    member _.RetargetComponents(values:float array,modifier,canWrite) =
        if isNull values then nullArg "values"
        for value in values do finite "values" value
        if scalar then invalidArg "values" "The property requires a scalar destination."
        let destinations=Array.copy values
        let current=capture canWrite
        let fromValues=Array.copy first.Components
        let toValues=Array.copy last.Components
        let mutable alive=current()
        for index in 0..Math.Min(destinations.Length,cachedComponents.Length)-1 do
            if alive then
                match modify modifier current cachedComponents[index] with
                | Some value -> fromValues[index]<-value; toValues[index]<-destinations[index]
                | None -> alive<-false
        if alive && current() then
            first<-{first with Components=fromValues}
            last<-{last with Components=toValues}
            generation<-generation+1L
            true
        else false
