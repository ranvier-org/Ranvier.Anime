namespace Ranvier.Anime

open System
open System.Collections.Generic
open System.Collections.Frozen
open Ranvier.Anime.ValueInterpolation

module internal TimelineKeyframeSequenceBinding =
    /// <summary>Captures named numeric targets and sparse authoring before constructing one guarded owned timeline child.</summary>
    let numeric (timeline:Timeline,bindings:(string*NumericProperty array) array,sequence:KeyframeSequence<NumericProperty,float>,
                 duration:float,options:KeyframeAnimationOptions,position:TimelinePosition option) =
        let precision=KeyframeBinding.validate(duration,options)
        if isNull bindings || bindings.Length=0 then invalidArg "properties" "Expected nonempty named numeric properties."
        if isNull(box sequence) then nullArg "sequence"
        let captured=Dictionary<string,NumericProperty array>(StringComparer.Ordinal)
        let mutable targetCount=0
        for name,targets in Array.copy bindings do
            if String.IsNullOrWhiteSpace name then invalidArg "properties" "Property names must be nonempty."
            if isNull targets || targets.Length=0 then invalidArg "properties" "Expected nonempty target arrays."
            let targets=Array.copy targets
            if targets |> Array.exists(fun target -> isNull(box target)) then invalidArg "properties" "Numeric handles are required."
            if captured.Count=0 then targetCount<-targets.Length
            elif targets.Length<>targetCount then invalidArg "properties" "Named properties must have equal target counts."
            if not(captured.TryAdd(name,targets)) then invalidArg "properties" "Named property bindings must be unique."
        let handles=captured.ToFrozenDictionary(StringComparer.Ordinal)
        let definitions=sequence.Capture duration
        let number value =
            if not(Double.IsFinite value) then invalidArg "value" "Expected finite numeric endpoints."
            Number value
        let columns=definitions |> Array.map(fun (name,frames) ->
            match handles.TryGetValue name with
            | true,targets -> name,frames,targets,(frames |> Array.map(fun frame -> frame.Template number))
            | _ -> invalidArg "properties" ("Missing numeric property binding: "+name))
        let cells=Array.init targetCount (fun target ->
            columns |> Array.map(fun (name,frames,handles,templates) -> target,name,frames,handles[target],templates)) |> Array.concat
        let properties=cells |> Array.map(fun (target,name,_,_,templates) ->
            {FrameProperty.Target=target;Name=name;Original=Number 0.;Frames=templates;ConvertUnit=None})
        let defaults={FrameDefaults.Default with Duration=duration;Delay=options.Playback.Delay;Ease=options.Ease;Modifier=options.Modifier}
        KeyframeCompilation.validate(defaults,properties)
        timeline.TryAddOwned((fun context ->
            let animation=new KeyframeAnimation(targetCount)
            context.Attach animation
            let active=animation.Guard()
            let current()=active() && context.CanWrite()
            let originals=Array.create cells.Length None
            let mutable index=0
            while index<cells.Length && current() do
                let _,_,frames,handle,_=cells[index]
                if frames.Length>0 then originals[index]<-Some handle.Value
                index<-index+1
            if not(current()) then None
            else
                animation.SetRestore(fun canWrite ->
                    let mutable failure:exn option=None
                    for index in 0..cells.Length-1 do
                        if canWrite() then
                            match originals[index] with
                            | Some value ->
                                try
                                    let _,_,_,handle,_=cells[index]
                                    handle.Value<-value
                                with error -> if failure.IsNone then failure<-Some error
                            | None -> ()
                    failure |> Option.iter(fun error -> Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw()))
                let resolve property frame (previous:FrameTween option) =
                    let target,_,frames,handle,_=cells[property]
                    frames[frame].Resolve(KeyframeContext(handle,uint target,uint targetCount,previous |> Option.bind(fun tween -> tween.CachedEnd.Scalar)),number,current)
                let original index=
                    let _,_,_,handle,_=cells[index]
                    Number handle.Value
                let write target name (output:Output)=handles[name].[target].Value<-output.Scalar.Value
                let keys=cells |> Array.map(fun (_,_,_,handle,_) -> struct(box handle,"value"))
                TimelineKeyframeBinding.install(context,animation,defaults,properties,keys,resolve,original,write,false,precision,options,current)),
            ?position=position,zeroDuration=(duration<=1e-11))
        |> Option.defaultWith(fun () -> invalidOp "Owned keyframe construction was superseded.")

/// <summary>Owned timeline overloads for existing sparse rows and normalized offset sequences.</summary>
[<AutoOpen>]
module TimelineKeyframeSequenceExtensions =
    type Timeline with
        /// <summary>Adds named numeric target arrays using sparse authoring and precise nominal milliseconds.</summary>
        member this.AddKeyframesPrecise(properties:(string*NumericProperty array) array,sequence:KeyframeSequence<NumericProperty,float>,duration:float,
                                        ?position:TimelinePosition,?options:KeyframeAnimationOptions) =
            TimelineKeyframeSequenceBinding.numeric(this,properties,sequence,duration,defaultArg options KeyframeAnimationOptions.Default,position)
        /// <summary>Adds named single-target numeric handles using sparse authoring and precise nominal milliseconds.</summary>
        member this.AddKeyframesPrecise(properties:(string*NumericProperty) array,sequence:KeyframeSequence<NumericProperty,float>,duration:float,
                                        ?position:TimelinePosition,?options:KeyframeAnimationOptions) =
            let captured=if isNull properties then null else properties |> Array.map(fun (name,target) -> name,[|target|])
            this.AddKeyframesPrecise(captured,sequence,duration,?position=position,?options=options)
        /// <summary>Adds named numeric target arrays; unsigned nominal milliseconds default to1000 and the timeline owns autoplay.</summary>
        member this.AddKeyframes(properties:(string*NumericProperty array) array,sequence:KeyframeSequence<NumericProperty,float>,
                                 ?duration:uint,?position:TimelinePosition,?options:KeyframeAnimationOptions) =
            this.AddKeyframesPrecise(properties,sequence,float(defaultArg duration 1000u),?position=position,?options=options)
        /// <summary>Adds named single-target numeric handles; unsigned nominal milliseconds default to1000.</summary>
        member this.AddKeyframes(properties:(string*NumericProperty) array,sequence:KeyframeSequence<NumericProperty,float>,
                                 ?duration:uint,?position:TimelinePosition,?options:KeyframeAnimationOptions) =
            this.AddKeyframesPrecise(properties,sequence,float(defaultArg duration 1000u),?position=position,?options=options)
