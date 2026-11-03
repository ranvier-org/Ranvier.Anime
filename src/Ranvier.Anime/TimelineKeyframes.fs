namespace Ranvier.Anime

open System
open Ranvier.Anime.ValueInterpolation

module internal TimelineKeyframeBinding =
    /// <summary>Compiles guarded captured properties and installs one owned driver with optional staged replacement siblings.</summary>
    let install (context:TimelineChildContext,animation:KeyframeAnimation,defaults:FrameDefaults,properties:FrameProperty array,
                 keys:struct(obj*string) array,resolve,original,write,css,precision,options:KeyframeAnimationOptions,current) =
        let replacing=options.Composition=KeyframeComposition.Replace
        let transaction=if replacing then Some(context.BeginSiblings keys) else None
        let mutable handedOff=false
        try
            let defaults={defaults with Offset=context.Offset}
            let program=
                match transaction with
                | Some siblings -> KeyframeCompilation.compileWithSiblings(defaults,properties,resolve,original,siblings.Compilation,current)
                | None -> KeyframeCompilation.compileDynamic(false,defaults,properties,resolve,original,current)
            match program with
            | Some program when current() ->
                let iterations=match options.Playback.Repeat with PlaybackRepeat.Count count -> float count+1. | PlaybackRepeat.Forever -> infinity
                let total=PlaybackTiming(program.IterationDuration,iterations,program.Delay,options.Playback.LoopDelay).Duration
                let nodes=transaction |> Option.map(fun siblings -> siblings.Bind(program,iterations,total))
                animation.SetCleanup(fun () ->
                    transaction |> Option.iter(fun siblings -> siblings.Rollback())
                    nodes |> Option.iter context.RemoveSiblings)
                let driver=new KeyframePlayback(context.Scheduler,program,write,replacing,css,precision,options.Playback,
                    onBegin=options.OnBegin,onBeforeUpdate=options.OnBeforeUpdate,onRender=options.OnRender,
                    onUpdate=options.OnUpdate,onLoop=options.OnLoop,onPause=options.OnPause,onComplete=options.OnComplete,?siblings=nodes)
                animation.SetDriver driver
                driver.Clock.OnCancel<-animation.Cancel
                driver.Clock.OnFault<-animation.Cancel
                driver.Initialize(timelineChild=true,notifyConstruction=true,canWrite=current)
                if not(current()) then None
                else
                    handedOff<-true
                    let commit()=match transaction with Some siblings -> siblings.Commit() | None -> true
                    Some(TimelinePreparedChild(driver.Clock,(fun () -> driver.RenderVersion),commit,animation))
            | _ -> None
        finally
            if not handedOff then transaction |> Option.iter(fun siblings -> siblings.Rollback())


    /// <summary>Validates and captures numeric authoring before parent effects, then constructs one timeline-owned animation.</summary>
    let numeric (timeline:Timeline,targets:NumericProperty array,authored:Keyframe<NumericProperty,float> array,
                 duration:float,options:KeyframeAnimationOptions,position:TimelinePosition option) =
        let precision=KeyframeBinding.validate(duration,options)
        if isNull targets || targets.Length=0 then invalidArg "targets" "Expected nonempty numeric properties."
        if isNull authored then nullArg "frames"
        let targets=Array.copy targets
        let authored=Array.copy authored
        if targets |> Array.exists(fun target -> isNull(box target)) then invalidArg "targets" "Numeric properties are required."
        if authored |> Array.exists(fun frame -> isNull(box frame)) then invalidArg "frames" "Frames are required."
        let number value =
            if not(Double.IsFinite value) then invalidArg "value" "Expected finite numeric endpoints."
            Number value
        let templates=authored |> Array.map(fun frame -> frame.Template number)
        let properties=targets |> Array.mapi(fun index _ ->
            {FrameProperty.Target=index;Name="value";Original=Number 0.;Frames=templates;ConvertUnit=None})
        let defaults={FrameDefaults.Default with Duration=duration;Delay=options.Playback.Delay;Ease=options.Ease;Modifier=options.Modifier}
        KeyframeCompilation.validate(defaults,properties)
        timeline.TryAddOwned((fun context ->
            let animation=new KeyframeAnimation(targets.Length)
            context.Attach animation
            let animationCurrent=animation.Guard()
            let current()=animationCurrent() && context.CanWrite()
            let originals=if authored.Length=0 then [||] else Array.zeroCreate targets.Length
            let mutable index=0
            while index<originals.Length && current() do
                originals[index]<-targets[index].Value
                index<-index+1
            if not(current()) then None
            else
                animation.SetRestore(fun canWrite ->
                    let mutable failure:exn option=None
                    for index in 0..originals.Length-1 do
                        if canWrite() then
                            try targets[index].Value<-originals[index]
                            with error -> if failure.IsNone then failure<-Some error
                    failure |> Option.iter(fun error -> Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw()))
                let resolve target frame (previous:FrameTween option) =
                    authored[frame].Resolve(KeyframeContext(targets[target],uint target,uint targets.Length,
                        previous |> Option.bind(fun tween -> tween.CachedEnd.Scalar)),number,current)
                let original index=Number(targets[index].Value)
                let write target _ (output:Output)=targets[target].Value<-output.Scalar.Value
                install(context,animation,defaults,properties,targets |> Array.map(fun target -> struct(box target,"value")),
                        resolve,original,write,false,precision,options,current)),
            ?position=position,zeroDuration=(duration<=1e-11))
        |> Option.defaultWith(fun () -> invalidOp "Owned keyframe construction was superseded.")

/// <summary>Owned timeline factories using existing typed keyframe values and numeric property identities.</summary>
[<AutoOpen>]
module TimelineKeyframeExtensions =
    type Timeline with
        /// <summary>Adds nonempty numeric targets with precise nominal milliseconds; the timeline owns scheduling and cancellation.</summary>
        /// <remarks>Superseded construction raises InvalidOperationException. Autoplay belongs to the root timeline.</remarks>
        member this.AddKeyframesPrecise(targets:NumericProperty array,frames:Keyframe<NumericProperty,float> array,duration:float,
                                        ?position:TimelinePosition,?options:KeyframeAnimationOptions) =
            TimelineKeyframeBinding.numeric(this,targets,frames,duration,defaultArg options KeyframeAnimationOptions.Default,position)
        /// <summary>Adds one numeric target with precise nominal milliseconds and returns its owned animation.</summary>
        member this.AddKeyframesPrecise(target:NumericProperty,frames:Keyframe<NumericProperty,float> array,duration:float,
                                        ?position:TimelinePosition,?options:KeyframeAnimationOptions) =
            this.AddKeyframesPrecise([|target|],frames,duration,?position=position,?options=options)
        /// <summary>Adds nonempty numeric targets using unsigned nominal milliseconds, defaulting to1000; root playback owns autoplay.</summary>
        member this.AddKeyframes(targets:NumericProperty array,frames:Keyframe<NumericProperty,float> array,
                                 ?duration:uint,?position:TimelinePosition,?options:KeyframeAnimationOptions) =
            this.AddKeyframesPrecise(targets,frames,float(defaultArg duration 1000u),?position=position,?options=options)
        /// <summary>Adds one numeric target using unsigned nominal milliseconds, defaulting to1000; root playback owns autoplay.</summary>
        member this.AddKeyframes(target:NumericProperty,frames:Keyframe<NumericProperty,float> array,
                                 ?duration:uint,?position:TimelinePosition,?options:KeyframeAnimationOptions) =
            this.AddKeyframesPrecise(target,frames,float(defaultArg duration 1000u),?position=position,?options=options)
