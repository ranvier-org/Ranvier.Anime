namespace Ranvier.Anime

open System
open Ranvier.Dom
open Ranvier.Css
open Ranvier.Css.Dom
open Ranvier.Anime.ValueInterpolation

module internal TimelineCssKeyframeBinding =
    /// <summary>Captures typed CSS frames and scoped snapshots, then constructs one guarded timeline-owned style animation.</summary>
    let css (timeline:Timeline,targets:'Target array,name:string,authored:Keyframe<'Target,Declaration> array,
             getStyle:'Target -> CSSStyleDeclaration,getConverter:'Target -> (float -> string -> string -> float) option,
             initial:(unit -> unit) -> 'Target -> CSSStyleDeclaration -> string,initialImportant:bool,
             duration:float,options:Ranvier.Anime.KeyframeAnimationOptions,position:TimelinePosition option) =
        let precision=KeyframeBinding.validate(duration,options)
        if String.IsNullOrWhiteSpace name then invalidArg "name" "A CSS property name is required."
        if isNull targets || targets.Length=0 then invalidArg "targets" "Expected nonempty CSS targets."
        if isNull authored then nullArg "frames"
        let targets=Array.copy targets
        let authored=Array.copy authored
        if targets |> Array.exists(fun target -> isNull(box target)) then invalidArg "targets" "CSS targets are required."
        if authored |> Array.exists(fun frame -> isNull(box frame)) then invalidArg "frames" "Frames are required."
        let mutable constantImportant=initialImportant
        let literal (value:Declaration) =
            if Declaration.name value<>name then invalidArg "declaration" "Frame endpoints must name the captured property."
            Text(Declaration.value value)
        let template value =
            constantImportant<-constantImportant || Declaration.isImportant value
            literal value
        let templates=authored |> Array.map(fun frame -> frame.Template template)
        let properties=targets |> Array.mapi(fun index _ ->
            {FrameProperty.Target=index;Name=name;Original=Number 0.;Frames=templates;ConvertUnit=None})
        let defaults={FrameDefaults.Default with Duration=duration;Delay=options.Playback.Delay;Ease=options.Ease;Modifier=options.Modifier}
        KeyframeCompilation.validate(defaults,properties)
        timeline.TryAddOwned((fun context ->
            let animation=new KeyframeAnimation(targets.Length)
            context.Attach animation
            let active=animation.Guard()
            let current()=active() && context.CanWrite()
            let interrupted=InvalidOperationException("Owned style construction was superseded.")
            let ensure()=if not(current()) then raise interrupted
            let read action =
                ensure()
                let value=action()
                ensure()
                value
            try
                let styles=
                    if authored.Length=0 then [||]
                    else targets |> Array.map(fun target ->
                        let style=read(fun () -> getStyle target)
                        if isNull(box style) then invalidArg "targets" "A style declaration is required."
                        style)
                let originals=styles |> Array.map(fun style -> CssPropertySnapshot.Capture(style,name,ensure))
                let important=originals |> Array.map(fun original -> constantImportant || original.Important)
                animation.SetRestore(fun canWrite ->
                    let mutable failure:exn option=None
                    for index in 0..originals.Length-1 do
                        if canWrite() then
                            try originals[index].Restore(styles[index],canWrite)
                            with error -> if failure.IsNone then failure<-Some error
                    failure |> Option.iter(fun error -> Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw()))
                let properties=properties |> Array.mapi(fun index property ->
                    {property with ConvertUnit=(if authored.Length=0 then None else read(fun () -> getConverter targets[index]))})
                let resolve target frame (previous:FrameTween option) =
                    let previous=previous |> Option.map(fun tween ->
                        let value=Declaration.create name tween.CachedEnd.Text
                        if important[target] then Declaration.important value else value)
                    let convert value =
                        let result=literal value
                        if Declaration.isImportant value then important[target]<-true
                        result
                    authored[frame].Resolve(KeyframeContext(targets[target],uint target,uint targets.Length,previous),convert,current)
                let original index =
                    let value=read(fun () -> initial ensure targets[index] styles[index])
                    Text(if value="auto" then "0" else value)
                let write target _ (output:Output) =
                    let value=Declaration.create name output.Text
                    StyleDeclaration.apply styles[target] (if important[target] then Declaration.important value else value)
                let keys=styles |> Array.map(fun style -> struct(box style,name))
                TimelineKeyframeBinding.install(context,animation,defaults,properties,keys,resolve,original,write,true,precision,options,current)
            with error when obj.ReferenceEquals(error,interrupted) -> None),
            ?position=position,zeroDuration=(duration<=1e-11))
        |> Option.defaultWith(fun () -> invalidOp "Owned keyframe construction was superseded.")

