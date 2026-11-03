namespace Ranvier.Anime

open System
open Ranvier.Anime.ValueInterpolation

/// <summary>Arithmetic applied to the preceding raw endpoint during keyframe construction.</summary>
/// <remarks>Relative arithmetic precedes scalar unit conversion.</remarks>
type RelativeOperation =
    /// <summary>Adds the operand, matching += authoring.</summary>
    | Add = 0
    /// <summary>Subtracts the operand, matching -= authoring.</summary>
    | Subtract = 1
    /// <summary>Multiplies by the operand, matching *= authoring.</summary>
    | Multiply = 2

module private RelativeEndpoint =
    let symbol operation =
        match operation with
        | RelativeOperation.Add -> '+'
        | RelativeOperation.Subtract -> '-'
        | RelativeOperation.Multiply -> '*'
        | _ -> invalidArg "operation" "Expected Add, Subtract or Multiply."

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

type private FrameValue<'Context,'Value>(constant:'Value option,compute:('Context -> 'Value) option,?operation:char) =
    member _.Constant=constant
    member _.Read(context)=match compute with Some read -> read context | None -> constant.Value
    member _.Convert(convert:'Value -> Literal,value:'Value) =
        let literal=convert value
        match operation with
        | None -> literal
        | Some operation ->
            let operand=match literal with Number value -> number value | Text value -> value
            Text(string operation+"="+operand)

/// <summary>An immutable typed frame with optional endpoints, timing overrides, easing and component modification.</summary>
/// <remarks>Callbacks run during construction in destination, source, duration, delay order; disposal suppresses later callbacks.</remarks>
[<Sealed>]
type Keyframe<'Target,'Value> private
    (first:FrameValue<KeyframeContext<'Target,'Value>,'Value> option,
     last:FrameValue<KeyframeContext<'Target,'Value>,'Value> option,
     duration:FrameValue<KeyframeContext<'Target,'Value>,float> option,
     delay:FrameValue<KeyframeContext<'Target,'Value>,float> option,
     ease:(float -> float) option,modifier:(float -> float) option,
     computed:(KeyframeContext<'Target,'Value> -> Keyframe<'Target,'Value>) option,
     pair:(KeyframeContext<'Target,'Value> -> 'Value*'Value) option) =
    let checkCallback name action=if isNull(box action) then invalidArg name "A callback is required."
    let timing value=if not(Double.IsFinite value) || value<0. then invalidArg "duration" "Expected finite nonnegative milliseconds."
    let finiteDelay value=if not(Double.IsFinite value) then invalidArg "delay" "Expected finite milliseconds."
    /// <summary>Sets an unsigned whole-millisecond duration override.</summary>
    member _.WithDuration(value:uint)=Keyframe<'Target,'Value>(first,last,Some(FrameValue(Some(float value),None)),delay,ease,modifier,computed,pair)
    /// <summary>Sets a finite nonnegative precise duration override in milliseconds.</summary>
    member _.WithDurationPrecise(value:float)=timing value;Keyframe<'Target,'Value>(first,last,Some(FrameValue(Some value,None)),delay,ease,modifier,computed,pair)
    /// <summary>Computes an unsigned duration once for each target during construction.</summary>
    member _.WithDurationBy(read:KeyframeContext<'Target,'Value> -> uint)=
        checkCallback "read" read
        Keyframe<'Target,'Value>(first,last,Some(FrameValue(None,Some(fun context -> float(read context)))),delay,ease,modifier,computed,pair)
    /// <summary>Computes finite nonnegative precise milliseconds once for each target.</summary>
    member _.WithDurationPreciseBy(read:KeyframeContext<'Target,'Value> -> float)=
        checkCallback "read" read
        Keyframe<'Target,'Value>(first,last,Some(FrameValue(None,Some read)),delay,ease,modifier,computed,pair)
    /// <summary>Sets a finite signed delay override in milliseconds.</summary>
    member _.WithDelay(value:float)=finiteDelay value;Keyframe<'Target,'Value>(first,last,duration,Some(FrameValue(Some value,None)),ease,modifier,computed,pair)
    /// <summary>Computes a finite signed delay once for each target.</summary>
    member _.WithDelayBy(read:KeyframeContext<'Target,'Value> -> float)=
        checkCallback "read" read
        Keyframe<'Target,'Value>(first,last,duration,Some(FrameValue(None,Some read)),ease,modifier,computed,pair)
    /// <summary>Sets per-frame interpolation easing, including overshoot.</summary>
    member _.WithEase(value:float -> float)=checkCallback "ease" value;Keyframe<'Target,'Value>(first,last,duration,delay,Some value,modifier,computed,pair)
    /// <summary>Sets the callback applied to each interpolated scalar or color/compound component.</summary>
    member _.WithModifier(value:float -> float)=checkCallback "modifier" value;Keyframe<'Target,'Value>(first,last,duration,delay,ease,Some value,computed,pair)
    /// <summary>Computes the source endpoint once for each target.</summary>
    member _.WithFromBy(read:KeyframeContext<'Target,'Value> -> 'Value)=
        checkCallback "read" read
        Keyframe<'Target,'Value>(Some(FrameValue(None,Some read)),last,duration,delay,ease,modifier,computed,pair)
    /// <summary>Sets an explicit source endpoint.</summary>
    member _.WithFrom(value:'Value)=Keyframe<'Target,'Value>(Some(FrameValue(Some value,None)),last,duration,delay,ease,modifier,computed,pair)
    /// <summary>Sets a source operand relative to the preceding raw destination; CSS units come from the typed declaration.</summary>
    member _.WithFromRelative(operation:RelativeOperation,value:'Value) =
        let operation=RelativeEndpoint.symbol operation
        Keyframe<'Target,'Value>(Some(FrameValue(Some value,None,operation=operation)),last,duration,delay,ease,modifier,computed,pair)
    /// <summary>Computes a relative source operand in the ordinary source callback phase.</summary>
    member _.WithFromRelativeBy(operation:RelativeOperation,read:KeyframeContext<'Target,'Value> -> 'Value) =
        let operation=RelativeEndpoint.symbol operation
        checkCallback "read" read
        Keyframe<'Target,'Value>(Some(FrameValue(None,Some read,operation=operation)),last,duration,delay,ease,modifier,computed,pair)
    member private _.DurationInput=duration
    member private _.DelayInput=delay
    member private _.EaseInput=ease
    member private _.ModifierInput=modifier
    member private _.DestinationInput=last
    /// <summary>Shares row timing providers without changing endpoint capture or callback evaluation order.</summary>
    member internal _.WithTimingDefaults(defaults:Keyframe<'Target,'Value>) =
        Keyframe<'Target,'Value>(first,last,Option.orElse defaults.DurationInput duration,Option.orElse defaults.DelayInput delay,
                 Option.orElse defaults.EaseInput ease,Option.orElse defaults.ModifierInput modifier,computed,pair)
    /// <summary>Uses another offset key's destination provider as the explicit source endpoint.</summary>
    member internal _.WithFromDestination(value:Keyframe<'Target,'Value>) =
        Keyframe<'Target,'Value>(value.DestinationInput,last,duration,delay,ease,modifier,computed,pair)
    /// <summary>Captures constant inputs for validation before target reads or callback effects.</summary>
    member internal _.Template(convert:'Value -> Literal) : FrameInput =
        let constant provider = provider |> Option.bind(fun (value:FrameValue<_,_>) -> value.Constant |> Option.map(fun input -> value.Convert(convert,input)))
        if computed.IsSome then
            {From=None;To=None;Duration=Some 0.;Delay=Some 0.;Ease=None;Modifier=None}
        else
            {From=(if pair.IsSome then None else constant first)
             To=constant last
             Duration=duration |> Option.map(fun value -> defaultArg value.Constant 0.);Delay=delay |> Option.map(fun value -> defaultArg value.Constant 0.);Ease=ease;Modifier=modifier}
    /// <summary>Evaluates callbacks under a construction guard and returns no frame after interruption.</summary>
    member internal _.Resolve(context:KeyframeContext<'Target,'Value>,convert:'Value -> Literal,current:unit -> bool) : FrameInput option =
        let read provider =
            match provider with
            | Some (provider:FrameValue<KeyframeContext<'Target,'Value>,'T>) when current() ->
                let value=provider.Read context
                if current() then Some value else None
            | _ -> None
        let endpoint (provider:FrameValue<KeyframeContext<'Target,'Value>,'Value> option) value =
            match provider with Some provider -> provider.Convert(convert,value) | None -> convert value
        match computed with
        | Some compute when current() ->
            let frame=compute context
            if not(current()) then None
            else
                if isNull(box frame) then invalidArg "read" "A computed frame is required."
                if not(frame.IsComputedResult) then invalidArg "read" "Computed frames require a constant destination without nested frame providers."
                frame.Resolve(context,convert,current)
        | Some _ -> None
        | None ->
            let endpoints=
                match pair with
                | Some compute when current() ->
                    let endpoints=compute context
                    if current() then Some endpoints else None
                | _ -> None
            let destination=if pair.IsSome then endpoints |> Option.map snd else read last
            let source=read first
            let milliseconds=read duration
            let wait=read delay
            if current() then
                let source=match endpoints with Some(first,_) -> Some first | None -> source
                Some {From=source |> Option.map(if endpoints.IsSome then convert else endpoint first)
                      To=destination |> Option.map(if pair.IsSome then convert else endpoint last)
                      Duration=milliseconds;Delay=wait;Ease=ease;Modifier=modifier}
            else None
    member private _.IsComputedResult=computed.IsNone && pair.IsNone && (last |> Option.exists(fun value -> value.Constant.IsSome))
    /// <summary>Computes a complete frame once per target during construction.</summary>
    /// <remarks>The result replaces outer frame settings and requires a constant destination. Source and timing providers remain deferred.
    /// Nested computed frames and destination providers in the result raise ArgumentException.</remarks>
    static member Compute(read:KeyframeContext<'Target,'Value> -> Keyframe<'Target,'Value>) : Keyframe<'Target,'Value> =
        if isNull(box read) then nullArg "read"
        Keyframe<'Target,'Value>(None,None,None,None,None,None,Some read,None)
    /// <summary>Computes one source/destination pair per target, retaining outer timing and easing.</summary>
    /// <remarks>The pair callback precedes outer source and timing providers; its source overrides the outer source result.</remarks>
    static member BetweenBy(read:KeyframeContext<'Target,'Value> -> 'Value*'Value) : Keyframe<'Target,'Value> =
        if isNull(box read) then nullArg "read"
        Keyframe<'Target,'Value>(None,None,None,None,None,None,None,Some read)
    /// <summary>Creates a constant destination frame; missing source inherits the preceding raw endpoint.</summary>
    static member To(value:'Value) : Keyframe<'Target,'Value> =Keyframe<'Target,'Value>(None,Some(FrameValue(Some value,None)),None,None,None,None,None,None)
    /// <summary>Creates a destination operand relative to the preceding raw endpoint during construction.</summary>
    static member ToRelative(operation:RelativeOperation,value:'Value) : Keyframe<'Target,'Value> =
        let operation=RelativeEndpoint.symbol operation
        Keyframe<'Target,'Value>(None,Some(FrameValue(Some value,None,operation=operation)),None,None,None,None,None,None)
    /// <summary>Computes a relative destination operand in the ordinary destination callback phase.</summary>
    static member ToRelativeBy(operation:RelativeOperation,read:KeyframeContext<'Target,'Value> -> 'Value) : Keyframe<'Target,'Value> =
        let operation=RelativeEndpoint.symbol operation
        if isNull(box read) then nullArg "read"
        Keyframe<'Target,'Value>(None,Some(FrameValue(None,Some read,operation=operation)),None,None,None,None,None,None)
    /// <summary>Creates a target-aware destination frame evaluated once during construction.</summary>
    static member ToBy(read:KeyframeContext<'Target,'Value> -> 'Value)=
        if isNull(box read) then nullArg "read"
        Keyframe<'Target,'Value>(None,Some(FrameValue(None,Some read)),None,None,None,None,None,None)
    /// <summary>Creates a hold frame retaining the preceding raw endpoint.</summary>
    static member Hold() : Keyframe<'Target,'Value> =Keyframe<'Target,'Value>(None,None,None,None,None,None,None,None)
    /// <summary>A diagnostic identifying this frame's authored endpoint shape.</summary>
    override _.ToString()=if computed.IsSome then "Keyframe(computed)" elif pair.IsSome then "Keyframe(pair)" elif first.IsSome then "Keyframe(from/to)" elif last.IsSome then "Keyframe(to)" else "Keyframe(hold)"

/// <summary>Typed keyframe factories; the binding infers the target type for constant values.</summary>
[<RequireQualifiedAccess>]
module Keyframe =
    /// <summary>Creates a constant destination frame.</summary>
    let toValue value=Keyframe<'Target,'Value>.To value
    /// <summary>Creates a typed relative destination; numeric operands use floats and CSS operands use existing declarations.</summary>
    let toRelative operation value=Keyframe<'Target,'Value>.ToRelative(operation,value)
    /// <summary>Computes a typed relative destination during construction.</summary>
    let toRelativeBy operation read=Keyframe<'Target,'Value>.ToRelativeBy(operation,read)
    /// <summary>Creates a frame with explicit constant endpoints.</summary>
    let between first last=(Keyframe<'Target,'Value>.To last).WithFrom first
    /// <summary>Creates a destination callback with a statically typed target context.</summary>
    let toBy read=Keyframe<'Target,'Value>.ToBy read
    /// <summary>Computes a complete typed frame once per target during construction.</summary>
    let compute read=Keyframe<'Target,'Value>.Compute read
    /// <summary>Computes one source/destination pair while retaining outer frame settings.</summary>
    let betweenBy read=Keyframe<'Target,'Value>.BetweenBy read
    /// <summary>Creates a hold frame.</summary>
    let hold<'Target,'Value> ()=Keyframe<'Target,'Value>.Hold()
    /// <summary>Captures raw values with source one-value, two-value and longer-array meanings.</summary>
    let values (input:'Value array) : Keyframe<'Target,'Value> array =
        if isNull input then nullArg "input"
        let input=Array.copy input
        if input.Length<2 then input |> Array.map toValue
        else Array.init(input.Length-1)(fun index -> if index=0 then between input[0] input[1] else toValue input[index+1])
