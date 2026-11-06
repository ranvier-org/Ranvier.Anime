namespace Ranvier.Anime

open System
open System.Collections.Generic
open System.Globalization
open System.Runtime.CompilerServices
open System.Text.RegularExpressions
open Ranvier.Dom

/// <summary>Target property dispatch classification matching Anime.js tweenTypes.</summary>
[<RequireQualifiedAccess; Diagnostics.DebuggerDisplay("{ToString(),nq}")>]
type TweenTargetKind =
    | Object
    | Attribute
    | Css
    | CssVar
    | Transform
    /// <summary>Invariant tween classification name.</summary>
    override this.ToString() =
        match this with
        | Object -> "Object"
        | Attribute -> "Attribute"
        | Css -> "Css"
        | CssVar -> "CssVar"
        | Transform -> "Transform"

/// <summary>Mutable key/value property bag representing a plain JS-style animatable object target.</summary>
[<Sealed; Diagnostics.DebuggerDisplay("{ToString(),nq}")>]
type AnimatedObject(?initial: seq<string * AdaptedValue>) =
    let values = Dictionary<string, AdaptedValue>(StringComparer.Ordinal)
    do
        match initial with
        | Some pairs -> for k, v in pairs do values[k] <- v
        | None -> ()

    /// <summary>Gets or sets a named property value on this object.</summary>
    member _.Item
        with get(name: string) = values[name]
        and set (name: string) (value: AdaptedValue) = values[name] <- value

    /// <summary>Attempts to read a named property value.</summary>
    member _.TryGet(name: string) =
        match values.TryGetValue name with
        | true, v -> Some v
        | _ -> None

    /// <summary>Sets a scalar numeric property.</summary>
    member _.SetNumber(name: string, value: float) = values[name] <- AdaptedValue.Scalar value

    /// <summary>Gets a scalar numeric property, parsing numeric strings when needed.</summary>
    member _.GetNumber(name: string) =
        match values.TryGetValue name with
        | true, AdaptedValue.Scalar v -> v
        | true, AdaptedValue.Text s -> Double.Parse(s, CultureInfo.InvariantCulture)
        | _ -> 0.

    /// <summary>Sets a text/unit/compound property.</summary>
    member _.SetText(name: string, value: string) = values[name] <- AdaptedValue.Text value

    /// <summary>Gets the string representation of a property.</summary>
    member _.GetText(name: string) =
        match values.TryGetValue name with
        | true, AdaptedValue.Text s -> s
        | true, AdaptedValue.Scalar v -> ValueInterpolation.number v
        | _ -> ""

    /// <summary>Number of stored properties.</summary>
    member _.Count = values.Count

    /// <summary>Invariant diagnostic of stored property count.</summary>
    override _.ToString() = "AnimatedObject(Count=" + values.Count.ToString(CultureInfo.InvariantCulture) + ")"

/// <summary>Per-property endpoint and timing specification for <see cref="T:Ranvier.Anime.ComposedAnimation"/>.</summary>
[<NoEquality; NoComparison; Diagnostics.DebuggerDisplay("{ToString(),nq}")>]
type TweenStep =
    { /// <summary>Optional explicit starting value (Scalar or Text).</summary>
      From: AdaptedValue option
      /// <summary>Optional destination value (Scalar or Text); defaults to original value when omitted.</summary>
      To: AdaptedValue option
      /// <summary>Optional target-aware destination callback (target, index, total).</summary>
      ToBy: (obj -> int -> int -> AdaptedValue) option
      /// <summary>Optional step duration override in the engine's time unit.</summary>
      Duration: float option
      /// <summary>Optional target-aware step duration callback.</summary>
      DurationBy: (obj -> int -> int -> float) option
      /// <summary>Optional step delay in the engine's time unit.</summary>
      Delay: float option
      /// <summary>Optional target-aware step delay callback.</summary>
      DelayBy: (obj -> int -> int -> float) option
      /// <summary>Optional per-step easing function.</summary>
      Ease: (float -> float) option
      /// <summary>Optional per-step component modifier.</summary>
      Modifier: (float -> float) option
      /// <summary>Optional per-step composition override.</summary>
      Composition: KeyframeComposition option }
    /// <summary>Empty step inheriting outer animation timing, easing, modifier and composition.</summary>
    static member Empty =
        { From = None; To = None; ToBy = None; Duration = None; DurationBy = None
          Delay = None; DelayBy = None; Ease = None; Modifier = None; Composition = None }
    /// <summary>Creates a single scalar destination step.</summary>
    static member ToScalar(value: float) = { TweenStep.Empty with To = Some (AdaptedValue.Scalar value) }
    /// <summary>Creates a single string/CSS/unit/color destination step.</summary>
    static member ToText(value: string) = { TweenStep.Empty with To = Some (AdaptedValue.Text value) }
    /// <summary>Creates an explicit scalar from/to step.</summary>
    static member FromToScalar(fromValue: float, toValue: float) =
        { TweenStep.Empty with From = Some (AdaptedValue.Scalar fromValue); To = Some (AdaptedValue.Scalar toValue) }
    /// <summary>Creates an explicit text from/to step.</summary>
    static member FromToText(fromValue: string, toValue: string) =
        { TweenStep.Empty with From = Some (AdaptedValue.Text fromValue); To = Some (AdaptedValue.Text toValue) }
    /// <summary>Invariant diagnostic of the tween step.</summary>
    override this.ToString() =
        "TweenStep(To=" + (this.To |> Option.map _.ToString() |> Option.defaultValue "computed") + ")"

/// <summary>Named property track specification for <see cref="T:Ranvier.Anime.ComposedAnimation"/>.</summary>
[<NoEquality; NoComparison; Diagnostics.DebuggerDisplay("{ToString(),nq}")>]
type PropertySpec =
    { /// <summary>Property, CSS declaration, transform alias or attribute name.</summary>
      Name: string
      /// <summary>Ordered keyframe steps for this property.</summary>
      Steps: TweenStep array }
    /// <summary>Creates a single-step scalar property specification.</summary>
    static member Scalar(name: string, toValue: float, ?fromValue: float, ?duration: float, ?delay: float,
                         ?ease: float -> float, ?modifier: float -> float, ?composition: KeyframeComposition) =
        { Name = name
          Steps =
            [| { TweenStep.Empty with
                   From = fromValue |> Option.map AdaptedValue.Scalar
                   To = Some (AdaptedValue.Scalar toValue)
                   Duration = duration
                   Delay = delay
                   Ease = ease
                   Modifier = modifier
                   Composition = composition } |] }
    /// <summary>Creates a single-step text/CSS/color/unit property specification.</summary>
    static member Text(name: string, toValue: string, ?fromValue: string, ?duration: float, ?delay: float,
                       ?ease: float -> float, ?modifier: float -> float, ?composition: KeyframeComposition) =
        { Name = name
          Steps =
            [| { TweenStep.Empty with
                   From = fromValue |> Option.map AdaptedValue.Text
                   To = Some (AdaptedValue.Text toValue)
                   Duration = duration
                   Delay = delay
                   Ease = ease
                   Modifier = modifier
                   Composition = composition } |] }
    /// <summary>Creates a multi-step keyframe property specification.</summary>
    static member StepsOf(name: string, steps: TweenStep array) = { Name = name; Steps = steps }
    /// <summary>Invariant diagnostic of the property specification.</summary>
    override this.ToString() = "PropertySpec(" + this.Name + ", Steps=" + this.Steps.Length.ToString(CultureInfo.InvariantCulture) + ")"

