namespace Ranvier.Anime

open System
open System.Collections.Generic
open System.Collections.Frozen
open Ranvier.Dom
open Ranvier.Css
open Ranvier.Css.Dom
open Ranvier.Anime.ValueInterpolation

module internal TimelineCssKeyframeSequenceBinding =
    /// <summary>Binds typed CSS sequences using native styles, per-element unit contexts and scoped inline snapshots.</summary>
    let css (timeline:Timeline,targets:'Target array,sequence:KeyframeSequence<'Target,Declaration>,
             getStyle:'Target -> CSSStyleDeclaration,getConverter:'Target -> (float -> string -> string -> float) option,
             initial:(unit -> unit) -> 'Target -> CSSStyleDeclaration -> string -> string,initials:Declaration array option,
             duration:float,options:Ranvier.Anime.KeyframeAnimationOptions,position:TimelinePosition option) =
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
        let defaults={FrameDefaults.Default with Duration=duration;Delay=options.Playback.Delay;Ease=options.Ease;Modifier=options.Modifier}
        KeyframeCompilation.validate(defaults,properties)
        timeline.TryAddOwned((fun context ->
            let animation=new KeyframeAnimation(targets.Length)
            context.Attach animation
            let active=animation.Guard()
            let current()=active() && context.CanWrite()
            let interrupted=InvalidOperationException("Owned style sequence construction was superseded.")
            let ensure()=if not(current()) then raise interrupted
            let read action =
                ensure()
                let value=action()
                ensure()
                value
            try
                let used=columns |> Array.exists(fun (_,frames,_,_) -> frames.Length>0)
                let styles=
                    if not used then [||]
                    else targets |> Array.map(fun target ->
                        let style=read(fun () -> getStyle target)
                        if isNull(box style) then invalidArg "targets" "A style declaration is required."
                        style)
                let originals=cells |> Array.map(fun (target,_,name,frames,_,_) ->
                    if frames.Length=0 then None
                    else Some(CssPropertySnapshot.Capture(styles[target],name,ensure)))
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
                let converters=if used then targets |> Array.map(fun target -> read(fun () -> getConverter target)) else Array.create targets.Length None
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
                        | None -> initial ensure targets[target] styles[target] name)
                    Text(if value="auto" then "0" else value)
                let names=(definitions |> Array.mapi(fun index (name,_) -> name,index) |> dict).ToFrozenDictionary(StringComparer.Ordinal)
                let write target name (output:Output)=
                    let property=target*definitions.Length+names[name]
                    let value=Declaration.create name output.Text
                    StyleDeclaration.apply styles[target] (if important[property] then Declaration.important value else value)
                let keys=cells |> Array.map(fun (target,_,name,frames,_,_) ->
                    struct((if frames.Length=0 then box targets[target] else box styles[target]),name))
                TimelineKeyframeBinding.install(context,animation,defaults,properties,keys,resolve,original,write,true,precision,options,current)
            with error when obj.ReferenceEquals(error,interrupted) -> None),
            ?position=position,zeroDuration=(duration<=1e-11))
        |> Option.defaultWith(fun () -> invalidOp "Owned keyframe construction was superseded.")

