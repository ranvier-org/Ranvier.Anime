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
    // Scalar endpoints, kept out of the Data records so a retarget copies no record.
    let mutable firstNumber=initialFirst.Number
    let mutable lastNumber=initialLast.Number
    let mutable generation=0L
    let scalar=first.Kind=ValueKind.Number || first.Kind=ValueKind.Unit
    let finite name value = if not(Double.IsFinite value) then invalidArg name "Expected finite input."
    let capture canWrite =
        let expected=generation
        fun () -> expected=generation && canWrite()
    // The hot paths' guard: one closure for the value's life over a snapshot that each call saves and restores, so a
    // nested call (a modifier that retargets this value) still invalidates the outer one. Guards are only checked
    // while their call runs.
    let mutable guardExpected= -1L
    let mutable guardInner:unit -> bool=Unchecked.defaultof<_>
    let guard=Closure.keep (fun () -> guardExpected=generation && guardInner())
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
    member _.Fork() =
        let fork=AnimatableValue(first,last,roundNumbers,cachedNumber,Array.copy cachedComponents)
        fork.SetNumbers(firstNumber,lastNumber)
        fork
    member internal _.SetNumbers(fromNumber,toNumber) =
        firstNumber<-fromNumber
        lastNumber<-toNumber
    /// <summary>The normalized endpoint category retained across retargets.</summary>
    member _.Kind=first.Kind
    /// <summary>Copies captured unit and component-template metadata for typed adapter validation.</summary>
    member _.Metadata={first with Number=firstNumber;Components=Array.copy first.Components;Strings=Array.copy first.Strings}
    /// <summary>The cached modified scalar before the getter applies its modifier.</summary>
    member _.CachedScalar=cachedNumber
    /// <summary>An independent copy of cached color or compound components.</summary>
    member _.ComponentValues=Array.copy cachedComponents
    /// <summary>Applies the getter modifier to the cached scalar while the operation remains current.</summary>
    member _.ScalarValue(modifier,canWrite) =
        if not scalar then invalidOp "The property has component values."
        modify modifier (capture canWrite) cachedNumber
    /// <summary>The getter's scalar without an option or a guard closure per read, as <c>ScalarValue</c>.</summary>
    member _.TryScalarValue(modifier:float -> float,canWrite:unit -> bool) : float voption =
        if not scalar then invalidOp "The property has component values."
        let savedExpected=guardExpected
        let savedInner=guardInner
        guardExpected<-generation
        guardInner<-canWrite
        try
            if not(guard()) then ValueNone
            else
                let result=modifier cachedNumber
                if guard() then ValueSome result else ValueNone
        finally
            guardExpected<-savedExpected
            guardInner<-savedInner
    /// <summary>Composes an independent cached snapshot.</summary>
    /// <remarks>Scalar getters apply their modifier again; component getters retain cached values.</remarks>
    member _.Snapshot(modifier,canWrite) =
        let current=capture canWrite
        if scalar then
            modify modifier current cachedNumber |> Option.map(fun value -> compose(first,value,[||]))
        elif current() then Some(compose(last,0.,Array.copy cachedComponents))
        else None
    /// <summary>Samples cached endpoints into an output, or null when superseded; superseded modifiers discard the cache update.</summary>
    member _.TrySample(progress,precision,modifier,canWrite:unit -> bool) : Output =
        let savedExpected=guardExpected
        let savedInner=guardInner
        guardExpected<-generation
        guardInner<-canWrite
        try
            let sampled=
                if scalar then sampleScalar(roundNumbers,first,firstNumber,lastNumber,progress,precision,modifier,guard)
                else sampleOutput(roundNumbers,first,last,progress,precision,modifier,guard)
            if not(isNull sampled) && guard() then
                cachedNumber<-sampled.Number
                cachedComponents<-(if sampled.Components.Length=0 then sampled.Components else Array.copy sampled.Components)
                generation<-generation+1L
                sampled
            else null
        finally
            guardExpected<-savedExpected
            guardInner<-savedInner
    /// <summary>Samples cached endpoints; superseded modifiers discard the whole pending cache update.</summary>
    member this.Sample(progress,precision,modifier,canWrite) =
        match this.TrySample(progress,precision,modifier,canWrite) with
        | null -> None
        | output -> Some output
    /// <summary>Stages a finite scalar destination from the modified cache; sampling remains explicit.</summary>
    member _.RetargetScalar(value,modifier,canWrite:unit -> bool) =
        finite "value" value
        if not scalar then invalidArg "value" "The property requires component destinations."
        let savedExpected=guardExpected
        let savedInner=guardInner
        guardExpected<-generation
        guardInner<-canWrite
        try
            if guard() then
                let valueFrom=modifier cachedNumber
                if guard() then
                    firstNumber<-valueFrom
                    lastNumber<-value
                    generation<-generation+1L
                    true
                else false
            else false
        finally
            guardExpected<-savedExpected
            guardInner<-savedInner
    member internal _.State:struct(Data*Data*float*float array)=struct(first,last,cachedNumber,cachedComponents)
    member internal _.Numbers=struct(firstNumber,lastNumber)
    /// <summary>Copies another value's endpoints and caches into this one, reusing it as a staging fork.</summary>
    member internal _.CopyFrom(source:AnimatableValue) =
        let struct(sourceFirst,sourceLast,sourceNumber,sourceComponents)=source.State
        first<-sourceFirst
        last<-sourceLast
        cachedNumber<-sourceNumber
        cachedComponents<-(if sourceComponents.Length=0 then sourceComponents else Array.copy sourceComponents)
        let struct(sourceFrom,sourceTo)=source.Numbers
        firstNumber<-sourceFrom
        lastNumber<-sourceTo
        generation<-generation+1L
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
