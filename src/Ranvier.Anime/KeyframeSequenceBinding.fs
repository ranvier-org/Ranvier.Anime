namespace Ranvier.Anime

open System
open System.Collections.Generic
open System.Collections.Frozen
open Ranvier
open Ranvier.Dom
open Ranvier.Css
open Ranvier.Css.Dom
open Ranvier.Anime.ValueInterpolation

module internal KeyframeSequenceBinding =
    let private defaults duration (options:Ranvier.Anime.KeyframeAnimationOptions) =
        {FrameDefaults.Default with Duration=duration;Delay=options.Playback.Delay;Ease=options.Ease;Modifier=options.Modifier}

    let private install scheduler (animation:KeyframeAnimation) program write css precision (options:Ranvier.Anime.KeyframeAnimationOptions) current =
        let driver=new KeyframePlayback(scheduler,program,write,options.Composition=KeyframeComposition.Replace,css,precision,options.Playback,
                                       onBegin=options.OnBegin,onBeforeUpdate=options.OnBeforeUpdate,onRender=options.OnRender,
                                       onUpdate=options.OnUpdate,onLoop=options.OnLoop,onPause=options.OnPause,onComplete=options.OnComplete)
        animation.SetDriver driver
        driver.Clock.OnFault<-animation.Cancel
        driver.Clock.OnCancel<-animation.Cancel
        driver.Initialize(notifyConstruction=not options.Autoplay)
        if current() && options.Autoplay then driver.Clock.Resume()
        animation

    /// <summary>Binds named numeric handles across equal-size target arrays to one owned shared driver.</summary>
    let numeric (scheduler:FrameScheduler,bindings:(string*NumericProperty array) array,sequence:KeyframeSequence<NumericProperty,float>,
                 duration:float,options:Ranvier.Anime.KeyframeAnimationOptions,owner:Owner option) =
        let precision=KeyframeBinding.validate(duration,options)
        if isNull bindings || bindings.Length=0 then invalidArg "properties" "Expected nonempty named numeric properties."
        if isNull(box sequence) then nullArg "sequence"
        let captured=Dictionary<string,NumericProperty array>(StringComparer.Ordinal)
        let mutable targetCount=0
        for name,targets in bindings do
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
        let cells=
            Array.init targetCount (fun target ->
                columns |> Array.map(fun (name,frames,handles,templates) -> target,name,frames,handles[target],templates))
            |> Array.concat
        let properties=cells |> Array.map(fun (target,name,_,_,templates) ->
            {FrameProperty.Target=target;Name=name;Original=Number 0.;Frames=templates;ConvertUnit=None})
        let defaults=defaults duration options
        KeyframeCompilation.validate(defaults,properties)
        let animation=new KeyframeAnimation(targetCount)
        try
            owner |> Option.iter(fun owner -> owner.Attach animation)
            let current=animation.Guard()
            let read index =
                animation.EnsureLive()
                let _,_,_,handle,_=cells[index]
                let value=handle.Value
                animation.EnsureLive()
                value
            let originals=cells |> Array.mapi(fun index (_,_,frames,_,_) -> if frames.Length=0 then None else Some(read index))
            animation.SetRestore(fun current ->
                let mutable failure:exn option=None
                for index in 0..cells.Length-1 do
                    if current() then
                        match originals[index] with
                        | Some value ->
                            try
                                let _,_,_,handle,_=cells[index]
                                handle.Value<-value
                            with error -> if failure.IsNone then failure<-Some error
                        | None -> ()
                failure |> Option.iter(fun error -> Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw()))
            let resolve property frame previous =
                let target,_,frames,handle,_=cells[property]
                let previous=previous |> Option.bind(fun (tween:FrameTween) -> tween.CachedEnd.Scalar)
                frames[frame].Resolve(KeyframeContext(handle,uint target,uint targetCount,previous),number,current)
            let program=KeyframeCompilation.compileDynamic(options.Composition=KeyframeComposition.Replace,defaults,properties,resolve,(fun index -> Number(read index)),current)
            animation.EnsureLive()
            let program=program |> Option.defaultWith(fun () -> invalidOp "Keyframe construction was superseded.")
            let write target name (output:Output)=handles[name].[target].Value<-output.Scalar.Value
            install scheduler animation program write false precision options current
        with _ ->
            try animation.Cancel() with _ -> ()
            reraise()

    /// <summary>Binds typed CSS sequences using native styles, per-element unit contexts and scoped inline snapshots.</summary>
    let css (scheduler:FrameScheduler,targets:'Target array,sequence:KeyframeSequence<'Target,Declaration>,
             getStyle:'Target -> CSSStyleDeclaration,getConverter:'Target -> (float -> string -> string -> float) option,
             initial:KeyframeAnimation -> 'Target -> CSSStyleDeclaration -> string -> string,initials:Declaration array option,
             duration:float,options:Ranvier.Anime.KeyframeAnimationOptions,owner:Owner option) =
        let precision=KeyframeBinding.validate(duration,options)
        if isNull targets || targets.Length=0 then invalidArg "targets" "Expected nonempty CSS targets."
        if isNull(box sequence) then nullArg "sequence"
        let targets=Array.copy targets
        if targets |> Array.exists(fun target -> isNull(box target)) then invalidArg "targets" "CSS targets are required."
        let explicitValues=
            initials |> Option.map(fun values ->
                if isNull values then nullArg "initials"
                let captured=Dictionary<string,struct(string*bool)>(StringComparer.Ordinal)
                for value in Array.copy values do
                    let name,text,important=KeyframeBinding.explicitInitial value
                    captured[name]<-struct(text,important)
                captured.ToFrozenDictionary(StringComparer.Ordinal))
        let definitions=sequence.Capture duration
        let constantImportant=Array.zeroCreate<bool> definitions.Length
        let columns=definitions |> Array.mapi(fun property (name,frames) ->
            explicitValues |> Option.iter(fun values ->
                match values.TryGetValue name with
                | true,struct(_,important) -> constantImportant[property]<-important
                | _ -> invalidArg "initials" ("Missing initial declaration: "+name))
            let literal (value:Declaration) =
                if Declaration.name value<>name then invalidArg "declaration" "Frame endpoints must name their sequence property."
                Text(Declaration.value value)
            let template value =
                constantImportant[property]<-constantImportant[property] || Declaration.isImportant value
                literal value
            name,frames,literal,(frames |> Array.map(fun frame -> frame.Template template)))
        let cells=
            Array.init targets.Length (fun target ->
                columns |> Array.mapi(fun property (name,frames,literal,templates) -> target,property,name,frames,literal,templates))
            |> Array.concat
        let properties=cells |> Array.map(fun (target,_,name,_,_,templates) ->
            {FrameProperty.Target=target;Name=name;Original=Number 0.;Frames=templates;ConvertUnit=None})
        let defaults=defaults duration options
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
            let used=columns |> Array.exists(fun (_,frames,_,_) -> frames.Length>0)
            let styles=
                if not used then [||]
                else targets |> Array.map(fun target ->
                    let style=read(fun () -> getStyle target)
                    if isNull(box style) then invalidArg "targets" "A style declaration is required."
                    style)
            let originals=cells |> Array.map(fun (target,_,name,frames,_,_) ->
                if frames.Length=0 then None
                else Some(CssPropertySnapshot.Capture(styles[target],name,animation.EnsureLive)))
            let important=cells |> Array.mapi(fun index (_,property,_,_,_,_) ->
                constantImportant[property] || (originals[index] |> Option.exists _.Important))
            animation.SetRestore(fun current ->
                let mutable failure:exn option=None
                for index in 0..cells.Length-1 do
                    if current() then
                        match originals[index] with
                        | Some snapshot ->
                            try
                                let target,_,_,_,_,_=cells[index]
                                snapshot.Restore(styles[target],current)
                            with error -> if failure.IsNone then failure<-Some error
                        | None -> ()
                failure |> Option.iter(fun error -> Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw()))
            let converters=if used then targets |> Array.map getConverter else Array.create targets.Length None
            let properties=properties |> Array.mapi(fun index property ->
                let target,_,_,frames,_,_=cells[index]
                {property with ConvertUnit=(if frames.Length=0 then None else converters[target])})
            let resolve property frame previous =
                let target,_,name,frames,literal,_=cells[property]
                let previous=previous |> Option.map(fun (tween:FrameTween) ->
                    let value=Declaration.create name tween.CachedEnd.Text
                    if important[property] then Declaration.important value else value)
                let convert value =
                    let literal=literal value
                    if Declaration.isImportant value then important[property]<-true
                    literal
                frames[frame].Resolve(KeyframeContext(targets[target],uint target,uint targets.Length,previous),convert,current)
            let original property =
                let target,_,name,_,_,_=cells[property]
                let value=read(fun () ->
                    match explicitValues with
                    | Some values -> let struct(value,_)=values[name] in value
                    | None -> initial animation targets[target] styles[target] name)
                Text(if value="auto" then "0" else value)
            let program=KeyframeCompilation.compileDynamic(options.Composition=KeyframeComposition.Replace,defaults,properties,resolve,original,current)
            animation.EnsureLive()
            let program=program |> Option.defaultWith(fun () -> invalidOp "Keyframe construction was superseded.")
            let names=(definitions |> Array.mapi(fun index (name,_) -> name,index) |> dict).ToFrozenDictionary(StringComparer.Ordinal)
            let write target name (output:Output)=
                let property=target*definitions.Length+names[name]
                let value=Declaration.create name output.Text
                StyleDeclaration.apply styles[target] (if important[property] then Declaration.important value else value)
            install scheduler animation program write true precision options current
        with _ ->
            try animation.Cancel() with _ -> ()
            reraise()


