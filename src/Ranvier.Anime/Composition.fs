namespace Ranvier.Anime

open System
open System.Collections.Generic
open System.Globalization
open System.Runtime.CompilerServices
open System.Runtime.InteropServices
open System.Runtime.Intrinsics
open System.Text.RegularExpressions
open Ranvier.Dom

module private CompositionTargetComparer =
    let instance =
        { new IEqualityComparer<struct(obj * string)> with
            member _.Equals(struct(a, pa), struct(b, pb)) =
                Object.ReferenceEquals(a, b) && StringComparer.Ordinal.Equals(pa, pb)
            member _.GetHashCode(struct(t, p)) =
                HashCode.Combine(RuntimeHelpers.GetHashCode t, StringComparer.Ordinal.GetHashCode p) }

module private BlendSimd =
    let addChannels (dst: float array) (src: float array) =
        let count = min dst.Length src.Length
        let mutable i = 0
        let rDst = &MemoryMarshal.GetArrayDataReference(dst)
        let rSrc = &MemoryMarshal.GetArrayDataReference(src)
        if Vector512.IsHardwareAccelerated && count >= 8 then
            while i <= count - 8 do
                let vd = Vector512.LoadUnsafe(&rDst, unativeint i)
                let vs = Vector512.LoadUnsafe(&rSrc, unativeint i)
                (vd + vs).StoreUnsafe(&rDst, unativeint i)
                i <- i + 8
        elif Vector256.IsHardwareAccelerated && count >= 4 then
            while i <= count - 4 do
                let vd = Vector256.LoadUnsafe(&rDst, unativeint i)
                let vs = Vector256.LoadUnsafe(&rSrc, unativeint i)
                (vd + vs).StoreUnsafe(&rDst, unativeint i)
                i <- i + 4
        elif Vector128.IsHardwareAccelerated && count >= 2 then
            while i <= count - 2 do
                let vd = Vector128.LoadUnsafe(&rDst, unativeint i)
                let vs = Vector128.LoadUnsafe(&rSrc, unativeint i)
                (vd + vs).StoreUnsafe(&rDst, unativeint i)
                i <- i + 2
        while i < count do
            dst[i] <- dst[i] + src[i]
            i <- i + 1

module private CompositionRestoration =
    let run restore =
        let mutable failure = None
        let attempt action =
            try action() with error -> if failure.IsNone then failure <- Some error
        restore attempt
        failure |> Option.iter (fun error -> System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw())

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
    // Unboxed struct(isText, number, text) avoids allocating an AdaptedValue heap DU on every per-frame SetNumber / SetText.
    let values = Dictionary<string, struct(bool * float * string)>(StringComparer.Ordinal)
    do
        match initial with
        | Some pairs ->
            for k, v in pairs do
                match v with
                | AdaptedValue.Scalar n -> values[k] <- struct(false, n, null)
                | AdaptedValue.Text s -> values[k] <- struct(true, 0., s)
        | None -> ()

    /// <summary>Gets or sets a named property value on this object.</summary>
    member _.Item
        with get(name: string) =
            let struct(isText, n, s) = values[name]
            if isText then AdaptedValue.Text s else AdaptedValue.Scalar n
        and set (name: string) (value: AdaptedValue) =
            match value with
            | AdaptedValue.Scalar n -> values[name] <- struct(false, n, null)
            | AdaptedValue.Text s -> values[name] <- struct(true, 0., s)

    /// <summary>Attempts to read a named property value.</summary>
    member _.TryGet(name: string) =
        match values.TryGetValue name with
        | true, struct(isText, n, s) -> Some (if isText then AdaptedValue.Text s else AdaptedValue.Scalar n)
        | _ -> None

    /// <summary>Sets a scalar numeric property.</summary>
    [<MethodImpl(MethodImplOptions.AggressiveInlining)>]
    member _.SetNumber(name: string, value: float) = values[name] <- struct(false, value, null)

    /// <summary>Gets a scalar numeric property, parsing numeric strings when needed.</summary>
    member _.GetNumber(name: string) =
        match values.TryGetValue name with
        | true, struct(false, v, _) -> v
        | true, struct(true, _, s) -> Double.Parse(s, CultureInfo.InvariantCulture)
        | _ -> 0.

    /// <summary>Sets a text/unit/compound property.</summary>
    [<MethodImpl(MethodImplOptions.AggressiveInlining)>]
    member _.SetText(name: string, value: string) = values[name] <- struct(true, 0., value)

    member internal _.Remove(name: string) = values.Remove name |> ignore

    /// <summary>Gets the string representation of a property.</summary>
    member _.GetText(name: string) =
        match values.TryGetValue name with
        | true, struct(true, _, s) -> s
        | true, struct(false, v, _) -> ValueInterpolation.number v
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
      Composition: KeyframeComposition option
      /// <summary>Suppresses this step's playback writes while retaining its place on the clock.</summary>
      Muted: bool }
    /// <summary>Empty step inheriting outer animation timing, easing, modifier and composition.</summary>
    static member Empty =
        { From = None; To = None; ToBy = None; Duration = None; DurationBy = None
          Delay = None; DelayBy = None; Ease = None; Modifier = None; Composition = None; Muted = false }
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
                         ?ease: float -> float, ?modifier: float -> float, ?composition: KeyframeComposition, ?muted: bool) =
        { Name = name
          Steps =
            [| { TweenStep.Empty with
                   From = fromValue |> Option.map AdaptedValue.Scalar
                   To = Some (AdaptedValue.Scalar toValue)
                   Duration = duration
                   Delay = delay
                   Ease = ease
                   Modifier = modifier
                   Composition = composition; Muted = defaultArg muted false } |] }
    /// <summary>Resolves a scalar destination once per target when the property track is constructed.</summary>
    static member ScalarPerTarget(name: string, next: unit -> float) =
        { Name = name
          Steps = [| { TweenStep.Empty with ToBy = Some (fun _ _ _ -> AdaptedValue.Scalar(next ())) } |] }

    /// <summary>Creates a single-step text/CSS/color/unit property specification.</summary>
    static member Text(name: string, toValue: string, ?fromValue: string, ?duration: float, ?delay: float,
                       ?ease: float -> float, ?modifier: float -> float, ?composition: KeyframeComposition, ?muted: bool) =
        { Name = name
          Steps =
            [| { TweenStep.Empty with
                   From = fromValue |> Option.map AdaptedValue.Text
                   To = Some (AdaptedValue.Text toValue)
                   Duration = duration
                   Delay = delay
                   Ease = ease
                   Modifier = modifier
                   Composition = composition; Muted = defaultArg muted false } |] }
    /// <summary>Creates a multi-step keyframe property specification.</summary>
    static member StepsOf(name: string, steps: TweenStep array) = { Name = name; Steps = steps }
    /// <summary>Invariant diagnostic of the property specification.</summary>
    override this.ToString() = "PropertySpec(" + this.Name + ", Steps=" + this.Steps.Length.ToString(CultureInfo.InvariantCulture) + ")"

