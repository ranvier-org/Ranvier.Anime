namespace Ranvier.Anime

open System
open System.Collections.Generic
open System.Globalization
open System.Runtime.CompilerServices
open System.Text.RegularExpressions
open Ranvier.Dom

/// <summary>Per-element cached CSS transform components matching Anime.js transformsSymbol state.</summary>
type internal ElementTransformState() =
    member val Cache = Dictionary<string, string>(StringComparer.Ordinal)
    /// <summary>The number and unit of each component whose cache value is <c>TransformCache.numeric</c>.</summary>
    member val Numbers = Dictionary<string, struct(float * string)>(StringComparer.Ordinal)
    member val FullTranslate : string option = None with get, set
    /// <summary>The element whose staged components await the end of the frame, or null.</summary>
    member val Pending : Element = Unchecked.defaultof<_> with get, set
    /// <summary>The priority the pending flush writes with.</summary>
    member val PendingPriority = "" with get, set
    /// <summary>Serializes the cache in canonical order into the element's inline transform.</summary>
    member this.Write(element: Element, priority: string) =
        this.Pending <- Unchecked.defaultof<_>
        let style =
            match box element with
            | :? ElementCSSInlineStyle as styled -> styled.style :> CSSStyleDeclaration
            | _ -> invalidArg "element" "Element does not expose inline CSS style."
        let text = TransformCache.buildDeclaration this.Cache this.Numbers
        if text.Length = 0 then style.removeProperty "transform" |> ignore
        else style.setProperty("transform", text, ?priority = TransformCache.priorityOption priority)
    interface IDeferredWrite with
        member this.Flush() =
            let element = this.Pending
            if not(isNull(box element)) then this.Write(element, this.PendingPriority)
    /// <summary>Stores a component as a number in a unit, formatted only when the transform is written.</summary>
    member this.SetNumber(canonical: string, value: float, unitSuffix: string) =
        this.Numbers[canonical] <- struct(value, (if isNull unitSuffix then "" else unitSuffix))
        this.Cache[canonical] <- TransformCache.numeric
    /// <summary>Replaces numeric components with their text, for paths that read or parse the cache.</summary>
    member this.Materialize() =
        if this.Numbers.Count > 0 then
            for KeyValue(canonical, struct(value, unitSuffix)) in this.Numbers do
                match this.Cache.TryGetValue canonical with
                | true, cached when Object.ReferenceEquals(cached, TransformCache.numeric) ->
                    this.Cache[canonical] <- TransformCache.numberText value unitSuffix
                | _ -> ()
            this.Numbers.Clear()

/// <summary>Authored individual CSS transform endpoint or keyframe value.</summary>
[<RequireQualifiedAccess; Diagnostics.DebuggerDisplay("{ToString(),nq}")>]
type TransformValue =
    /// <summary>Numeric value using the transform property's default unit (px, deg or unitless).</summary>
    | Scalar of float
    /// <summary>Explicit CSS string value, such as "5rem", "+=45deg", or "calc(50px + 10vw)".</summary>
    | Css of string
    /// <summary>Explicit from/to pair.</summary>
    | FromTo of fromValue: TransformValue * toValue: TransformValue
    /// <summary>Sequential keyframe values.</summary>
    | Sequence of values: TransformValue array
    /// <summary>An invariant diagnostic of the transform value.</summary>
    override this.ToString() =
        match this with
        | Scalar v -> "Scalar " + v.ToString("R", CultureInfo.InvariantCulture)
        | Css s -> "Css " + (if isNull s then "<null>" else s)
        | FromTo(a, b) -> "FromTo(" + a.ToString() + ", " + b.ToString() + ")"
        | Sequence items -> "Sequence(" + items.Length.ToString(CultureInfo.InvariantCulture) + ")"

