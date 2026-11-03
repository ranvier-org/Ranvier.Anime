namespace Ranvier.Anime

open System
open System.Collections.Generic
open Ranvier.Anime.ValueInterpolation

/// <summary>Optional raw endpoints and per-frame overrides before keyframe compilation.</summary>
[<NoEquality; NoComparison; Diagnostics.DebuggerDisplay("{ToString(),nq}")>]
type internal FrameInput =
    { From:Literal option; To:Literal option; Duration:float option; Delay:float option
      Ease:(float -> float) option; Modifier:(float -> float) option }
    /// <summary>A hold frame inheriting timing, easing and modifier defaults.</summary>
    static member Default={From=None;To=None;Duration=None;Delay=None;Ease=None;Modifier=None}
    /// <summary>A diagnostic identifying the endpoint syntax without formatting callbacks.</summary>
    override this.ToString()=
        let value=match this.To with Some value -> value.ToString() | None -> "hold"
        "FrameInput("+value+")"

/// <summary>Construction defaults; duration is divided by the number of frames in each property.</summary>
[<NoEquality; NoComparison; Diagnostics.DebuggerDisplay("{ToString(),nq}")>]
type internal FrameDefaults =
    { Duration:float; Delay:float; Offset:float; Ease:float -> float; Modifier:float -> float }
    /// <summary>1000 milliseconds, zero delay/offset, source out-quad easing and identity modification.</summary>
    static member Default={Duration=1000.;Delay=0.;Offset=0.;Ease=Ease.named "outQuad";Modifier=id}
    /// <summary>An invariant duration diagnostic.</summary>
    override this.ToString()="FrameDefaults("+this.Duration.ToString("R",Globalization.CultureInfo.InvariantCulture)+")"

/// <summary>A sparse duration row with named values and shared per-frame overrides.</summary>
[<NoEquality; NoComparison; Diagnostics.DebuggerDisplay("{ToString(),nq}")>]
type internal FrameRow =
    { Values:(string*Literal) array; Frame:FrameInput }
    /// <summary>A diagnostic identifying the number of authored values.</summary>
    override this.ToString()="FrameRow("+this.Values.Length.ToString(Globalization.CultureInfo.InvariantCulture)+")"

/// <summary>A normalized offset row; its easing applies to the following segment for each present property.</summary>
[<NoEquality; NoComparison; Diagnostics.DebuggerDisplay("{ToString(),nq}")>]
type internal FrameOffset =
    { Offset:float; Values:(string*Literal) array; Ease:(float -> float) option }
    /// <summary>An invariant normalized-offset diagnostic.</summary>
    override this.ToString()="FrameOffset("+this.Offset.ToString("R",Globalization.CultureInfo.InvariantCulture)+")"

/// <summary>A captured property and normalized frame sequence, with optional contextual scalar-unit conversion.</summary>
[<NoEquality; NoComparison; Diagnostics.DebuggerDisplay("{ToString(),nq}")>]
type internal FrameProperty =
    { Target:int; Name:string; Original:Literal; Frames:FrameInput array; ConvertUnit:(float -> string -> string -> float) option }
    /// <summary>The captured property name.</summary>
    override this.ToString()=this.Name

/// <summary>Normalized immutable endpoints, source timing and a precomputed modified end value for one frame.</summary>
type internal FrameTween(target:int,name:string,first:Data,last:Data,start:float,delay:float,updateDuration:float,
                         absoluteStart:float,absoluteUpdateStart:float,absoluteEnd:float,hasFrom:bool,
                         ease:float -> float,modifier:float -> float,cachedEnd:Output) =
    /// <summary>The captured target index.</summary>
    member _.Target=target
    /// <summary>The captured property identity within the target.</summary>
    member _.Name=name
    /// <summary>Normalized source endpoints; later frames inherit the raw Last value.</summary>
    member _.First=first
    /// <summary>The raw normalized destination before its modifier.</summary>
    member _.Last=last
    /// <summary>Start relative to the animation's trimmed initial delay.</summary>
    member _.Start=start
    /// <summary>Frame delay after initial-delay extraction.</summary>
    member _.Delay=delay
    /// <summary>Twelve-place construction duration; authored zero becomes source epsilon.</summary>
    member _.UpdateDuration=updateDuration
    /// <summary>The precision-matched absolute start, including the engine/timeline offset.</summary>
    member _.AbsoluteStart=absoluteStart
    /// <summary>The preceding frame end, or zero, rounded with the captured absolute offset.</summary>
    member _.AbsoluteUpdateStart=absoluteUpdateStart
    /// <summary>The rounded absolute end before any composition truncation.</summary>
    member _.AbsoluteEnd=absoluteEnd
    /// <summary>Whether authoring supplied an explicit first endpoint.</summary>
    member _.HasFrom=hasFrom
    /// <summary>The resolved per-frame easing callback.</summary>
    member _.Ease=ease
    /// <summary>The resolved per-frame component modifier.</summary>
    member _.Modifier=modifier
    /// <summary>The construction-time end cache; colors preserve unmodified destination channels.</summary>
    member _.CachedEnd=cachedEnd
    /// <summary>Produces trimmed timing while retaining precision-matched absolute geometry.</summary>
    member this.Trim(initialDelay) =
        FrameTween(target,name,first,last,start-initialDelay,(if start-delay=0. then delay-initialDelay else delay),updateDuration,
                   absoluteStart,absoluteUpdateStart,absoluteEnd,hasFrom,ease,modifier,cachedEnd)

