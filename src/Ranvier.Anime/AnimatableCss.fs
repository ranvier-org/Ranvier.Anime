namespace Ranvier.Anime

open System
open Ranvier.Css
open Ranvier.Css.Dom
open Ranvier.Dom
open Ranvier.Anime.ValueInterpolation

/// <summary>A cached scalar, color or compound CSS property handle using existing Ranvier.Css declarations.</summary>
/// <remarks>Getters read the first target's cache. Retargeting retains omitted duration/easing and captured component templates.</remarks>
[<Sealed>]
type AnimatableDeclaration internal (track:AnimatableTrack,name:string,metadata:Data array,important:bool array,
                                     converters:(float -> string -> string -> float) option array,group:Animatable) =
    let live ()=group.EnsureLive()
    let options duration ease =
        live()
        if track.Clock.Cancelled then raise(ObjectDisposedException(nameof AnimatableDeclaration))
        duration |> Option.iter(fun value ->
            if not(Double.IsFinite value) || value<0. then invalidArg "duration" "Expected finite nonnegative milliseconds.")
        ease |> Option.iter(fun callback -> if isNull(box callback) then invalidArg "ease" "An easing callback is required.")
    let retarget (declaration:Declaration,duration,ease) =
        options duration ease
        if Declaration.name declaration<>name then invalidArg "declaration" "The destination must name the captured property."
        let target=decompose(Text(Declaration.value declaration))
        if target.Operator.IsSome then invalidArg "declaration" "Use an absolute destination."
        let expected=track.Clock.Generation
        let current ()=expected=track.Clock.Generation && not track.Clock.Cancelled && not group.Cancelled
        let commit () =
            if Declaration.isImportant declaration then Array.fill important 0 important.Length true
        if target.Kind=ValueKind.Number || target.Kind=ValueKind.Unit then
            let targets=Array.zeroCreate metadata.Length
            for index in 0..metadata.Length-1 do
                if current() then
                    let captured=metadata[index]
                    if captured.Kind<>target.Kind then invalidArg "declaration" "Destination shape must match every captured target."
                    targets[index]<-
                        if captured.Unit=target.Unit then target.Number
                        else
                            match converters[index] with
                            | Some convert -> convert target.Number target.Unit captured.Unit
                            | None -> invalidArg "declaration" "Differing scalar units require an element context."
            if current() then track.RetargetScalars(targets,?newDuration=duration,?newEase=ease,onCommit=commit)
        else
            for captured in metadata do
                if captured.Kind<>target.Kind || (target.Kind=ValueKind.Complex && captured.Strings<>target.Strings) then
                    invalidArg "declaration" "Destination component templates must match every captured target."
            if current() then track.RetargetComponents(target.Components,?newDuration=duration,?newEase=ease,onCommit=commit)
    /// <summary>Composed first-target cache with retained or requested importance.</summary>
    /// <remarks>Interrupted scalar getters raise InvalidOperationException.</remarks>
    member _.Value =
        let output=track.Snapshot 0 |> Option.defaultWith(fun () -> invalidOp "The cached getter was superseded.")
        let value=Declaration.create name output.Text
        if important[0] then Declaration.important value else value
    /// <summary>First-target scalar magnitude with the source getter modifier applied again; component properties reject this getter.</summary>
    member _.Magnitude =
        track.ScalarValue 0 |> Option.defaultWith(fun () -> invalidOp "The cached getter was superseded.")
    /// <summary>An independent snapshot of the first target's cached color or compound components.</summary>
    member _.Components =
        if metadata[0].Kind=ValueKind.Number || metadata[0].Kind=ValueKind.Unit then invalidOp "The property has a scalar value."
        track.Components 0
    /// <summary>The shared property controller, including seek, pause and stretch controls.</summary>
    member _.Playback=track.Clock
    /// <summary>Number of captured native style targets.</summary>
    member _.TargetCount=track.Count
    /// <summary>Retargets an absolute declaration using optional unsigned milliseconds.</summary>
    /// <remarks>An important destination requests retained importance.</remarks>
    member _.To(declaration:Declaration,?duration:uint,?ease:float -> float) =
        retarget(declaration,(duration |> Option.map float),ease)
    /// <summary>Retargets an absolute declaration using optional finite nonnegative precise milliseconds.</summary>
    member _.ToPrecise(declaration:Declaration,?duration:float,?ease:float -> float) = retarget(declaration,duration,ease)
    /// <summary>Retargets scalar magnitudes in each target's captured unit using optional unsigned milliseconds.</summary>
    member _.ToNumber(value:float,?duration:uint,?ease:float -> float) =
        live()
        track.RetargetScalar(value,?newDuration=(duration |> Option.map float),?newEase=ease)
    /// <summary>Retargets scalar magnitudes in captured units using optional finite nonnegative precise milliseconds.</summary>
    member _.ToNumberPrecise(value:float,?duration:float,?ease:float -> float) =
        live()
        track.RetargetScalar(value,?newDuration=duration,?newEase=ease)
    /// <summary>Retargets supplied color/compound components; omitted endpoints persist and surplus finite components are ignored.</summary>
    member _.ToComponents(values:float array,?duration:uint,?ease:float -> float) =
        live()
        track.RetargetComponents(values,?newDuration=(duration |> Option.map float),?newEase=ease)
    /// <summary>Retargets supplied components using optional finite nonnegative precise milliseconds.</summary>
    member _.ToComponentsPrecise(values:float array,?duration:float,?ease:float -> float) =
        live()
        track.RetargetComponents(values,?newDuration=duration,?newEase=ease)