/// <summary>Internal DOM/object/adapter classification and value conversion helpers.</summary>
module internal DomTargetHelpers =
    let private lowerCaseRgx = Regex(@"([a-z0-9])([A-Z])", RegexOptions.CultureInvariant)
    let private commonPxCss =
        HashSet<string>(
            [| "width"; "height"; "min-width"; "min-height"; "max-width"; "max-height"
               "top"; "right"; "bottom"; "left"; "inset"
               "margin"; "margin-top"; "margin-right"; "margin-bottom"; "margin-left"
               "padding"; "padding-top"; "padding-right"; "padding-bottom"; "padding-left"
               "border-width"; "border-top-width"; "border-right-width"; "border-bottom-width"; "border-left-width"
               "border-radius"; "border-top-left-radius"; "border-top-right-radius"
               "border-bottom-right-radius"; "border-bottom-left-radius"
               "font-size"; "letter-spacing"; "word-spacing"; "line-height"; "outline-width"; "outline-offset"
               "perspective"; "stroke-width"; "stroke-dashoffset" |],
            StringComparer.Ordinal)
    let private svgCssReserved = HashSet<string>([| "opacity"; "rotate"; "overflow"; "color" |], StringComparer.Ordinal)
    let private domObjectProps = HashSet<string>([| "innerHTML"; "textContent"; "value"; "scrollTop"; "scrollLeft" |], StringComparer.Ordinal)

    let toKebab (name: string) =
        if String.IsNullOrEmpty name || name.StartsWith("--", StringComparison.Ordinal) then name
        else lowerCaseRgx.Replace(name, "$1-$2").ToLowerInvariant()

    let tryStyle (target: obj) : CSSStyleDeclaration option =
        match target with
        | :? ElementCSSInlineStyle as styled -> Some (styled.style :> CSSStyleDeclaration)
        | _ -> None

    let classify (registry: AdapterRegistry) (target: obj) (rawName: string) : TweenTargetKind * string * TargetAdapterEntry option =
        match registry.Resolve(target, rawName) with
        | Some entry -> TweenTargetKind.Object, rawName, Some entry
        | None ->
            match target with
            | :? Element as el ->
                if domObjectProps.Contains rawName then
                    TweenTargetKind.Object, rawName, None
                else
                    match ElementTransforms.canonicalName rawName with
                    | Some canonical -> TweenTargetKind.Transform, canonical, None
                    | None when rawName.StartsWith("--", StringComparison.Ordinal) ->
                        TweenTargetKind.CssVar, rawName, None
                    | None ->
                        let isSvg = el :? SVGElement
                        let kebab = toKebab rawName
                        if isSvg && not (svgCssReserved.Contains rawName) && not (isNull (el.getAttribute rawName)) then
                            TweenTargetKind.Attribute, rawName, None
                        elif isSvg && (rawName = "d" || rawName = "points" || rawName = "viewBox" || rawName = "cx" || rawName = "cy" || rawName = "r" || rawName = "x1" || rawName = "y1" || rawName = "x2" || rawName = "y2") then
                            TweenTargetKind.Attribute, rawName, None
                        elif not isSvg && (rawName.StartsWith("data-", StringComparison.Ordinal) || rawName = "min" || rawName = "max") then
                            TweenTargetKind.Attribute, rawName, None
                        elif not (isNull (el.getAttribute rawName)) && not (commonPxCss.Contains kebab) && not (kebab.Contains "-") && rawName <> "opacity" && rawName <> "color" && rawName <> "background" && rawName <> "filter" && rawName <> "scale" && rawName <> "translate" then
                            TweenTargetKind.Attribute, rawName, None
                        else
                            TweenTargetKind.Css, kebab, None
            | _ -> TweenTargetKind.Object, rawName, None

    let defaultCssScalarText (kebabName: string) (value: float) =
        let num = ValueInterpolation.number value
        if commonPxCss.Contains kebabName then num + "px" else num

    let readOriginal (computed: ComputedStyleService option) (target: obj) (kind: TweenTargetKind) (canonical: string) (adapter: TargetAdapterEntry option) : ValueInterpolation.Literal * string option =
        match adapter with
        | Some entry ->
            match entry.Get target with
            | AdaptedValue.Scalar n -> ValueInterpolation.Number n, Some (ValueInterpolation.number n)
            | AdaptedValue.Text s -> ValueInterpolation.Text s, Some s
        | None ->
            match kind, target with
            | TweenTargetKind.Transform, (:? Element as el) ->
                let current, inlineOrig = ElementTransforms.readWithInlineOrigin el canonical
                ValueInterpolation.Text current, inlineOrig
            | (TweenTargetKind.Css | TweenTargetKind.CssVar), (:? Element as el) ->
                let style = (tryStyle el).Value
                let inlineVal = style.getPropertyValue canonical
                let inlineOrig = if String.IsNullOrEmpty inlineVal then None else Some inlineVal
                let effective =
                    if not (String.IsNullOrEmpty inlineVal) then inlineVal
                    else
                        match computed with
                        | Some svc ->
                            let v = (svc.Get(el, None)).getPropertyValue canonical
                            if String.IsNullOrEmpty v || v = "auto" || v = "none" then
                                if canonical = "opacity" then "1" elif commonPxCss.Contains canonical then "0px" else "0"
                            else v
                        | None ->
                            if canonical = "opacity" then "1" elif commonPxCss.Contains canonical then "0px" else "0"
                ValueInterpolation.Text effective, inlineOrig
            | TweenTargetKind.Attribute, (:? Element as el) ->
                let raw = el.getAttribute canonical
                let inlineOrig = if isNull raw then None else Some raw
                ValueInterpolation.Text (if isNull raw then "0" else raw), inlineOrig
            | TweenTargetKind.Object, (:? Element as el) ->
                match canonical with
                | "innerHTML" ->
                    let v = (el :?> HTMLElement).innerHTML
                    ValueInterpolation.Text (if isNull v then "" else v), Some (if isNull v then "" else v)
                | "textContent" ->
                    let v = (el :> Node).textContent
                    ValueInterpolation.Text (if isNull v then "" else v), Some (if isNull v then "" else v)
                | "value" ->
                    let raw = el.getAttribute "value"
                    let s = if isNull raw then "0" else raw
                    ValueInterpolation.Text s, Some s
                | _ -> ValueInterpolation.Number 0., None
            | TweenTargetKind.Object, (:? NumericProperty as prop) ->
                let v = prop.Value
                ValueInterpolation.Number v, Some (ValueInterpolation.number v)
            | TweenTargetKind.Object, (:? AnimatedObject as bag) ->
                match bag.TryGet canonical with
                | Some (AdaptedValue.Scalar n) -> ValueInterpolation.Number n, Some (ValueInterpolation.number n)
                | Some (AdaptedValue.Text s) -> ValueInterpolation.Text s, Some s
                | None -> ValueInterpolation.Number 0., None
            | _ -> ValueInterpolation.Number 0., None

    let toLiteral (kind: TweenTargetKind) (canonical: string) (value: AdaptedValue) : ValueInterpolation.Literal =
        match value with
        | AdaptedValue.Scalar n ->
            match kind with
            | TweenTargetKind.Transform -> ValueInterpolation.Text (ElementTransforms.formatScalar canonical n)
            | TweenTargetKind.Css -> ValueInterpolation.Text (defaultCssScalarText canonical n)
            | _ -> ValueInterpolation.Number n
        | AdaptedValue.Text s ->
            match kind with
            | TweenTargetKind.Transform -> ValueInterpolation.Text (ElementTransforms.normalizeText canonical s)
            | _ -> ValueInterpolation.Text s

    let writeOutput (target: obj) (kind: TweenTargetKind) (canonical: string) (adapter: TargetAdapterEntry option)
                    (scalar: float) (output: ValueInterpolation.Output) (flushTransform: bool) (priority: string) =
        match adapter with
        | Some entry ->
            let info = AdaptedTweenInfo(target, canonical, scalar, output.Components)
            entry.Set target output.Scalar info
        | None ->
            match kind, target with
            | TweenTargetKind.Transform, (:? Element as el) ->
                ElementTransforms.stageComponent el canonical output.Text
                if flushTransform then ElementTransforms.flush el priority
            | (TweenTargetKind.Css | TweenTargetKind.CssVar), (:? Element as el) ->
                let style = (tryStyle el).Value
                let valueText =
                    if output.Text.StartsWith("rgba(", StringComparison.Ordinal) && output.Components.Length >= 4 && output.Components[3] = 1. then
                        let r = int (PropertyNumbers.round 1. output.Components[0])
                        let g = int (PropertyNumbers.round 1. output.Components[1])
                        let b = int (PropertyNumbers.round 1. output.Components[2])
                        "rgb(" + string r + ", " + string g + ", " + string b + ")"
                    else output.Text
                style.setProperty(canonical, valueText, priority = priority)
            | TweenTargetKind.Attribute, (:? Element as el) ->
                el.setAttribute(canonical, output.Text)
            | TweenTargetKind.Object, (:? Element as el) ->
                match canonical with
                | "innerHTML" -> (el :?> HTMLElement).innerHTML <- output.Text
                | "textContent" -> (el :> Node).textContent <- output.Text
                | "value" -> el.setAttribute("value", output.Text)
                | _ -> ()
            | TweenTargetKind.Object, (:? NumericProperty as prop) ->
                let v = match output.Scalar with Some s -> s | None -> scalar
                prop.Value <- v
            | TweenTargetKind.Object, (:? AnimatedObject as bag) ->
                match output.Scalar with
                | Some s -> bag.SetNumber(canonical, s)
                | None -> bag.SetText(canonical, output.Text)
            | _ -> ()