/// <summary>Timeline-owned typed CSS frames using existing declarations, computed services and contextual unit conversion.</summary>
[<AutoOpen>]
module TimelineCssKeyframeExtensions =
    type Timeline with
        /// <summary>Adds typed style targets with an explicit initial declaration and precise nominal milliseconds.</summary>
        member this.AddKeyframesPrecise(styles:CSSStyleDeclaration array,initial:Declaration,frames:Keyframe<CSSStyleDeclaration,Declaration> array,duration:float,
                                        ?position:TimelinePosition,?options:Ranvier.Anime.KeyframeAnimationOptions) =
            let name,text,important=KeyframeBinding.explicitInitial initial
            TimelineCssKeyframeBinding.css(this,styles,name,frames,id,(fun _ -> None),(fun _ _ _ -> text),important,duration,
                                          defaultArg options Ranvier.Anime.KeyframeAnimationOptions.Default,position)
        /// <summary>Adds one style target with an explicit typed initial declaration and precise nominal milliseconds.</summary>
        member this.AddKeyframesPrecise(style:CSSStyleDeclaration,initial:Declaration,frames:Keyframe<CSSStyleDeclaration,Declaration> array,duration:float,
                                        ?position:TimelinePosition,?options:Ranvier.Anime.KeyframeAnimationOptions) =
            this.AddKeyframesPrecise([|style|],initial,frames,duration,?position=position,?options=options)
        /// <summary>Adds typed style targets with unsigned nominal milliseconds, defaulting to1000.</summary>
        member this.AddKeyframes(styles:CSSStyleDeclaration array,initial:Declaration,frames:Keyframe<CSSStyleDeclaration,Declaration> array,
                                 ?duration:uint,?position:TimelinePosition,?options:Ranvier.Anime.KeyframeAnimationOptions) =
            this.AddKeyframesPrecise(styles,initial,frames,float(defaultArg duration 1000u),?position=position,?options=options)
        /// <summary>Adds one style target with unsigned nominal milliseconds, defaulting to1000.</summary>
        member this.AddKeyframes(style:CSSStyleDeclaration,initial:Declaration,frames:Keyframe<CSSStyleDeclaration,Declaration> array,
                                 ?duration:uint,?position:TimelinePosition,?options:Ranvier.Anime.KeyframeAnimationOptions) =
            this.AddKeyframesPrecise(style,initial,frames,float(defaultArg duration 1000u),?position=position,?options=options)
        /// <summary>Adds elements with typed initial values, per-element unit conversion and precise nominal milliseconds.</summary>
        member this.AddKeyframesPrecise(elements:HTMLElement array,initial:Declaration,frames:Keyframe<HTMLElement,Declaration> array,duration:float,
                                        ?position:TimelinePosition,?options:Ranvier.Anime.KeyframeAnimationOptions) =
            let name,text,important=KeyframeBinding.explicitInitial initial
            TimelineCssKeyframeBinding.css(this,elements,name,frames,KeyframeBinding.elementStyle,(fun element -> Some(CssUnits.convert element)),
                                          (fun _ _ _ -> text),important,duration,defaultArg options Ranvier.Anime.KeyframeAnimationOptions.Default,position)
        /// <summary>Adds one element with typed initial values, contextual units and precise nominal milliseconds.</summary>
        member this.AddKeyframesPrecise(element:HTMLElement,initial:Declaration,frames:Keyframe<HTMLElement,Declaration> array,duration:float,
                                        ?position:TimelinePosition,?options:Ranvier.Anime.KeyframeAnimationOptions) =
            this.AddKeyframesPrecise([|element|],initial,frames,duration,?position=position,?options=options)
        /// <summary>Adds elements with contextual units and unsigned nominal milliseconds, defaulting to1000.</summary>
        member this.AddKeyframes(elements:HTMLElement array,initial:Declaration,frames:Keyframe<HTMLElement,Declaration> array,
                                 ?duration:uint,?position:TimelinePosition,?options:Ranvier.Anime.KeyframeAnimationOptions) =
            this.AddKeyframesPrecise(elements,initial,frames,float(defaultArg duration 1000u),?position=position,?options=options)
        /// <summary>Adds one element with contextual units and unsigned nominal milliseconds, defaulting to1000.</summary>
        member this.AddKeyframes(element:HTMLElement,initial:Declaration,frames:Keyframe<HTMLElement,Declaration> array,
                                 ?duration:uint,?position:TimelinePosition,?options:Ranvier.Anime.KeyframeAnimationOptions) =
            this.AddKeyframesPrecise(element,initial,frames,float(defaultArg duration 1000u),?position=position,?options=options)
        /// <summary>Captures inline values after frame callbacks and adds style targets using precise nominal milliseconds.</summary>
        member this.CaptureInlineKeyframesPrecise(styles:CSSStyleDeclaration array,name:string,frames:Keyframe<CSSStyleDeclaration,Declaration> array,duration:float,
                                                 ?position:TimelinePosition,?options:Ranvier.Anime.KeyframeAnimationOptions) =
            TimelineCssKeyframeBinding.css(this,styles,name,frames,id,(fun _ -> None),(fun _ _ style -> style.getPropertyValue name),
                                          false,duration,defaultArg options Ranvier.Anime.KeyframeAnimationOptions.Default,position)
        /// <summary>Captures one inline style target and adds typed frames using precise nominal milliseconds.</summary>
        member this.CaptureInlineKeyframesPrecise(style:CSSStyleDeclaration,name:string,frames:Keyframe<CSSStyleDeclaration,Declaration> array,duration:float,
                                                 ?position:TimelinePosition,?options:Ranvier.Anime.KeyframeAnimationOptions) =
            this.CaptureInlineKeyframesPrecise([|style|],name,frames,duration,?position=position,?options=options)
        /// <summary>Captures inline values and adds style targets using unsigned nominal milliseconds, defaulting to1000.</summary>
        member this.CaptureInlineKeyframes(styles:CSSStyleDeclaration array,name:string,frames:Keyframe<CSSStyleDeclaration,Declaration> array,
                                          ?duration:uint,?position:TimelinePosition,?options:Ranvier.Anime.KeyframeAnimationOptions) =
            this.CaptureInlineKeyframesPrecise(styles,name,frames,float(defaultArg duration 1000u),?position=position,?options=options)
        /// <summary>Captures one inline target using unsigned nominal milliseconds, defaulting to1000.</summary>
        member this.CaptureInlineKeyframes(style:CSSStyleDeclaration,name:string,frames:Keyframe<CSSStyleDeclaration,Declaration> array,
                                          ?duration:uint,?position:TimelinePosition,?options:Ranvier.Anime.KeyframeAnimationOptions) =
            this.CaptureInlineKeyframesPrecise(style,name,frames,float(defaultArg duration 1000u),?position=position,?options=options)
        /// <summary>Captures actual computed element values and unit contexts, then adds owned frames using precise milliseconds.</summary>
        member this.CaptureCssKeyframesPrecise(computed:ComputedStyleService,elements:HTMLElement array,name:string,frames:Keyframe<HTMLElement,Declaration> array,duration:float,
                                              ?position:TimelinePosition,?options:Ranvier.Anime.KeyframeAnimationOptions) =
            if isNull(box computed) then nullArg "computed"
            let initial ensure (element:HTMLElement) _ =
                let style=computed.Get((element:>Element),None)
                ensure()
                style.getPropertyValue name
            TimelineCssKeyframeBinding.css(this,elements,name,frames,KeyframeBinding.elementStyle,(fun element -> Some(CssUnits.convert element)),
                                          initial,false,duration,defaultArg options Ranvier.Anime.KeyframeAnimationOptions.Default,position)
        /// <summary>Captures one computed element and its unit context using precise nominal milliseconds.</summary>
        member this.CaptureCssKeyframesPrecise(computed:ComputedStyleService,element:HTMLElement,name:string,frames:Keyframe<HTMLElement,Declaration> array,duration:float,
                                              ?position:TimelinePosition,?options:Ranvier.Anime.KeyframeAnimationOptions) =
            this.CaptureCssKeyframesPrecise(computed,[|element|],name,frames,duration,?position=position,?options=options)
        /// <summary>Adds computed element targets using unsigned nominal milliseconds, defaulting to1000.</summary>
        member this.CaptureCssKeyframes(computed:ComputedStyleService,elements:HTMLElement array,name:string,frames:Keyframe<HTMLElement,Declaration> array,
                                       ?duration:uint,?position:TimelinePosition,?options:Ranvier.Anime.KeyframeAnimationOptions) =
            this.CaptureCssKeyframesPrecise(computed,elements,name,frames,float(defaultArg duration 1000u),?position=position,?options=options)
        /// <summary>Adds one computed element using unsigned nominal milliseconds, defaulting to1000.</summary>
        member this.CaptureCssKeyframes(computed:ComputedStyleService,element:HTMLElement,name:string,frames:Keyframe<HTMLElement,Declaration> array,
                                       ?duration:uint,?position:TimelinePosition,?options:Ranvier.Anime.KeyframeAnimationOptions) =
            this.CaptureCssKeyframesPrecise(computed,element,name,frames,float(defaultArg duration 1000u),?position=position,?options=options)