module private CssAnimatableCapture =
    let bind (group:Animatable,styles:CSSStyleDeclaration array,name:string,readInitial:int -> string -> string,
              initialImportant,converters,duration,options:AnimatablePropertyOptions) =
        let precision=group.Prepare(duration,options)
        if String.IsNullOrWhiteSpace name then invalidArg "name" "A CSS property name is required."
        if isNull styles then nullArg "styles"
        if styles.Length=0 then invalidArg "styles" "Expected at least one style target."
        let styles=Array.copy styles
        if styles |> Array.exists(fun style -> isNull(box style)) then invalidArg "styles" "Style targets are required."
        let read action =
            group.EnsureLive()
            let result=action()
            group.EnsureLive()
            result
        let originals=styles |> Array.map(fun style ->
            let value=read(fun () -> style.getPropertyValue name)
            let snapshot=CssPropertySnapshot.Capture(style,name,group.EnsureLive)
            struct(value,snapshot))
        let starts=Array.mapi(fun index struct(value,_) -> read(fun () -> readInitial index value)) originals
        let kernels=starts |> Array.map(fun value -> AnimatableValue(Text value,None,true,None))
        let metadata=kernels |> Array.map _.Metadata
        let important=originals |> Array.map(fun struct(_,original) -> initialImportant || original.Important)
        let write index (output:Output) =
            let declaration=Declaration.create name output.Text
            StyleDeclaration.apply styles[index] (if important[index] then Declaration.important declaration else declaration)
        let restore current =
            let mutable failure:exn option=None
            for index in 0..styles.Length-1 do
                if current() then
                    try
                        let struct(_,original)=originals[index]
                        original.Restore(styles[index],current)
                    with error -> if failure.IsNone then failure<-Some error
            failure |> Option.iter(fun error -> Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw())
        let track=group.Register(kernels,write,restore,duration,options,precision)
        AnimatableDeclaration(track,name,metadata,important,converters,group)
    let explicit (group,styles,declaration,converters,duration,options) =
        let text=Declaration.value declaration
        if (decompose(Text text)).Operator.IsSome then invalidArg "initial" "Use an absolute initial declaration."
        bind(group,styles,Declaration.name declaration,(fun _ _ -> text),Declaration.isImportant declaration,converters,duration,options)
    let contexts (group:Animatable,elements:HTMLElement array) =
        if isNull elements then nullArg "elements"
        let elements=Array.copy elements
        if elements |> Array.exists(fun element -> isNull(box element)) then invalidArg "elements" "Elements are required."
        let styles=elements |> Array.map(fun element ->
            group.EnsureLive()
            let style=((element:>ElementCSSInlineStyle).style:>CSSStyleDeclaration)
            group.EnsureLive()
            style)
        let converters=elements |> Array.map(fun element -> Some(CssUnits.convert element))
        elements,styles,converters