/// <summary>Options controlling <see cref="T:Ranvier.Anime.ComposedAnimation"/> construction and playback.</summary>
[<NoEquality; NoComparison; Diagnostics.DebuggerDisplay("{ToString(),nq}")>]
type ComposedAnimationOptions =
    { /// <summary>Shared clock repeat/delay/speed/fps settings.</summary>
      Playback: PlaybackOptions
      /// <summary>Optional target-aware delay provider (overrides Playback.Delay per target).</summary>
      DelayBy: (obj -> int -> int -> float) option
      /// <summary>Default duration in the engine's current time unit.</summary>
      Duration: float
      /// <summary>Optional target-aware duration provider.</summary>
      DurationBy: (obj -> int -> int -> float) option
      /// <summary>Default easing curve.</summary>
      Ease: float -> float
      /// <summary>Default numeric component modifier.</summary>
      Modifier: float -> float
      /// <summary>Default composition mode (Replace by default).</summary>
      Composition: KeyframeComposition
      /// <summary>Decimal rounding precision (inherits Engine.Precision by default).</summary>
      Precision: uint option
      /// <summary>Whether playback starts automatically upon construction.</summary>
      Autoplay: bool
      /// <summary>Optional computed style service for reading initial CSS values on DOM elements.</summary>
      Computed: ComputedStyleService option
      /// <summary>Optional custom adapter registry (uses Adapters.DefaultRegistry when None).</summary>
      Registry: AdapterRegistry option
      OnBegin: ComposedAnimation -> unit
      OnBeforeUpdate: ComposedAnimation -> unit
      OnRender: ComposedAnimation -> unit
      OnUpdate: ComposedAnimation -> unit
      OnLoop: ComposedAnimation -> unit
      OnPause: ComposedAnimation -> unit
      OnComplete: ComposedAnimation -> unit }
    /// <summary>Default composed animation options inheriting active scope and engine settings.</summary>
    static member Default =
        { Playback = PlaybackDefaults.current()
          DelayBy = None
          Duration = (AnimationDefaults.current()).Duration
          DurationBy = None
          Ease = (AnimationDefaults.current()).Ease
          Modifier = AnimationDefaults.modifier id
          Composition = AnimationDefaults.composition KeyframeComposition.Replace
          Precision = EngineDefaults.precision()
          Autoplay = AnimationDefaults.autoplay true
          Computed = None
          Registry = None
          OnBegin = ignore
          OnBeforeUpdate = ignore
          OnRender = ignore
          OnUpdate = ignore
          OnLoop = ignore
          OnPause = ignore
          OnComplete = ignore }
    /// <summary>Invariant diagnostic of composed animation options.</summary>
    override this.ToString() =
        "ComposedAnimationOptions(Duration=" + this.Duration.ToString("R", CultureInfo.InvariantCulture) +
        "; Composition=" + this.Composition.ToString() + "; Autoplay=" + this.Autoplay.ToString() + ")"

and [<AllowNullLiteral>] internal ComposedTween
    (parent: ComposedAnimation, target: obj, targetIndex: int, property: string, kind: TweenTargetKind,
     adapter: TargetAdapterEntry option, composition: KeyframeComposition,
     ease: float -> float, modifier: float -> float,
     start: float, delay: float, updateDuration: float,
     absStart: float, absUpdateStart: float, absEnd: float,
     hasFrom: bool, inlineOriginal: string option, priority: string,
     first: ValueInterpolation.Data, last: ValueInterpolation.Data,
     stepSource: TweenStep, totalTargets: int) =
    member _.Parent = parent
    member _.Target = target
    member _.TargetIndex = targetIndex
    member _.TotalTargets = totalTargets
    member _.Property = property
    member _.Kind = kind
    member _.Adapter = adapter
    member _.Composition = composition
    member val Ease = ease with get, set
    member val Modifier = modifier with get, set
    member val Start = start with get, set
    member val Delay = delay with get, set
    member val UpdateDuration = updateDuration with get, set
    member val ChangeDuration = updateDuration with get, set
    member val AbsoluteStart = absStart with get, set
    member val AbsoluteUpdateStart = absUpdateStart with get, set
    member val AbsoluteEnd = absEnd with get, set
    member val TweenTime = 0. with get, set
    member val HasFrom = hasFrom with get, set
    member _.InlineOriginal = inlineOriginal
    member _.Priority = priority
    member val First = first with get, set
    member val Last = last with get, set
    member val AuthoredFirst = first with get, set
    member val AuthoredLast = last with get, set
    member val CurrentNumber = first.Number with get, set
    member val CurrentNumbers = Array.copy first.Components with get, set
    member val CachedEnd = "" with get, set
    member val RenderTransforms = false with get, set
    member val Overlapped = false with get, set
    member val Overridden = false with get, set
    member val Removed = false with get, set
    member val PrevRep : ComposedTween = null with get, set
    member val NextRep : ComposedTween = null with get, set
    member val PrevAdd : ComposedTween = null with get, set
    member val NextAdd : ComposedTween = null with get, set
    member _.StepSource = stepSource
    member this.Override() =
        this.Overlapped <- true
        this.Overridden <- true
        this.ChangeDuration <- 1e-11
        this.TweenTime <- 1e-11

and internal AdditiveLookupEntry
    (target: obj, property: string, kind: TweenTargetKind, adapter: TargetAdapterEntry option,
     template: ValueInterpolation.Data, fromNumber: float, fromNumbers: float array, priority: string) =
    member _.Target = target
    member _.Property = property
    member _.Kind = kind
    member _.Adapter = adapter
    member val Template = template with get, set
    member val FromNumber = fromNumber with get, set
    member val FromNumbers = fromNumbers with get, set
    member _.Priority = priority
    member val Tweens = ResizeArray<ComposedTween>()