/// <summary>Individual CSS transform property parsing, caching, canonical ordering and partial restoration.</summary>
[<RequireQualifiedAccess>]
module ElementTransforms =
    let private states = ConditionalWeakTable<Element, ElementTransformState>()
    let private bareNumberPattern = Regex(@"^([-+]?(?:\d*\.)?\d+(?:[eE][-+]?\d+)?)$", RegexOptions.CultureInvariant)
    let private relativeNumberPattern = Regex(@"^(\+=|-=|\*=)([-+]?(?:\d*\.)?\d+(?:[eE][-+]?\d+)?)$", RegexOptions.CultureInvariant)

    let private validSet =
        System.Collections.Frozen.FrozenSet.ToFrozenSet(
            [| "perspective"; "translateX"; "translateY"; "translateZ"
               "rotate"; "rotateX"; "rotateY"; "rotateZ"
               "scale"; "scaleX"; "scaleY"; "scaleZ"
               "skew"; "skewX"; "skewY"
               "translate3d"; "rotate3d"; "matrix"; "matrix3d" |],
            StringComparer.Ordinal)

    /// <summary>Maps Anime.js shorthand aliases (x, y, z) and validates CSS transform component names.</summary>
    let canonicalName (propertyName: string) : string option =
        if isNull propertyName then None
        else
            match propertyName with
            | "x" -> Some "translateX"
            | "y" -> Some "translateY"
            | "z" -> Some "translateZ"
            | name when validSet.Contains name -> Some name
            | _ -> None

    /// <summary>Whether the property name is an Anime.js individual transform or shorthand alias.</summary>
    let isTransform (propertyName: string) : bool = (canonicalName propertyName).IsSome

    /// <summary>Default CSS unit for numeric values of a canonical transform property.</summary>
    let defaultUnit (canonical: string) : string =
        if canonical.StartsWith("scale", StringComparison.Ordinal) then ""
        elif canonical.StartsWith("rotate", StringComparison.Ordinal) || canonical.StartsWith("skew", StringComparison.Ordinal) then "deg"
        elif canonical = "perspective" || canonical.StartsWith("translate", StringComparison.Ordinal) then "px"
        else ""

    /// <summary>Normalizes a scalar number to its canonical transform string with default unit.</summary>
    let formatScalar (canonical: string) (value: float) : string =
        if not (Double.IsFinite value) then invalidArg "value" "Expected a finite transform scalar."
        // Shortest round-trip digits and the unit in one string; exponent forms take the source formatter's expansion.
        TransformCache.numberText value (defaultUnit canonical)

    /// <summary>Normalizes a string transform endpoint, appending default units to bare or relative unitless numbers.</summary>
    let normalizeText (canonical: string) (raw: string) : string =
        if isNull raw then nullArg "raw"
        let trimmed = raw.Trim()
        let unitSuffix = defaultUnit canonical
        if unitSuffix = "" then trimmed
        elif bareNumberPattern.IsMatch trimmed then trimmed + unitSuffix
        else
            let rel = relativeNumberPattern.Match trimmed
            if rel.Success then trimmed + unitSuffix else trimmed

    let private inlineStyle (element: Element) : CSSStyleDeclaration =
        match box element with
        | :? ElementCSSInlineStyle as styled -> styled.style :> CSSStyleDeclaration
        | _ -> invalidArg "element" "Element does not expose inline CSS style."

    let internal stateFor (element: Element) =
        match states.TryGetValue element with
        | true, state -> state
        | _ -> states.GetValue(element, fun _ -> ElementTransformState())

    /// <summary>Applies components staged during the current frame, so the inline declaration is current.</summary>
    let private settle (state: ElementTransformState) (element: Element) =
        if not(isNull(box state.Pending)) then state.Write(element, state.PendingPriority)
        state.Materialize()

    /// <summary>Parses current inline style.transform into the element's transform cache and reads one component plus its inline presence.</summary>
    let internal readWithInlineOrigin (element: Element) (canonical: string) : string * string option =
        let style = inlineStyle element
        let state = stateFor element
        settle state element
        let inlineTransforms = style.getPropertyValue "transform"
        if not (String.IsNullOrEmpty inlineTransforms) then
            let struct(parsed, fullTranslate) = TransformCache.parse inlineTransforms
            for KeyValue(k, v) in parsed do state.Cache[k] <- v
            state.FullTranslate <- fullTranslate
            if canonical = "translate3d" && fullTranslate.IsSome then
                fullTranslate.Value, Some fullTranslate.Value
            else
                match state.Cache.TryGetValue canonical with
                | true, cached ->
                    let fromInline = if parsed.ContainsKey canonical then Some cached else None
                    cached, fromInline
                | _ ->
                    TransformCache.read parsed fullTranslate canonical, None
        else
            state.Cache.Clear()
            state.FullTranslate <- None
            TransformCache.read state.Cache None canonical, None

    /// <summary>Reads the current value of an individual transform property (or x/y/z alias) from an element.</summary>
    let get (element: Element) (propertyName: string) : string =
        if isNull (box element) then nullArg "element"
        match canonicalName propertyName with
        | None -> invalidArg "propertyName" ("Unknown transform property: " + propertyName)
        | Some canonical -> fst (readWithInlineOrigin element canonical)

    /// <summary>Updates a single cached transform component without flushing style.transform yet.</summary>
    let internal stageComponent (element: Element) (canonical: string) (value: string) =
        let state = stateFor element
        state.Cache[canonical] <- value

    /// <summary>Updates a single cached transform component to a number in a unit, without flushing style.transform yet.</summary>
    let internal stageNumber (element: Element) (canonical: string) (value: float) (unitSuffix: string) =
        (stateFor element).SetNumber(canonical, value, unitSuffix)

    /// <summary>Serializes all cached transform components in canonical order and writes style.transform.</summary>
    /// <summary>Drops components staged during the current frame without writing them, before an inline restore.</summary>
    let internal discard (element: Element) =
        match states.TryGetValue element with
        | true, state -> state.Pending <- Unchecked.defaultof<_>
        | _ -> ()

    let internal flush (element: Element) (priority: string) =
        (stateFor element).Write(element, priority)

    let private commitState (state: ElementTransformState) (element: Element) (priority: string) =
        if DeferredWrites.Deferring then
            state.PendingPriority <- priority
            if isNull(box state.Pending) then
                state.Pending <- element
                DeferredWrites.Add state
        else state.Write(element, priority)

    /// <summary>
    /// Writes one canonical component as a scalar in its default unit through the element's cached transform, without
    /// re-reading the inline declaration: the per-frame write of a retargeted follower.
    /// </summary>
    /// <remarks>
    /// During an engine frame the write is staged and the element's transform is set once when the frame ends, however
    /// many of its components changed; a transform read through this module applies it first.
    /// </remarks>
    let internal writeScalar (element: Element) (canonical: string) (priority: string) (value: float) =
        if not (Double.IsFinite value) then invalidArg "value" "Expected a finite transform scalar."
        let state = stateFor element
        state.SetNumber(canonical, value, defaultUnit canonical)
        commitState state element priority

    /// <summary>Writes the cached components: at the end of the frame during an engine frame, otherwise now.</summary>
    let internal commit (element: Element) (priority: string) = commitState (stateFor element) element priority

    /// <summary>Sets one or more transform components on an element immediately and flushes style.transform.</summary>
    let set (element: Element) (values: seq<string * TransformValue>) =
        if isNull (box element) then nullArg "element"
        if isNull (box values) then nullArg "values"
        let style = inlineStyle element
        let state = stateFor element
        settle state element
        let priority = style.getPropertyPriority "transform"
        let inlineTransforms = style.getPropertyValue "transform"
        if not (String.IsNullOrEmpty inlineTransforms) then
            let struct(parsed, fullTranslate) = TransformCache.parse inlineTransforms
            for KeyValue(k, v) in parsed do state.Cache[k] <- v
            state.FullTranslate <- fullTranslate
        else
            state.Cache.Clear()
            state.FullTranslate <- None
        let rec resolveText canonical = function
            | TransformValue.Scalar n -> formatScalar canonical n
            | TransformValue.Css s -> normalizeText canonical s
            | TransformValue.FromTo(_, b) -> resolveText canonical b
            | TransformValue.Sequence items when items.Length > 0 -> resolveText canonical items[items.Length - 1]
            | TransformValue.Sequence _ -> TransformCache.read state.Cache state.FullTranslate canonical
        for name, raw in values do
            match canonicalName name with
            | None -> invalidArg "values" ("Unknown transform property: " + name)
            | Some canonical -> state.Cache[canonical] <- resolveText canonical raw
        flush element priority

    /// <summary>Restores only the specified transform components to their pre-animation inline state and cleans empty style attributes.</summary>
    let internal revertComponents (element: Element) (originals: (string * string option) array) (originalPriority: string) =
        let style = inlineStyle element
        let state = stateFor element
        settle state element
        let currentInline = style.getPropertyValue "transform"
        if not (String.IsNullOrEmpty currentInline) then
            let struct(parsed, fullTranslate) = TransformCache.parse currentInline
            state.Cache.Clear()
            for KeyValue(k, v) in parsed do state.Cache[k] <- v
            state.FullTranslate <- fullTranslate
        for canonical, originalOpt in originals do
            match originalOpt with
            | Some v -> state.Cache[canonical] <- v
            | None -> state.Cache.Remove canonical |> ignore
        let built = TransformCache.buildDeclaration state.Cache state.Numbers
        if built.Length = 0 then
            style.removeProperty "transform" |> ignore
        else
            style.setProperty("transform", built, priority = originalPriority)
        if element.getAttribute "style" = "" then
            element.removeAttribute "style"