/// <summary>A complete keyframe construction result; no property writes or scheduling have occurred.</summary>
type internal FrameProgram(delay:float,iterationDuration:float,offset:float,tweens:FrameTween array) =
    /// <summary>Minimum first-frame delay across captured properties and targets.</summary>
    member _.Delay=delay
    /// <summary>Last property end minus initial delay; empty programs use source epsilon.</summary>
    member _.IterationDuration=iterationDuration
    /// <summary>The captured absolute engine/timeline offset.</summary>
    member _.Offset=offset
    /// <summary>Tweens in target/property/frame authoring order.</summary>
    member _.Tweens=tweens

/// <summary>Source keyframe syntax normalization and guarded construction above the shared interpolation representation.</summary>
module internal KeyframeCompilation =
    let private finite name value=if not(Double.IsFinite value) then invalidArg name "Expected finite input."
    let private copy (value:Data)={value with Operator=None;Components=Array.copy value.Components;Strings=Array.copy value.Strings}
    let private callback name value=if isNull(box value) then invalidArg name "A callback is required."
    let private timing name value =
        finite name value
        if value<0. then invalidArg name "Expected nonnegative milliseconds."
        let rounded=PropertyNumbers.round 1e12 (if value=0. then 1e-11 else value)
        if rounded=0. then invalidArg name "Positive duration rounds to zero at source construction precision."
        rounded
    let private namedValues (values:(string*Literal) array) =
        if isNull values then nullArg "values"
        let result=Dictionary<string,Literal>(StringComparer.Ordinal)
        for name,value in values do
            if String.IsNullOrWhiteSpace name then invalidArg "values" "Property names must be nonempty."
            result[name]<-value
        result
    /// <summary>Converts one raw value to a destination, two to a from/to pair, and longer arrays to that pair plus later destinations.</summary>
    let values (input:Literal array) =
        if isNull input then nullArg "input"
        let input=Array.copy input
        if input.Length<2 then input |> Array.map(fun value -> {FrameInput.Default with To=Some value})
        else
            Array.init (input.Length-1) (fun index ->
                {FrameInput.Default with From=(if index=0 then Some input[0] else None);To=Some input[index+1]})
    /// <summary>Expands sparse duration rows to ordered property sequences, preserving hold frames and shared overrides.</summary>
    let rows (input:FrameRow array) =
        if isNull input then nullArg "input"
        let captured=input |> Array.map(fun row ->
            if isNull(box row) || isNull(box row.Frame) then invalidArg "input" "Rows and frame options are required."
            if row.Frame.From.IsSome || row.Frame.To.IsSome then invalidArg "input" "Row endpoints belong to named values."
            row.Frame,namedValues row.Values)
        let names=ResizeArray<string>()
        for _,values in captured do for name in values.Keys do if not(names.Contains name) then names.Add name
        names |> Seq.map(fun name ->
            name,captured |> Array.map(fun (frame,values) ->
                match values.TryGetValue name with true,value -> {frame with To=Some value} | _ -> frame)) |> Seq.toArray
    /// <summary>Sorts finite normalized offset rows, distributes durations and transfers each property's preceding key ease.</summary>
    let offsets (duration:float,input:FrameOffset array) =
        timing "duration" duration |> ignore
        if isNull input then nullArg "input"
        let ordered=input |> Array.mapi(fun index row ->
            if isNull(box row) then invalidArg "input" "Offset rows are required."
            finite "offset" row.Offset
            row.Ease |> Option.iter(callback "ease")
            row.Offset,index,namedValues row.Values,row.Ease) |> Array.sortBy(fun (offset,index,_,_) -> offset,index)
        let properties=Dictionary<string,ResizeArray<FrameInput>>(StringComparer.Ordinal)
        let names=ResizeArray<string>()
        for offset,_,values,ease in ordered do
            for KeyValue(name,value) in values do
                let frames=
                    match properties.TryGetValue name with
                    | true,frames -> frames
                    | _ -> let frames=ResizeArray<FrameInput>() in properties.Add(name,frames);names.Add name;frames
                let elapsed=frames |> Seq.sumBy(fun frame -> defaultArg frame.Duration 0.)
                frames.Add {FrameInput.Default with To=Some value;From=(if frames.Count=1 then frames[0].To else None)
                                                    Duration=Some(offset*duration-elapsed);Ease=ease}
        names |> Seq.map(fun name ->
            let frames=properties[name].ToArray()
            let mutable previous=None
            for index in 0..frames.Length-1 do
                let next=frames[index].Ease
                frames[index]<-{frames[index] with Ease=previous}
                previous<-next
            name,(if frames.Length>0 && frames[0].Duration=Some 0. then frames[1..] else frames)) |> Seq.toArray
    /// <summary>Compiles complete captured sequences atomically; an interrupted modifier or converter returns None.</summary>
    /// <remarks>Default durations divide by frame count despite frame overrides. Raw endpoints chain before modifiers.</remarks>
    let compile (defaults:FrameDefaults,properties:FrameProperty array,canWrite:unit -> bool) =
        if isNull(box defaults) then nullArg "defaults"
        timing "duration" defaults.Duration |> ignore
        finite "delay" defaults.Delay
        finite "offset" defaults.Offset
        callback "ease" defaults.Ease
        callback "modifier" defaults.Modifier
        callback "canWrite" canWrite
        if isNull properties then nullArg "properties"
        let properties=properties |> Array.map(fun property ->
            if isNull(box property) || isNull property.Frames then invalidArg "properties" "Captured properties and frame arrays are required."
            if String.IsNullOrWhiteSpace property.Name then invalidArg "properties" "Property names must be nonempty."
            {property with Frames=Array.copy property.Frames})
        for property in properties do
            for frame in property.Frames do
                if isNull(box frame) then invalidArg "frames" "Frames are required."
                frame.From |> Option.iter(fun value -> decompose value |> ignore)
                frame.To |> Option.iter(fun value -> decompose value |> ignore)
                timing "duration" (defaultArg frame.Duration (defaults.Duration/float property.Frames.Length)) |> ignore
                frame.Delay |> Option.iter(finite "delay")
                frame.Ease |> Option.iter(callback "ease")
                frame.Modifier |> Option.iter(callback "modifier")
            if property.Frames.Length>0 then
                let frame=property.Frames[0]
                let needsOriginal=frame.From.IsNone || frame.To.IsNone || (frame.From |> Option.exists(fun value -> let data=decompose value in data.Kind=ValueKind.Number || data.Operator.IsSome))
                if needsOriginal then decompose property.Original |> ignore
        let tweens=ResizeArray<FrameTween>()
        let mutable alive=canWrite()
        let mutable initialDelay=nan
        let mutable endTime=nan
        for property in properties do
            let original=lazy(decompose property.Original)
            let mutable previous:Data option=None
            let mutable previousEnd=0.
            let mutable firstStart=nan
            for index in 0..property.Frames.Length-1 do
                if alive then
                    let frame=property.Frames[index]
                    let preceding=previous |> Option.map copy |> Option.defaultWith(fun () ->
                        if frame.From.IsNone || frame.To.IsNone || (frame.From |> Option.exists(fun value -> let data=decompose value in data.Kind=ValueKind.Number || data.Operator.IsSome)) then copy original.Value
                        else decompose(Number 0.))
                    let mutable first=frame.From |> Option.map decompose |> Option.defaultWith(fun () -> copy preceding)
                    let last=frame.To |> Option.map decompose |> Option.defaultWith(fun () -> copy preceding)
                    if frame.From.IsSome && frame.To.IsSome && first.Kind=ValueKind.Number && preceding.Kind=ValueKind.Unit then
                        first<-{first with Kind=ValueKind.Unit;Unit=preceding.Unit}
                    let first,last=normalizeData(property.ConvertUnit,preceding,first,last)
                    alive<-canWrite()
                    let duration=timing "duration" (defaultArg frame.Duration (defaults.Duration/float property.Frames.Length))
                    let delay=defaultArg frame.Delay (if index=0 then defaults.Delay else 0.)
                    let start=previousEnd+delay
                    let nextEnd=PropertyNumbers.round 1e12 (start+duration)
                    let ease=defaultArg frame.Ease defaults.Ease
                    let modifier=defaultArg frame.Modifier defaults.Modifier
                    let cached=
                        if not alive then None
                        elif last.Kind=ValueKind.Color then Some(compose(last,0.,Array.copy last.Components))
                        else sample(false,last,last,1.,-1,modifier,canWrite) |> Option.map(fun struct(_,output) -> output)
                    match cached with
                    | Some cached when canWrite() ->
                        tweens.Add(FrameTween(property.Target,property.Name,first,last,start,delay,duration,
                            PropertyNumbers.round 1e12 (defaults.Offset+start),PropertyNumbers.round 1e12 (defaults.Offset+previousEnd),
                            PropertyNumbers.round 1e12 (defaults.Offset+nextEnd),frame.From.IsSome,ease,modifier,cached))
                        if Double.IsNaN firstStart then firstStart<-start
                        previous<-Some last
                        previousEnd<-nextEnd
                    | _ -> alive<-false
            if Double.IsNaN initialDelay || firstStart<initialDelay then initialDelay<-firstStart
            if Double.IsNaN endTime || previousEnd>endTime then endTime<-previousEnd
        if not alive || not(canWrite()) then None
        else
            let delay=if Double.IsNaN initialDelay || initialDelay=0. then 0. else initialDelay
            let duration=endTime-delay
            let duration=if Double.IsNaN duration || duration=0. then 1e-11 else duration
            Some(FrameProgram(delay,duration,defaults.Offset,tweens |> Seq.map(fun tween -> tween.Trim delay) |> Seq.toArray))