and internal CompositionManager(queue: EngineFrameQueue) as this =
    static let comparer =
        { new IEqualityComparer<struct(obj * string)> with
            member _.Equals(struct(a, pa), struct(b, pb)) =
                Object.ReferenceEquals(a, b) && StringComparer.Ordinal.Equals(pa, pb)
            member _.GetHashCode(struct(t, p)) =
                HashCode.Combine(RuntimeHelpers.GetHashCode t, StringComparer.Ordinal.GetHashCode p) }

    let replaceMap = Dictionary<struct(obj * string), ResizeArray<ComposedTween>>(comparer)
    let additiveMap = Dictionary<struct(obj * string), AdditiveLookupEntry>(comparer)
    let activeAnimations = HashSet<ComposedAnimation>()
    do
        let prev = queue.AfterUpdate
        queue.AfterUpdate <- (fun () -> prev(); this.FlushAdditive())

    member _.RegisterAnimation(anim: ComposedAnimation) = activeAnimations.Add anim |> ignore
    member _.UnregisterAnimation(anim: ComposedAnimation) = activeAnimations.Remove anim |> ignore
    member _.ActiveAnimations = activeAnimations |> Seq.toArray

    member _.FlushAdditive() =
        if additiveMap.Count > 0 then
            let transformTargets = HashSet<Element>(HashIdentity.Reference)
            let priorities = Dictionary<Element, string>(HashIdentity.Reference)
            for KeyValue(_, entry) in additiveMap do
                if entry.Tweens.Count > 0 then
                    let isCompound = entry.Template.Kind = ValueInterpolation.ValueKind.Complex || entry.Template.Kind = ValueInterpolation.ValueKind.Color
                    if isCompound then
                        let values = Array.copy entry.FromNumbers
                        for i in entry.Tweens.Count - 1 .. -1 .. 0 do
                            let tw = entry.Tweens[i]
                            for c in 0 .. min (values.Length - 1) (tw.CurrentNumbers.Length - 1) do
                                values[c] <- values[c] + tw.CurrentNumbers[c]
                        let composed = ValueInterpolation.compose(entry.Template, 0., values)
                        let isTransform = entry.Kind = TweenTargetKind.Transform
                        DomTargetHelpers.writeOutput entry.Target entry.Kind entry.Property entry.Adapter 0. composed false entry.Priority
                        if isTransform then
                            match entry.Target with
                            | :? Element as el ->
                                transformTargets.Add el |> ignore
                                priorities[el] <- entry.Priority
                            | _ -> ()
                    else
                        let mutable sum = entry.FromNumber
                        for i in entry.Tweens.Count - 1 .. -1 .. 0 do
                            sum <- sum + entry.Tweens[i].CurrentNumber
                        let precision = match EngineDefaults.precision() with Some p -> int p | None -> -1
                        let rounded =
                            if precision < 0 || (entry.Template.Kind = ValueInterpolation.ValueKind.Number && entry.Kind = TweenTargetKind.Object) then sum
                            else PropertyNumbers.round (Math.Pow(10., float precision)) sum
                        let composed = ValueInterpolation.compose(entry.Template, rounded, [||])
                        let isTransform = entry.Kind = TweenTargetKind.Transform
                        DomTargetHelpers.writeOutput entry.Target entry.Kind entry.Property entry.Adapter rounded composed false entry.Priority
                        if isTransform then
                            match entry.Target with
                            | :? Element as el ->
                                transformTargets.Add el |> ignore
                                priorities[el] <- entry.Priority
                            | _ -> ()
            for el in transformTargets do
                ElementTransforms.flush el priorities[el]

    member _.LatestReplaceSibling(target: obj, property: string, absStart: float) : ComposedTween option =
        match replaceMap.TryGetValue(struct(target, property)) with
        | true, list when list.Count > 0 ->
            let mutable best : ComposedTween = null
            for tw in list do
                if not tw.Overridden && tw.AbsoluteStart <= absStart then best <- tw
            if isNull best then None else Some best
        | _ -> None

    member _.ComposeTween(tween: ComposedTween) =
        let key = struct(tween.Target, tween.Property)
        match tween.Composition with
        | KeyframeComposition.None -> ()
        | KeyframeComposition.Replace ->
            let list =
                match replaceMap.TryGetValue key with
                | true, existing -> existing
                | _ ->
                    let created = ResizeArray<ComposedTween>()
                    replaceMap[key] <- created
                    created
            let mutable insertIdx = list.Count
            while insertIdx > 0 && (list[insertIdx - 1].Overridden || list[insertIdx - 1].AbsoluteStart > tween.AbsoluteStart) do
                insertIdx <- insertIdx - 1
            let prev = if insertIdx > 0 then list[insertIdx - 1] else null
            let next = if insertIdx < list.Count then list[insertIdx] else null
            tween.PrevRep <- prev
            tween.NextRep <- next
            if not (isNull prev) then prev.NextRep <- tween
            if not (isNull next) then next.PrevRep <- tween
            list.Insert(insertIdx, tween)
            if not (isNull prev) then
                let prevParent = prev.Parent
                if not (Object.ReferenceEquals(tween.Parent, prevParent)) &&
                   prevParent.IterationCount > 1. &&
                   prev.AbsoluteEnd + (prevParent.Duration - prevParent.IterationDuration) > tween.AbsoluteStart then
                    prev.Override()
                    let mutable earlier = prev.PrevRep
                    while not (isNull earlier) && Object.ReferenceEquals(earlier.Parent, prevParent) do
                        earlier.Override()
                        earlier <- earlier.PrevRep
                if prev.AbsoluteEnd > tween.AbsoluteUpdateStart then
                    let prevTLOffset = prev.AbsoluteEnd - (prev.Start + prev.UpdateDuration)
                    let updated = PropertyNumbers.round 1e12 (tween.AbsoluteUpdateStart - prevTLOffset - prev.Start)
                    prev.ChangeDuration <- updated
                    prev.TweenTime <- updated
                    prev.Overlapped <- true
                    if updated < 1e-11 then prev.Override()
                let sameTimeline =
                    match tween.Parent.ParentTimeline, prevParent.ParentTimeline with
                    | Some a, Some b -> Object.ReferenceEquals(a, b)
                    | _ -> false
                if not sameTimeline then
                    if prevParent.AllTweensOverlapped() then
                        match (prevParent.ParentTimeline : ComposedTimeline option) with
                        | Some (parentTl: ComposedTimeline) ->
                            if parentTl.AllChildrenOverlapped() then parentTl.Cancel()
                        | None -> prevParent.Cancel()
        | KeyframeComposition.Blend ->
            let entry =
                match additiveMap.TryGetValue key with
                | true, existing -> existing
                | _ ->
                    let created =
                        AdditiveLookupEntry(
                            tween.Target, tween.Property, tween.Kind, tween.Adapter,
                            tween.AuthoredLast, tween.AuthoredFirst.Number, Array.copy tween.AuthoredFirst.Components, tween.Priority)
                    additiveMap[key] <- created
                    created
            let toNumber = tween.AuthoredLast.Number
            let deltaFromNumber = entry.FromNumber - toNumber
            entry.FromNumber <- toNumber
            if tween.AuthoredLast.Components.Length > 0 then
                let toNumbers = Array.copy tween.AuthoredLast.Components
                let baseComponents =
                    if entry.FromNumbers.Length = toNumbers.Length then entry.FromNumbers
                    else Array.init toNumbers.Length (fun i -> if i < entry.FromNumbers.Length then entry.FromNumbers[i] else 0.)
                let deltaFromNumbers = Array.init toNumbers.Length (fun i -> baseComponents[i] - toNumbers[i])
                tween.First <- { tween.AuthoredFirst with Number = deltaFromNumber; Components = deltaFromNumbers }
                tween.Last <- { tween.AuthoredLast with Number = 0.; Components = Array.zeroCreate toNumbers.Length }
                tween.CurrentNumbers <- Array.copy deltaFromNumbers
                entry.FromNumbers <- toNumbers
                entry.Template <- tween.AuthoredLast
            else
                tween.First <- { tween.AuthoredFirst with Number = deltaFromNumber }
                tween.Last <- { tween.AuthoredLast with Number = 0. }
                tween.CurrentNumber <- deltaFromNumber
            let count = entry.Tweens.Count
            if count > 0 then
                let tail = entry.Tweens[count - 1]
                tail.NextAdd <- tween
                tween.PrevAdd <- tail
            entry.Tweens.Add tween

    member _.RemoveTweenSiblings(tween: ComposedTween) =
        let key = struct(tween.Target, tween.Property)
        match tween.Composition with
        | KeyframeComposition.None -> ()
        | KeyframeComposition.Replace ->
            match replaceMap.TryGetValue key with
            | true, list ->
                if list.Remove tween then
                    for i in 0 .. list.Count - 1 do
                        list[i].PrevRep <- if i = 0 then null else list[i - 1]
                        list[i].NextRep <- if i + 1 = list.Count then null else list[i + 1]
                    if list.Count = 0 then replaceMap.Remove key |> ignore
                tween.PrevRep <- null
                tween.NextRep <- null
            | _ -> ()
        | KeyframeComposition.Blend ->
            match additiveMap.TryGetValue key with
            | true, entry ->
                if entry.Tweens.Remove tween then
                    for i in 0 .. entry.Tweens.Count - 1 do
                        entry.Tweens[i].PrevAdd <- if i = 0 then null else entry.Tweens[i - 1]
                        entry.Tweens[i].NextAdd <- if i + 1 = entry.Tweens.Count then null else entry.Tweens[i + 1]
                    if entry.Tweens.Count = 0 then additiveMap.Remove key |> ignore
                tween.PrevAdd <- null
                tween.NextAdd <- null
            | _ -> ()

