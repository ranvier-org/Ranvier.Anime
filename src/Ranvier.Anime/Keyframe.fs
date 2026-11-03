namespace Ranvier.Anime

open System
open Ranvier.Anime.ValueInterpolation

/// <summary>Typed target, unsigned index/count and the preceding frame's cached endpoint during construction.</summary>
[<Sealed>]
type KeyframeContext<'Target,'Value> internal(target:'Target,index:uint,count:uint,previous:'Value option) =
    /// <summary>The current captured target.</summary>
    member _.Target=target
    /// <summary>Zero-based position in the captured target array.</summary>
    member _.Index=index
    /// <summary>Total number of captured targets.</summary>
    member _.Count=count
    /// <summary>The preceding authored frame's modified end cache, when a preceding frame exists.</summary>
    member _.PreviousValue=previous

type private FrameValue<'Context,'Value>(constant:'Value option,compute:('Context -> 'Value) option) =
    member _.Constant=constant
    member _.Read(context)=match compute with Some read -> read context | None -> constant.Value

/// <summary>An immutable typed frame with optional endpoints, timing overrides, easing and component modification.</summary>
/// <remarks>Callbacks run during construction in destination, source, duration, delay order; disposal suppresses later callbacks.</remarks>
[<Sealed>]
type Keyframe<'Target,'Value> private
    (first:FrameValue<KeyframeContext<'Target,'Value>,'Value> option,
     last:FrameValue<KeyframeContext<'Target,'Value>,'Value> option,
     duration:FrameValue<KeyframeContext<'Target,'Value>,float> option,
     delay:FrameValue<KeyframeContext<'Target,'Value>,float> option,
     ease:(float -> float) option,modifier:(float -> float) option) =
    let checkCallback name action=if isNull(box action) then invalidArg name "A callback is required."
    let timing value=if not(Double.IsFinite value) || value<0. then invalidArg "duration" "Expected finite nonnegative milliseconds."
    let finiteDelay value=if not(Double.IsFinite value) then invalidArg "delay" "Expected finite milliseconds."
    /// <summary>Sets an unsigned whole-millisecond duration override.</summary>
    member _.WithDuration(value:uint)=Keyframe(first,last,Some(FrameValue(Some(float value),None)),delay,ease,modifier)
    /// <summary>Sets a finite nonnegative precise duration override in milliseconds.</summary>
    member _.WithDurationPrecise(value:float)=timing value;Keyframe(first,last,Some(FrameValue(Some value,None)),delay,ease,modifier)
    /// <summary>Computes an unsigned duration once for each target during construction.</summary>
    member _.WithDurationBy(read:KeyframeContext<'Target,'Value> -> uint)=
        checkCallback "read" read
        Keyframe(first,last,Some(FrameValue(None,Some(fun context -> float(read context)))),delay,ease,modifier)
    /// <summary>Computes finite nonnegative precise milliseconds once for each target.</summary>
    member _.WithDurationPreciseBy(read:KeyframeContext<'Target,'Value> -> float)=
        checkCallback "read" read
        Keyframe(first,last,Some(FrameValue(None,Some read)),delay,ease,modifier)
    /// <summary>Sets a finite signed delay override in milliseconds.</summary>
    member _.WithDelay(value:float)=finiteDelay value;Keyframe(first,last,duration,Some(FrameValue(Some value,None)),ease,modifier)
    /// <summary>Computes a finite signed delay once for each target.</summary>
    member _.WithDelayBy(read:KeyframeContext<'Target,'Value> -> float)=
        checkCallback "read" read
        Keyframe(first,last,duration,Some(FrameValue(None,Some read)),ease,modifier)
    /// <summary>Sets per-frame interpolation easing, including overshoot.</summary>
    member _.WithEase(value:float -> float)=checkCallback "ease" value;Keyframe(first,last,duration,delay,Some value,modifier)
    /// <summary>Sets the callback applied to each interpolated scalar or color/compound component.</summary>
    member _.WithModifier(value:float -> float)=checkCallback "modifier" value;Keyframe(first,last,duration,delay,ease,Some value)
    /// <summary>Computes the source endpoint once for each target.</summary>
    member _.WithFromBy(read:KeyframeContext<'Target,'Value> -> 'Value)=
        checkCallback "read" read
        Keyframe(Some(FrameValue(None,Some read)),last,duration,delay,ease,modifier)
    /// <summary>Sets an explicit source endpoint.</summary>
    member _.WithFrom(value:'Value)=Keyframe(Some(FrameValue(Some value,None)),last,duration,delay,ease,modifier)
    member private _.DurationInput=duration
    member private _.DelayInput=delay
    member private _.EaseInput=ease
    member private _.ModifierInput=modifier
    member private _.DestinationInput=last
    /// <summary>Shares row timing providers without changing endpoint capture or callback evaluation order.</summary>
    member internal _.WithTimingDefaults(defaults:Keyframe<'Target,'Value>) =
        Keyframe(first,last,Option.orElse defaults.DurationInput duration,Option.orElse defaults.DelayInput delay,
                 Option.orElse defaults.EaseInput ease,Option.orElse defaults.ModifierInput modifier)
    /// <summary>Uses another offset key's destination provider as the explicit source endpoint.</summary>
    member internal _.WithFromDestination(value:Keyframe<'Target,'Value>) =
        Keyframe(value.DestinationInput,last,duration,delay,ease,modifier)
    /// <summary>Captures constant inputs for validation before target reads or callback effects.</summary>
    member internal _.Template(convert:'Value -> Literal) : FrameInput =
        {From=first |> Option.bind _.Constant |> Option.map convert;To=last |> Option.bind _.Constant |> Option.map convert
         Duration=duration |> Option.map(fun value -> defaultArg value.Constant 0.);Delay=delay |> Option.map(fun value -> defaultArg value.Constant 0.);Ease=ease;Modifier=modifier}
    /// <summary>Evaluates callbacks under a construction guard and returns no frame after interruption.</summary>
    member internal _.Resolve(context:KeyframeContext<'Target,'Value>,convert:'Value -> Literal,current:unit -> bool) =
        let read provider =
            match provider with
            | Some (provider:FrameValue<KeyframeContext<'Target,'Value>,'T>) when current() ->
                let value=provider.Read context
                if current() then Some value else None
            | _ -> None
        let destination=read last
        let source=read first
        let milliseconds=read duration
        let wait=read delay
        if current() then Some {From=source |> Option.map convert;To=destination |> Option.map convert
                                Duration=milliseconds;Delay=wait;Ease=ease;Modifier=modifier}
        else None
    /// <summary>Creates a constant destination frame; missing source inherits the preceding raw endpoint.</summary>
    static member To(value:'Value) : Keyframe<'Target,'Value> =Keyframe(None,Some(FrameValue(Some value,None)),None,None,None,None)
    /// <summary>Creates a target-aware destination frame evaluated once during construction.</summary>
    static member ToBy(read:KeyframeContext<'Target,'Value> -> 'Value)=
        if isNull(box read) then nullArg "read"
        Keyframe(None,Some(FrameValue(None,Some read)),None,None,None,None)
    /// <summary>Creates a hold frame retaining the preceding raw endpoint.</summary>
    static member Hold() : Keyframe<'Target,'Value> =Keyframe(None,None,None,None,None,None)
    /// <summary>A diagnostic identifying this frame's authored endpoint shape.</summary>
    override _.ToString()=if first.IsSome then "Keyframe(from/to)" elif last.IsSome then "Keyframe(to)" else "Keyframe(hold)"

/// <summary>Typed keyframe factories; the binding infers the target type for constant values.</summary>
[<RequireQualifiedAccess>]
module Keyframe =
    /// <summary>Creates a constant destination frame.</summary>
    let toValue value=Keyframe<'Target,'Value>.To value
    /// <summary>Creates a frame with explicit constant endpoints.</summary>
    let between first last=(Keyframe<'Target,'Value>.To last).WithFrom first
    /// <summary>Creates a destination callback with a statically typed target context.</summary>
    let toBy read=Keyframe<'Target,'Value>.ToBy read
    /// <summary>Creates a hold frame.</summary>
    let hold<'Target,'Value> ()=Keyframe<'Target,'Value>.Hold()
    /// <summary>Captures raw values with source one-value, two-value and longer-array meanings.</summary>
    let values (input:'Value array) : Keyframe<'Target,'Value> array =
        if isNull input then nullArg "input"
        let input=Array.copy input
        if input.Length<2 then input |> Array.map toValue
        else Array.init(input.Length-1)(fun index -> if index=0 then between input[0] input[1] else toValue input[index+1])