/// <summary>Typed declaration authoring and native CSS capture for existing animatable groups.</summary>
/// <remarks>Revert restores expanded longhands and logical precedence. Unrecoverable pending snapshots reject before dispatch.</remarks>
[<AutoOpen>]
module AnimatableCssExtensions =
    type Animatable with
        /// <summary>Captures nonempty style targets and renders an explicit typed initial declaration.</summary>
        /// <remarks>Duration is finite nonnegative precise milliseconds. Style-only retargets require matching scalar units.</remarks>
        member group.DeclarationPrecise(styles:CSSStyleDeclaration array,initial:Declaration,duration:float,?options:AnimatablePropertyOptions) =
            let converters=if isNull styles then [||] else Array.create styles.Length None
            CssAnimatableCapture.explicit(group,styles,initial,converters,duration,defaultArg options AnimatablePropertyOptions.Default)
        /// <summary>Captures one style target with an explicit typed initial declaration and precise milliseconds.</summary>
        member group.DeclarationPrecise(style:CSSStyleDeclaration,initial:Declaration,duration:float,?options:AnimatablePropertyOptions) =
            group.DeclarationPrecise([|style|],initial,duration,?options=options)
        /// <summary>Captures nonempty style targets with an explicit typed initial declaration; unsigned duration defaults to 1000 ms.</summary>
        member group.Declaration(styles:CSSStyleDeclaration array,initial:Declaration,?duration:uint,?options:AnimatablePropertyOptions) =
            group.DeclarationPrecise(styles,initial,float(defaultArg duration 1000u),?options=options)
        /// <summary>Captures one style target with an explicit typed initial declaration; unsigned duration defaults to 1000 ms.</summary>
        member group.Declaration(style:CSSStyleDeclaration,initial:Declaration,?duration:uint,?options:AnimatablePropertyOptions) =
            group.Declaration([|style|],initial,?duration=duration,?options=options)
        /// <summary>Captures existing inline property values from nonempty style targets with precise milliseconds.</summary>
        /// <remarks>An absent inline value starts at numeric zero. Revert restores each original value, priority and absence.</remarks>
        member group.CaptureInlinePrecise(styles:CSSStyleDeclaration array,name:string,duration:float,?options:AnimatablePropertyOptions) =
            let converters=if isNull styles then [||] else Array.create styles.Length None
            CssAnimatableCapture.bind(group,styles,name,(fun _ value -> value),false,converters,duration,defaultArg options AnimatablePropertyOptions.Default)
        /// <summary>Captures one existing inline property with precise milliseconds.</summary>
        member group.CaptureInlinePrecise(style:CSSStyleDeclaration,name:string,duration:float,?options:AnimatablePropertyOptions) =
            group.CaptureInlinePrecise([|style|],name,duration,?options=options)
        /// <summary>Captures existing inline values from nonempty style targets; unsigned duration defaults to 1000 ms.</summary>
        member group.CaptureInline(styles:CSSStyleDeclaration array,name:string,?duration:uint,?options:AnimatablePropertyOptions) =
            group.CaptureInlinePrecise(styles,name,float(defaultArg duration 1000u),?options=options)
        /// <summary>Captures one existing inline property; unsigned duration defaults to 1000 ms.</summary>
        member group.CaptureInline(style:CSSStyleDeclaration,name:string,?duration:uint,?options:AnimatablePropertyOptions) =
            group.CaptureInline([|style|],name,?duration=duration,?options=options)
        /// <summary>Authors an explicit initial declaration on nonempty elements with native scalar-unit conversion at retarget time.</summary>
        member group.DeclarationOnPrecise(elements:HTMLElement array,initial:Declaration,duration:float,?options:AnimatablePropertyOptions) =
            let options=defaultArg options AnimatablePropertyOptions.Default
            group.Prepare(duration,options) |> ignore
            let _,styles,converters=CssAnimatableCapture.contexts(group,elements)
            CssAnimatableCapture.explicit(group,styles,initial,converters,duration,options)
        /// <summary>Authors an initial declaration on one element with native scalar-unit conversion and precise milliseconds.</summary>
        member group.DeclarationOnPrecise(element:HTMLElement,initial:Declaration,duration:float,?options:AnimatablePropertyOptions) =
            group.DeclarationOnPrecise([|element|],initial,duration,?options=options)
        /// <summary>Authors an initial declaration on nonempty elements with unit contexts; unsigned duration defaults to 1000 ms.</summary>
        member group.DeclarationOn(elements:HTMLElement array,initial:Declaration,?duration:uint,?options:AnimatablePropertyOptions) =
            group.DeclarationOnPrecise(elements,initial,float(defaultArg duration 1000u),?options=options)
        /// <summary>Authors an initial declaration on one element with its unit context; unsigned duration defaults to 1000 ms.</summary>
        member group.DeclarationOn(element:HTMLElement,initial:Declaration,?duration:uint,?options:AnimatablePropertyOptions) =
            group.DeclarationOn([|element|],initial,?duration=duration,?options=options)
        /// <summary>Captures computed values through Ranvier's service and each element's unit context using precise milliseconds.</summary>
        /// <remarks>Writes target inline styles. Revert restores their original inline values, priority and absence.</remarks>
        member group.CaptureCssPrecise(computed:ComputedStyleService,elements:HTMLElement array,name:string,duration:float,
                                       ?options:AnimatablePropertyOptions) =
            let options=defaultArg options AnimatablePropertyOptions.Default
            group.Prepare(duration,options) |> ignore
            if isNull(box computed) then nullArg "computed"
            let elements,styles,converters=CssAnimatableCapture.contexts(group,elements)
            let readComputed index _ =
                let style=computed.Get((elements[index]:>Element),None)
                group.EnsureLive()
                style.getPropertyValue name
            CssAnimatableCapture.bind(group,styles,name,readComputed,false,converters,duration,options)
        /// <summary>Captures one element's computed property through Ranvier's service using precise milliseconds.</summary>
        member group.CaptureCssPrecise(computed:ComputedStyleService,element:HTMLElement,name:string,duration:float,
                                       ?options:AnimatablePropertyOptions) =
            group.CaptureCssPrecise(computed,[|element|],name,duration,?options=options)
        /// <summary>Captures computed properties and unit contexts from nonempty elements; unsigned duration defaults to 1000 ms.</summary>
        member group.CaptureCss(computed:ComputedStyleService,elements:HTMLElement array,name:string,?duration:uint,
                                ?options:AnimatablePropertyOptions) =
            group.CaptureCssPrecise(computed,elements,name,float(defaultArg duration 1000u),?options=options)
        /// <summary>Captures one computed property and unit context; unsigned duration defaults to 1000 ms.</summary>
        member group.CaptureCss(computed:ComputedStyleService,element:HTMLElement,name:string,?duration:uint,
                                ?options:AnimatablePropertyOptions) =
            group.CaptureCss(computed,[|element|],name,?duration=duration,?options=options)