/// <summary>Multi-target, multi-property animation supporting None, Replace and Blend (additive) composition.</summary>
and [<Sealed; Diagnostics.DebuggerDisplay("{ToString(),nq}")>] ComposedAnimation
    internal (scheduler: FrameScheduler, targets: obj array, specs: PropertySpec array,
              options: ComposedAnimationOptions, parentTimeline: ComposedTimeline option, timelineOffset: float) as this =
    let queue = EngineFrames.forScheduler scheduler
    let manager = CompositionManagers.forQueue queue
    let timeScale = if queue.Seconds then 0.001 else 1.
    let registry = defaultArg options.Registry Adapters.DefaultRegistry
    let ownerAllowsWrite = ScopeRegistration.captureWriteGuard()
    let tweens = ResizeArray<ComposedTween>()
    let mutable siblingsAttached = false
    let mutable cancelled = false
    let mutable parentTimelineRef = parentTimeline
    let mutable offset = timelineOffset

    let assignRenderTransforms () =
        let lastByTarget = Dictionary<obj, ComposedTween>(HashIdentity.Reference)
        for tw in tweens do
            tw.RenderTransforms <- false
            if not tw.Removed && tw.Kind = TweenTargetKind.Transform then
                lastByTarget[tw.Target] <- tw
        for KeyValue(_, tw) in lastByTarget do tw.RenderTransforms <- true

    let buildTweens () =
        tweens.Clear()
        let mutable minDelay = Double.PositiveInfinity
        let mutable maxEnd = 0.
        for tIdx in 0 .. targets.Length - 1 do
            let target = targets[tIdx]
            let outerDelay =
                match options.DelayBy with
                | Some fn -> fn target tIdx targets.Length
                | None -> options.Playback.Delay
            let outerDuration =
                match options.DurationBy with
                | Some fn -> fn target tIdx targets.Length
                | None -> options.Duration
            for spec in specs do
                let kind, canonical, adapter = DomTargetHelpers.classify registry target spec.Name
                let convertUnit =
                    match kind, target with
                    | (TweenTargetKind.Css | TweenTargetKind.Transform), (:? HTMLElement as htmlEl) ->
                        Some (CssUnits.convert htmlEl)
                    | _ -> None
                let stylePriority =
                    match DomTargetHelpers.tryStyle target with
                    | Some st ->
                        let pName = if kind = TweenTargetKind.Transform then "transform" else canonical
                        st.getPropertyPriority pName
                    | None -> ""
                let origLiteral, inlineOrig = DomTargetHelpers.readOriginal options.Computed target kind canonical adapter
                let mutable inlineCaptured = inlineOrig
                let mutable runningTime = 0.
                let mutable prevTween : ComposedTween option = None
                for sIdx in 0 .. spec.Steps.Length - 1 do
                    let step = spec.Steps[sIdx]
                    let stepDelay =
                        match step.DelayBy with
                        | Some fn -> fn target tIdx targets.Length
                        | None ->
                            match step.Delay with
                            | Some d -> d
                            | None -> if sIdx = 0 then outerDelay else 0.
                    let rawStepDur =
                        match step.DurationBy with
                        | Some fn -> fn target tIdx targets.Length
                        | None ->
                            match step.Duration with
                            | Some d -> d
                            | None ->
                                if spec.Steps.Length > 1 then outerDuration / float spec.Steps.Length
                                else outerDuration
                    let stepUpdateDur = PropertyNumbers.round 1e12 (if rawStepDur <= 0. then 1e-11 else rawStepDur)
                    let stepStart = PropertyNumbers.round 1e12 (runningTime + stepDelay)
                    let absUpdateStart = PropertyNumbers.round 1e12 (offset + runningTime)
                    let absStart = PropertyNumbers.round 1e12 (offset + stepStart)
                    let absEnd = PropertyNumbers.round 1e12 (offset + stepStart + stepUpdateDur)
                    let comp = defaultArg step.Composition options.Composition
                    let prevSibling =
                        if comp = KeyframeComposition.Replace then
                            manager.LatestReplaceSibling(target, canonical, absStart)
                        else None
                    let fallbackStartLiteral =
                        match prevTween with
                        | Some (prev: ComposedTween) -> ValueInterpolation.Text prev.CachedEnd
                        | None ->
                            match prevSibling with
                            | Some (sib: ComposedTween) ->
                                match sib.Last.Kind with
                                | ValueInterpolation.ValueKind.Number -> ValueInterpolation.Number sib.CurrentNumber
                                | _ -> ValueInterpolation.Text sib.CachedEnd
                            | None -> origLiteral
                    let rawFrom =
                        match step.From with
                        | Some f -> DomTargetHelpers.toLiteral kind canonical f
                        | None -> fallbackStartLiteral
                    let resolvedToAdapted =
                        match step.ToBy with
                        | Some fn -> Some (fn target tIdx targets.Length)
                        | None -> step.To
                    let rawTo =
                        match resolvedToAdapted with
                        | Some t -> DomTargetHelpers.toLiteral kind canonical t
                        | None -> fallbackStartLiteral
                    let normFirst, normLast = ValueInterpolation.normalize convertUnit origLiteral rawFrom rawTo
                    let ease = defaultArg step.Ease options.Ease
                    let modifier = defaultArg step.Modifier options.Modifier
                    let tw =
                        ComposedTween(
                            this, target, tIdx, canonical, kind, adapter, comp,
                            ease, modifier, stepStart, stepDelay, stepUpdateDur,
                            absStart, absUpdateStart, absEnd,
                            step.From.IsSome, inlineCaptured, stylePriority,
                            normFirst, normLast, step, targets.Length)
                    inlineCaptured <- None
                    let endComposed =
                        if normLast.Kind = ValueInterpolation.ValueKind.Number || normLast.Kind = ValueInterpolation.ValueKind.Unit then
                            let m = modifier normLast.Number
                            ValueInterpolation.compose(normLast, m, [||]).Text
                        elif normLast.Kind = ValueInterpolation.ValueKind.Color then
                            let d = normLast.Components
                            "rgba(" + ValueInterpolation.number (PropertyNumbers.round 1. d[0]) + "," +
                            ValueInterpolation.number (PropertyNumbers.round 1. d[1]) + "," +
                            ValueInterpolation.number (PropertyNumbers.round 1. d[2]) + "," +
                            ValueInterpolation.number d[3] + ")"
                        else
                            let vals = normLast.Components |> Array.map modifier
                            ValueInterpolation.compose(normLast, 0., vals).Text
                    tw.CachedEnd <- endComposed
                    if sIdx = 0 && stepStart < minDelay then minDelay <- stepStart
                    let stepFinish = PropertyNumbers.round 1e12 (stepStart + stepUpdateDur)
                    if stepFinish > maxEnd then maxEnd <- stepFinish
                    runningTime <- stepFinish
                    prevTween <- Some tw
                    tweens.Add tw
        let effectiveDelay = if Double.IsPositiveInfinity minDelay || tweens.Count = 0 then options.Playback.Delay else minDelay
        let iterDuration =
            if tweens.Count = 0 then 1e-11
            else
                let trimmed = PropertyNumbers.round 1e12 (maxEnd - effectiveDelay)
                if trimmed <= 0. then 1e-11 else trimmed
        for tw in tweens do
            tw.Start <- PropertyNumbers.round 1e12 (tw.Start - effectiveDelay)
        assignRenderTransforms()
        effectiveDelay, iterDuration

    let initialDelay, initialIterDuration = buildTweens()
    let effectivePlayback =
        { options.Playback with
            Delay = initialDelay
            Repeat = if tweens.Count = 0 then PlaybackRepeat.Count 0u else options.Playback.Repeat }

    let attachSiblings () =
        if not siblingsAttached then
            siblingsAttached <- true
            manager.RegisterAnimation this
            for tw in tweens do
                if not tw.Removed then manager.ComposeTween tw

    let detachSiblings () =
        if siblingsAttached then
            siblingsAttached <- false
            manager.UnregisterAnimation this
            for tw in tweens do manager.RemoveTweenSiblings tw

    let mutable clock : Timer = Unchecked.defaultof<Timer>
    let render (sample: PlaybackSample) mode muted =
        let canWrite = clock.CaptureWriteGuard()
        let delta = sample.AbsoluteTime - clock.PreviousAbsoluteTime
        let forced = mode = PlaybackTickMode.Force || (if sample.Backwards then -delta else delta) >= clock.TickThreshold
        let absolute = PropertyNumbers.round 1e12 (offset + clock.Delay + sample.IterationTime)
        let precision = match options.Precision with Some p -> int p | None -> -1
        let mutable rendered = false
        let mutable hasBlend = false
        for i in 0 .. tweens.Count - 1 do
            let tw = tweens[i]
            if not tw.Removed && canWrite() then
                let prev = tw.PrevRep
                let next = tw.NextRep
                let prevCross = not (isNull prev) && not (Object.ReferenceEquals(prev.Parent, tw.Parent))
                let nextSame = not (isNull next) && Object.ReferenceEquals(next.Parent, tw.Parent)
                let prevEnd = if isNull prev then 0. else prev.AbsoluteStart + prev.ChangeDuration
                let ending = tw.AbsoluteStart + tw.ChangeDuration
                let takeover =
                    if isNull next || next.Overridden then ending
                    elif nextSame then ending + next.Delay
                    elif next.AbsoluteStart < next.AbsoluteUpdateStart then next.AbsoluteStart
                    else next.AbsoluteUpdateStart
                let history =
                    (tw.TweenTime <> tw.ChangeDuration || absolute <= takeover ||
                     (not (isNull prev) && not prevCross && (isNull next || not nextSame))) &&
                    (tw.TweenTime <> 0. || absolute >= tw.AbsoluteStart ||
                     (prevCross && not tw.HasFrom && not prev.Overridden && absolute >= prevEnd) ||
                     (nextSame && not next.Overridden && next.TweenTime <> 0. && sample.IterationTime < next.Start))
                let isReplace = tw.Composition = KeyframeComposition.Replace
                let eligible =
                    (forced || history) &&
                    (isNull prev || prevCross || sample.IterationTime >= tw.Start) &&
                    (not isReplace ||
                     (not tw.Overridden && (not tw.Overlapped || absolute <= ending) &&
                      (isNull next || next.Overridden || absolute <= takeover) &&
                      (isNull prev || prev.Overridden ||
                       (if not prevCross then absolute >= prevEnd + tw.Delay
                        else absolute >= tw.AbsoluteStart || (not tw.HasFrom && absolute >= prevEnd)))))
                if eligible then
                    tw.TweenTime <- Math.Clamp(sample.IterationTime - tw.Start, 0., tw.ChangeDuration)
                    let progress = tw.Ease (tw.TweenTime / tw.UpdateDuration)
                    let roundNumbers = tw.Kind <> TweenTargetKind.Object
                    match ValueInterpolation.sample(roundNumbers, tw.First, tw.Last, progress, precision, tw.Modifier, canWrite) with
                    | Some struct(scalar, output) when canWrite() ->
                        tw.CurrentNumber <- scalar
                        if output.Components.Length > 0 then tw.CurrentNumbers <- Array.copy output.Components
                        if not clock.InternalRender then
                            if tw.Composition = KeyframeComposition.Blend then
                                hasBlend <- true
                                rendered <- true
                            else
                                DomTargetHelpers.writeOutput tw.Target tw.Kind tw.Property tw.Adapter scalar output tw.RenderTransforms tw.Priority
                                rendered <- true
                    | _ -> ()
                elif tw.TweenTime <> 0. && not (isNull prev) && not prevCross && sample.IterationTime < tw.Start then
                    tw.TweenTime <- 0.
        if hasBlend && not clock.InternalRender then manager.FlushAdditive()
        if rendered && sample.AboveZero && not muted && canWrite() then
            options.OnRender this

    do
        clock <- ScopeRegistration.run ignore (fun () ->
            new Timer(
                scheduler, initialIterDuration, options = effectivePlayback,
                onBegin = (fun _ -> options.OnBegin this),
                onBeforeUpdate = (fun _ -> options.OnBeforeUpdate this),
                onUpdate = (fun _ -> options.OnUpdate this),
                onLoop = (fun _ -> options.OnLoop this),
                onPause = (fun _ -> options.OnPause this),
                onComplete = (fun _ ->
                    options.OnComplete this
                    if parentTimelineRef.IsNone then
                        scheduler.Microtask(fun () ->
                            if clock.Completed && clock.Paused then detachSiblings()) |> ignore)))
        clock.ConfigureRenderer(render, ?totalDuration = (if tweens.Count = 0 || initialIterDuration <= 1e-11 then Some 1e-11 else None))
        clock.OnCancel <- detachSiblings
        clock.OnFault <- detachSiblings
        attachSiblings()
        ScopeRegistration.register (fun () -> this.Revert() |> ignore)
        if parentTimelineRef.IsSome then
            clock.AttachToTimeline(internalRender = true)
        else
            clock.Reset()
            if options.Autoplay && not cancelled && ownerAllowsWrite() then clock.Resume()
            elif tweens.Count = 0 && options.Autoplay then clock.Complete()
            else ignore timeScale

    member internal _.ParentTimeline
        with get() : ComposedTimeline option = parentTimelineRef
        and set (value: ComposedTimeline option) = parentTimelineRef <- value

    member internal _.Clock = clock
    member internal _.Tweens = tweens
    member internal _.AllTweensOverlapped() =
        let mutable activeCount = 0
        let mutable allOver = true
        for tw in tweens do
            if not tw.Removed then
                activeCount <- activeCount + 1
                if not tw.Overlapped then allOver <- false
        activeCount > 0 && allOver

    member internal _.HasReplaceLink(index: int) =
        if index < 0 || index >= tweens.Count then false
        else not (isNull tweens[index].PrevRep) || not (isNull tweens[index].NextRep)

    /// <summary>Targets controlled by this animation.</summary>
    member _.Targets = Array.copy targets
    /// <summary>Count of active non-removed tweens in this animation.</summary>
    member _.ActiveTweenCount =
        let mutable c = 0
        for tw in tweens do if not tw.Removed then c <- c + 1
        c
    /// <summary>Total duration across iterations, excluding initial delay.</summary>
    member _.Duration = clock.Duration
    /// <summary>Single-iteration duration.</summary>
    member _.IterationDuration = clock.IterationDuration
    /// <summary>Total iteration count (infinity for Forever).</summary>
    member _.IterationCount = clock.IterationCount
    /// <summary>Current playhead time.</summary>
    member _.CurrentTime with get() = clock.CurrentTime and set v = clock.CurrentTime <- v
    /// <summary>Overall normalized progress in [0,1].</summary>
    member _.Progress with get() = clock.Progress and set v = clock.Progress <- v
    /// <summary>Current iteration index.</summary>
    member _.CurrentIteration = clock.CurrentIteration
    /// <summary>Directional iteration progress in [0,1].</summary>
    member _.IterationProgress with get() = clock.IterationProgress and set v = clock.IterationProgress <- v
    /// <summary>Playback speed multiplier.</summary>
    member _.Speed with get() = clock.Speed and set v = clock.Speed <- v
    /// <summary>Whether playback is paused.</summary>
    member _.Paused = clock.Paused
    /// <summary>Whether playback has begun.</summary>
    member _.Began = clock.Began
    /// <summary>Whether playback has completed.</summary>
    member _.Completed = clock.Completed
    /// <summary>Whether playback has been cancelled.</summary>
    member _.Cancelled = cancelled || clock.Cancelled
    /// <summary>Whether base playback direction is reversed.</summary>
    member _.Reversed with get() = clock.Reversed and set v = clock.Reversed <- v

    /// <summary>Seeks to the specified time and returns this animation.</summary>
    member this.Seek(time: float, ?muteCallbacks: bool) =
        if clock.Cancelled && not cancelled then clock.ReviveReset(); attachSiblings()
        clock.Seek(time, ?muteCallbacks = muteCallbacks)
        this

    /// <summary>Pauses scheduling and returns this animation.</summary>
    member this.Pause() = clock.Pause(); this

    /// <summary>Resumes scheduling in the current direction.</summary>
    member this.Resume() =
        if clock.Cancelled && not cancelled then clock.ReviveReset(); attachSiblings()
        clock.Resume()
        this

    /// <summary>Plays forward.</summary>
    member this.Play() =
        if clock.Cancelled && not cancelled then clock.ReviveReset(); attachSiblings()
        clock.Play()
        this

    /// <summary>Plays in reverse.</summary>
    member this.Reverse() =
        if clock.Cancelled && not cancelled then clock.ReviveReset(); attachSiblings()
        clock.Reverse()
        this

    /// <summary>Toggles direction according to repeat parity.</summary>
    member this.Alternate() = clock.Alternate(); this

    /// <summary>Resets to zero, re-linking siblings if previously completed.</summary>
    member this.Reset() =
        cancelled <- false
        if clock.Cancelled then clock.ReviveReset()
        for tw in tweens do
            tw.Overlapped <- false
            tw.Overridden <- false
            tw.ChangeDuration <- tw.UpdateDuration
            tw.TweenTime <- 0.
        detachSiblings()
        attachSiblings()
        clock.Reset()
        this

    /// <summary>Resets and resumes playback, re-attaching composition siblings.</summary>
    member this.Restart() =
        this.Reset() |> ignore
        clock.Resume()
        this

    /// <summary>Completes immediately and cancels scheduling.</summary>
    member this.Complete(?muteCallbacks: bool) =
        clock.Complete(?muteCallbacks = muteCallbacks)
        detachSiblings()
        this

    /// <summary>Stretches total duration proportionally.</summary>
    member this.Stretch(newDuration: float) =
        if clock.Duration > 0. then
            let scale = newDuration / clock.Duration
            for tw in tweens do
                tw.Start <- tw.Start * scale
                tw.Delay <- tw.Delay * scale
                tw.UpdateDuration <- max 1e-11 (PropertyNumbers.round 1e12 (tw.UpdateDuration * scale))
                tw.ChangeDuration <- if tw.Overridden then 1e-11 else max 1e-11 (PropertyNumbers.round 1e12 (tw.ChangeDuration * scale))
                tw.AbsoluteStart <- tw.AbsoluteStart * scale
                tw.AbsoluteUpdateStart <- tw.AbsoluteUpdateStart * scale
                tw.AbsoluteEnd <- tw.AbsoluteEnd * scale
                tw.TweenTime <- if tw.Overridden then 1e-11 else tw.TweenTime * scale
            offset <- offset * scale
            clock.Stretch newDuration
        this

    /// <summary>Re-evaluates dynamic endpoints and target originals in place.</summary>
    member this.Refresh() =
        let wasPlaying = not clock.Paused
        let savedTime = clock.CurrentTime
        detachSiblings()
        let _, iterDur = buildTweens()
        attachSiblings()
        clock.Resize iterDur
        clock.Seek(min savedTime clock.Duration, muteCallbacks = true)
        if wasPlaying then clock.Resume()
        this

    /// <summary>Removes matching targets (and optional property) from this animation, cancelling if none remain.</summary>
    member internal this.RemoveMatching(targetsToRemove: HashSet<obj>, propertyName: string option) : bool =
        let mutable matched = false
        for tw in tweens do
            if not tw.Removed && targetsToRemove.Contains tw.Target then
                let propMatches =
                    match propertyName with
                    | None -> true
                    | Some raw ->
                        let _, canonical, _ = DomTargetHelpers.classify registry tw.Target raw
                        StringComparer.Ordinal.Equals(canonical, tw.Property)
                if propMatches then
                    tw.Removed <- true
                    manager.RemoveTweenSiblings tw
                    matched <- true
        if matched then
            assignRenderTransforms()
            if this.ActiveTweenCount = 0 then this.Cancel()
        matched

    /// <summary>Cancels playback while retaining current rendered target values.</summary>
    member _.Cancel() =
        cancelled <- true
        detachSiblings()
        clock.Cancel()

    member internal this.RevertValues(inlineStylesOnly: bool) =
        clock.Pause()
        let transformRestores = Dictionary<Element, ResizeArray<string * string option> * string>(HashIdentity.Reference)
        let domElements = HashSet<Element>(HashIdentity.Reference)
        for tw in tweens do
            match tw.Target with
            | :? Element as el -> domElements.Add el |> ignore
            | _ -> ()
            match tw.Adapter with
            | Some entry when not inlineStylesOnly ->
                match tw.InlineOriginal with
                | Some orig ->
                    let d = ValueInterpolation.decompose (ValueInterpolation.Text orig)
                    let comps = if d.Components.Length > 0 then Array.copy d.Components else [||]
                    let scalarOpt =
                        if d.Kind = ValueInterpolation.ValueKind.Color || d.Kind = ValueInterpolation.ValueKind.Complex then None
                        else Some d.Number
                    let info = AdaptedTweenInfo(tw.Target, tw.Property, d.Number, comps)
                    entry.Set tw.Target scalarOpt info
                | None -> ()
            | Some _ -> ()
            | None ->
                match tw.Kind, tw.Target with
                | TweenTargetKind.Transform, (:? Element as el) ->
                    let list, prio =
                        match transformRestores.TryGetValue el with
                        | true, existing -> existing
                        | _ ->
                            let created = ResizeArray<string * string option>(), tw.Priority
                            transformRestores[el] <- created
                            created
                    ignore prio
                    list.Add(tw.Property, tw.InlineOriginal)
                | (TweenTargetKind.Css | TweenTargetKind.CssVar), (:? Element as el) ->
                    let style = (DomTargetHelpers.tryStyle el).Value
                    match tw.InlineOriginal with
                    | None | Some "" -> style.removeProperty tw.Property |> ignore
                    | Some orig -> style.setProperty(tw.Property, orig, priority = tw.Priority)
                | TweenTargetKind.Attribute, (:? Element as el) when not inlineStylesOnly ->
                    match tw.InlineOriginal with
                    | None | Some "" -> el.removeAttribute tw.Property
                    | Some orig -> el.setAttribute(tw.Property, orig)
                | TweenTargetKind.Object, (:? Element as el) when not inlineStylesOnly ->
                    match tw.InlineOriginal with
                    | Some orig ->
                        match tw.Property with
                        | "innerHTML" -> (el :?> HTMLElement).innerHTML <- orig
                        | "textContent" -> (el :> Node).textContent <- orig
                        | "value" -> el.setAttribute("value", orig)
                        | _ -> ()
                    | None -> ()
                | TweenTargetKind.Object, (:? NumericProperty as prop) when not inlineStylesOnly ->
                    tw.InlineOriginal |> Option.iter (fun s -> prop.Value <- Double.Parse(s, CultureInfo.InvariantCulture))
                | TweenTargetKind.Object, (:? AnimatedObject as bag) when not inlineStylesOnly ->
                    match tw.InlineOriginal with
                    | Some orig ->
                        if tw.AuthoredFirst.Kind = ValueInterpolation.ValueKind.Number then
                            bag.SetNumber(tw.Property, Double.Parse(orig, CultureInfo.InvariantCulture))
                        else bag.SetText(tw.Property, orig)
                    | None -> ()
                | _ -> ()
        for KeyValue(el, (list, prio)) in transformRestores do
            ElementTransforms.revertComponents el (list.ToArray()) prio
        for el in domElements do
            if el.getAttribute "style" = "" then el.removeAttribute "style"

    /// <summary>Rewinds, restores pre-animation inline/object/adapted values and cancels playback.</summary>
    member this.Revert() =
        if not cancelled then
            clock.Seek(0., muteCallbacks = true)
            this.RevertValues false
            this.Cancel()
        this

    /// <summary>Registers an optional completion callback and returns a task settling when this animation completes.</summary>
    member this.Then(?callback: ComposedAnimation -> unit) : Threading.Tasks.Task<ComposedAnimation> =
        let invoke () = callback |> Option.iter (fun action -> action this)
        if clock.Completed then
            invoke()
            Threading.Tasks.Task.FromResult this
        elif cancelled then
            Threading.Tasks.Task.FromCanceled<ComposedAnimation>(Threading.CancellationToken(true))
        else
            let source = Threading.Tasks.TaskCompletionSource<ComposedAnimation>()
            clock.Then(fun _ -> invoke()).ContinueWith((fun (task: Threading.Tasks.Task<Timer>) ->
                if task.IsCanceled then source.TrySetCanceled() |> ignore
                elif task.IsFaulted then source.TrySetException(task.Exception.InnerExceptions) |> ignore
                else source.TrySetResult this |> ignore
            ), Threading.Tasks.TaskContinuationOptions.ExecuteSynchronously) |> ignore
            source.Task

    /// <summary>Returns a task settling when this animation completes.</summary>
    member this.AsTask() : Threading.Tasks.Task<ComposedAnimation> = this.Then()

    /// <summary>Invariant diagnostic of the composed animation.</summary>
    override this.ToString() =
        "ComposedAnimation(Targets=" + targets.Length.ToString(CultureInfo.InvariantCulture) +
        "; Tweens=" + this.ActiveTweenCount.ToString(CultureInfo.InvariantCulture) + ")"

    interface IDisposable with
        member this.Dispose() = this.Cancel()