/// <summary>Internal DOM/object/adapter classification and value conversion helpers.</summary>
module internal DomTargetHelpers =
    let private lowerCaseRgx = Regex(@"([a-z0-9])([A-Z])", RegexOptions.CultureInvariant)
    let private commonPxCss =
        System.Collections.Frozen.FrozenSet.ToFrozenSet(
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
    let private svgCssReserved = System.Collections.Frozen.FrozenSet.ToFrozenSet([| "opacity"; "rotate"; "overflow"; "color" |], StringComparer.Ordinal)
    let private domObjectProps = System.Collections.Frozen.FrozenSet.ToFrozenSet([| "innerHTML"; "textContent"; "value"; "scrollTop"; "scrollLeft" |], StringComparer.Ordinal)

    let toKebab (name: string) =
        if String.IsNullOrEmpty name || name.StartsWith("--", StringComparison.Ordinal) then
            name
        else
            let span = name.AsSpan()
            if span.IndexOfAnyInRange('\u0080', '\uFFFF') >= 0 then
                lowerCaseRgx.Replace(name, "$1-$2").ToLowerInvariant()
            elif span.IndexOfAnyInRange('A', 'Z') < 0 then
                name
            else
                let buf = Array.zeroCreate<char> (span.Length * 2)
                let mutable outPos = 0
                for i = 0 to span.Length - 1 do
                    let c = span[i]
                    if c >= 'A' && c <= 'Z' then
                        if i > 0 then
                            let prev = span[i - 1]
                            if (prev >= 'a' && prev <= 'z') || (prev >= '0' && prev <= '9') then
                                buf[outPos] <- '-'
                                outPos <- outPos + 1
                        buf[outPos] <- char (int c + 32)
                        outPos <- outPos + 1
                    else
                        buf[outPos] <- c
                        outPos <- outPos + 1
                String(ReadOnlySpan<char>(buf, 0, outPos))

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
        ValueInterpolation.formatNumberWithUnit value (if commonPxCss.Contains kebabName then "px" else "")

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
                let inlineOrig =
                    if not (String.IsNullOrEmpty inlineVal) then Some inlineVal
                    else
                        let mutable index = 0u
                        while index < style.length && style.item index <> canonical do index <- index + 1u
                        if index < style.length then Some "" else None
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
                    ValueInterpolation.Text s, Option.ofObj raw
                | "scrollTop" ->
                    let v = float el.scrollTop
                    ValueInterpolation.Number v, Some (ValueInterpolation.number v)
                | "scrollLeft" ->
                    let v = float el.scrollLeft
                    ValueInterpolation.Number v, Some (ValueInterpolation.number v)
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
                if output.IsScalar && Double.IsFinite output.Number then
                    ElementTransforms.stageNumber el canonical output.Number output.UnitSuffix
                else ElementTransforms.stageComponent el canonical output.Text
                if flushTransform then ElementTransforms.commit el priority
            | (TweenTargetKind.Css | TweenTargetKind.CssVar), (:? Element as el) ->
                let style = (tryStyle el).Value
                let valueText =
                    if output.Kind = ValueInterpolation.ValueKind.Color && output.Components.Length = 4 &&
                       output.Components[3] = 1. && output.Text.StartsWith("rgba(", StringComparison.Ordinal) then
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
                | "scrollTop" -> el.scrollTop <- int (Math.Round(match output.Scalar with Some s -> s | None -> scalar))
                | "scrollLeft" -> el.scrollLeft <- int (Math.Round(match output.Scalar with Some s -> s | None -> scalar))
                | _ -> ()
            | TweenTargetKind.Object, (:? NumericProperty as prop) ->
                let v = match output.Scalar with Some s -> s | None -> scalar
                prop.Value <- v
            | TweenTargetKind.Object, (:? AnimatedObject as bag) ->
                match output.Scalar with
                | Some s -> bag.SetNumber(canonical, s)
                | None -> bag.SetText(canonical, output.Text)
            | _ -> ()

    let writeComponentText (target: obj) (kind: TweenTargetKind) (canonical: string)
                           (text: string) (flushTransform: bool) (priority: string) =
        match kind, target with
        | TweenTargetKind.Transform, (:? Element as el) ->
            ElementTransforms.stageComponent el canonical text
            if flushTransform then ElementTransforms.commit el priority
        | (TweenTargetKind.Css | TweenTargetKind.CssVar), (:? Element as el) ->
            let style = (tryStyle el).Value
            style.setProperty(canonical, text, priority = priority)
        | TweenTargetKind.Attribute, (:? Element as el) ->
            el.setAttribute(canonical, text)
        | TweenTargetKind.Object, (:? AnimatedObject as bag) ->
            bag.SetText(canonical, text)
        | TweenTargetKind.Object, (:? Element as el) ->
            match canonical with
            | "innerHTML" -> (el :?> HTMLElement).innerHTML <- text
            | "textContent" -> (el :> Node).textContent <- text
            | "value" -> el.setAttribute("value", text)
            | "scrollTop" -> el.scrollTop <- 0
            | "scrollLeft" -> el.scrollLeft <- 0
            | _ -> ()
        | TweenTargetKind.Object, (:? NumericProperty as prop) ->
            prop.Value <- 0.
        | _ -> ()

    let writeScalar (target: obj) (kind: TweenTargetKind) (canonical: string) (adapter: TargetAdapterEntry option)
                    (valueKind: ValueInterpolation.ValueKind) (unitSuffix: string) (scalar: float)
                    (flushTransform: bool) (priority: string) =
        match adapter with
        | Some entry ->
            let scalarOpt = if valueKind = ValueInterpolation.ValueKind.Number then Some scalar else None
            let info = AdaptedTweenInfo(target, canonical, scalar, Array.Empty<float>())
            entry.Set target scalarOpt info
        | None ->
            let suffix = if valueKind = ValueInterpolation.ValueKind.Unit then unitSuffix else ""
            match kind, target with
            | TweenTargetKind.Transform, (:? Element as el) ->
                if Double.IsFinite scalar then
                    ElementTransforms.stageNumber el canonical scalar suffix
                else
                    ElementTransforms.stageComponent el canonical (ValueInterpolation.formatNumberWithUnit scalar suffix)
                if flushTransform then ElementTransforms.commit el priority
            | (TweenTargetKind.Css | TweenTargetKind.CssVar), (:? Element as el) ->
                let style = (tryStyle el).Value
                style.setProperty(canonical, ValueInterpolation.formatNumberWithUnit scalar suffix, priority = priority)
            | TweenTargetKind.Attribute, (:? Element as el) ->
                el.setAttribute(canonical, ValueInterpolation.formatNumberWithUnit scalar suffix)
            | TweenTargetKind.Object, (:? NumericProperty as prop) ->
                prop.Value <- scalar
            | TweenTargetKind.Object, (:? AnimatedObject as bag) ->
                if valueKind = ValueInterpolation.ValueKind.Number then
                    bag.SetNumber(canonical, scalar)
                else
                    bag.SetText(canonical, ValueInterpolation.formatNumberWithUnit scalar suffix)
            | TweenTargetKind.Object, (:? Element as el) ->
                match canonical with
                | "scrollTop" -> el.scrollTop <- int (Math.Round scalar)
                | "scrollLeft" -> el.scrollLeft <- int (Math.Round scalar)
                | _ ->
                    let text = ValueInterpolation.formatNumberWithUnit scalar suffix
                    match canonical with
                    | "innerHTML" -> (el :?> HTMLElement).innerHTML <- text
                    | "textContent" -> (el :> Node).textContent <- text
                    | "value" -> el.setAttribute("value", text)
                    | _ -> ()
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
          Modifier = AnimationDefaults.modifier PropertyNumbers.identityFloat
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
     hasFrom: bool, original: ValueInterpolation.Literal, inlineOriginal: string option, priority: string,
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
    member _.Original = original
    member _.Priority = priority
    member val First = first with get, set
    member val Last = last with get, set
    member val AuthoredFirst = first with get, set
    member val AuthoredLast = last with get, set
    member val CurrentNumber = first.Number with get, set
    member val CurrentNumbers = Array.copy first.Components with get, set
    member val ScratchNumbers : float array = null with get, set
    member val CachedEnd = "" with get, set
    member val RenderTransforms = false with get, set
    member val LastTransform : ComposedTween = null with get, set
    member val AfterMuted : ComposedTween = null with get, set
    member val BeforeMuted : ComposedTween = null with get, set
    // A timeline grants the whole child track an interval in parent time. Its
    // individual steps still follow the child's own (possibly reversed) clock.
    member val TrackHead : ComposedTween = null with get, set
    member val NextStep : ComposedTween = null with get, set
    member val TrackStart = 0. with get, set
    member val PreviousTrack : ComposedTween = null with get, set
    member val NextTrack : ComposedTween = null with get, set
    member this.OwnsTimelineTime(time:float) =
        (isNull this.PreviousTrack || time >= this.TrackStart) &&
        (isNull this.NextTrack || time < this.NextTrack.TrackStart)
    member val InWriteRange = true with get, set
    member val Overlapped = false with get, set
    member val Overridden = false with get, set
    member val Replaced = false with get, set
    member val Removed = false with get, set
    member val PrevRep : ComposedTween = null with get, set
    member val NextRep : ComposedTween = null with get, set
    member val PrevAdd : ComposedTween = null with get, set
    member val NextAdd : ComposedTween = null with get, set
    member val SharedLeader : ComposedTween = null with get, set
    member val SharedFrameStamp : int64 = -1L with get, set
    member val SharedProgress : float = Double.NaN with get, set
    member val SharedText : string = null with get, set
    member _.StepSource = stepSource
    member this.Override() =
        this.Overlapped <- true
        this.Overridden <- true
        this.ChangeDuration <- 1e-11
        this.TweenTime <- 1e-11
    member this.RestoreOwnership() =
        this.Replaced <- false
        this.Overridden <- false
        this.Overlapped <- false
        this.ChangeDuration <- this.UpdateDuration
        this.TweenTime <- 0.

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
    member val ScratchNumbers = Array.zeroCreate<float> fromNumbers.Length with get, set
    member _.Priority = priority
    member val Tweens = ResizeArray<ComposedTween>()

and internal CompositionManager(queue: EngineFrameQueue) =
    let replaceMap = Dictionary<struct(obj * string), ResizeArray<ComposedTween>>(CompositionTargetComparer.instance)
    // A completed owner detaches its tweens. Keep its identity while the target lives
    // without retaining the animation, timeline or their other targets.
    let lastOwners = ConditionalWeakTable<obj, Dictionary<string, obj>>()
    let additiveMap = Dictionary<struct(obj * string), AdditiveLookupEntry>(CompositionTargetComparer.instance)
    let activeAnimations = HashSet<ComposedAnimation>()
    let flushTransforms = HashSet<Element>(HashIdentity.Reference)
    let flushPriorities = Dictionary<Element, string>(HashIdentity.Reference)
    let mutable dirtyAdditive = false

    let hasOtherOwner (tween: ComposedTween) =
        if tween.Composition <> KeyframeComposition.Replace then false
        else
            match lastOwners.TryGetValue tween.Target with
            | true, properties ->
                match properties.TryGetValue tween.Property with
                | true, owner -> not (obj.ReferenceEquals(owner, tween.Parent.OwnerToken))
                | _ -> false
            | _ -> false

    let flushAdditive () =
        dirtyAdditive <- false
        if additiveMap.Count > 0 then
            flushTransforms.Clear()
            flushPriorities.Clear()
            try
                for KeyValue(_, entry) in additiveMap do
                    let tweens = entry.Tweens
                    let mutable anyActive = false
                    let mutable idx = 0
                    while idx < tweens.Count && not anyActive do
                        let tw = tweens[idx]
                        if tw.InWriteRange && not tw.Parent.EffectivelyMuted then anyActive <- true
                        idx <- idx + 1
                    if anyActive then
                        let isCompound = entry.Template.Kind = ValueInterpolation.ValueKind.Complex || entry.Template.Kind = ValueInterpolation.ValueKind.Color
                        if isCompound then
                            let fromNums = entry.FromNumbers
                            let mutable scratch = entry.ScratchNumbers
                            if isNull scratch || scratch.Length <> fromNums.Length then
                                scratch <- Array.zeroCreate<float> fromNums.Length
                                entry.ScratchNumbers <- scratch
                            Array.Copy(fromNums, 0, scratch, 0, fromNums.Length)
                            for i = tweens.Count - 1 downto 0 do
                                let tw = tweens[i]
#if BLEND_BENCHMARK
                                if BlendKernel.useVector then BlendKernel.add scratch tw.CurrentNumbers else
                                for c in 0 .. min (scratch.Length - 1) (tw.CurrentNumbers.Length - 1) do
                                    scratch[c] <- scratch[c] + tw.CurrentNumbers[c]
#else
                                BlendSimd.addChannels scratch tw.CurrentNumbers
#endif
                            let isTransform = entry.Kind = TweenTargetKind.Transform
                            match entry.Adapter with
                            | Some _ ->
                                let composed = ValueInterpolation.compose(entry.Template, 0., Array.copy scratch)
                                DomTargetHelpers.writeOutput entry.Target entry.Kind entry.Property entry.Adapter 0. composed false entry.Priority
                            | None ->
                                let isCss = entry.Kind = TweenTargetKind.Css || entry.Kind = TweenTargetKind.CssVar
                                let text = ValueInterpolation.formatComponentsText entry.Template scratch isCss
                                DomTargetHelpers.writeComponentText entry.Target entry.Kind entry.Property text false entry.Priority
                            if isTransform then
                                match entry.Target with
                                | :? Element as el ->
                                    flushTransforms.Add el |> ignore
                                    flushPriorities[el] <- entry.Priority
                                | _ -> ()
                        else
                            let mutable sum = entry.FromNumber
                            for i = tweens.Count - 1 downto 0 do
                                sum <- sum + tweens[i].CurrentNumber
                            let precision = match EngineDefaults.precision() with Some p -> int p | None -> -1
                            let rounded =
                                if precision < 0 || (entry.Template.Kind = ValueInterpolation.ValueKind.Number && entry.Kind = TweenTargetKind.Object) then sum
                                else PropertyNumbers.round (PropertyNumbers.pow10 precision) sum
                            let isTransform = entry.Kind = TweenTargetKind.Transform
                            DomTargetHelpers.writeScalar entry.Target entry.Kind entry.Property entry.Adapter entry.Template.Kind entry.Template.Unit rounded false entry.Priority
                            if isTransform then
                                match entry.Target with
                                | :? Element as el ->
                                    flushTransforms.Add el |> ignore
                                    flushPriorities[el] <- entry.Priority
                                | _ -> ()
                if flushTransforms.Count > 0 then
                    for el in flushTransforms do
                        ElementTransforms.flush el flushPriorities[el]
            finally
                flushTransforms.Clear()
                flushPriorities.Clear()

    do
        let prev = queue.AfterUpdate
        queue.AfterUpdate <- (fun () -> prev(); if dirtyAdditive then flushAdditive())

    member _.RegisterAnimation(anim: ComposedAnimation) = activeAnimations.Add anim |> ignore
    member _.UnregisterAnimation(anim: ComposedAnimation) = activeAnimations.Remove anim |> ignore
    member _.ActiveAnimations = activeAnimations |> Seq.toArray
    member _.MarkAdditiveDirty() = if additiveMap.Count > 0 then dirtyAdditive <- true
    member _.IsAdditiveDirty = dirtyAdditive
    member _.FlushAdditive() = flushAdditive()

    member _.PreviousTimelineTrack(target: obj, property: string, timeline:ComposedTimeline, time:float) =
        match replaceMap.TryGetValue(struct(target, property)) with
        | true, list when list.Count > 0 ->
            let mutable best : ComposedTween = null
            for tw in list do
                if obj.ReferenceEquals(tw.TrackHead, tw) && tw.TrackStart <= time &&
                   (isNull best || tw.TrackStart >= best.TrackStart) &&
                   (tw.Parent.ParentTimeline |> Option.exists (fun t -> obj.ReferenceEquals(t, timeline))) then
                    best <- tw
            if isNull best then None else Some best
        | _ -> None

    member _.HasOtherOwner(tween: ComposedTween) = hasOtherOwner tween

    member _.ReleaseOwner(target: obj, property: string, token: obj) =
        match lastOwners.TryGetValue target with
        | true, properties ->
            match properties.TryGetValue property with
            | true, owner when obj.ReferenceEquals(owner, token) -> properties.Remove property |> ignore
            | _ -> ()
        | _ -> ()

    member _.ComposeTween(tween: ComposedTween) =
        let key = struct(tween.Target, tween.Property)
        match tween.Composition with
        | KeyframeComposition.None -> ()
        | KeyframeComposition.Replace ->
            // Independent playheads cannot recover ownership by seeking into each other's history.
            match replaceMap.TryGetValue key with
            | true, existing when hasOtherOwner tween ->
                replaceMap.Remove key |> ignore
                let owners = HashSet<ComposedAnimation>()
                for prior in existing do
                    prior.Override()
                    prior.Replaced <- true
                    prior.PrevRep <- null
                    prior.NextRep <- null
                    owners.Add prior.Parent |> ignore
                for owner in owners do owner.OnPropertyReplaced()
            | _ -> ()
            lastOwners.GetOrCreateValue(tween.Target)[tween.Property] <- tween.Parent.OwnerToken
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
            if not (isNull prev) then
                prev.NextRep <- tween
                prev.Parent.InvalidateSoaBatch()
                tween.Parent.InvalidateSoaBatch()
            if not (isNull next) then
                next.PrevRep <- tween
                next.Parent.InvalidateSoaBatch()
                tween.Parent.InvalidateSoaBatch()
            list.Insert(insertIdx, tween)
            if obj.ReferenceEquals(tween.TrackHead, tween) then
                let mutable before : ComposedTween = null
                let mutable after : ComposedTween = null
                for track in list do
                    if obj.ReferenceEquals(track.TrackHead, track) && not (obj.ReferenceEquals(track,tween)) then
                        if track.TrackStart <= tween.TrackStart && (isNull before || track.TrackStart >= before.TrackStart) then
                            before <- track
                        elif track.TrackStart > tween.TrackStart && (isNull after || track.TrackStart < after.TrackStart) then
                            after <- track
                tween.PreviousTrack <- before
                tween.NextTrack <- after
                if not (isNull before) then before.NextTrack <- tween
                if not (isNull after) then after.PreviousTrack <- tween
            elif isNull tween.TrackHead && not (isNull prev) then
                if prev.AbsoluteEnd > tween.AbsoluteUpdateStart then
                    let prevTLOffset = prev.AbsoluteEnd - (prev.Start + prev.UpdateDuration)
                    let updated = PropertyNumbers.round 1e12 (tween.AbsoluteUpdateStart - prevTLOffset - prev.Start)
                    prev.ChangeDuration <- updated
                    prev.TweenTime <- updated
                    prev.Overlapped <- true
                    if updated < 1e-11 then prev.Override()
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

    member _.RemoveTweenSiblings(tween: ComposedTween, ?rewindBlend: bool) =
        let rewindBlend = defaultArg rewindBlend false
        let key = struct(tween.Target, tween.Property)
        match tween.Composition with
        | KeyframeComposition.None -> ()
        | KeyframeComposition.Replace ->
            match replaceMap.TryGetValue key with
            | true, list ->
                if list.Remove tween then
                    if not (isNull tween.PreviousTrack) then tween.PreviousTrack.NextTrack <- tween.NextTrack
                    if not (isNull tween.NextTrack) then tween.NextTrack.PreviousTrack <- tween.PreviousTrack
                    tween.NextTrack <- null
                    tween.PreviousTrack <- null
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
                    if entry.Tweens.Count > 0 then
                        let residualNumber = if rewindBlend then tween.First.Number else tween.CurrentNumber
                        entry.FromNumber <- entry.FromNumber + residualNumber
                        let residualNumbers = if rewindBlend then tween.First.Components else tween.CurrentNumbers
                        if residualNumbers.Length > 0 && entry.FromNumbers.Length > 0 then
                            let bound = min entry.FromNumbers.Length residualNumbers.Length
                            for c = 0 to bound - 1 do
                                entry.FromNumbers[c] <- entry.FromNumbers[c] + residualNumbers[c]
                    for i in 0 .. entry.Tweens.Count - 1 do
                        entry.Tweens[i].PrevAdd <- if i = 0 then null else entry.Tweens[i - 1]
                        entry.Tweens[i].NextAdd <- if i + 1 = entry.Tweens.Count then null else entry.Tweens[i + 1]
                    if entry.Tweens.Count = 0 then additiveMap.Remove key |> ignore
                tween.PrevAdd <- null
                tween.NextAdd <- null
            | _ -> ()

and [<Sealed>] private SoaScalarBatch
    (start: float, updateDuration: float, changeDuration: float, absStart: float,
     ease: float -> float, targetKind: TweenTargetKind, valueKind: ValueInterpolation.ValueKind,
     froms: float array, tos: float array, outValues: float array,
     bagTargets: AnimatedObject array, sharedProperty: string,
     targets: obj array, properties: string array, units: string array, priorities: string array, renderTransforms: bool array) =
    member val Start = start with get, set
    member val UpdateDuration = updateDuration with get, set
    member val ChangeDuration = changeDuration with get, set
    member val AbsoluteStart = absStart with get, set
    member _.Ease = ease
    member _.TargetKind = targetKind
    member _.ValueKind = valueKind
    member _.Froms = froms
    member _.Tos = tos
    member _.OutValues = outValues
    member _.BagTargets = bagTargets
    member _.SharedProperty = sharedProperty
    member _.Targets = targets
    member _.Properties = properties
    member _.Units = units
    member _.Priorities = priorities
    member _.RenderTransforms = renderTransforms
    member val LastTweenTime = 0. with get, set
    member val HasSampled = false with get, set

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
    let mutable soaBatch : SoaScalarBatch voption = ValueNone
    let mutable hasSharedLeaders = false
    let mutable renderFrameCounter = 0L
    let mutable siblingsAttached = false
    let mutable cancelled = false
    let mutable restoreVersion = 0
    let mutable parentTimelineRef = parentTimeline
    let ownerToken = match parentTimeline with Some timeline -> timeline.OwnerToken | None -> obj()
    // anime's absolute offset: a top-level animation starts at the engine's last tick and a timeline child at its
    // timeline's, so Replace orders the tweens of separate animations and timelines on one clock.
    let mutable offset =
        match parentTimeline with
        | Some timeline -> timeline.AbsoluteOffset + timelineOffset
        | None -> queue.OffsetTick() * timeScale + timelineOffset

    let assignRenderTransforms () =
        let lastByTarget = Dictionary<obj, ComposedTween>(HashIdentity.Reference)
        for tw in tweens do
            tw.RenderTransforms <- false
            tw.LastTransform <- null
            if not tw.Removed && not tw.Replaced && not tw.StepSource.Muted && tw.Kind = TweenTargetKind.Transform then
                lastByTarget[tw.Target] <- tw
        for tw in tweens do
            if not tw.Removed && not tw.Replaced && not tw.StepSource.Muted && tw.Kind = TweenTargetKind.Transform then
                let last = lastByTarget[tw.Target]
                tw.LastTransform <- last
                // A muted interval can suppress the usual final commit for this element.
                tw.RenderTransforms <- obj.ReferenceEquals(tw, last) || not (isNull last.AfterMuted && isNull last.BeforeMuted)

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
                let mutable runningTime = 0.
                let mutable prevTween : ComposedTween option = None
                let trackStart = tweens.Count
                let mutable trackHead : ComposedTween = null
                let mutable previousStep : ComposedTween = null
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
                    let parentStart = timelineOffset + stepStart / options.Playback.Speed
                    // As anime.js, a replaced property starts from its rendered value; only a sibling of the same timeline
                    // hands over its own value.
                    let fallbackStartLiteral =
                        match prevTween with
                        | Some (prev: ComposedTween) -> ValueInterpolation.Text prev.CachedEnd
                        | None ->
                            match parentTimelineRef with
                            | Some timeline when comp = KeyframeComposition.Replace && step.From.IsNone ->
                                match manager.PreviousTimelineTrack(target, canonical, timeline, parentStart) with
                                | Some head -> head.Parent.SampleTrackAt(head, parentStart)
                                | None -> origLiteral
                            | _ -> origLiteral
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
                            step.From.IsSome, origLiteral, inlineOrig, stylePriority,
                            normFirst, normLast, step, targets.Length)
                    if parentTimelineRef.IsSome && comp = KeyframeComposition.Replace && not step.Muted then
                        if isNull trackHead then
                            trackHead <- tw
                            tw.TrackStart <- parentStart
                        else previousStep.NextStep <- tw
                        tw.TrackHead <- trackHead
                        previousStep <- tw
                    let endComposed =
                        if normLast.Kind = ValueInterpolation.ValueKind.Number || normLast.Kind = ValueInterpolation.ValueKind.Unit then
                            let m = modifier normLast.Number
                            ValueInterpolation.formatNumberWithUnit m (if normLast.Kind = ValueInterpolation.ValueKind.Unit then normLast.Unit else "")
                        elif normLast.Kind = ValueInterpolation.ValueKind.Color then
                            ValueInterpolation.formatComponentsText normLast normLast.Components false
                        else
                            let vals =
                                if Object.ReferenceEquals(modifier, PropertyNumbers.identityFloat) then normLast.Components
                                else normLast.Components |> Array.map modifier
                            ValueInterpolation.formatComponentsText normLast vals false
                    tw.CachedEnd <- endComposed
                    if sIdx = 0 && stepStart < minDelay then minDelay <- stepStart
                    let stepFinish = PropertyNumbers.round 1e12 (stepStart + stepUpdateDur)
                    if stepFinish > maxEnd then maxEnd <- stepFinish
                    runningTime <- stepFinish
                    prevTween <- Some tw
                    tweens.Add tw
                if spec.Steps |> Array.exists _.Muted then
                    // A forced seek also samples out-of-range tweens. Bound each run of unmuted
                    // steps so later or earlier endpoints cannot write inside a muted interval.
                    let mutable afterMuted : ComposedTween = null
                    for i in trackStart .. tweens.Count - 1 do
                        let tw = tweens[i]
                        if i > trackStart && tweens[i-1].StepSource.Muted && not tw.StepSource.Muted then
                            afterMuted <- tw
                        tw.AfterMuted <- afterMuted
                        tw.InWriteRange <- isNull afterMuted
                    let mutable beforeMuted : ComposedTween = null
                    for i in tweens.Count - 1 .. -1 .. trackStart do
                        let tw = tweens[i]
                        if tw.StepSource.Muted then beforeMuted <- tw
                        tw.BeforeMuted <- beforeMuted
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

    let sameFloatBits (a: float array) (b: float array) =
        Object.ReferenceEquals(a, b) ||
        (a.Length = b.Length &&
         MemoryMarshal.Cast<float, int64>(ReadOnlySpan<float>(a)).SequenceEqual(
             MemoryMarshal.Cast<float, int64>(ReadOnlySpan<float>(b))))

    let sameStringsOrdinal (a: string array) (b: string array) =
        Object.ReferenceEquals(a, b) ||
        (a.Length = b.Length &&
         (let mutable eq = true
          let mutable idx = 0
          while idx < a.Length && eq do
              if not (StringComparer.Ordinal.Equals(a[idx], b[idx])) then eq <- false
              idx <- idx + 1
          eq))

    let clearSharedLeaders () =
        if hasSharedLeaders then
            hasSharedLeaders <- false
            for k = 0 to tweens.Count - 1 do
                tweens[k].SharedLeader <- null

    let rebuildSoaBatch () =
        clearSharedLeaders()
        let n = tweens.Count
        if n < 8 || parentTimelineRef.IsSome then
            soaBatch <- ValueNone
        else
            let tw0 = tweens[0]
            let vKind = tw0.First.Kind
            let mutable ok =
                (vKind = ValueInterpolation.ValueKind.Number || vKind = ValueInterpolation.ValueKind.Unit) &&
                tw0.Last.Kind = vKind &&
                tw0.Composition <> KeyframeComposition.Blend &&
                tw0.Adapter.IsNone &&
                Object.ReferenceEquals(tw0.Modifier, PropertyNumbers.identityFloat)
            let mutable allBagsSameProp = ok && tw0.Kind = TweenTargetKind.Object && vKind = ValueInterpolation.ValueKind.Number && (tw0.Target :? AnimatedObject)
            let prop0 = tw0.Property
            let mutable i = 0
            while i < n && ok do
                let tw = tweens[i]
                if tw.Removed || tw.StepSource.Muted || tw.Replaced || tw.Overridden || tw.Overlapped ||
                   not (isNull tw.PrevRep) || not (isNull tw.NextRep) || not (isNull tw.TrackHead) ||
                   not (isNull tw.AfterMuted) || not (isNull tw.BeforeMuted) ||
                   tw.First.Kind <> vKind || tw.Last.Kind <> vKind ||
                   tw.Kind <> tw0.Kind || tw.Composition <> tw0.Composition || tw.Adapter.IsSome ||
                   tw.Start <> tw0.Start || tw.UpdateDuration <> tw0.UpdateDuration ||
                   tw.ChangeDuration <> tw0.ChangeDuration || tw.AbsoluteStart <> tw0.AbsoluteStart ||
                   not (Object.ReferenceEquals(tw.Ease, tw0.Ease)) ||
                   not (Object.ReferenceEquals(tw.Modifier, PropertyNumbers.identityFloat)) then
                    ok <- false
                elif allBagsSameProp && (not (tw.Target :? AnimatedObject) || not (StringComparer.Ordinal.Equals(tw.Property, prop0))) then
                    allBagsSameProp <- false
                i <- i + 1
            if not ok then
                soaBatch <- ValueNone
            else
                let froms = Array.zeroCreate<float> n
                let tos = Array.zeroCreate<float> n
                let outVals = Array.zeroCreate<float> n
                for k = 0 to n - 1 do
                    let tw = tweens[k]
                    froms[k] <- tw.First.Number
                    tos[k] <- tw.Last.Number
                    outVals[k] <- tw.CurrentNumber
                if allBagsSameProp then
                    let bags = Array.init n (fun k -> tweens[k].Target :?> AnimatedObject)
                    soaBatch <-
                        ValueSome(
                            SoaScalarBatch(
                                tw0.Start, tw0.UpdateDuration, tw0.ChangeDuration, tw0.AbsoluteStart,
                                tw0.Ease, tw0.Kind, vKind, froms, tos, outVals,
                                bags, prop0, null, null, null, null, null))
                else
                    let tgts = Array.init n (fun k -> tweens[k].Target)
                    let props = Array.init n (fun k -> tweens[k].Property)
                    let units = Array.init n (fun k -> if vKind = ValueInterpolation.ValueKind.Unit then tweens[k].Last.Unit else "")
                    let prios = Array.init n (fun k -> tweens[k].Priority)
                    let rts = Array.init n (fun k -> tweens[k].RenderTransforms)
                    soaBatch <-
                        ValueSome(
                            SoaScalarBatch(
                                tw0.Start, tw0.UpdateDuration, tw0.ChangeDuration, tw0.AbsoluteStart,
                                tw0.Ease, tw0.Kind, vKind, froms, tos, outVals,
                                null, prop0, tgts, props, units, prios, rts))
        if soaBatch.IsNone && n >= 2 && parentTimelineRef.IsNone then
            let leaders = ResizeArray<ComposedTween>(4)
            for k = 0 to n - 1 do
                let tw = tweens[k]
                let vk = tw.First.Kind
                if (vk = ValueInterpolation.ValueKind.Color || vk = ValueInterpolation.ValueKind.Complex) &&
                   tw.Last.Kind = vk &&
                   tw.Composition <> KeyframeComposition.Blend &&
                   tw.Adapter.IsNone &&
                   Object.ReferenceEquals(tw.Modifier, PropertyNumbers.identityFloat) &&
                   not tw.Removed && not tw.StepSource.Muted && not tw.Replaced && not tw.Overridden && not tw.Overlapped &&
                   isNull tw.PrevRep && isNull tw.NextRep && isNull tw.TrackHead &&
                   isNull tw.AfterMuted && isNull tw.BeforeMuted then
                    let twCss = tw.Kind = TweenTargetKind.Css || tw.Kind = TweenTargetKind.CssVar
                    let twRound = tw.Kind <> TweenTargetKind.Object
                    let mutable matched : ComposedTween = null
                    let mutable li = 0
                    while li < leaders.Count && isNull matched do
                        let cand = leaders[li]
                        if cand.First.Kind = vk &&
                           (cand.Kind = TweenTargetKind.Css || cand.Kind = TweenTargetKind.CssVar) = twCss &&
                           (cand.Kind <> TweenTargetKind.Object) = twRound &&
                           cand.Start = tw.Start &&
                           cand.UpdateDuration = tw.UpdateDuration &&
                           cand.ChangeDuration = tw.ChangeDuration &&
                           cand.AbsoluteStart = tw.AbsoluteStart &&
                           Object.ReferenceEquals(cand.Ease, tw.Ease) &&
                           sameFloatBits cand.First.Components tw.First.Components &&
                           sameFloatBits cand.Last.Components tw.Last.Components &&
                           (vk = ValueInterpolation.ValueKind.Color || sameStringsOrdinal cand.Last.Strings tw.Last.Strings) then
                            matched <- cand
                        li <- li + 1
                    if not (isNull matched) then
                        tw.SharedLeader <- matched
                        hasSharedLeaders <- true
                    elif leaders.Count < 16 then
                        leaders.Add tw

    let invalidateSoaBatch () =
        clearSharedLeaders()
        match soaBatch with
        | ValueNone -> ()
        | ValueSome batch ->
            soaBatch <- ValueNone
            if batch.HasSampled then
                let tt = batch.LastTweenTime
                let outVals = batch.OutValues
                let count = min tweens.Count outVals.Length
                for k = 0 to count - 1 do
                    let tw = tweens[k]
                    tw.TweenTime <- tt
                    tw.CurrentNumber <- outVals[k]

    let initialDelay, initialIterDuration = buildTweens()
    let effectivePlayback =
        { options.Playback with
            Delay = initialDelay
            Repeat = if tweens.Count = 0 then PlaybackRepeat.Count 0u else options.Playback.Repeat }

    let attachSiblings reclaim =
        if not siblingsAttached then
            siblingsAttached <- true
            manager.RegisterAnimation this
            for tw in tweens do
                if not tw.Removed && not tw.StepSource.Muted && not tw.Replaced then
                    if not reclaim && manager.HasOtherOwner tw then
                        tw.Override()
                        tw.Replaced <- true
                    else manager.ComposeTween tw
            rebuildSoaBatch()

    let detachSiblings rewindBlend =
        if siblingsAttached then
            siblingsAttached <- false
            manager.UnregisterAnimation this
            for tw in tweens do manager.RemoveTweenSiblings(tw, rewindBlend = rewindBlend)

    let mutable clock : Timer = Unchecked.defaultof<Timer>
    let mutable fullyReplaced = false
    let mutable pendingInitialAttach = false
    // One guard closure over a token each render saves and restores; guards are only checked while the render runs.
    let mutable renderToken = Int64.MinValue
    let canWrite = Closure.keep (fun () -> clock.IsCurrent renderToken && not clock.EffectivelyMuted)
    let render = PlaybackRenderer(fun (sample: PlaybackSample) mode muted ->
        if pendingInitialAttach && not clock.EffectivelyMuted && not cancelled && not fullyReplaced then
            pendingInitialAttach <- false
            attachSiblings true
            assignRenderTransforms()
        let savedToken = renderToken
        renderToken <- clock.WriteToken
        let frameStamp =
            if hasSharedLeaders then
                let nextStamp = renderFrameCounter + 1L
                renderFrameCounter <- nextStamp
                nextStamp
            else -1L
        try
            let delta = sample.AbsoluteTime - clock.PreviousAbsoluteTime
            let forced = mode = PlaybackTickMode.Force || (if sample.Backwards then -delta else delta) >= clock.TickThreshold
            let absolute = PropertyNumbers.round 1e12 (offset + clock.Delay + sample.IterationTime)
            let precision = match options.Precision with Some p -> int p | None -> -1
            let mutable rendered = false
            let mutable hasBlend = false
            let mutable startIdx = 0
            match soaBatch with
            | ValueSome batch when clock.IsCurrent renderToken ->
                startIdx <- tweens.Count
                let ending = batch.AbsoluteStart + batch.ChangeDuration
                let history =
                    (batch.LastTweenTime <> batch.ChangeDuration || absolute <= ending) &&
                    (batch.LastTweenTime <> 0. || absolute >= batch.AbsoluteStart)
                if forced || history then
                    let tt = Math.Clamp(sample.IterationTime - batch.Start, 0., batch.ChangeDuration)
                    batch.LastTweenTime <- tt
                    batch.HasSampled <- true
                    if not clock.EffectivelyMuted then
                        let progress = batch.Ease (tt / batch.UpdateDuration)
                        if not (Double.IsFinite progress) then invalidArg "progress" "Expected finite input."
                        if canWrite() && soaBatch.IsSome then
                            let roundNumbers = batch.TargetKind <> TweenTargetKind.Object
                            let effPrecision =
                                if progress = 0. || progress = 1. || (batch.ValueKind = ValueInterpolation.ValueKind.Number && not roundNumbers) then -1
                                else precision
                            let outVals = batch.OutValues
                            ValueInterpolation.lerpComplexSimd batch.Froms batch.Tos outVals progress effPrecision
                            if not clock.InternalRender then
                                let bags = batch.BagTargets
                                if not (isNull bags) then
                                    let prop = batch.SharedProperty
                                    for i = 0 to bags.Length - 1 do
                                        bags[i].SetNumber(prop, outVals[i])
                                else
                                    let tgts = batch.Targets
                                    let props = batch.Properties
                                    let units = batch.Units
                                    let prios = batch.Priorities
                                    let rts = batch.RenderTransforms
                                    let tKind = batch.TargetKind
                                    let vKind = batch.ValueKind
                                    for i = 0 to tgts.Length - 1 do
                                        DomTargetHelpers.writeScalar tgts[i] tKind props[i] None vKind units[i] outVals[i] rts[i] prios[i]
                                rendered <- true
            | _ -> ()
            for i in startIdx .. tweens.Count - 1 do
                let tw = tweens[i]
                if not tw.Removed && not tw.StepSource.Muted && clock.IsCurrent renderToken then
                    tw.InWriteRange <-
                        (isNull tw.AfterMuted || sample.IterationTime >= tw.AfterMuted.Start) &&
                        (isNull tw.BeforeMuted || sample.IterationTime < tw.BeforeMuted.Start)
                    let prev = tw.PrevRep
                    let eligible =
                        if not tw.InWriteRange || tw.Replaced then false
                        elif not (isNull tw.TrackHead) then
                            tw.TrackHead.OwnsTimelineTime(parentTimelineRef.Value.SampleTime) &&
                            (obj.ReferenceEquals(tw.TrackHead, tw) || sample.IterationTime >= tw.Start) &&
                            (isNull tw.NextStep || sample.IterationTime < tw.NextStep.Start)
                        else
                            let next = tw.NextRep
                            let ending = tw.AbsoluteStart + tw.ChangeDuration
                            let takeover = if isNull next || next.Overridden then ending else ending + next.Delay
                            let history =
                                (tw.TweenTime <> tw.ChangeDuration || absolute <= takeover || (not (isNull prev) && isNull next)) &&
                                (tw.TweenTime <> 0. || absolute >= tw.AbsoluteStart ||
                                 (not (isNull next) && not next.Overridden && next.TweenTime <> 0. && sample.IterationTime < next.Start))
                            (forced || history) && (isNull prev || sample.IterationTime >= tw.Start) &&
                            (tw.Composition <> KeyframeComposition.Replace ||
                             (not tw.Overridden && (not tw.Overlapped || absolute <= ending) &&
                              (isNull next || next.Overridden || absolute <= takeover) &&
                              (isNull prev || prev.Overridden || absolute >= prev.AbsoluteStart + prev.ChangeDuration + tw.Delay)))
                    if eligible && clock.EffectivelyMuted then
                        // Retain the last visible blend contribution until the clock can write again.
                        tw.TweenTime <- Math.Clamp(sample.IterationTime - tw.Start, 0., tw.ChangeDuration)
                    elif eligible then
                        tw.TweenTime <- Math.Clamp(sample.IterationTime - tw.Start, 0., tw.ChangeDuration)
                        let progress = tw.Ease (tw.TweenTime / tw.UpdateDuration)
                        let roundNumbers = tw.Kind <> TweenTargetKind.Object
                        let vKind = tw.First.Kind
                        if vKind = ValueInterpolation.ValueKind.Number || vKind = ValueInterpolation.ValueKind.Unit then
                            let struct(ok, scalar) =
                                ValueInterpolation.trySampleScalarValue(
                                    roundNumbers, vKind, tw.First.Number, tw.Last.Number, progress, precision, tw.Modifier, canWrite)
                            if ok && not tw.Replaced && canWrite() then
                                tw.CurrentNumber <- scalar
                                let initialFrom =
                                    clock.InternalRender && tw.HasFrom && obj.ReferenceEquals(tw.TrackHead, tw) &&
                                    isNull tw.PreviousTrack && parentTimelineRef.Value.SampleTime <= tw.TrackStart
                                if initialFrom then
                                    DomTargetHelpers.writeScalar tw.Target tw.Kind tw.Property tw.Adapter tw.Last.Kind tw.Last.Unit scalar true tw.Priority
                                elif not clock.InternalRender then
                                    if tw.Composition = KeyframeComposition.Blend then
                                        hasBlend <- true
                                        rendered <- true
                                    else
                                        let last = tw.LastTransform
                                        let flush =
                                            tw.RenderTransforms ||
                                            (not (isNull last) && not (isNull last.TrackHead) &&
                                             not (last.TrackHead.OwnsTimelineTime(parentTimelineRef.Value.SampleTime)))
                                        DomTargetHelpers.writeScalar tw.Target tw.Kind tw.Property tw.Adapter tw.Last.Kind tw.Last.Unit scalar flush tw.Priority
                                        rendered <- true
                        else
                            let initialFrom =
                                clock.InternalRender && tw.HasFrom && obj.ReferenceEquals(tw.TrackHead, tw) &&
                                isNull tw.PreviousTrack && parentTimelineRef.Value.SampleTime <= tw.TrackStart
                            if not initialFrom && (clock.InternalRender || tw.Composition = KeyframeComposition.Blend) then
                                let dst =
                                    if Object.ReferenceEquals(tw.Modifier, PropertyNumbers.identityFloat) then
                                        if tw.CurrentNumbers.Length <> tw.Last.Components.Length then
                                            tw.CurrentNumbers <- Array.zeroCreate<float> tw.Last.Components.Length
                                        tw.CurrentNumbers
                                    else
                                        let mutable s = tw.ScratchNumbers
                                        if isNull s || s.Length <> tw.Last.Components.Length then
                                            s <- Array.zeroCreate<float> tw.Last.Components.Length
                                            tw.ScratchNumbers <- s
                                        s
                                if ValueInterpolation.sampleComponentsInto(roundNumbers, tw.First, tw.Last, progress, precision, tw.Modifier, canWrite, dst) &&
                                   not tw.Replaced && canWrite() then
                                    tw.CurrentNumber <- 0.
                                    if not (Object.ReferenceEquals(dst, tw.CurrentNumbers)) then
                                        if tw.CurrentNumbers.Length <> dst.Length then
                                            tw.CurrentNumbers <- Array.copy dst
                                        else
                                            Array.Copy(dst, 0, tw.CurrentNumbers, 0, dst.Length)
                                    if not clock.InternalRender then
                                        hasBlend <- true
                                        rendered <- true
                            elif tw.Adapter.IsSome then
                                let output = ValueInterpolation.sampleOutput(roundNumbers, tw.First, tw.Last, progress, precision, tw.Modifier, canWrite)
                                if not (isNull output) && not tw.Replaced && canWrite() then
                                    let scalar = output.Number
                                    tw.CurrentNumber <- scalar
                                    if output.Components.Length > 0 then
                                        if tw.CurrentNumbers.Length = output.Components.Length then
                                            Array.Copy(output.Components, 0, tw.CurrentNumbers, 0, output.Components.Length)
                                        else
                                            tw.CurrentNumbers <- Array.copy output.Components
                                    if initialFrom then
                                        DomTargetHelpers.writeOutput tw.Target tw.Kind tw.Property tw.Adapter scalar output true tw.Priority
                                    elif not clock.InternalRender then
                                        let last = tw.LastTransform
                                        let flush =
                                            tw.RenderTransforms ||
                                            (not (isNull last) && not (isNull last.TrackHead) &&
                                             not (last.TrackHead.OwnsTimelineTime(parentTimelineRef.Value.SampleTime)))
                                        DomTargetHelpers.writeOutput tw.Target tw.Kind tw.Property tw.Adapter scalar output flush tw.Priority
                                        rendered <- true
                            else
                                let ldr = tw.SharedLeader
                                if not initialFrom && not (isNull ldr) && ldr.SharedFrameStamp = frameStamp &&
                                   ldr.SharedProgress = progress && not ldr.Replaced && not ldr.Removed && not ldr.Overlapped then
                                    if not (Double.IsFinite progress) then invalidArg "progress" "Expected finite input."
                                    if not tw.Replaced && canWrite() then
                                        tw.CurrentNumber <- 0.
                                        let src = ldr.CurrentNumbers
                                        if tw.CurrentNumbers.Length = src.Length then
                                            Array.Copy(src, 0, tw.CurrentNumbers, 0, src.Length)
                                        else
                                            tw.CurrentNumbers <- Array.copy src
                                        if not clock.InternalRender then
                                            let last = tw.LastTransform
                                            let flush =
                                                tw.RenderTransforms ||
                                                (not (isNull last) && not (isNull last.TrackHead) &&
                                                 not (last.TrackHead.OwnsTimelineTime(parentTimelineRef.Value.SampleTime)))
                                            DomTargetHelpers.writeComponentText tw.Target tw.Kind tw.Property ldr.SharedText flush tw.Priority
                                            rendered <- true
                                else
                                    let dst =
                                        if Object.ReferenceEquals(tw.Modifier, PropertyNumbers.identityFloat) then
                                            if tw.CurrentNumbers.Length <> tw.Last.Components.Length then
                                                tw.CurrentNumbers <- Array.zeroCreate<float> tw.Last.Components.Length
                                            tw.CurrentNumbers
                                        else
                                            let mutable s = tw.ScratchNumbers
                                            if isNull s || s.Length <> tw.Last.Components.Length then
                                                s <- Array.zeroCreate<float> tw.Last.Components.Length
                                                tw.ScratchNumbers <- s
                                            s
                                    if ValueInterpolation.sampleComponentsInto(roundNumbers, tw.First, tw.Last, progress, precision, tw.Modifier, canWrite, dst) &&
                                       not tw.Replaced && canWrite() then
                                        tw.CurrentNumber <- 0.
                                        if not (Object.ReferenceEquals(dst, tw.CurrentNumbers)) then
                                            if tw.CurrentNumbers.Length <> dst.Length then
                                                tw.CurrentNumbers <- Array.copy dst
                                            else
                                                Array.Copy(dst, 0, tw.CurrentNumbers, 0, dst.Length)
                                        let isCss = tw.Kind = TweenTargetKind.Css || tw.Kind = TweenTargetKind.CssVar
                                        let text = ValueInterpolation.formatComponentsText tw.Last tw.CurrentNumbers isCss
                                        if not initialFrom && hasSharedLeaders then
                                            tw.SharedProgress <- progress
                                            tw.SharedText <- text
                                            tw.SharedFrameStamp <- frameStamp
                                        if initialFrom then
                                            DomTargetHelpers.writeComponentText tw.Target tw.Kind tw.Property text true tw.Priority
                                        elif not clock.InternalRender then
                                            let last = tw.LastTransform
                                            let flush =
                                                tw.RenderTransforms ||
                                                (not (isNull last) && not (isNull last.TrackHead) &&
                                                 not (last.TrackHead.OwnsTimelineTime(parentTimelineRef.Value.SampleTime)))
                                            DomTargetHelpers.writeComponentText tw.Target tw.Kind tw.Property text flush tw.Priority
                                            rendered <- true
                    elif tw.TweenTime <> 0. && not (isNull prev) && sample.IterationTime < tw.Start then
                        tw.TweenTime <- 0.
            if hasBlend && not clock.InternalRender then
                if queue.Pumping || parentTimelineRef.IsSome then manager.MarkAdditiveDirty()
                else manager.FlushAdditive()
            if rendered && sample.AboveZero && not muted && canWrite() then
                options.OnRender this
        finally renderToken <- savedToken)

    do
        clock <- ScopeRegistration.run ignore (fun () ->
            new Timer(
                scheduler, initialIterDuration, options = effectivePlayback,
                onBegin = (fun _ -> options.OnBegin this),
                onBeforeUpdate = (fun _ -> options.OnBeforeUpdate this),
                onUpdate = (fun _ -> options.OnUpdate this),
                onLoop = (fun _ -> options.OnLoop this),
                onPause = (fun _ -> options.OnPause this),
                onComplete = (fun _ -> options.OnComplete this)))
        if parentTimelineRef.IsNone then
            clock.OnSettled <- fun () ->
                scheduler.Microtask(fun () ->
                    if clock.Completed && clock.Paused then detachSiblings false) |> ignore
        parentTimelineRef |> Option.iter (fun timeline -> clock.InheritMute(fun () -> timeline.Muted))
        clock.ConfigureRenderer(render, sampleWhenMuted = true, ?totalDuration = (if tweens.Count = 0 || initialIterDuration <= 1e-11 then Some 1e-11 else None))
        clock.OnCancel <- (fun () -> manager.UnregisterAnimation this; detachSiblings false)
        clock.OnFault <- (fun () -> manager.UnregisterAnimation this; detachSiblings false)
        if clock.EffectivelyMuted then
            pendingInitialAttach <- true
            manager.RegisterAnimation this
        else
            attachSiblings true
        ScopeRegistration.register (fun () -> this.Revert() |> ignore)
        if parentTimelineRef.IsSome then
            try clock.AttachToTimeline(internalRender = true)
            with constructionError ->
                try this.Revert(false) |> ignore
                with restorationError ->
                    raise (System.AggregateException(constructionError, restorationError))
                reraise()
        else
            clock.Reset()
            if options.Autoplay && not cancelled && ownerAllowsWrite() then clock.Resume()
            elif tweens.Count = 0 && options.Autoplay then clock.Complete()
            else ignore timeScale

    member internal _.ParentTimeline
        with get() : ComposedTimeline option = parentTimelineRef
        and set (value: ComposedTimeline option) = parentTimelineRef <- value

    member internal _.Clock = clock
    member internal _.SampleTrackAt(head:ComposedTween, parentTime:float) : ValueInterpolation.Literal =
        let time = clock.IterationTimeAt((parentTime - timelineOffset) * clock.Speed)
        let mutable step = head
        while not (isNull step.NextStep) && step.NextStep.Start <= time do step <- step.NextStep
        let current = clock.CaptureWriteGuard()
        let live () = current() && not step.Removed && not step.Replaced
        let progress = step.Ease(Math.Clamp((time - step.Start) / step.UpdateDuration, 0., 1.))
        let precision = options.Precision |> Option.map int |> Option.defaultValue -1
        let output = ValueInterpolation.sampleOutput(step.Kind <> TweenTargetKind.Object, step.First, step.Last, progress, precision, step.Modifier, live)
        if isNull output then invalidOp "The property owner changed while sampling the timeline handoff."
        if output.HasScalar then ValueInterpolation.Number output.Number
        else ValueInterpolation.Text output.Text
    member internal _.RegainsTrack(previousTime:float, time:float) =
        tweens.Exists(fun tw -> obj.ReferenceEquals(tw.TrackHead,tw) && not tw.Replaced &&
                                tw.OwnsTimelineTime(time) && not (tw.OwnsTimelineTime(previousTime)))
    member internal _.InvalidateSoaBatch() = invalidateSoaBatch()
    member internal _.OnPropertyReplaced() =
        invalidateSoaBatch()
        if tweens.TrueForAll(fun tw -> tw.Removed || tw.StepSource.Muted || tw.Replaced) then
            fullyReplaced <- true
            match parentTimelineRef with
            | Some timeline when timeline.AllChildrenOverlapped() -> timeline.Cancel()
            | _ -> clock.Cancel()
        else assignRenderTransforms()
    member internal _.EffectivelyMuted : bool = clock.EffectivelyMuted
    member internal _.Tweens = tweens
    member internal _.OwnerToken : obj = ownerToken
    member internal _.AllTweensOverlapped() =
        let mutable activeCount = 0
        let mutable allOver = true
        for tw in tweens do
            if not tw.Removed && not tw.StepSource.Muted then
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
    /// <summary>Signed interval between preceding playback samples, in the engine time unit.</summary>
    member _.DeltaTime = clock.DeltaTime
    /// <summary>Current playhead time.</summary>
    member this.CurrentTime
        with get() = clock.CurrentTime
        and set v =
            if this.PrepareSample() then clock.CurrentTime <- v
    /// <summary>Overall normalized progress in [0,1].</summary>
    member this.Progress
        with get() = clock.Progress
        and set v =
            if this.PrepareSample() then clock.Progress <- v
    /// <summary>Current iteration index.</summary>
    member _.CurrentIteration = clock.CurrentIteration
    /// <summary>Directional iteration progress in [0,1].</summary>
    member this.IterationProgress
        with get() = clock.IterationProgress
        and set v =
            if this.PrepareSample() then clock.IterationProgress <- v
    /// <summary>Playback speed multiplier.</summary>
    member _.Speed with get() = clock.Speed and set v = clock.Speed <- v
    /// <summary>Whether playback is paused.</summary>
    member _.Paused = clock.Paused
    /// <summary>Suppresses playback writes, callbacks and completion delivery while the playhead advances.</summary>
    member this.Muted
        with get() : bool = clock.Muted
        and set value =
            clock.Muted <- value
            if not clock.EffectivelyMuted && pendingInitialAttach && not cancelled && not fullyReplaced then
                pendingInitialAttach <- false
                attachSiblings true
                assignRenderTransforms()
    /// <summary>Whether playback has begun.</summary>
    member _.Began = clock.Began
    /// <summary>Whether playback has completed.</summary>
    member _.Completed = clock.Completed
    /// <summary>Whether playback has been cancelled.</summary>
    member _.Cancelled = cancelled || clock.Cancelled
    /// <summary>Whether base playback direction is reversed.</summary>
    member _.Reversed with get() = clock.Reversed and set v = clock.Reversed <- v

    member private this.PrepareSample() : bool =
        if not cancelled && not fullyReplaced then
            if not siblingsAttached && not clock.EffectivelyMuted then
                let reclaim = pendingInitialAttach
                pendingInitialAttach <- false
                attachSiblings reclaim
                if tweens.Exists _.Replaced then this.OnPropertyReplaced()
            if clock.Cancelled && not fullyReplaced then clock.ReviveReset()
        cancelled || not fullyReplaced

    /// <summary>Seeks to the specified time and returns this animation.</summary>
    member this.Seek(time: float, ?muteCallbacks: bool) =
        if this.PrepareSample() then clock.Seek(time, ?muteCallbacks = muteCallbacks)
        this

    /// <summary>Pauses scheduling and returns this animation.</summary>
    member this.Pause() = clock.Pause(); this

    /// <summary>Resumes scheduling in the current direction.</summary>
    member this.Resume() =
        if this.PrepareSample() then clock.Resume()
        this

    /// <summary>Plays forward.</summary>
    member this.Play() =
        if not cancelled then
            if clock.Cancelled || (not siblingsAttached && not pendingInitialAttach) then this.Reset() |> ignore
            elif tweens.Exists(fun tw -> tw.Replaced && not tw.Removed) then
                invalidateSoaBatch()
                for tw in tweens do
                    if not tw.Removed && tw.Replaced then
                        tw.RestoreOwnership()
                        manager.ComposeTween tw
                assignRenderTransforms()
                rebuildSoaBatch()
        clock.Play()
        this

    /// <summary>Plays in reverse.</summary>
    member this.Reverse() =
        if this.PrepareSample() then clock.Reverse()
        this

    /// <summary>Toggles direction according to repeat parity.</summary>
    member this.Alternate() =
        if this.PrepareSample() then clock.Alternate()
        this

    /// <summary>Resets to zero, re-linking siblings if previously completed.</summary>
    member this.Reset() =
        invalidateSoaBatch()
        restoreVersion <- restoreVersion + 1
        cancelled <- false
        fullyReplaced <- false
        for tw in tweens do tw.RestoreOwnership()
        detachSiblings true
        if clock.EffectivelyMuted then
            pendingInitialAttach <- true
            manager.RegisterAnimation this
        else
            pendingInitialAttach <- false
            attachSiblings true
        assignRenderTransforms()
        if clock.Cancelled then clock.ReviveReset() else clock.Reset()
        this

    /// <summary>Resets and resumes playback, re-attaching composition siblings.</summary>
    member this.Restart() =
        this.Reset() |> ignore
        clock.Resume()
        this

    /// <summary>Completes immediately and cancels scheduling.</summary>
    member this.Complete(?muteCallbacks: bool) =
        if not fullyReplaced && not cancelled then
            clock.Complete(?muteCallbacks = muteCallbacks)
            if manager.IsAdditiveDirty then manager.FlushAdditive()
            detachSiblings false
        this

    /// <summary>Stretches total duration proportionally.</summary>
    member this.Stretch(newDuration: float) =
        let previousDuration = clock.Duration
        let previousDelay = clock.Delay
        clock.Stretch newDuration
        if previousDuration > 0. && (clock.Duration <> previousDuration || clock.Delay <> previousDelay) then
            invalidateSoaBatch()
            let scale = clock.Duration / previousDuration
            if parentTimelineRef.IsSome then
                offset <- parentTimelineRef.Value.AbsoluteOffset + timelineOffset * scale
            let scaledClockDelay = clock.Delay
            for tw in tweens do
                tw.Start <- PropertyNumbers.round 1e12 (tw.Start * scale)
                tw.Delay <- PropertyNumbers.round 1e12 (tw.Delay * scale)
                tw.UpdateDuration <- max 1e-11 (PropertyNumbers.round 1e12 (tw.UpdateDuration * scale))
                tw.ChangeDuration <- if tw.Overridden then 1e-11 else max 1e-11 (PropertyNumbers.round 1e12 (tw.ChangeDuration * scale))
                tw.AbsoluteStart <- PropertyNumbers.round 1e12 (offset + scaledClockDelay + tw.Start)
                tw.AbsoluteUpdateStart <- PropertyNumbers.round 1e12 (tw.AbsoluteStart - tw.Delay)
                tw.AbsoluteEnd <- PropertyNumbers.round 1e12 (tw.AbsoluteStart + tw.UpdateDuration)
                if not (isNull tw.TrackHead) && obj.ReferenceEquals(tw.TrackHead, tw) then
                    tw.TrackStart <- timelineOffset * scale + (scaledClockDelay + tw.Start) / clock.Speed
                tw.TweenTime <- if tw.Overridden then 1e-11 else tw.TweenTime * scale
            rebuildSoaBatch()
        this

    /// <summary>Re-evaluates dynamic endpoints and target originals in place.</summary>
    member this.Refresh() =
        invalidateSoaBatch()
        let wasPlaying = not clock.Paused
        let savedTime = clock.CurrentTime
        detachSiblings false
        let newDelay, iterDur = buildTweens()
        attachSiblings true
        clock.ResizeWithDelay(iterDur, newDelay)
        clock.Seek(min savedTime clock.Duration, muteCallbacks = true)
        if wasPlaying then clock.Resume()
        this

    /// <summary>Removes matching targets (and optional property) from this animation, cancelling if none remain.</summary>
    member internal this.RemoveMatching(targetsToRemove: HashSet<obj>, propertyName: string option, ?notifyParent: bool) : bool =
        let notifyParent = defaultArg notifyParent true
        let matchesTarget (twTarget: obj) =
            if targetsToRemove.Contains twTarget then true
            else
                match twTarget with
                | :? Element as el ->
                    let mutable found = false
                    for candidate in targetsToRemove do
                        if not found then
                            match candidate with
                            | :? Node as node ->
                                if (try el.isSameNode node with _ -> false) then found <- true
                            | _ -> ()
                    found
                | _ -> false
        let mutable matched = false
        for tw in tweens do
            if not tw.Removed && matchesTarget tw.Target then
                let propMatches =
                    match propertyName with
                    | None -> true
                    | Some raw ->
                        let _, canonical, _ = DomTargetHelpers.classify registry tw.Target raw
                        StringComparer.Ordinal.Equals(canonical, tw.Property)
                if propMatches then
                    tw.Removed <- true
                    manager.RemoveTweenSiblings(tw, rewindBlend = false)
                    matched <- true
        if matched then
            invalidateSoaBatch()
            assignRenderTransforms()
            let noLiveTweens = tweens.TrueForAll(fun tw -> tw.Removed || tw.StepSource.Muted || tw.Replaced)
            if noLiveTweens then
                this.Cancel()
            else
                let mutable maxEnd = 0.
                for tw in tweens do
                    if not tw.Removed && not tw.StepSource.Muted then
                        let finish = tw.Start + tw.UpdateDuration
                        if finish > maxEnd then maxEnd <- finish
                let nextIterDur = if maxEnd <= 1e-11 then 1e-11 else maxEnd
                clock.ResizeWithoutReset nextIterDur
                rebuildSoaBatch()
            if notifyParent then
                parentTimelineRef |> Option.iter (fun tl -> tl.OnChildTargetsModified())
        matched

    /// <summary>Cancels playback while retaining current rendered target values.</summary>
    member _.Cancel() =
        restoreVersion <- restoreVersion + 1
        cancelled <- true
        manager.UnregisterAnimation this
        detachSiblings false
        clock.Cancel()

    member internal this.RevertValues(inlineStylesOnly: bool) =
        let version = restoreVersion
        if not clock.Cancelled then clock.Pause() |> ignore
        let transformRestores = Dictionary<Element, ResizeArray<ComposedTween>>(HashIdentity.Reference)
        let restored = Dictionary<obj, HashSet<string>>(HashIdentity.Reference)
        let eligible (tw:ComposedTween) =
            version = restoreVersion && not tw.Removed && not tw.StepSource.Muted &&
            not (manager.HasOtherOwner tw)
        let firstSnapshot (tw:ComposedTween) =
            match restored.TryGetValue tw.Target with
            | true, properties -> properties.Add tw.Property
            | _ ->
                restored[tw.Target] <- HashSet<string>([| tw.Property |], StringComparer.Ordinal)
                true
        let domElements = HashSet<Element>(HashIdentity.Reference)
        let restore (tw:ComposedTween) =
            manager.ReleaseOwner(tw.Target, tw.Property, ownerToken)
            match tw.Target with
            | :? Element as el -> domElements.Add el |> ignore
            | _ -> ()
            match tw.Adapter with
            | Some entry when not inlineStylesOnly ->
                match tw.InlineOriginal with
                | Some _ ->
                    let d = ValueInterpolation.decompose tw.Original
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
                    let list =
                        match transformRestores.TryGetValue el with
                        | true, existing -> existing
                        | _ ->
                            let created = ResizeArray<ComposedTween>()
                            transformRestores[el] <- created
                            created
                    list.Add tw
                | (TweenTargetKind.Css | TweenTargetKind.CssVar), (:? Element as el) ->
                    let style = (DomTargetHelpers.tryStyle el).Value
                    match tw.InlineOriginal with
                    | None -> style.removeProperty tw.Property |> ignore
                    | Some orig -> style.setProperty(tw.Property, orig, priority = tw.Priority)
                | TweenTargetKind.Attribute, (:? Element as el) when not inlineStylesOnly ->
                    match tw.InlineOriginal with
                    | None -> el.removeAttribute tw.Property
                    | Some orig -> el.setAttribute(tw.Property, orig)
                | TweenTargetKind.Object, (:? Element as el) when not inlineStylesOnly ->
                    match tw.Property with
                    | "scrollTop" ->
                        match tw.Original with
                        | ValueInterpolation.Number n -> el.scrollTop <- int (Math.Round n)
                        | _ -> ()
                    | "scrollLeft" ->
                        match tw.Original with
                        | ValueInterpolation.Number n -> el.scrollLeft <- int (Math.Round n)
                        | _ -> ()
                    | _ ->
                        match tw.InlineOriginal with
                        | Some orig ->
                            match tw.Property with
                            | "innerHTML" -> (el :?> HTMLElement).innerHTML <- orig
                            | "textContent" -> (el :> Node).textContent <- orig
                            | "value" -> el.setAttribute("value", orig)
                            | _ -> ()
                        | None when tw.Property = "value" -> el.removeAttribute "value"
                        | None -> ()
                | TweenTargetKind.Object, (:? NumericProperty as prop) when not inlineStylesOnly ->
                    match tw.Original with
                    | ValueInterpolation.Number n -> prop.Value <- n
                    | _ -> ()
                | TweenTargetKind.Object, (:? AnimatedObject as bag) when not inlineStylesOnly ->
                    match tw.InlineOriginal, tw.Original with
                    | Some _, ValueInterpolation.Number n -> bag.SetNumber(tw.Property, n)
                    | Some _, ValueInterpolation.Text s -> bag.SetText(tw.Property, s)
                    | None, _ -> bag.Remove tw.Property
                | _ -> ()
        CompositionRestoration.run (fun attempt ->
            for tw in tweens.ToArray() do
                if eligible tw && firstSnapshot tw then attempt (fun () -> restore tw)
            for KeyValue(el, list) in transformRestores do
                let current = list |> Seq.filter eligible |> Seq.toArray
                if current.Length > 0 then
                    attempt (fun () ->
                        ElementTransforms.revertComponents el (current |> Array.map (fun tw -> tw.Property, tw.InlineOriginal)) current[0].Priority)
            for el in domElements do
                attempt (fun () -> if el.getAttribute "style" = "" then el.removeAttribute "style"))

    /// <summary>Rewinds, restores pre-animation inline/object/adapted values and cancels playback.</summary>
    /// <remarks>A completed animation, whose clock <c>Complete</c> cancelled, still restores its values, as anime's revert does.</remarks>
    member this.Revert() =
        if not cancelled then
            if not clock.Cancelled then clock.Seek(0., muteCallbacks = true)
            this.RevertValues false
            this.Cancel()
        this

    /// <summary>Restores original property values and cancels playback. Passing false skips the rewind sample.</summary>
    member this.Revert(seekToStart: bool) =
        if seekToStart then this.Revert()
        else
            if not cancelled then
                cancelled <- true
                manager.UnregisterAnimation this
                CompositionRestoration.run (fun attempt ->
                    attempt (fun () -> clock.StopForRevert(false))
                    attempt (fun () -> detachSiblings false)
                    attempt (fun () -> this.RevertValues false))
            this

    /// <summary>Maps observable completion to the callback result.</summary>
    member this.ThenResult<'T>(callback: ComposedAnimation -> 'T) : Threading.Tasks.Task<'T> =
        clock.ThenResult(fun _ -> callback this)

    /// <summary>Awaits the task returned by the completion callback.</summary>
    member this.ThenAsync<'T>(callback: ComposedAnimation -> Threading.Tasks.Task<'T>) : Threading.Tasks.Task<'T> =
        Threading.Tasks.TaskExtensions.Unwrap(this.ThenResult(callback))

    /// <summary>Awaits a completion callback that produces no result.</summary>
    member this.ThenAsync(callback: ComposedAnimation -> Threading.Tasks.Task) : Threading.Tasks.Task =
        Threading.Tasks.TaskExtensions.Unwrap(this.ThenResult(callback))

    /// <summary>Runs the optional completion callback and returns this instance.</summary>
    member this.Then(?callback: ComposedAnimation -> unit) : Threading.Tasks.Task<ComposedAnimation> =
        this.ThenResult(CompletionTask.withInstance callback)
    /// <summary>Returns a task settling when this animation completes.</summary>
    member this.AsTask() : Threading.Tasks.Task<ComposedAnimation> = this.Then()

    /// <summary>Invariant diagnostic of the composed animation.</summary>
    override this.ToString() =
        "ComposedAnimation(Targets=" + targets.Length.ToString(CultureInfo.InvariantCulture) +
        "; Tweens=" + this.ActiveTweenCount.ToString(CultureInfo.InvariantCulture) + ")"

    interface ITickable with
        member this.Revert() = this.Revert() |> ignore
        member this.Completed = this.Completed
        member this.Cancelled = this.Cancelled
        member this.Pause() = this.Pause() |> ignore
        member this.Cancel() = this.Cancel()
        member this.Complete() = this.Complete() |> ignore
        member this.WhenCompleted(callback) = this.Then(fun _ -> callback ()) |> ignore
    interface IDisposable with
        member this.Dispose() = this.Cancel()

