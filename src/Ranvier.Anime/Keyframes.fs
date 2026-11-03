namespace Ranvier.Anime

open System
open Ranvier
open Ranvier.Dom
open Ranvier.Css
open Ranvier.Css.Dom
open Ranvier.Anime.ValueInterpolation
open Ranvier.Anime

/// <summary>Owned construction helpers shared by single-property and sparse keyframe sequences.</summary>
module internal KeyframeBinding =
    let validate (duration:float,options:KeyframeAnimationOptions) =
        if isNull(box options) || isNull(box options.Playback) then nullArg "options"
        let finite name value=if not(Double.IsFinite value) then invalidArg name "Expected finite milliseconds."
        finite "duration" duration
        if duration<0. then invalidArg "duration" "Expected nonnegative milliseconds."
        let rounded=PropertyNumbers.round 1e12 (if duration=0. then 1e-11 else duration)
        if rounded=0. then invalidArg "duration" "Positive duration rounds to zero at source construction precision."
        for name,value in ["delay",options.Playback.Delay;"loopDelay",options.Playback.LoopDelay;"speed",options.Playback.Speed;"frameRate",options.Playback.FrameRate] do finite name value
        for action in [box options.Ease;box options.Modifier;box options.OnBegin;box options.OnBeforeUpdate;box options.OnRender
                       box options.OnUpdate;box options.OnLoop;box options.OnPause;box options.OnComplete] do
            if isNull action then invalidArg "options" "Callbacks are required."
        match options.Precision with None -> -1 | Some value when value<=308u -> int value | _ -> invalidArg "options" "Precision exceeds 308."
    let numeric (scheduler:FrameScheduler,targets:NumericProperty array,authored:Keyframe<NumericProperty,float> array,
                 duration:float,options:KeyframeAnimationOptions,owner:Owner option) =
        let precision=validate(duration,options)
        if isNull targets || targets.Length=0 then invalidArg "targets" "Expected nonempty numeric properties."
        if isNull authored then nullArg "frames"
        let targets=Array.copy targets
        let authored=Array.copy authored
        if targets |> Array.exists(fun target -> isNull(box target)) then invalidArg "targets" "Numeric properties are required."
        if authored |> Array.exists(fun frame -> isNull(box frame)) then invalidArg "frames" "Frames are required."
        let convert value =
            if not(Double.IsFinite value) then invalidArg "value" "Expected finite numeric endpoints."
            Number value
        let templates=authored |> Array.map(fun frame -> frame.Template convert)
        let properties=targets |> Array.mapi(fun index _ ->
            {FrameProperty.Target=index;Name="value";Original=Number 0.;Frames=templates;ConvertUnit=None})
        let defaults={FrameDefaults.Default with Duration=duration;Delay=options.Playback.Delay;Ease=options.Ease;Modifier=options.Modifier}
        KeyframeCompilation.validate(defaults,properties)
        let animation=new KeyframeAnimation(targets.Length)
        try
            owner |> Option.iter(fun owner -> owner.Attach animation)
            let current=animation.Guard()
            let read index =
                animation.EnsureLive()
                let value=targets[index].Value
                animation.EnsureLive()
                value
            let originals=if authored.Length=0 then [||] else targets |> Array.mapi(fun index _ -> read index)
            animation.SetRestore(fun current ->
                let mutable failure:exn option=None
                for index in 0..originals.Length-1 do
                    if current() then
                        try targets[index].Value<-originals[index]
                        with error -> if failure.IsNone then failure<-Some error
                failure |> Option.iter(fun error -> Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw()))
            let resolve target frame previous =
                let previous=previous |> Option.bind(fun (tween:FrameTween) -> tween.CachedEnd.Scalar)
                let context=KeyframeContext(targets[target],uint target,uint targets.Length,previous)
                authored[frame].Resolve(context,convert,current)
            let program=KeyframeCompilation.compileDynamic(options.Composition=KeyframeComposition.Replace,defaults,properties,resolve,(fun index -> Number(read index)),current)
            animation.EnsureLive()
            let program=program |> Option.defaultWith(fun () -> invalidOp "Keyframe construction was superseded.")
            let driver=new KeyframePlayback(scheduler,program,(fun target _ output -> targets[target].Value<-output.Scalar.Value),
                                           options.Composition=KeyframeComposition.Replace,false,precision,options.Playback,
                                           onBegin=options.OnBegin,onBeforeUpdate=options.OnBeforeUpdate,onRender=options.OnRender,
                                           onUpdate=options.OnUpdate,onLoop=options.OnLoop,onPause=options.OnPause,onComplete=options.OnComplete)
            animation.SetDriver driver
            driver.Clock.OnFault<-animation.Cancel
            driver.Clock.OnCancel<-animation.Cancel
            driver.Initialize(notifyConstruction=not options.Autoplay)
            if current() && options.Autoplay then driver.Clock.Resume()
            animation
        with _ ->
            try animation.Cancel() with _ -> ()
            reraise()

    let css (scheduler:FrameScheduler,targets:'Target array,name:string,authored:Keyframe<'Target,Ranvier.Css.Declaration> array,
             getStyle:'Target -> CSSStyleDeclaration,getConverter:'Target -> (float -> string -> string -> float) option,
             initial:KeyframeAnimation -> 'Target -> CSSStyleDeclaration -> string,initialImportant:bool,
             duration:float,options:KeyframeAnimationOptions,owner:Owner option) =
        let precision=validate(duration,options)
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
        let template value=
            constantImportant<-constantImportant || Declaration.isImportant value
            literal value
        let templates=authored |> Array.map(fun frame -> frame.Template template)
        let properties=targets |> Array.mapi(fun index _ ->
            {FrameProperty.Target=index;Name=name;Original=Number 0.;Frames=templates;ConvertUnit=None})
        let defaults={FrameDefaults.Default with Duration=duration;Delay=options.Playback.Delay;Ease=options.Ease;Modifier=options.Modifier}
        KeyframeCompilation.validate(defaults,properties)
        let animation=new KeyframeAnimation(targets.Length)
        try
            owner |> Option.iter(fun owner -> owner.Attach animation)
            let current=animation.Guard()
            let read action =
                animation.EnsureLive()
                let value=action()
                animation.EnsureLive()
                value
            let styles=
                if authored.Length=0 then [||]
                else targets |> Array.map(fun target ->
                    let style=read(fun () -> getStyle target)
                    if isNull(box style) then invalidArg "targets" "A style declaration is required."
                    style)
            let originals=styles |> Array.map(fun style -> CssPropertySnapshot.Capture(style,name,animation.EnsureLive))
            let important=originals |> Array.map(fun original -> constantImportant || original.Important)
            animation.SetRestore(fun current ->
                let mutable failure:exn option=None
                for index in 0..originals.Length-1 do
                    if current() then
                        try
                            originals[index].Restore(styles[index],current)
                        with error -> if failure.IsNone then failure<-Some error
                failure |> Option.iter(fun error -> Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw()))
            let properties=properties |> Array.mapi(fun index property ->
                {property with ConvertUnit=(if authored.Length=0 then None else getConverter targets[index])})
            let resolve target frame previous =
                let previous=previous |> Option.map(fun (tween:FrameTween) ->
                    let value=Declaration.create name tween.CachedEnd.Text
                    if important[target] then Declaration.important value else value)
                let context=KeyframeContext(targets[target],uint target,uint targets.Length,previous)
                let convert value =
                    let result=literal value
                    if Declaration.isImportant value then important[target]<-true
                    result
                authored[frame].Resolve(context,convert,current)
            let readOriginal index=
                let value=read(fun () -> initial animation targets[index] styles[index])
                Text(if value="auto" then "0" else value)
            let program=KeyframeCompilation.compileDynamic(options.Composition=KeyframeComposition.Replace,defaults,properties,resolve,readOriginal,current)
            animation.EnsureLive()
            let program=program |> Option.defaultWith(fun () -> invalidOp "Keyframe construction was superseded.")
            let write target _ (output:Output) =
                let value=Declaration.create name output.Text
                StyleDeclaration.apply styles[target] (if important[target] then Declaration.important value else value)
            let driver=new KeyframePlayback(scheduler,program,write,options.Composition=KeyframeComposition.Replace,true,precision,options.Playback,
                                           onBegin=options.OnBegin,onBeforeUpdate=options.OnBeforeUpdate,onRender=options.OnRender,
                                           onUpdate=options.OnUpdate,onLoop=options.OnLoop,onPause=options.OnPause,onComplete=options.OnComplete)
            animation.SetDriver driver
            driver.Clock.OnFault<-animation.Cancel
            driver.Clock.OnCancel<-animation.Cancel
            driver.Initialize(notifyConstruction=not options.Autoplay)
            if current() && options.Autoplay then driver.Clock.Resume()
            animation
        with _ ->
            try animation.Cancel() with _ -> ()
            reraise()
    let explicitInitial (declaration:Declaration) =
        let name=Declaration.name declaration
        let text=Declaration.value declaration
        if String.IsNullOrWhiteSpace name then invalidArg "initial" "A CSS property name is required."
        if (decompose(Text text)).Operator.IsSome then invalidArg "initial" "Use an absolute initial declaration."
        name,text,Declaration.isImportant declaration
    let elementStyle (element:HTMLElement)=((element:>ElementCSSInlineStyle).style:>CSSStyleDeclaration)