/// <summary>Timeline coordinating <see cref="T:Ranvier.Anime.ComposedAnimation"/> children with cross-timeline Replace and Remove parity.</summary>
and [<Sealed; Diagnostics.DebuggerDisplay("{ToString(),nq}")>] ComposedTimeline
    (scheduler: FrameScheduler, ?options: PlaybackOptions, ?autoplay: bool, ?onComplete: ComposedTimeline -> unit) as this =
    let queue = EngineFrames.forScheduler scheduler
    let manager = CompositionManagers.forQueue queue
    let entries = ResizeArray<float * ComposedAnimation>()
    let labels = Dictionary<string, float>(StringComparer.Ordinal)
    let mutable iterDuration = 0.
    let mutable cancelled = false
    let auto = defaultArg autoplay (AnimationDefaults.autoplay true)
    let clock = ScopeRegistration.run ignore (fun () ->
        new Timer(scheduler, 0., ?options = options, onComplete = (fun _ -> onComplete |> Option.iter (fun cb -> cb this))))

    let recalcDuration () =
        let mutable maxEnd = 0.
        for childOffset, child in entries do
            if not child.Cancelled then
                let finish = childOffset + child.Clock.Delay + child.Duration
                if finish > maxEnd then maxEnd <- finish
        iterDuration <- maxEnd
        clock.Resize iterDuration

    do
        clock.ConfigureChildren(
            (fun _ _ _ ->
                for _, child in entries do
                    if not child.Cancelled then child.Clock.ResetChildFlags()),
            (fun sample mode muted ->
                let backwards = sample.Backwards
                let ordered = if backwards then entries.ToArray() |> Array.rev else entries.ToArray()
                let mutable allDone = true
                for childOffset, child in ordered do
                    if not child.Cancelled then
                        let localTime = PropertyNumbers.round 1e12 ((sample.IterationTime - childOffset) * child.Speed)
                        if not (backwards && localTime > child.Clock.Delay + child.Duration) then
                            child.Clock.SampleChild(localTime, mode, muted, clock.Fps, backwards, clock.Began, (fun () -> not cancelled))
                        allDone <- allDone && (child.Completed || child.Cancelled)
                manager.FlushAdditive()
                allDone))
        ScopeRegistration.register (fun () -> this.Revert() |> ignore)

    member internal _.AllChildrenOverlapped() =
        entries.Count > 0 && (entries |> Seq.forall (fun (_, child) -> child.Cancelled || child.AllTweensOverlapped()))

    member internal _.Children = entries

    /// <summary>Adds a multi-target/property child animation at an optional position.</summary>
    member this.Add(targets: obj array, specs: PropertySpec array, ?options: ComposedAnimationOptions, ?position: TimelinePosition) =
        let prev =
            if entries.Count = 0 then None
            else
                let lastOff, lastChild = entries[entries.Count - 1]
                Some(lastOff, lastOff + lastChild.Clock.Delay + lastChild.Duration)
        let resolvedOffset =
            TimelinePositions.resolve iterDuration prev
                (fun lbl -> match labels.TryGetValue lbl with true, v -> Some v | _ -> None) position
        let childOpts = { defaultArg options ComposedAnimationOptions.Default with Autoplay = false }
        let child = new ComposedAnimation(scheduler, targets, specs, childOpts, Some this, resolvedOffset)
        entries.Add(resolvedOffset, child)
        recalcDuration()
        if auto && not cancelled then clock.Resume()
        this

    /// <summary>Records a named position label.</summary>
    member this.Label(name: string, ?position: TimelinePosition) =
        let prev =
            if entries.Count = 0 then None
            else
                let lastOff, lastChild = entries[entries.Count - 1]
                Some(lastOff, lastOff + lastChild.Clock.Delay + lastChild.Duration)
        labels[name] <-
            TimelinePositions.resolve iterDuration prev
                (fun lbl -> match labels.TryGetValue lbl with true, v -> Some v | _ -> None) position
        this

    /// <summary>Removes matching targets (and optional property) across child animations, cancelling empty children and this timeline if empty.</summary>
    member this.RemoveTargets(targets: obj array, ?propertyName: string) =
        let targetSet = HashSet<obj>(targets, HashIdentity.Reference)
        let mutable anyRemoved = false
        let remaining = ResizeArray<float * ComposedAnimation>()
        for off, child in entries do
            if child.RemoveMatching(targetSet, propertyName) then anyRemoved <- true
            if not child.Cancelled && child.ActiveTweenCount > 0 then remaining.Add(off, child)
        if anyRemoved then
            entries.Clear()
            entries.AddRange remaining
            if entries.Count = 0 then this.Cancel()
            else recalcDuration()
        this

    /// <summary>Total duration across iterations.</summary>
    member _.Duration = if entries.Count = 0 then 0. else clock.Duration
    /// <summary>Single-iteration duration.</summary>
    member _.IterationDuration = iterDuration
    /// <summary>Current playhead time.</summary>
    member _.CurrentTime with get() = clock.CurrentTime and set v = clock.CurrentTime <- v
    /// <summary>Whether this timeline has active children.</summary>
    member _.HasChildren = entries.Count > 0
    /// <summary>Whether scheduling is paused.</summary>
    member _.Paused = clock.Paused
    /// <summary>Whether this timeline completed.</summary>
    member _.Completed = clock.Completed
    /// <summary>Whether this timeline was cancelled.</summary>
    member _.Cancelled = cancelled || clock.Cancelled

    /// <summary>Seeks to milliseconds and flushes any additive blend updates.</summary>
    member this.Seek(time: float, ?muteCallbacks: bool) =
        clock.Seek(time, ?muteCallbacks = muteCallbacks)
        manager.FlushAdditive()
        this

    /// <summary>Pauses scheduling.</summary>
    member this.Pause() = clock.Pause(); this
    /// <summary>Resumes scheduling.</summary>
    member this.Resume() = clock.Resume(); this
    /// <summary>Plays forward.</summary>
    member this.Play() = clock.Play(); this
    /// <summary>Reverses playback.</summary>
    member this.Reverse() = clock.Reverse(); this

    /// <summary>Refreshes all active child animations.</summary>
    member this.Refresh() =
        for _, child in entries do
            if not child.Cancelled then child.Refresh() |> ignore
        recalcDuration()
        this

    /// <summary>Reverts all children in reverse order and cancels this timeline.</summary>
    member this.Revert() =
        if not cancelled then
            clock.Seek(0., muteCallbacks = true)
            for i in entries.Count - 1 .. -1 .. 0 do
                let _, child = entries[i]
                child.RevertValues false
                child.Cancel()
            this.Cancel()
        this

    /// <summary>Cancels this timeline and all owned children.</summary>
    member _.Cancel() =
        cancelled <- true
        for _, child in entries do child.Cancel()
        entries.Clear()
        clock.Cancel()

    /// <summary>Registers an optional completion callback and returns a settling task.</summary>
    member this.Then(?callback: ComposedTimeline -> unit) : Threading.Tasks.Task<ComposedTimeline> =
        let invoke () = callback |> Option.iter (fun action -> action this)
        if clock.Completed then
            invoke()
            Threading.Tasks.Task.FromResult this
        elif cancelled then
            Threading.Tasks.Task.FromCanceled<ComposedTimeline>(Threading.CancellationToken(true))
        else
            let source = Threading.Tasks.TaskCompletionSource<ComposedTimeline>()
            clock.Then(fun _ -> invoke()).ContinueWith((fun (task: Threading.Tasks.Task<Timer>) ->
                if task.IsCanceled then source.TrySetCanceled() |> ignore
                elif task.IsFaulted then source.TrySetException(task.Exception.InnerExceptions) |> ignore
                else source.TrySetResult this |> ignore
            ), Threading.Tasks.TaskContinuationOptions.ExecuteSynchronously) |> ignore
            source.Task

    /// <summary>Returns a task settling when this timeline completes.</summary>
    member this.AsTask() : Threading.Tasks.Task<ComposedTimeline> = this.Then()

    /// <summary>Invariant diagnostic of the composed timeline.</summary>
    override _.ToString() = "ComposedTimeline(Children=" + entries.Count.ToString(CultureInfo.InvariantCulture) + ")"

    interface IDisposable with
        member this.Dispose() = this.Cancel()