/// <summary>Timeline-owned sparse typed CSS rows and normalized offset sequences using existing styles and computed services.</summary>
[<AutoOpen>]
module TimelineCssKeyframeSequenceExtensions =
    type Timeline with
        /// <summary>Animates a sparse sequence on nonempty targets using explicit typed initial declarations and precise milliseconds.</summary>
        member this.AddKeyframesPrecise(styles:CSSStyleDeclaration array,initials:Declaration array,sequence:KeyframeSequence<CSSStyleDeclaration,Declaration>,duration:float,?options:Ranvier.Anime.KeyframeAnimationOptions,?position:TimelinePosition) =
            TimelineCssKeyframeSequenceBinding.css(this,styles,sequence,id,(fun _ -> None),
                                        (fun _ _ _ _ -> invalidOp "Explicit initial values are required."),Some initials,duration,defaultArg options Ranvier.Anime.KeyframeAnimationOptions.Default,position)
        /// <summary>Animates one target using explicit typed initial declarations and precise milliseconds.</summary>
        member this.AddKeyframesPrecise(style:CSSStyleDeclaration,initials:Declaration array,sequence:KeyframeSequence<CSSStyleDeclaration,Declaration>,duration:float,?options:Ranvier.Anime.KeyframeAnimationOptions,?position:TimelinePosition) =
            this.AddKeyframesPrecise([|style|],initials,sequence,duration,?options=options,?position=position)
        /// <summary>Animates nonempty targets from typed initial declarations; omitted duration inherits scope defaults.</summary>
        member this.AddKeyframes(styles:CSSStyleDeclaration array,initials:Declaration array,sequence:KeyframeSequence<CSSStyleDeclaration,Declaration>,?duration:uint,?options:Ranvier.Anime.KeyframeAnimationOptions,?position:TimelinePosition) =
            this.AddKeyframesPrecise(styles,initials,sequence,AnimationDefaults.duration(duration |> Option.map float),?options=options,?position=position)
        /// <summary>Animates one target from typed initial declarations; omitted duration inherits scope defaults.</summary>
        member this.AddKeyframes(style:CSSStyleDeclaration,initials:Declaration array,sequence:KeyframeSequence<CSSStyleDeclaration,Declaration>,?duration:uint,?options:Ranvier.Anime.KeyframeAnimationOptions,?position:TimelinePosition) =
            this.AddKeyframesPrecise(style,initials,sequence,AnimationDefaults.duration(duration |> Option.map float),?options=options,?position=position)
        /// <summary>Animates a sparse sequence on nonempty targets using explicit typed declarations and per-element unit contexts and
        /// precise milliseconds.</summary>
        member this.AddKeyframesPrecise(elements:HTMLElement array,initials:Declaration array,sequence:KeyframeSequence<HTMLElement,Declaration>,duration:float,?options:Ranvier.Anime.KeyframeAnimationOptions,?position:TimelinePosition) =
            TimelineCssKeyframeSequenceBinding.css(this,elements,sequence,KeyframeBinding.elementStyle,(fun element -> Some(CssUnits.convert element)),
                                        (fun _ _ _ _ -> invalidOp "Explicit initial values are required."),Some initials,duration,defaultArg options Ranvier.Anime.KeyframeAnimationOptions.Default,position)
        /// <summary>Animates one target using explicit typed declarations and per-element unit contexts and precise milliseconds.</summary>
        member this.AddKeyframesPrecise(element:HTMLElement,initials:Declaration array,sequence:KeyframeSequence<HTMLElement,Declaration>,duration:float,?options:Ranvier.Anime.KeyframeAnimationOptions,?position:TimelinePosition) =
            this.AddKeyframesPrecise([|element|],initials,sequence,duration,?options=options,?position=position)
        /// <summary>Animates nonempty targets using explicit typed declarations and per-element unit contexts; optional unsigned milliseconds;
        /// omitted duration inherits scope defaults.</summary>
        member this.AddKeyframes(elements:HTMLElement array,initials:Declaration array,sequence:KeyframeSequence<HTMLElement,Declaration>,?duration:uint,?options:Ranvier.Anime.KeyframeAnimationOptions,?position:TimelinePosition) =
            this.AddKeyframesPrecise(elements,initials,sequence,AnimationDefaults.duration(duration |> Option.map float),?options=options,?position=position)
        /// <summary>Animates one target using explicit typed declarations and per-element unit contexts; optional unsigned milliseconds;
        /// omitted duration inherits scope defaults.</summary>
        member this.AddKeyframes(element:HTMLElement,initials:Declaration array,sequence:KeyframeSequence<HTMLElement,Declaration>,?duration:uint,?options:Ranvier.Anime.KeyframeAnimationOptions,?position:TimelinePosition) =
            this.AddKeyframesPrecise(element,initials,sequence,AnimationDefaults.duration(duration |> Option.map float),?options=options,?position=position)
        /// <summary>Animates a sparse sequence on nonempty targets using captured inline property values and precise milliseconds.</summary>
        member this.CaptureInlineKeyframesPrecise(styles:CSSStyleDeclaration array,sequence:KeyframeSequence<CSSStyleDeclaration,Declaration>,duration:float,?options:Ranvier.Anime.KeyframeAnimationOptions,?position:TimelinePosition) =
            TimelineCssKeyframeSequenceBinding.css(this,styles,sequence,id,(fun _ -> None),
                                        (fun _ _ style name -> style.getPropertyValue name),None,duration,defaultArg options Ranvier.Anime.KeyframeAnimationOptions.Default,position)
        /// <summary>Animates one target using captured inline property values and precise milliseconds.</summary>
        member this.CaptureInlineKeyframesPrecise(style:CSSStyleDeclaration,sequence:KeyframeSequence<CSSStyleDeclaration,Declaration>,duration:float,?options:Ranvier.Anime.KeyframeAnimationOptions,?position:TimelinePosition) =
            this.CaptureInlineKeyframesPrecise([|style|],sequence,duration,?options=options,?position=position)
        /// <summary>Animates nonempty targets from inline values; omitted duration inherits scope defaults.</summary>
        member this.CaptureInlineKeyframes(styles:CSSStyleDeclaration array,sequence:KeyframeSequence<CSSStyleDeclaration,Declaration>,?duration:uint,?options:Ranvier.Anime.KeyframeAnimationOptions,?position:TimelinePosition) =
            this.CaptureInlineKeyframesPrecise(styles,sequence,AnimationDefaults.duration(duration |> Option.map float),?options=options,?position=position)
        /// <summary>Animates one target from inline values; omitted duration inherits scope defaults.</summary>
        member this.CaptureInlineKeyframes(style:CSSStyleDeclaration,sequence:KeyframeSequence<CSSStyleDeclaration,Declaration>,?duration:uint,?options:Ranvier.Anime.KeyframeAnimationOptions,?position:TimelinePosition) =
            this.CaptureInlineKeyframesPrecise(style,sequence,AnimationDefaults.duration(duration |> Option.map float),?options=options,?position=position)
        /// <summary>Animates a sparse sequence on nonempty targets using actual computed styles and per-element unit contexts and precise
        /// milliseconds.</summary>
        member this.CaptureCssKeyframesPrecise(computed:ComputedStyleService,elements:HTMLElement array,sequence:KeyframeSequence<HTMLElement,Declaration>,duration:float,?options:Ranvier.Anime.KeyframeAnimationOptions,?position:TimelinePosition) =
            if isNull(box computed) then nullArg "computed"
            let initial ensure (element:HTMLElement) _ name =
                let style=computed.Get((element:>Element),None)
                ensure()
                style.getPropertyValue name
            TimelineCssKeyframeSequenceBinding.css(this,elements,sequence,KeyframeBinding.elementStyle,(fun element -> Some(CssUnits.convert element)),
                                        initial,None,duration,defaultArg options Ranvier.Anime.KeyframeAnimationOptions.Default,position)
        /// <summary>Animates one target using actual computed styles and per-element unit contexts and precise milliseconds.</summary>
        member this.CaptureCssKeyframesPrecise(computed:ComputedStyleService,element:HTMLElement,sequence:KeyframeSequence<HTMLElement,Declaration>,duration:float,?options:Ranvier.Anime.KeyframeAnimationOptions,?position:TimelinePosition) =
            this.CaptureCssKeyframesPrecise(computed,[|element|],sequence,duration,?options=options,?position=position)
        /// <summary>Animates nonempty targets using actual computed styles and per-element unit contexts; optional unsigned milliseconds;
        /// omitted duration inherits scope defaults.</summary>
        member this.CaptureCssKeyframes(computed:ComputedStyleService,elements:HTMLElement array,sequence:KeyframeSequence<HTMLElement,Declaration>,?duration:uint,?options:Ranvier.Anime.KeyframeAnimationOptions,?position:TimelinePosition) =
            this.CaptureCssKeyframesPrecise(computed,elements,sequence,AnimationDefaults.duration(duration |> Option.map float),?options=options,?position=position)
        /// <summary>Animates one target using actual computed styles and per-element unit contexts; unsigned nominal duration defaults to
        /// 1000 ms.</summary>
        member this.CaptureCssKeyframes(computed:ComputedStyleService,element:HTMLElement,sequence:KeyframeSequence<HTMLElement,Declaration>,?duration:uint,?options:Ranvier.Anime.KeyframeAnimationOptions,?position:TimelinePosition) =
            this.CaptureCssKeyframesPrecise(computed,element,sequence,AnimationDefaults.duration(duration |> Option.map float),?options=options,?position=position)