/// <summary>Timeline coordinating <see cref="T:Ranvier.Anime.ComposedAnimation"/> children with cross-timeline Replace and Remove parity.</summary>
and [<Sealed; Diagnostics.DebuggerDisplay("{ToString(),nq}")>] ComposedTimeline
    (scheduler: FrameScheduler, ?options: PlaybackOptions, ?autoplay: bool, ?onComplete: ComposedTimeline -> unit) as this =
    let queue = EngineFrames.forScheduler scheduler
    let manager = CompositionManagers.forQueue queue
    let absoluteOffset = queue.OffsetTick() * (if queue.Seconds then 0.001 else 1.)
    let ownerToken = obj()
    let entries = ResizeArray<float * ComposedAnimation>()
    let mutable entriesSnapshot : (float * ComposedAnimation) array = null
    let getEntriesSnapshot () =
        let mutable snap = entriesSnapshot
        if isNull snap then
            snap <- entries.ToArray()
            entriesSnapshot <- snap
        snap
    let labels = Dictionary<string, float>(StringComparer.Ordinal)
    let mutable iterDuration = 0.
    let mutable lastSampleTime = 0.
    let mutable cancelled = false
    let auto = defaultArg autoplay (AnimationDefaults.autoplay true)
    let mutable defaultComputed: ComputedStyleService option = None
    let clock = ScopeRegistration.run ignore (fun () ->
        new Timer(scheduler, 0., ?options = options, onComplete = (fun _ -> onComplete |> Option.iter (fun cb -> cb this))))
    let mutable renderToken = Int64.MinValue
    let live = Closure.keep (fun () -> clock.IsCurrent renderToken)

    let computeMaxEnd () =
        let mutable maxEnd = 0.
        for childOffset, child in entries do
            if not child.Cancelled then
                let finish = childOffset + (child.Clock.Delay + child.Duration) / child.Speed
                if finish > maxEnd then maxEnd <- finish
        maxEnd

    let recalcDuration () =
        iterDuration <- computeMaxEnd()
        clock.Resize iterDuration

    let recalcDurationWithoutReset () =
        iterDuration <- computeMaxEnd()
        clock.ResizeWithoutReset iterDuration

    do
        clock.ConfigureChildren(
            PlaybackRenderer(fun _ _ _ ->
                for i = 0 to entries.Count - 1 do
                    let _, child = entries[i]
                    if not child.Cancelled then child.Clock.ResetChildFlags()),
            PlaybackChildRenderer(fun sample mode muted ->
                let savedToken = renderToken
                renderToken <- clock.WriteToken
                try
                    let backwards = sample.Backwards
                    let previousTime = lastSampleTime
                    lastSampleTime <- sample.IterationTime
                    let snapshot = getEntriesSnapshot()
                    let count = snapshot.Length
                    let mutable allDone = true
                    for idx = 0 to count - 1 do
                        let childOffset, child = snapshot[if backwards then count - 1 - idx else idx]
                        if not child.Cancelled && live() then
                            let localTime = PropertyNumbers.round 1e12 ((sample.IterationTime - childOffset) * child.Speed)
                            let regained = sample.IterationTime < previousTime && child.RegainsTrack(previousTime, sample.IterationTime)
                            if regained || not (backwards && localTime > child.Clock.Delay + child.Duration) then
                                let childMode = if regained then PlaybackTickMode.Force else mode
                                child.Clock.SampleChild(localTime, childMode, muted, clock.Fps, backwards, clock.Began, live, applyFrameRate = not regained)
                            allDone <- allDone && (child.Completed || child.Cancelled)
                    if not queue.Pumping && manager.IsAdditiveDirty then manager.FlushAdditive()
                    allDone
                finally renderToken <- savedToken))
        ScopeRegistration.register (fun () -> this.Revert() |> ignore)

    member internal _.AllChildrenOverlapped() =
        entries.Count > 0 && (entries |> Seq.forall (fun (_, child) -> child.Cancelled || child.AllTweensOverlapped()))

    member internal _.Children = entries
    member internal _.OwnerToken : obj = ownerToken

    // The computed styles children added without their own read from; set by Anime.timeline.
    member internal _.DefaultComputed with get() = defaultComputed and set value = defaultComputed <- value

    /// <summary>Adds a multi-target/property child animation at an optional position.</summary>
    member this.Add(targets: obj array, specs: PropertySpec array, ?options: ComposedAnimationOptions, ?position: TimelinePosition) =
        if cancelled then invalidOp "Cannot add a child to a cancelled timeline."
        let prev =
            if entries.Count = 0 then ValueNone
            else
                let lastOff, lastChild = entries[entries.Count - 1]
                ValueSome(struct(lastOff, lastOff + (lastChild.Clock.Delay + lastChild.Duration) / lastChild.Speed))
        let resolvedOffset =
            TimelinePositions.resolve iterDuration prev labels position
        let authored = defaultArg options ComposedAnimationOptions.Default
        let childOpts = { authored with Autoplay = false; Computed = authored.Computed |> Option.orElse defaultComputed }
        let child = new ComposedAnimation(scheduler, targets, specs, childOpts, Some this, resolvedOffset)
        if cancelled then
            child.Cancel()
            invalidOp "The timeline was cancelled during child construction."
        entries.Add(resolvedOffset, child)
        entriesSnapshot <- null
        recalcDuration()
        if auto && not cancelled then clock.Resume()
        this

    /// <summary>Records a named position label.</summary>
    member this.Label(name: string, ?position: TimelinePosition) =
        let prev =
            if entries.Count = 0 then ValueNone
            else
                let lastOff, lastChild = entries[entries.Count - 1]
                ValueSome(struct(lastOff, lastOff + (lastChild.Clock.Delay + lastChild.Duration) / lastChild.Speed))
        labels[name] <- TimelinePositions.resolve iterDuration prev labels position
        this

    member internal this.OnChildTargetsModified() =
        if not cancelled then
            let remaining = ResizeArray<float * ComposedAnimation>(entries.Count)
            for off, child in entries do
                if not child.Cancelled && child.ActiveTweenCount > 0 then remaining.Add(off, child)
            entries.Clear()
            entries.AddRange remaining
            entriesSnapshot <- null
            if entries.Count = 0 then this.Cancel()
            else recalcDurationWithoutReset()

    /// <summary>Removes matching targets (and optional property) across child animations, cancelling empty children and this timeline if empty.</summary>
    member this.RemoveTargets(targets: obj array, ?propertyName: string) =
        let targetSet = HashSet<obj>(targets, HashIdentity.Reference)
        let mutable anyRemoved = false
        let remaining = ResizeArray<float * ComposedAnimation>()
        for off, child in entries do
            if child.RemoveMatching(targetSet, propertyName, notifyParent = false) then anyRemoved <- true
            if not child.Cancelled && child.ActiveTweenCount > 0 then remaining.Add(off, child)
        if anyRemoved then
            entries.Clear()
            entries.AddRange remaining
            entriesSnapshot <- null
            if entries.Count = 0 then this.Cancel()
            else recalcDurationWithoutReset()
        this

    /// <summary>Total duration across iterations.</summary>
    /// <summary>The engine time this timeline started at, which its children's absolute tween times count from.</summary>
    member internal _.AbsoluteOffset = absoluteOffset
    member internal _.SampleTime : float = clock.Sample.IterationTime
    member _.Duration = if entries.Count = 0 then 0. else clock.Duration
    /// <summary>Single-iteration duration.</summary>
    member _.IterationDuration = iterDuration
    /// <summary>Current playhead time.</summary>
    member _.CurrentTime with get() = clock.CurrentTime and set v = clock.CurrentTime <- v
    /// <summary>Overall normalized progress in [0,1].</summary>
    member _.Progress with get() = clock.Progress and set v = clock.Progress <- v
    /// <summary>Whether base playback direction is reversed.</summary>
    member _.Reversed with get() = clock.Reversed and set v = clock.Reversed <- v
    /// <summary>Playback speed multiplier.</summary>
    member _.Speed with get() = clock.Speed and set v = clock.Speed <- v
    /// <summary>Signed interval between preceding playback samples, in the engine time unit.</summary>
    member _.DeltaTime = clock.DeltaTime
    /// <summary>Whether this timeline has active children.</summary>
    member _.HasChildren = entries.Count > 0
    /// <summary>Whether scheduling is paused.</summary>
    member _.Paused = clock.Paused
    /// <summary>Suppresses playback effects on this timeline and its owned children while their playheads advance.</summary>
    member _.Muted with get() : bool = clock.Muted and set value = clock.Muted <- value
    /// <summary>Whether this timeline completed.</summary>
    member _.Completed = clock.Completed
    /// <summary>Whether this timeline was cancelled.</summary>
    member _.Cancelled = cancelled || clock.Cancelled

    /// <summary>Seeks to milliseconds and flushes any additive blend updates.</summary>
    member this.Seek(time: float, ?muteCallbacks: bool) =
        clock.Seek(time, ?muteCallbacks = muteCallbacks)
        if manager.IsAdditiveDirty then manager.FlushAdditive()
        this

    /// <summary>Pauses scheduling.</summary>
    member this.Pause() = clock.Pause(); this
    /// <summary>Resumes scheduling.</summary>
    member this.Resume() = clock.Resume(); this
    /// <summary>Plays forward.</summary>
    member this.Play() = clock.Play(); this
    /// <summary>Reverses playback.</summary>
    member this.Reverse() = clock.Reverse(); this
    /// <summary>Toggles direction according to repeat parity.</summary>
    member this.Alternate() = clock.Alternate(); this
    /// <summary>Resets to zero and clears child completion flags.</summary>
    member this.Reset() =
        clock.Reset()
        for _, child in entries do
            if not child.Cancelled then child.Clock.ResetChildFlags()
        this
    /// <summary>Resets and resumes playback.</summary>
    member this.Restart() =
        this.Reset() |> ignore
        clock.Resume()
        this
    /// <summary>Samples the end while retaining the timeline's reusable lifetime.</summary>
    member this.Complete(?muteCallbacks: bool) =
        this.Seek(this.Duration, ?muteCallbacks = muteCallbacks)

    /// <summary>Refreshes all active child animations.</summary>
    member this.Refresh() =
        let wasPlaying = not clock.Paused
        let savedTime = clock.CurrentTime
        for _, child in entries do
            if not child.Cancelled then child.Refresh() |> ignore
        recalcDurationWithoutReset()
        clock.Seek(min savedTime clock.Duration, muteCallbacks = true)
        if wasPlaying then clock.Resume()
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

    /// <summary>Restores children in reverse order, optionally skipping the timeline rewind sample.</summary>
    member this.Revert(seekToStart: bool) =
        if seekToStart then this.Revert()
        else
            if not cancelled then
                cancelled <- true
                let children = entries.ToArray()
                CompositionRestoration.run (fun attempt ->
                    attempt (fun () -> clock.StopForRevert(false))
                    for index = children.Length - 1 downto 0 do
                        let _, child = children[index]
                        attempt (fun () -> child.Revert(false) |> ignore)
                    entries.Clear()
                    entriesSnapshot <- null)
            this

    /// <summary>Cancels this timeline and all owned children.</summary>
    member _.Cancel() =
        cancelled <- true
        for _, child in entries do child.Cancel()
        entries.Clear()
        entriesSnapshot <- null
        clock.Cancel()

    /// <summary>Maps observable completion to the callback result.</summary>
    member this.ThenResult<'T>(callback: ComposedTimeline -> 'T) : Threading.Tasks.Task<'T> =
        clock.ThenResult(fun _ -> callback this)

    /// <summary>Awaits the task returned by the completion callback.</summary>
    member this.ThenAsync<'T>(callback: ComposedTimeline -> Threading.Tasks.Task<'T>) : Threading.Tasks.Task<'T> =
        Threading.Tasks.TaskExtensions.Unwrap(this.ThenResult(callback))

    /// <summary>Awaits a completion callback that produces no result.</summary>
    member this.ThenAsync(callback: ComposedTimeline -> Threading.Tasks.Task) : Threading.Tasks.Task =
        Threading.Tasks.TaskExtensions.Unwrap(this.ThenResult(callback))

    /// <summary>Runs the optional completion callback and returns this instance.</summary>
    member this.Then(?callback: ComposedTimeline -> unit) : Threading.Tasks.Task<ComposedTimeline> =
        this.ThenResult(CompletionTask.withInstance callback)
    /// <summary>Returns a task settling when this timeline completes.</summary>
    member this.AsTask() : Threading.Tasks.Task<ComposedTimeline> = this.Then()

    /// <summary>Invariant diagnostic of the composed timeline.</summary>
    override _.ToString() = "ComposedTimeline(Children=" + entries.Count.ToString(CultureInfo.InvariantCulture) + ")"

    interface ITickable with
        member this.Revert() = this.Revert() |> ignore
        member this.Completed = this.Completed
        member this.Cancelled = this.Cancelled
        member this.Pause() = this.Pause() |> ignore
        member this.Cancel() = this.Cancel()
        member this.Complete() = this.Seek(this.Duration) |> ignore
        member this.WhenCompleted(callback) = this.Then(fun _ -> callback ()) |> ignore
    interface IDisposable with
        member this.Dispose() = this.Cancel()

and internal CompositionManagers =
    static let table = ConditionalWeakTable<EngineFrameQueue, CompositionManager>()
    static member forQueue (queue: EngineFrameQueue) : CompositionManager =
        match table.TryGetValue queue with
        | true, manager -> manager
        | _ -> table.GetValue(queue, fun q -> CompositionManager q)
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