and internal CompositionManagers =
    static let table = ConditionalWeakTable<EngineFrameQueue, CompositionManager>()
    static member forQueue (queue: EngineFrameQueue) : CompositionManager =
        table.GetValue(queue, fun q -> CompositionManager q)
    static member forScheduler (scheduler: FrameScheduler) : CompositionManager =
        CompositionManagers.forQueue (EngineFrames.forScheduler scheduler)

/// <summary>High-level Anime.js animate() and createTimeline() entry points with cross-animation composition.</summary>
[<RequireQualifiedAccess>]
module Composed =
    /// <summary>Animates multiple targets across CSS, individual transforms, SVG/DOM attributes, NumericProperty or adapted objects.</summary>
    let animateTargets (scheduler: FrameScheduler) (targets: obj array) (specs: PropertySpec array)
                       (options: ComposedAnimationOptions option) : ComposedAnimation =
        if isNull (box scheduler) then nullArg "scheduler"
        if isNull targets then nullArg "targets"
        if isNull specs then nullArg "specs"
        new ComposedAnimation(scheduler, Array.copy targets, Array.copy specs, defaultArg options ComposedAnimationOptions.Default, None, 0.)

    /// <summary>Animates a single target with full composition support.</summary>
    let animate (scheduler: FrameScheduler) (target: obj) (specs: PropertySpec array)
                (options: ComposedAnimationOptions option) : ComposedAnimation =
        if isNull target then nullArg "target"
        animateTargets scheduler [| target |] specs options

    /// <summary>Creates a <see cref="T:Ranvier.Anime.ComposedTimeline"/> supporting cross-timeline Replace and target removal.</summary>
    let createTimeline (scheduler: FrameScheduler) (options: PlaybackOptions option) (autoplay: bool option) : ComposedTimeline =
        new ComposedTimeline(scheduler, ?options = options, ?autoplay = autoplay)