/// <summary>Typed keyframe sequence construction on the native shared scheduler.</summary>
/// <remarks>Empty frame arrays complete as an epsilon no-op and do not read their property accessors.</remarks>
/// <remarks>CSS bindings snapshot affected longhands and preserve current logical-counterpart values during restoration.</remarks>
/// <remarks>Unrecoverable pending shorthand values raise NotSupportedException before dispatch.</remarks>
[<AbstractClass;Sealed>]
type Keyframes private () =
    /// <summary>Animates nonempty numeric property handles using finite nonnegative precise milliseconds.</summary>
    /// <remarks>Pass a Ranvier owner to attach before capture and callbacks. The caller owns disposal when omitted.</remarks>
    static member NumericPrecise(scheduler:FrameScheduler,targets:NumericProperty array,frames:Keyframe<NumericProperty,float> array,
                                 duration:float,?options:KeyframeAnimationOptions,?owner:Owner) =
        KeyframeBinding.numeric(scheduler,targets,frames,duration,defaultArg options KeyframeAnimationOptions.Default,owner)
    /// <summary>Animates one numeric property using finite nonnegative precise milliseconds.</summary>
    static member NumericPrecise(scheduler:FrameScheduler,target:NumericProperty,frames:Keyframe<NumericProperty,float> array,
                                 duration:float,?options:KeyframeAnimationOptions,?owner:Owner) =
        Keyframes.NumericPrecise(scheduler,[|target|],frames,duration,?options=options,?owner=owner)
    /// <summary>Animates nonempty numeric property handles; unsigned total default duration is 1000 ms.</summary>
    static member Numeric(scheduler:FrameScheduler,targets:NumericProperty array,frames:Keyframe<NumericProperty,float> array,
                          ?duration:uint,?options:KeyframeAnimationOptions,?owner:Owner) =
        Keyframes.NumericPrecise(scheduler,targets,frames,AnimationDefaults.duration(duration |> Option.map float),?options=options,?owner=owner)
    /// <summary>Animates one numeric property; unsigned total default duration is 1000 ms.</summary>
    static member Numeric(scheduler:FrameScheduler,target:NumericProperty,frames:Keyframe<NumericProperty,float> array,
                          ?duration:uint,?options:KeyframeAnimationOptions,?owner:Owner) =
        Keyframes.Numeric(scheduler,[|target|],frames,?duration=duration,?options=options,?owner=owner)

    /// <summary>Animates one typed property on nonempty style targets using explicit initial values and precise milliseconds.</summary>
    /// <remarks>Scalar units must match. Revert restores original inline value, priority and presence; unrelated edits persist.</remarks>
    static member DeclarationPrecise(scheduler:FrameScheduler,styles:CSSStyleDeclaration array,initial:Declaration,
                                     frames:Keyframe<CSSStyleDeclaration,Declaration> array,duration:float,
                                     ?options:KeyframeAnimationOptions,?owner:Owner) =
        let name,text,important=KeyframeBinding.explicitInitial initial
        KeyframeBinding.css(scheduler,styles,name,frames,id,(fun _ -> None),(fun _ _ _ -> text),important,duration,
                            defaultArg options KeyframeAnimationOptions.Default,owner)
    /// <summary>Animates one style target with an explicit typed initial value and precise milliseconds.</summary>
    static member DeclarationPrecise(scheduler:FrameScheduler,style:CSSStyleDeclaration,initial:Declaration,
                                     frames:Keyframe<CSSStyleDeclaration,Declaration> array,duration:float,
                                     ?options:KeyframeAnimationOptions,?owner:Owner) =
        Keyframes.DeclarationPrecise(scheduler,[|style|],initial,frames,duration,?options=options,?owner=owner)
    /// <summary>Animates one typed property on nonempty style targets; omitted duration inherits scope defaults.</summary>
    static member Declaration(scheduler:FrameScheduler,styles:CSSStyleDeclaration array,initial:Declaration,
                              frames:Keyframe<CSSStyleDeclaration,Declaration> array,?duration:uint,
                              ?options:KeyframeAnimationOptions,?owner:Owner) =
        Keyframes.DeclarationPrecise(scheduler,styles,initial,frames,AnimationDefaults.duration(duration |> Option.map float),?options=options,?owner=owner)
    /// <summary>Animates one style target with an explicit initial value; omitted duration inherits scope defaults.</summary>
    static member Declaration(scheduler:FrameScheduler,style:CSSStyleDeclaration,initial:Declaration,
                              frames:Keyframe<CSSStyleDeclaration,Declaration> array,?duration:uint,
                              ?options:KeyframeAnimationOptions,?owner:Owner) =
        Keyframes.Declaration(scheduler,[|style|],initial,frames,?duration=duration,?options=options,?owner=owner)
    /// <summary>Animates one typed property on nonempty elements with per-element scalar-unit conversion and precise milliseconds.</summary>
    static member DeclarationOnPrecise(scheduler:FrameScheduler,elements:HTMLElement array,initial:Declaration,
                                       frames:Keyframe<HTMLElement,Declaration> array,duration:float,
                                       ?options:KeyframeAnimationOptions,?owner:Owner) =
        let name,text,important=KeyframeBinding.explicitInitial initial
        KeyframeBinding.css(scheduler,elements,name,frames,KeyframeBinding.elementStyle,(fun element -> Some(CssUnits.convert element)),
                            (fun _ _ _ -> text),important,duration,defaultArg options KeyframeAnimationOptions.Default,owner)
    /// <summary>Animates one element with contextual units, an explicit initial value and precise milliseconds.</summary>
    static member DeclarationOnPrecise(scheduler:FrameScheduler,element:HTMLElement,initial:Declaration,
                                       frames:Keyframe<HTMLElement,Declaration> array,duration:float,
                                       ?options:KeyframeAnimationOptions,?owner:Owner) =
        Keyframes.DeclarationOnPrecise(scheduler,[|element|],initial,frames,duration,?options=options,?owner=owner)
    /// <summary>Animates one typed property on nonempty elements with contextual units; omitted duration inherits scope defaults.</summary>
    static member DeclarationOn(scheduler:FrameScheduler,elements:HTMLElement array,initial:Declaration,
                                frames:Keyframe<HTMLElement,Declaration> array,?duration:uint,
                                ?options:KeyframeAnimationOptions,?owner:Owner) =
        Keyframes.DeclarationOnPrecise(scheduler,elements,initial,frames,AnimationDefaults.duration(duration |> Option.map float),?options=options,?owner=owner)
    /// <summary>Animates one element with contextual units; omitted duration inherits scope defaults.</summary>
    static member DeclarationOn(scheduler:FrameScheduler,element:HTMLElement,initial:Declaration,
                                frames:Keyframe<HTMLElement,Declaration> array,?duration:uint,
                                ?options:KeyframeAnimationOptions,?owner:Owner) =
        Keyframes.DeclarationOn(scheduler,[|element|],initial,frames,?duration=duration,?options=options,?owner=owner)
    /// <summary>Captures current inline values after frame callbacks and animates one property using precise milliseconds.</summary>
    /// <remarks>Empty inline values start at zero. Scalar units must match. Revert restores pre-construction inline snapshots.</remarks>
    static member CaptureInlinePrecise(scheduler:FrameScheduler,styles:CSSStyleDeclaration array,name:string,
                                       frames:Keyframe<CSSStyleDeclaration,Declaration> array,duration:float,
                                       ?options:KeyframeAnimationOptions,?owner:Owner) =
        KeyframeBinding.css(scheduler,styles,name,frames,id,(fun _ -> None),(fun _ _ style -> style.getPropertyValue name),false,duration,
                            defaultArg options KeyframeAnimationOptions.Default,owner)
    /// <summary>Captures one inline property and animates it using precise milliseconds.</summary>
    static member CaptureInlinePrecise(scheduler:FrameScheduler,style:CSSStyleDeclaration,name:string,
                                       frames:Keyframe<CSSStyleDeclaration,Declaration> array,duration:float,
                                       ?options:KeyframeAnimationOptions,?owner:Owner) =
        Keyframes.CaptureInlinePrecise(scheduler,[|style|],name,frames,duration,?options=options,?owner=owner)
    /// <summary>Captures one inline property on nonempty style targets; omitted duration inherits scope defaults.</summary>
    static member CaptureInline(scheduler:FrameScheduler,styles:CSSStyleDeclaration array,name:string,
                                frames:Keyframe<CSSStyleDeclaration,Declaration> array,?duration:uint,
                                ?options:KeyframeAnimationOptions,?owner:Owner) =
        Keyframes.CaptureInlinePrecise(scheduler,styles,name,frames,AnimationDefaults.duration(duration |> Option.map float),?options=options,?owner=owner)
    /// <summary>Captures one inline property; omitted duration inherits scope defaults.</summary>
    static member CaptureInline(scheduler:FrameScheduler,style:CSSStyleDeclaration,name:string,
                                frames:Keyframe<CSSStyleDeclaration,Declaration> array,?duration:uint,
                                ?options:KeyframeAnimationOptions,?owner:Owner) =
        Keyframes.CaptureInline(scheduler,[|style|],name,frames,?duration=duration,?options=options,?owner=owner)
    /// <summary>Captures computed values through Ranvier's service and per-element unit contexts using precise milliseconds.</summary>
    /// <remarks>Frame callbacks precede initial computed reads. Revert restores original inline presence, value and priority.</remarks>
    static member CaptureCssPrecise(scheduler:FrameScheduler,computed:ComputedStyleService,elements:HTMLElement array,name:string,
                                    frames:Keyframe<HTMLElement,Declaration> array,duration:float,
                                    ?options:KeyframeAnimationOptions,?owner:Owner) =
        if isNull(box computed) then nullArg "computed"
        let initial (animation:KeyframeAnimation) (element:HTMLElement) _ =
            let style=computed.Get((element:>Element),None)
            animation.EnsureLive()
            style.getPropertyValue name
        KeyframeBinding.css(scheduler,elements,name,frames,KeyframeBinding.elementStyle,(fun element -> Some(CssUnits.convert element)),
                            initial,false,duration,defaultArg options KeyframeAnimationOptions.Default,owner)
    /// <summary>Captures one computed element property through Ranvier's service using precise milliseconds.</summary>
    static member CaptureCssPrecise(scheduler:FrameScheduler,computed:ComputedStyleService,element:HTMLElement,name:string,
                                    frames:Keyframe<HTMLElement,Declaration> array,duration:float,
                                    ?options:KeyframeAnimationOptions,?owner:Owner) =
        Keyframes.CaptureCssPrecise(scheduler,computed,[|element|],name,frames,duration,?options=options,?owner=owner)
    /// <summary>Captures computed values on nonempty elements with contextual units; omitted duration inherits scope defaults.</summary>
    static member CaptureCss(scheduler:FrameScheduler,computed:ComputedStyleService,elements:HTMLElement array,name:string,
                             frames:Keyframe<HTMLElement,Declaration> array,?duration:uint,
                             ?options:KeyframeAnimationOptions,?owner:Owner) =
        Keyframes.CaptureCssPrecise(scheduler,computed,elements,name,frames,AnimationDefaults.duration(duration |> Option.map float),?options=options,?owner=owner)
    /// <summary>Captures one computed element property with contextual units; omitted duration inherits scope defaults.</summary>
    static member CaptureCss(scheduler:FrameScheduler,computed:ComputedStyleService,element:HTMLElement,name:string,
                             frames:Keyframe<HTMLElement,Declaration> array,?duration:uint,
                             ?options:KeyframeAnimationOptions,?owner:Owner) =
        Keyframes.CaptureCss(scheduler,computed,[|element|],name,frames,?duration=duration,?options=options,?owner=owner)