/// <summary>Shared-clock sparse-row and normalized-offset overloads on the existing keyframe facade.</summary>
[<AutoOpen>]
module KeyframeSequenceExtensions =
    type Keyframes with
        /// <summary>Animates named numeric property arrays through one clock using finite nonnegative precise milliseconds.</summary>
        static member NumericPrecise(scheduler:FrameScheduler,properties:(string*NumericProperty array) array,sequence:KeyframeSequence<NumericProperty,float>,duration:float,?options:Ranvier.Anime.KeyframeAnimationOptions,?owner:Owner) =
            KeyframeSequenceBinding.numeric(scheduler,properties,sequence,duration,defaultArg options Ranvier.Anime.KeyframeAnimationOptions.Default,owner)
        /// <summary>Animates named single-target numeric properties through one clock using precise milliseconds.</summary>
        static member NumericPrecise(scheduler:FrameScheduler,properties:(string*NumericProperty) array,sequence:KeyframeSequence<NumericProperty,float>,duration:float,?options:Ranvier.Anime.KeyframeAnimationOptions,?owner:Owner) =
            let properties=if isNull properties then null else properties |> Array.map(fun (name,value) -> name,[|value|])
            Keyframes.NumericPrecise(scheduler,properties,sequence,duration,?options=options,?owner=owner)
        /// <summary>Animates named numeric target arrays; optional unsigned milliseconds; omitted duration inherits scope defaults.</summary>
        static member Numeric(scheduler:FrameScheduler,properties:(string*NumericProperty array) array,sequence:KeyframeSequence<NumericProperty,float>,?duration:uint,?options:Ranvier.Anime.KeyframeAnimationOptions,?owner:Owner) =
            Keyframes.NumericPrecise(scheduler,properties,sequence,AnimationDefaults.duration(duration |> Option.map float),?options=options,?owner=owner)
        /// <summary>Animates named numeric properties; omitted duration inherits scope defaults.</summary>
        static member Numeric(scheduler:FrameScheduler,properties:(string*NumericProperty) array,sequence:KeyframeSequence<NumericProperty,float>,?duration:uint,?options:Ranvier.Anime.KeyframeAnimationOptions,?owner:Owner) =
            Keyframes.NumericPrecise(scheduler,properties,sequence,AnimationDefaults.duration(duration |> Option.map float),?options=options,?owner=owner)
        /// <summary>Animates a sparse sequence on nonempty targets using explicit typed initial declarations and precise milliseconds.</summary>
        static member DeclarationPrecise(scheduler:FrameScheduler,styles:CSSStyleDeclaration array,initials:Declaration array,sequence:KeyframeSequence<CSSStyleDeclaration,Declaration>,duration:float,?options:Ranvier.Anime.KeyframeAnimationOptions,?owner:Owner) =
            KeyframeSequenceBinding.css(scheduler,styles,sequence,id,(fun _ -> None),
                                        (fun _ _ _ _ -> invalidOp "Explicit initial values are required."),Some initials,duration,defaultArg options Ranvier.Anime.KeyframeAnimationOptions.Default,owner)
        /// <summary>Animates one target using explicit typed initial declarations and precise milliseconds.</summary>
        static member DeclarationPrecise(scheduler:FrameScheduler,style:CSSStyleDeclaration,initials:Declaration array,sequence:KeyframeSequence<CSSStyleDeclaration,Declaration>,duration:float,?options:Ranvier.Anime.KeyframeAnimationOptions,?owner:Owner) =
            Keyframes.DeclarationPrecise(scheduler,[|style|],initials,sequence,duration,?options=options,?owner=owner)
        /// <summary>Animates nonempty targets from typed initial declarations; omitted duration inherits scope defaults.</summary>
        static member Declaration(scheduler:FrameScheduler,styles:CSSStyleDeclaration array,initials:Declaration array,sequence:KeyframeSequence<CSSStyleDeclaration,Declaration>,?duration:uint,?options:Ranvier.Anime.KeyframeAnimationOptions,?owner:Owner) =
            Keyframes.DeclarationPrecise(scheduler,styles,initials,sequence,AnimationDefaults.duration(duration |> Option.map float),?options=options,?owner=owner)
        /// <summary>Animates one target from typed initial declarations; omitted duration inherits scope defaults.</summary>
        static member Declaration(scheduler:FrameScheduler,style:CSSStyleDeclaration,initials:Declaration array,sequence:KeyframeSequence<CSSStyleDeclaration,Declaration>,?duration:uint,?options:Ranvier.Anime.KeyframeAnimationOptions,?owner:Owner) =
            Keyframes.DeclarationPrecise(scheduler,style,initials,sequence,AnimationDefaults.duration(duration |> Option.map float),?options=options,?owner=owner)
        /// <summary>Animates a sparse sequence on nonempty targets using explicit typed declarations and per-element unit contexts and
        /// precise milliseconds.</summary>
        static member DeclarationOnPrecise(scheduler:FrameScheduler,elements:HTMLElement array,initials:Declaration array,sequence:KeyframeSequence<HTMLElement,Declaration>,duration:float,?options:Ranvier.Anime.KeyframeAnimationOptions,?owner:Owner) =
            KeyframeSequenceBinding.css(scheduler,elements,sequence,KeyframeBinding.elementStyle,(fun element -> Some(CssUnits.convert element)),
                                        (fun _ _ _ _ -> invalidOp "Explicit initial values are required."),Some initials,duration,defaultArg options Ranvier.Anime.KeyframeAnimationOptions.Default,owner)
        /// <summary>Animates one target using explicit typed declarations and per-element unit contexts and precise milliseconds.</summary>
        static member DeclarationOnPrecise(scheduler:FrameScheduler,element:HTMLElement,initials:Declaration array,sequence:KeyframeSequence<HTMLElement,Declaration>,duration:float,?options:Ranvier.Anime.KeyframeAnimationOptions,?owner:Owner) =
            Keyframes.DeclarationOnPrecise(scheduler,[|element|],initials,sequence,duration,?options=options,?owner=owner)
        /// <summary>Animates nonempty targets using explicit typed declarations and per-element unit contexts; optional unsigned milliseconds;
        /// omitted duration inherits scope defaults.</summary>
        static member DeclarationOn(scheduler:FrameScheduler,elements:HTMLElement array,initials:Declaration array,sequence:KeyframeSequence<HTMLElement,Declaration>,?duration:uint,?options:Ranvier.Anime.KeyframeAnimationOptions,?owner:Owner) =
            Keyframes.DeclarationOnPrecise(scheduler,elements,initials,sequence,AnimationDefaults.duration(duration |> Option.map float),?options=options,?owner=owner)
        /// <summary>Animates one target using explicit typed declarations and per-element unit contexts; optional unsigned milliseconds;
        /// omitted duration inherits scope defaults.</summary>
        static member DeclarationOn(scheduler:FrameScheduler,element:HTMLElement,initials:Declaration array,sequence:KeyframeSequence<HTMLElement,Declaration>,?duration:uint,?options:Ranvier.Anime.KeyframeAnimationOptions,?owner:Owner) =
            Keyframes.DeclarationOnPrecise(scheduler,element,initials,sequence,AnimationDefaults.duration(duration |> Option.map float),?options=options,?owner=owner)
        /// <summary>Animates a sparse sequence on nonempty targets using captured inline property values and precise milliseconds.</summary>
        static member CaptureInlinePrecise(scheduler:FrameScheduler,styles:CSSStyleDeclaration array,sequence:KeyframeSequence<CSSStyleDeclaration,Declaration>,duration:float,?options:Ranvier.Anime.KeyframeAnimationOptions,?owner:Owner) =
            KeyframeSequenceBinding.css(scheduler,styles,sequence,id,(fun _ -> None),
                                        (fun _ _ style name -> style.getPropertyValue name),None,duration,defaultArg options Ranvier.Anime.KeyframeAnimationOptions.Default,owner)
        /// <summary>Animates one target using captured inline property values and precise milliseconds.</summary>
        static member CaptureInlinePrecise(scheduler:FrameScheduler,style:CSSStyleDeclaration,sequence:KeyframeSequence<CSSStyleDeclaration,Declaration>,duration:float,?options:Ranvier.Anime.KeyframeAnimationOptions,?owner:Owner) =
            Keyframes.CaptureInlinePrecise(scheduler,[|style|],sequence,duration,?options=options,?owner=owner)
        /// <summary>Animates nonempty targets from inline values; omitted duration inherits scope defaults.</summary>
        static member CaptureInline(scheduler:FrameScheduler,styles:CSSStyleDeclaration array,sequence:KeyframeSequence<CSSStyleDeclaration,Declaration>,?duration:uint,?options:Ranvier.Anime.KeyframeAnimationOptions,?owner:Owner) =
            Keyframes.CaptureInlinePrecise(scheduler,styles,sequence,AnimationDefaults.duration(duration |> Option.map float),?options=options,?owner=owner)
        /// <summary>Animates one target from inline values; omitted duration inherits scope defaults.</summary>
        static member CaptureInline(scheduler:FrameScheduler,style:CSSStyleDeclaration,sequence:KeyframeSequence<CSSStyleDeclaration,Declaration>,?duration:uint,?options:Ranvier.Anime.KeyframeAnimationOptions,?owner:Owner) =
            Keyframes.CaptureInlinePrecise(scheduler,style,sequence,AnimationDefaults.duration(duration |> Option.map float),?options=options,?owner=owner)
        /// <summary>Animates a sparse sequence on nonempty targets using actual computed styles and per-element unit contexts and precise
        /// milliseconds.</summary>
        static member CaptureCssPrecise(scheduler:FrameScheduler,computed:ComputedStyleService,elements:HTMLElement array,sequence:KeyframeSequence<HTMLElement,Declaration>,duration:float,?options:Ranvier.Anime.KeyframeAnimationOptions,?owner:Owner) =
            if isNull(box computed) then nullArg "computed"
            let initial (animation:KeyframeAnimation) (element:HTMLElement) _ name =
                let style=computed.Get((element:>Element),None)
                animation.EnsureLive()
                style.getPropertyValue name
            KeyframeSequenceBinding.css(scheduler,elements,sequence,KeyframeBinding.elementStyle,(fun element -> Some(CssUnits.convert element)),
                                        initial,None,duration,defaultArg options Ranvier.Anime.KeyframeAnimationOptions.Default,owner)
        /// <summary>Animates one target using actual computed styles and per-element unit contexts and precise milliseconds.</summary>
        static member CaptureCssPrecise(scheduler:FrameScheduler,computed:ComputedStyleService,element:HTMLElement,sequence:KeyframeSequence<HTMLElement,Declaration>,duration:float,?options:Ranvier.Anime.KeyframeAnimationOptions,?owner:Owner) =
            Keyframes.CaptureCssPrecise(scheduler,computed,[|element|],sequence,duration,?options=options,?owner=owner)
        /// <summary>Animates nonempty targets using actual computed styles and per-element unit contexts; optional unsigned milliseconds;
        /// omitted duration inherits scope defaults.</summary>
        static member CaptureCss(scheduler:FrameScheduler,computed:ComputedStyleService,elements:HTMLElement array,sequence:KeyframeSequence<HTMLElement,Declaration>,?duration:uint,?options:Ranvier.Anime.KeyframeAnimationOptions,?owner:Owner) =
            Keyframes.CaptureCssPrecise(scheduler,computed,elements,sequence,AnimationDefaults.duration(duration |> Option.map float),?options=options,?owner=owner)
        /// <summary>Animates one target using actual computed styles and per-element unit contexts; unsigned nominal duration defaults to
        /// 1000 ms.</summary>
        static member CaptureCss(scheduler:FrameScheduler,computed:ComputedStyleService,element:HTMLElement,sequence:KeyframeSequence<HTMLElement,Declaration>,?duration:uint,?options:Ranvier.Anime.KeyframeAnimationOptions,?owner:Owner) =
            Keyframes.CaptureCssPrecise(scheduler,computed,element,sequence,AnimationDefaults.duration(duration |> Option.map float),?options=options,?owner=owner)
