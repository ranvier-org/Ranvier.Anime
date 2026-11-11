namespace Ranvier.Anime

open System
open System.Collections.Generic
open System.Collections.Frozen
open Ranvier.Css

module private KeyframeEntries =
    let capture (entries:(string*Keyframe<'Target,'Value>) array) =
        if isNull entries then nullArg "values"
        let positions=Dictionary<string,int>(StringComparer.Ordinal)
        let values=ResizeArray<string*Keyframe<'Target,'Value>>()
        for name,frame in entries do
            if String.IsNullOrWhiteSpace name then invalidArg "values" "Property names must be nonempty."
            if isNull(box frame) then invalidArg "values" "Values are required."
            match positions.TryGetValue name with
            | true,index -> values[index]<-name,frame
            | _ -> positions.Add(name,values.Count);values.Add(name,frame)
        values.ToArray()
    let replace name frame entries = capture(Array.append entries [|name,frame|])
    let constants (values:(string*'Value) array) =
        if isNull values then nullArg "values"
        values |> Array.map(fun (name,value) -> name,Keyframe<'Target,'Value>.To value) |> capture

/// <summary>An immutable sparse destination row sharing timing, easing and component modifiers across properties.</summary>
/// <remarks>Missing properties hold their preceding raw endpoint. Timing callbacks run separately for each target/property.</remarks>
[<Sealed>]
type KeyframeRow<'Target,'Value> private(entries:(string*Keyframe<'Target,'Value>) array,timing:Keyframe<'Target,'Value>) =
    let lookup=(dict entries).ToFrozenDictionary(StringComparer.Ordinal)
    /// <summary>Replaces or appends a constant destination; duplicate names retain their first insertion position.</summary>
    member _.WithValue(name:string,value:'Value)=KeyframeRow(KeyframeEntries.replace name (Keyframe.To value) entries,timing)
    /// <summary>Replaces or appends a destination evaluated once per target/property during construction.</summary>
    member _.WithValueBy(name:string,read:KeyframeContext<'Target,'Value> -> 'Value)=KeyframeRow(KeyframeEntries.replace name (Keyframe.ToBy read) entries,timing)
    /// <summary>Sets an unsigned whole-millisecond row duration override.</summary>
    member _.WithDuration(value:uint)=KeyframeRow(entries,timing.WithDuration value)
    /// <summary>Sets a finite nonnegative precise row duration in milliseconds.</summary>
    member _.WithDurationPrecise(value:float)=KeyframeRow(entries,timing.WithDurationPrecise value)
    /// <summary>Computes unsigned milliseconds for each captured target/property.</summary>
    member _.WithDurationBy(read:KeyframeContext<'Target,'Value> -> uint)=KeyframeRow(entries,timing.WithDurationBy read)
    /// <summary>Computes finite nonnegative precise milliseconds for each captured target/property.</summary>
    member _.WithDurationPreciseBy(read:KeyframeContext<'Target,'Value> -> float)=KeyframeRow(entries,timing.WithDurationPreciseBy read)
    /// <summary>Sets a finite signed delay override in milliseconds.</summary>
    member _.WithDelay(value:float)=KeyframeRow(entries,timing.WithDelay value)
    /// <summary>Computes a finite signed delay for each captured target/property.</summary>
    member _.WithDelayBy(read:KeyframeContext<'Target,'Value> -> float)=KeyframeRow(entries,timing.WithDelayBy read)
    /// <summary>Sets row interpolation easing, including overshoot.</summary>
    member _.WithEase(value:float -> float)=KeyframeRow(entries,timing.WithEase value)
    /// <summary>Sets the scalar/color/compound modifier shared by the row.</summary>
    member _.WithModifier(value:float -> float)=KeyframeRow(entries,timing.WithModifier value)
    /// <summary>Captures property names in insertion order.</summary>
    member internal _.Names=entries |> Array.map fst
    /// <summary>Expands a present destination or sparse hold while retaining deferred row timing providers.</summary>
    member internal _.Frame(name:string)=
        let frame=match lookup.TryGetValue name with true,frame -> frame | _ -> Keyframe<'Target,'Value>.Hold()
        frame.WithTimingDefaults timing
    /// <summary>Captures constant named destinations; an empty array is a timing-only hold row.</summary>
    static member Create(values:(string*'Value) array) : KeyframeRow<'Target,'Value> =
        KeyframeRow(KeyframeEntries.constants values,Keyframe<'Target,'Value>.Hold())
    /// <summary>A diagnostic identifying the number of authored properties.</summary>
    override _.ToString()="KeyframeRow("+entries.Length.ToString(Globalization.CultureInfo.InvariantCulture)+")"

/// <summary>An immutable normalized-position key with named destinations and easing for the following segment.</summary>
/// <remarks>Positions are finite ratios; zero and one correspond to the beginning and nominal duration.</remarks>
[<Sealed>]
type KeyframeOffset<'Target,'Value> private(position:float,entries:(string*Keyframe<'Target,'Value>) array,ease:(float -> float) option) =
    /// <summary>Replaces or appends a constant destination.</summary>
    member _.WithValue(name:string,value:'Value)=KeyframeOffset(position,KeyframeEntries.replace name (Keyframe.To value) entries,ease)
    /// <summary>Replaces or appends a target-aware destination provider.</summary>
    member _.WithValueBy(name:string,read:KeyframeContext<'Target,'Value> -> 'Value)=KeyframeOffset(position,KeyframeEntries.replace name (Keyframe.ToBy read) entries,ease)
    /// <summary>Sets this key's easing for the following segment of every property it contains.</summary>
    member _.WithEase(value:float -> float)=
        if isNull(box value) then nullArg "value"
        KeyframeOffset(position,entries,Some value)
    /// <summary>The finite normalized position.</summary>
    member _.Position=position
    /// <summary>Captures named destination providers in insertion order.</summary>
    member internal _.Entries=Array.copy entries
    /// <summary>The optional easing transferred to the next key of each present property.</summary>
    member internal _.Ease=ease
    /// <summary>Captures constant named destinations at a finite normalized position.</summary>
    static member At(position:float,values:(string*'Value) array) : KeyframeOffset<'Target,'Value> =
        if not(Double.IsFinite position) then invalidArg "position" "Expected a finite normalized position."
        KeyframeOffset(position,KeyframeEntries.constants values,None)
    /// <summary>An invariant normalized-position diagnostic.</summary>
    override _.ToString()="KeyframeOffset("+position.ToString("R",Globalization.CultureInfo.InvariantCulture)+")"

/// <summary>An immutable sparse-row or normalized-offset sequence compiled across named properties.</summary>
[<Sealed>]
type KeyframeSequence<'Target,'Value> private(rows:KeyframeRow<'Target,'Value> array option,offsets:KeyframeOffset<'Target,'Value> array option) =
    /// <summary>Expands captured authoring to ordered property/frame sequences without reading targets or invoking callbacks.</summary>
    member internal _.Capture(duration:float) =
        if not(Double.IsFinite duration) || duration<0. then invalidArg "duration" "Expected finite nonnegative milliseconds."
        let names=ResizeArray<string>()
        let seen=HashSet<string>(StringComparer.Ordinal)
        match rows with
        | Some rows ->
            for row in rows do for name in row.Names do if seen.Add name then names.Add name
            names |> Seq.map(fun name -> name,rows |> Array.map(fun row -> row.Frame name)) |> Seq.toArray
        | None ->
            let properties=Dictionary<string,ResizeArray<Keyframe<'Target,'Value>>*float*(float -> float) option>(StringComparer.Ordinal)
            let ordered=offsets.Value |> Array.mapi(fun index row -> row.Position,index,row) |> Array.sortBy(fun (position,index,_) -> position,index)
            for position,_,row in ordered do
                for name,value in row.Entries do
                    let frames,elapsed,previousEase=
                        match properties.TryGetValue name with
                        | true,value -> value
                        | _ -> names.Add name;ResizeArray<Keyframe<'Target,'Value>>(),0.,None
                    let elapsedEnd=position*duration
                    let frameDuration=elapsedEnd-elapsed
                    let mutable frame=value.WithDurationPrecise(frameDuration)
                    if frames.Count=1 then frame<-frame.WithFromDestination frames[0]
                    previousEase |> Option.iter(fun ease -> frame<-frame.WithEase ease)
                    frames.Add frame
                    properties[name]<-frames,elapsed+frameDuration,row.Ease
            names |> Seq.map(fun name ->
                let frames,_,_=properties[name]
                let frames=frames.ToArray()
                let zero=frames.Length>0 && ((frames[0].Template(fun _ -> ValueInterpolation.Number 0.)).Duration=Some 0.)
                name,(if zero then frames[1..] else frames)) |> Seq.toArray
    /// <summary>Captures sparse duration rows; later mutations of the input array have no effect.</summary>
    static member Rows(values:KeyframeRow<'Target,'Value> array) : KeyframeSequence<'Target,'Value> =
        if isNull values then nullArg "values"
        if values |> Array.exists(fun value -> isNull(box value)) then invalidArg "values" "Rows are required."
        KeyframeSequence(Some(Array.copy values),None)
    /// <summary>Captures normalized offset rows, retaining stable ordering for equal positions.</summary>
    static member Offsets(values:KeyframeOffset<'Target,'Value> array) : KeyframeSequence<'Target,'Value> =
        if isNull values then nullArg "values"
        if values |> Array.exists(fun value -> isNull(box value)) then invalidArg "values" "Offset keys are required."
        KeyframeSequence(None,Some(Array.copy values))
    /// <summary>A diagnostic identifying the authoring form without formatting providers.</summary>
    override _.ToString()=if rows.IsSome then "KeyframeSequence(rows)" else "KeyframeSequence(offsets)"

/// <summary>Sparse row conveniences using named values or existing typed declarations.</summary>
[<RequireQualifiedAccess>]
module KeyframeRow =
    /// <summary>Captures constant named destinations.</summary>
    let values values=KeyframeRow<'Target,'Value>.Create values
    /// <summary>Uses each existing declaration's property name as its row key.</summary>
    let declarations (values:Declaration array) : KeyframeRow<'Target,Declaration> =
        if isNull values then nullArg "values"
        values |> Array.map(fun value -> Declaration.name value,value) |> KeyframeRow.Create

/// <summary>Normalized offset conveniences using named values or existing typed declarations.</summary>
[<RequireQualifiedAccess>]
module KeyframeOffset =
    /// <summary>Captures constant named destinations at a finite normalized position.</summary>
    let at position values=KeyframeOffset<'Target,'Value>.At(position,values)
    /// <summary>Uses existing declaration names at the supplied normalized position.</summary>
    let declarations position (values:Declaration array) : KeyframeOffset<'Target,Declaration> =
        if isNull values then nullArg "values"
        values |> Array.map(fun value -> Declaration.name value,value) |> fun values -> KeyframeOffset.At(position,values)
