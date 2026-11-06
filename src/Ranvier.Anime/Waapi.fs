namespace Ranvier.Anime

open System
open System.Collections.Generic
open System.Globalization
open System.Runtime.CompilerServices
open Ranvier.Dom

/// <summary>Easing specification accepted by <see cref="T:Ranvier.Anime.WaapiAnimation"/>.</summary>
[<RequireQualifiedAccess; NoEquality; NoComparison; Diagnostics.DebuggerDisplay("{ToString(),nq}")>]
type WaapiEase =
    /// <summary>Named Anime.js or native CSS timing string (cached across calls).</summary>
    | Named of string
    /// <summary>Custom easing function sampled into CSS linear().</summary>
    | Curve of (float -> float)
    /// <summary>Target-dependent easing selector.</summary>
    | ByTarget of (Element -> int -> Element array -> WaapiEase)
    /// <summary>Spring curve using its settling duration and sampled linear() timing function.</summary>
    | Spring of Spring
    /// <summary>Invariant diagnostic of the WAAPI easing input.</summary>
    override this.ToString() =
        match this with
        | Named s -> "Named " + (if isNull s then "<null>" else s)
        | Curve _ -> "Curve"
        | ByTarget _ -> "ByTarget"
        | Spring s -> "Spring(" + s.SettlingDuration.ToString("R", CultureInfo.InvariantCulture) + ")"

/// <summary>Keyframe endpoint value accepted by WAAPI property tracks.</summary>
[<RequireQualifiedAccess; NoEquality; NoComparison; Diagnostics.DebuggerDisplay("{ToString(),nq}")>]
type WaapiValue =
    /// <summary>Numeric value using WAAPI default property units (px, deg or unitless).</summary>
    | Scalar of float
    /// <summary>Explicit CSS string value (preserves CSS variables without function evaluation).</summary>
    | Css of string
    /// <summary>Target-dependent endpoint callback (element, index, targets).</summary>
    | ByTarget of (Element -> int -> Element array -> WaapiValue)
    /// <summary>Array of sequential WAAPI keyframe endpoints.</summary>
    | Keyframes of WaapiValue array
    /// <summary>Explicit property-level options (from, to, duration, delay, ease, composite).</summary>
    | Options of WaapiTweenOptions
    /// <summary>Invariant diagnostic of the WAAPI value.</summary>
    override this.ToString() =
        match this with
        | Scalar v -> "Scalar " + v.ToString("R", CultureInfo.InvariantCulture)
        | Css s -> "Css " + (if isNull s then "<null>" else s)
        | ByTarget _ -> "ByTarget"
        | Keyframes items -> "Keyframes(" + items.Length.ToString(CultureInfo.InvariantCulture) + ")"
        | Options _ -> "Options"

/// <summary>Per-property WAAPI tween options matching Anime.js WAAPITweenOptions.</summary>
and [<NoEquality; NoComparison; Diagnostics.DebuggerDisplay("{ToString(),nq}")>] WaapiTweenOptions =
    { From: WaapiValue option
      To: WaapiValue option
      Duration: (Element -> int -> Element array -> float) option
      Delay: (Element -> int -> Element array -> float) option
      Ease: WaapiEase option
      Composite: CompositeOperation option }
    /// <summary>Default empty per-property WAAPI options inheriting parent settings.</summary>
    static member Default =
        { From = None; To = None; Duration = None; Delay = None; Ease = None; Composite = None }
    /// <summary>Invariant diagnostic of per-property WAAPI options.</summary>
    override this.ToString() =
        "WaapiTweenOptions(HasFrom=" + this.From.IsSome.ToString() + "; HasTo=" + this.To.IsSome.ToString() + ")"

/// <summary>Parameters for constructing a <see cref="T:Ranvier.Anime.WaapiAnimation"/>.</summary>
[<NoEquality; NoComparison; Diagnostics.DebuggerDisplay("{ToString(),nq}")>]
type WaapiAnimationOptions =
    { Properties: (string * WaapiValue) array
      Duration: (Element -> int -> Element array -> float) option
      Delay: (Element -> int -> Element array -> float) option
      Ease: WaapiEase option
      Loop: PlaybackRepeat
      Alternate: bool
      Reversed: bool
      Autoplay: bool
      Scroll: ScrollObserver option
      Persist: bool
      PlaybackRate: float
      Composite: CompositeOperation
      Computed: ComputedStyleService option
      CustomProperties: CustomPropertyService option
      Animations: AnimationService option
      Scheduler: FrameScheduler option
      OnComplete: WaapiAnimation -> unit }
    /// <summary>Default WAAPI animation options matching Anime.js globals.defaults.</summary>
    static member Default =
        let playback = PlaybackDefaults.current()
        let animDefaults = AnimationDefaults.current()
        { Properties = [||]
          Duration = None
          Delay = None
          Ease = None
          Loop = playback.Repeat
          Alternate = playback.Alternate
          Reversed = playback.Reversed
          Autoplay = animDefaults.Autoplay
          Scroll = None
          Persist = false
          PlaybackRate = playback.Speed
          Composite = CompositeOperation.Replace
          Computed = None
          CustomProperties = None
          Animations = None
          Scheduler = None
          OnComplete = ignore }
    /// <summary>Sets a constant duration across all targets.</summary>
    member this.WithDuration(duration: float) = { this with Duration = Some (fun _ _ _ -> duration) }
    /// <summary>Sets a constant delay across all targets.</summary>
    member this.WithDelay(delay: float) = { this with Delay = Some (fun _ _ _ -> delay) }
    /// <summary>Sets staggered delays across targets.</summary>
    member this.WithStaggerDelay(value: StaggerValue, ?staggerOptions: StaggerOptions) =
        let opts = defaultArg staggerOptions StaggerOptions.Default
        { this with
            Delay = Some (fun _ i targets ->
                let arr = Stagger.distributeAt opts.Start value opts (uint targets.Length)
                if i < arr.Length then arr[i] else 0.) }
    /// <summary>Invariant diagnostic of WAAPI options.</summary>
    override this.ToString() =
        "WaapiAnimationOptions(Properties=" + this.Properties.Length.ToString(CultureInfo.InvariantCulture) +
        "; Autoplay=" + this.Autoplay.ToString() + ")"

/// <summary>Active WAAPI lookup record for cross-animation replacement and Utils.remove.</summary>
and internal WaapiLookupRecord =
    { Parent: WaapiAnimation
      Animation: WebAnimation
      Element: Element
      Property: string }

and internal WaapiHelpers private () =
    static let easeStringCache = Dictionary<string, string>(StringComparer.Ordinal)
    static let registeredDocuments = ConditionalWeakTable<Document, obj>()
    static let activeLookups = ResizeArray<WaapiLookupRecord>()

    static let validIndividualTransforms =
        HashSet<string>(
            [| "x"; "y"; "z"
               "translateX"; "translateY"; "translateZ"
               "rotateX"; "rotateY"; "rotateZ"
               "scaleX"; "scaleY"; "scaleZ"
               "skewX"; "skewY" |],
            StringComparer.Ordinal)

    static let allRegisterableTransforms =
        [| "perspective"; "translateX"; "translateY"; "translateZ"
           "rotate"; "rotateX"; "rotateY"; "rotateZ"
           "scale"; "scaleX"; "scaleY"; "scaleZ"
           "skew"; "skewX"; "skewY" |]

    static let commonDefaultPx =
        HashSet<string>(
            [| "perspective"; "width"; "height"; "margin"; "padding"
               "top"; "right"; "bottom"; "left"
               "borderWidth"; "border-width"; "fontSize"; "font-size"; "borderRadius"; "border-radius"
               "x"; "y"; "z" |],
            StringComparer.Ordinal)

    static member ActiveLookups = activeLookups
    static member ValidIndividualTransforms = validIndividualTransforms
    static member AllRegisterableTransforms = allRegisterableTransforms

    static member toIdlPropertyName (name: string) =
        if String.IsNullOrEmpty name || name.StartsWith("--", StringComparison.Ordinal) then name
        elif not (name.Contains "-") then name
        else
            let parts = name.Split '-'
            let sb = Text.StringBuilder(parts[0])
            for i in 1 .. parts.Length - 1 do
                let p = parts[i]
                if p.Length > 0 then
                    sb.Append(Char.ToUpperInvariant p[0]).Append(p.Substring 1) |> ignore
            sb.ToString()

    static member toKebabName (name: string) = DomTargetHelpers.toKebab name

    static member easingToLinear (fn: float -> float) (samples: int) =
        let count = if samples <= 0 then 100 else samples
        let points = Array.init (count + 1) (fun i ->
            let v = PropertyNumbers.round 1e4 (fn (float i / float count))
            ValueInterpolation.number v)
        "linear(" + String.concat ", " points + ")"

    static member parseWaapiEaseString (ease: string) =
        match easeStringCache.TryGetValue ease with
        | true, cached -> cached
        | _ ->
            let parsed =
                if ease.StartsWith("linear", StringComparison.Ordinal) ||
                   ease.StartsWith("cubic-", StringComparison.Ordinal) ||
                   ease.StartsWith("steps", StringComparison.Ordinal) ||
                   ease.StartsWith("ease", StringComparison.Ordinal) then ease
                elif ease.StartsWith("cubicB", StringComparison.Ordinal) then
                    DomTargetHelpers.toKebab ease
                elif ease = "none" then "linear"
                else
                    match Ease.tryParseString ease with
                    | Some fn -> WaapiHelpers.easingToLinear fn 100
                    | None -> "linear"
            easeStringCache[ease] <- parsed
            parsed

    static member resolveWaapiEase (easeOpt: WaapiEase option) (el: Element) (idx: int) (targets: Element array) : string * Spring option =
        match easeOpt with
        | None ->
            let defaultEase = (AnimationDefaults.current()).Ease
            WaapiHelpers.easingToLinear defaultEase 100, None
        | Some (WaapiEase.Named s) -> WaapiHelpers.parseWaapiEaseString s, None
        | Some (WaapiEase.Curve fn) -> WaapiHelpers.easingToLinear fn 100, None
        | Some (WaapiEase.Spring sp) -> WaapiHelpers.easingToLinear sp.Ease 100, Some sp
        | Some (WaapiEase.ByTarget selector) ->
            WaapiHelpers.resolveWaapiEase (Some (selector el idx targets)) el idx targets

    static member registerTransformProperties (doc: Document) (customProps: CustomPropertyService option) : bool =
        match customProps with
        | None -> false
        | Some svc ->
            match registeredDocuments.TryGetValue doc with
            | true, _ -> true
            | _ ->
                for t in allRegisterableTransforms do
                    let isSkew = t.StartsWith("skew", StringComparison.Ordinal)
                    let isScale = t.StartsWith("scale", StringComparison.Ordinal)
                    let isRotate = t.StartsWith("rotate", StringComparison.Ordinal)
                    let isTranslate = t.StartsWith("translate", StringComparison.Ordinal)
                    let isAngle = isRotate || isSkew
                    let syntax =
                        if isAngle then "<angle>"
                        elif isScale then "<number>"
                        elif isTranslate then "<length-percentage>"
                        else "*"
                    let initVal =
                        if isTranslate then "0px"
                        elif isAngle then "0deg"
                        elif isScale then "1"
                        else "0"
                    try
                        svc.Register
                            { Name = "--" + t
                              Syntax = syntax
                              Inherits = false
                              InitialValue = CustomPropertyInitialValue.Value initVal }
                    with _ -> ()
                registeredDocuments.Add(doc, obj())
                true

    static member normalizeValue (propName: string) (v: WaapiValue) (el: Element) (idx: int) (targets: Element array) : string =
        match v with
        | WaapiValue.Css s -> s
        | WaapiValue.Scalar num ->
            let s = ValueInterpolation.number num
            if commonDefaultPx.Contains propName || propName.StartsWith("translate", StringComparison.Ordinal) then s + "px"
            elif propName.StartsWith("rotate", StringComparison.Ordinal) || propName.StartsWith("skew", StringComparison.Ordinal) then s + "deg"
            else s
        | WaapiValue.ByTarget fn ->
            WaapiHelpers.normalizeValue propName (fn el idx targets) el idx targets
        | WaapiValue.Keyframes items when items.Length > 0 ->
            WaapiHelpers.normalizeValue propName items[items.Length - 1] el idx targets
        | _ -> "0"

    static member parseTweenValues (el: Element) (propName: string) (fromOpt: WaapiValue option) (toOpt: WaapiValue option)
                                   (computed: ComputedStyleService option) (idx: int) (targets: Element array) : string array =
        let resolvedToValues =
            match toOpt with
            | Some (WaapiValue.Keyframes arr) -> arr |> Array.map (fun item -> WaapiHelpers.normalizeValue propName item el idx targets)
            | Some single -> [| WaapiHelpers.normalizeValue propName single el idx targets |]
            | None ->
                let fallback =
                    match computed with
                    | Some svc -> (svc.Get(el, None)).getPropertyValue (WaapiHelpers.toKebabName propName)
                    | None ->
                        match DomTargetHelpers.tryStyle el with
                        | Some st -> st.getPropertyValue (WaapiHelpers.toKebabName propName)
                        | None -> "0"
                [| if String.IsNullOrEmpty fallback then "0" else fallback |]
        match fromOpt with
        | Some f ->
            let fStr = WaapiHelpers.normalizeValue propName f el idx targets
            let lastStr = if resolvedToValues.Length > 0 then resolvedToValues[resolvedToValues.Length - 1] else "0"
            [| fStr; lastStr |]
        | None -> resolvedToValues

    static member removeWaapiAnimation (el: Element option) (property: string option) (parent: WaapiAnimation option) =
        let toRemove = ResizeArray<WaapiLookupRecord>()
        for record in activeLookups do
            let matchEl = match el with Some e -> Object.ReferenceEquals(record.Element, e) || (record.Element :> Node).isSameNode(e :> Node) | None -> true
            let matchProp =
                match property with
                | None -> true
                | Some p ->
                    StringComparer.Ordinal.Equals(record.Property, p) ||
                    StringComparer.Ordinal.Equals(WaapiHelpers.toKebabName record.Property, WaapiHelpers.toKebabName p)
            let matchParent = match parent with Some p -> Object.ReferenceEquals(record.Parent, p) | None -> true
            if matchEl && matchProp && matchParent then toRemove.Add record
        for record in toRemove do
            if activeLookups.Remove record then
                try record.Animation.commitStyles() with _ -> ()
                try record.Animation.cancel() with _ -> ()
                record.Parent.NotifyMemberRemoved()

/// <summary>Native Web Animations API (WAAPI) animation controller matching Anime.js WAAPIAnimation.</summary>
and [<Sealed; Diagnostics.DebuggerDisplay("{ToString(),nq}")>] WaapiAnimation
    (targets: Element array, options: WaapiAnimationOptions) as this =
    do
        if isNull targets then nullArg "targets"
        if isNull (box options) then nullArg "options"

    let parsedTargets = Array.copy targets
    let timeScale = EngineDefaults.timeScale()
    let scaleToMs = if timeScale = 1. then 1. else 1000.
    let animations = ResizeArray<WebAnimation>()
    let finishSubscriptions = ResizeArray<IDisposable>()
    let inlineSnapshots = Array.init parsedTargets.Length (fun _ -> Dictionary<string, string>(StringComparer.Ordinal))
    let completionWaiters = ResizeArray<WaapiAnimation -> unit>()
    let cancelWaiters = ResizeArray<unit -> unit>()
    let mutable controlAnimation : WebAnimation option = None
    let mutable totalDuration = 0.
    let mutable muteCallbacks = false
    let mutable completed = false
    let mutable completedCount = 0
    let mutable paused = not options.Autoplay || options.Scroll.IsSome
    let mutable reversed = options.Reversed
    let mutable persist = options.Persist
    let mutable speed = options.PlaybackRate
    let mutable cancelled = false

    let settleCompletion () =
        if completionWaiters.Count > 0 then
            let snapshot = completionWaiters.ToArray()
            completionWaiters.Clear()
            cancelWaiters.Clear()
            for waiter in snapshot do waiter this

    let settleCancelled () =
        completionWaiters.Clear()
        if cancelWaiters.Count > 0 then
            let snapshot = cancelWaiters.ToArray()
            cancelWaiters.Clear()
            for waiter in snapshot do waiter()

    let iterations =
        match options.Loop with
        | PlaybackRepeat.Forever -> infinity
        | PlaybackRepeat.Count n -> float n + 1.

    let direction =
        if options.Alternate then
            if reversed then PlaybackDirection.AlternateReverse else PlaybackDirection.Alternate
        else
            if reversed then PlaybackDirection.Reverse else PlaybackDirection.Normal

    let transformsSupported =
        if parsedTargets.Length > 0 then
            let doc = (parsedTargets[0] :> Node).ownerDocument
            if isNull (box doc) then false
            else WaapiHelpers.registerTransformProperties doc options.CustomProperties
        else false

    do
        ScopeRegistration.register (fun () -> this.Revert() |> ignore)
        for i in 0 .. parsedTargets.Length - 1 do
            let el = parsedTargets[i]
            let elStyle = (DomTargetHelpers.tryStyle el).Value
            let inlineSnap = inlineSnapshots[i]
            let hasIndivTransforms =
                transformsSupported &&
                (options.Properties |> Array.exists (fun (n, _) -> WaapiHelpers.ValidIndividualTransforms.Contains n))
            let cachedTransforms = Dictionary<string, string>(StringComparer.Ordinal)
            let baseEasing, outerSpring = WaapiHelpers.resolveWaapiEase options.Ease el i parsedTargets
            let baseDurationMs =
                match outerSpring with
                | Some sp -> sp.SettlingDuration * scaleToMs
                | None ->
                    let d =
                        match options.Duration with
                        | Some fn -> fn el i parsedTargets
                        | None -> (AnimationDefaults.current()).Duration
                    d * scaleToMs
            let baseDelayMs =
                let d =
                    match options.Delay with
                    | Some fn -> fn el i parsedTargets
                    | None -> (PlaybackDefaults.current()).Delay
                d * scaleToMs

            for propName, propValue in options.Properties do
                let indivTransform =
                    if hasIndivTransforms then
                        match ElementTransforms.canonicalName propName with
                        | Some c when c <> "translate3d" && c <> "rotate3d" && c <> "matrix" && c <> "matrix3d" -> Some c
                        | _ -> None
                    else None
                let styleSnapKey =
                    match indivTransform with
                    | Some _ -> "transform"
                    | None -> WaapiHelpers.toKebabName propName
                if not (inlineSnap.ContainsKey styleSnapKey) then
                    inlineSnap[styleSnapKey] <- elStyle.getPropertyValue styleSnapKey

                let mutable propDurMs = baseDurationMs
                let mutable propDelayMs = baseDelayMs
                let mutable propEasing = baseEasing
                let mutable propComposite = options.Composite
                let mutable explicitFrom : string option = None

                let valuesArray =
                    match propValue with
                    | WaapiValue.Options tweenOpts ->
                        let eStr, spOpt =
                            match tweenOpts.Ease with
                            | Some _ -> WaapiHelpers.resolveWaapiEase tweenOpts.Ease el i parsedTargets
                            | None -> baseEasing, None
                        propEasing <- eStr
                        propDurMs <-
                            match spOpt with
                            | Some sp -> sp.SettlingDuration * scaleToMs
                            | None ->
                                match tweenOpts.Duration with
                                | Some fn -> fn el i parsedTargets * scaleToMs
                                | None -> baseDurationMs
                        propDelayMs <-
                            match tweenOpts.Delay with
                            | Some fn -> fn el i parsedTargets * scaleToMs
                            | None -> baseDelayMs
                        propComposite <- defaultArg tweenOpts.Composite options.Composite
                        let parsedVals = WaapiHelpers.parseTweenValues el propName tweenOpts.From tweenOpts.To options.Computed i parsedTargets
                        if tweenOpts.From.IsSome && parsedVals.Length > 0 then
                            explicitFrom <- Some parsedVals[0]
                        parsedVals
                    | WaapiValue.Keyframes items ->
                        items |> Array.map (fun item -> WaapiHelpers.normalizeValue propName item el i parsedTargets)
                    | single ->
                        [| WaapiHelpers.normalizeValue propName single el i parsedTargets |]

                let keyframePropName =
                    match indivTransform with
                    | Some canonical ->
                        cachedTransforms[canonical] <- "1"
                        "--" + canonical
                    | None -> WaapiHelpers.toIdlPropertyName propName

                let domKeyframes =
                    valuesArray
                    |> Array.map (fun v ->
                        let kp = Ranvier.Dom.KeyframeProperty(name = keyframePropName, value = v)
                        Ranvier.Dom.Keyframe(properties = [| kp |]))

                let effectOpts =
                    Ranvier.Dom.KeyframeAnimationOptions(
                        duration = UnrestrictedDoubleOrCSSNumericValueOrDOMString.UnrestrictedDouble (max 0. propDurMs),
                        delay = propDelayMs,
                        iterations = iterations,
                        direction = direction,
                        fill = FillMode.Both,
                        easing = propEasing,
                        composite = propComposite)

                let webAnim = el.animate(domKeyframes, effectOpts)
                webAnim.playbackRate <- speed
                if paused then webAnim.pause()
                let animTotalMs = propDelayMs + (max 0. propDurMs) * (if Double.IsPositiveInfinity iterations then 1. else iterations)
                if totalDuration < animTotalMs then
                    totalDuration <- animTotalMs
                    controlAnimation <- Some webAnim

                WaapiHelpers.removeWaapiAnimation (Some el) (Some propName) None
                animations.Add webAnim
                let record = { Parent = this; Animation = webAnim; Element = el; Property = propName }
                WaapiHelpers.ActiveLookups.Add record

                if not persist then
                    match options.Animations with
                    | Some animSvc ->
                        let op = animSvc.Finished(webAnim, function
                            | ResourceOutcome.Succeeded _ -> WaapiHelpers.removeWaapiAnimation (Some el) (Some propName) (Some this)
                            | _ -> ())
                        finishSubscriptions.Add (op :> IDisposable)
                    | None ->
                        let finishListener =
                            { new EventListenerObject with
                                member _.handleEvent _ =
                                    WaapiHelpers.removeWaapiAnimation (Some el) (Some propName) (Some this) }
                        webAnim.addEventListener("finish", finishListener)
                        finishSubscriptions.Add
                            { new IDisposable with
                                member _.Dispose() =
                                    try webAnim.removeEventListener("finish", finishListener) with _ -> () }

                match explicitFrom with
                | Some firstVal ->
                    match indivTransform with
                    | Some canonical -> elStyle.setProperty("--" + canonical, firstVal)
                    | None -> elStyle.setProperty(WaapiHelpers.toKebabName propName, firstVal)
                | None -> ()

            if hasIndivTransforms && cachedTransforms.Count > 0 then
                let sb = Text.StringBuilder()
                for t in WaapiHelpers.AllRegisterableTransforms do
                    if cachedTransforms.ContainsKey t then
                        sb.Append(t).Append("(var(--").Append(t).Append(")) ") |> ignore
                elStyle.setProperty("transform", sb.ToString().TrimEnd())

        match options.Scroll with
        | Some scroller ->
            scroller.Link
                { Duration = (fun () -> this.Duration)
                  CurrentTime = (fun () -> this.CurrentTime)
                  Progress = (fun () -> this.Progress)
                  Completed = (fun () -> this.Completed)
                  Seek = (fun t mute -> this.Seek(t, mute) |> ignore)
                  Command = (function
                      | ScrollCommand.Play -> this.Play() |> ignore
                      | ScrollCommand.Pause -> this.Pause() |> ignore
                      | ScrollCommand.Resume -> this.Resume() |> ignore
                      | ScrollCommand.Reverse -> this.Reverse() |> ignore
                      | ScrollCommand.Alternate -> this.Alternate() |> ignore
                      | ScrollCommand.Restart -> this.Restart() |> ignore
                      | ScrollCommand.Reset -> this.Seek(0., true) |> ignore
                      | ScrollCommand.Complete -> this.Complete() |> ignore) } |> ignore
        | None -> ()

    member internal _.NotifyMemberRemoved() =
        completedCount <- completedCount + 1
        if animations.Count > 0 && completedCount >= animations.Count && not completed then
            completed <- true
            paused <- true
            if not muteCallbacks && not cancelled then
                options.OnComplete this
                settleCompletion()

    /// <summary>Executes an action for every underlying native WebAnimation.</summary>
    member this.ForEach(action: WebAnimation -> unit) =
        for anim in animations.ToArray() do
            try action anim with _ -> ()
        this

    /// <summary>DOM targets animated by this WAAPI controller.</summary>
    member _.Targets = Array.copy parsedTargets

    /// <summary>Underlying native WebAnimation instances.</summary>
    member _.Animations = animations.ToArray()

    /// <summary>Longest-duration control WebAnimation, if any.</summary>
    member _.ControlAnimation = controlAnimation

    /// <summary>Total duration in the engine's current time unit.</summary>
    member _.Duration = totalDuration * timeScale

    /// <summary>Whether WAAPI animations persist after finishing without auto-commit removal.</summary>
    member _.Persist
        with get() = persist
        and set value =
            persist <- value
            if value then
                for sub in finishSubscriptions do
                    try sub.Dispose() with _ -> ()
                finishSubscriptions.Clear()

    /// <summary>Whether scheduling is paused.</summary>
    member _.Paused = paused

    /// <summary>Whether all underlying WAAPI animations have completed.</summary>
    member _.Completed = completed

    /// <summary>Whether playback direction is reversed.</summary>
    member _.Reversed = reversed

    /// <summary>Playback speed multiplier.</summary>
    member this.Speed
        with get() = speed
        and set value =
            speed <- value
            this.ForEach(fun anim -> anim.playbackRate <- value) |> ignore

    /// <summary>Current time in the engine's current time unit.</summary>
    member this.CurrentTime
        with get() =
            if completed then this.Duration
            else
                match controlAnimation with
                | Some ctrl ->
                    match ctrl.currentTime with
                    | Some (DoubleOrCSSNumericValue.Double ms) -> ms * timeScale
                    | _ -> 0.
                | None -> 0.
        and set (time: float) =
            let ms = time * scaleToMs
            this.ForEach(fun anim ->
                if not persist && ms >= totalDuration then anim.play()
                anim.currentTime <- Some (DoubleOrCSSNumericValue.Double ms)) |> ignore
            if not persist && ms >= totalDuration && not completed then
                for el in parsedTargets do
                    WaapiHelpers.removeWaapiAnimation (Some el) None (Some this)

    /// <summary>Normalized overall progress in [0,1].</summary>
    member this.Progress
        with get() =
            let d = this.Duration
            if d <= 0. then 0. else this.CurrentTime / d
        and set (progress: float) =
            let ms = if Double.IsFinite progress then progress * totalDuration else 0.
            this.ForEach(fun anim -> anim.currentTime <- Some (DoubleOrCSSNumericValue.Double ms)) |> ignore

    /// <summary>Resumes playback on all underlying WebAnimations.</summary>
    member this.Resume() =
        if paused then
            paused <- false
            this.ForEach(fun anim -> anim.play()) |> ignore
        this

    /// <summary>Pauses playback on all underlying WebAnimations.</summary>
    member this.Pause() =
        if not paused then
            paused <- true
            this.ForEach(fun anim -> anim.pause()) |> ignore
        this

    /// <summary>Reverses playback direction on all underlying WebAnimations.</summary>
    member this.Alternate() =
        reversed <- not reversed
        this.ForEach(fun anim -> anim.reverse()) |> ignore
        if paused then this.ForEach(fun anim -> anim.pause()) |> ignore
        this

    /// <summary>Plays forward.</summary>
    member this.Play() =
        if reversed then this.Alternate() |> ignore
        this.Resume()

    /// <summary>Plays in reverse.</summary>
    member this.Reverse() =
        if not reversed then this.Alternate() |> ignore
        this.Resume()

    /// <summary>Seeks to the specified time in the engine's current time unit.</summary>
    member this.Seek(time: float, ?mute: bool) =
        let shouldMute = defaultArg mute false
        if shouldMute then muteCallbacks <- true
        if time < this.Duration then completed <- false
        this.CurrentTime <- time
        muteCallbacks <- false
        if paused then
            paused <- false
            this.Pause() |> ignore
        this

    /// <summary>Seeks to 0 with muted callbacks and resumes playback.</summary>
    member this.Restart() =
        completed <- false
        this.Seek(0., true).Resume()

    /// <summary>Commits animated styles to inline CSS on all active WebAnimations.</summary>
    member this.CommitStyles() =
        this.ForEach(fun anim -> anim.commitStyles())

    /// <summary>Seeks immediately to the end of the animation, firing completion and settling Then waiters.</summary>
    member this.Complete() =
        this.Seek(this.Duration)

    /// <summary>Commits styles and cancels all active WebAnimations without firing completion.</summary>
    member this.Cancel() =
        cancelled <- true
        muteCallbacks <- true
        for sub in finishSubscriptions do
            try sub.Dispose() with _ -> ()
        finishSubscriptions.Clear()
        this.CommitStyles().ForEach(fun anim -> anim.cancel()) |> ignore
        for i in WaapiHelpers.ActiveLookups.Count - 1 .. -1 .. 0 do
            if Object.ReferenceEquals(WaapiHelpers.ActiveLookups[i].Parent, this) then
                WaapiHelpers.ActiveLookups.RemoveAt i
        animations.Clear()
        let cleanNone () =
            for el in parsedTargets do
                match DomTargetHelpers.tryStyle el with
                | Some st when st.getPropertyValue "transform" = "none" ->
                    st.removeProperty "transform" |> ignore
                | _ -> ()
        match options.Scheduler with
        | Some sched -> sched.Frame(fun _ -> cleanNone()) |> ignore
        | None -> cleanNone()
        if not completed then settleCancelled()
        this

    /// <summary>Cancels all WebAnimations and restores pre-animation inline styles on every target.</summary>
    member this.Revert() =
        this.Cancel() |> ignore
        for i in 0 .. parsedTargets.Length - 1 do
            let el = parsedTargets[i]
            match DomTargetHelpers.tryStyle el with
            | Some st ->
                for KeyValue(name, orig) in inlineSnapshots[i] do
                    if String.IsNullOrEmpty orig then
                        st.removeProperty (WaapiHelpers.toKebabName name) |> ignore
                    else
                        st.setProperty(WaapiHelpers.toKebabName name, orig)
                if el.getAttribute "style" = "" then el.removeAttribute "style"
            | None -> ()
        this

    /// <summary>Registers an optional completion callback and returns a task settling when this WAAPI animation completes.</summary>
    member this.Then(?callback: WaapiAnimation -> unit) : Threading.Tasks.Task<WaapiAnimation> =
        let invoke () = callback |> Option.iter (fun action -> action this)
        if completed then
            invoke()
            Threading.Tasks.Task.FromResult this
        elif cancelled then
            Threading.Tasks.Task.FromCanceled<WaapiAnimation>(Threading.CancellationToken(true))
        else
            let source = Threading.Tasks.TaskCompletionSource<WaapiAnimation>()
            completionWaiters.Add(fun anim ->
                try
                    invoke()
                    source.TrySetResult anim |> ignore
                with error -> source.TrySetException error |> ignore)
            cancelWaiters.Add(fun () -> source.TrySetCanceled() |> ignore)
            source.Task

    /// <summary>Returns a task settling when this WAAPI animation completes.</summary>
    member this.AsTask() : Threading.Tasks.Task<WaapiAnimation> = this.Then()

    /// <summary>Invariant diagnostic of the WAAPI animation.</summary>
    override this.ToString() =
        "WaapiAnimation(Targets=" + parsedTargets.Length.ToString(CultureInfo.InvariantCulture) +
        "; Duration=" + this.Duration.ToString("R", CultureInfo.InvariantCulture) + ")"

    interface IDisposable with
        member this.Dispose() = this.Cancel() |> ignore

/// <summary>Anime.js waapi facade (waapi.animate and waapi.convertEase).</summary>
[<RequireQualifiedAccess>]
module Waapi =
    /// <summary>Samples an easing function into a CSS linear() timing function string (default 100 intervals).</summary>
    let convertEase (fn: float -> float) (samples: int option) : string =
        if isNull (box fn) then nullArg "fn"
        WaapiHelpers.easingToLinear fn (defaultArg samples 100)

    /// <summary>Creates a <see cref="T:Ranvier.Anime.WaapiAnimation"/> on one or more DOM elements.</summary>
    let animateTargets (targets: Element array) (options: WaapiAnimationOptions) : WaapiAnimation =
        new WaapiAnimation(targets, options)

    /// <summary>Creates a <see cref="T:Ranvier.Anime.WaapiAnimation"/> on a single DOM element.</summary>
    let animate (target: Element) (options: WaapiAnimationOptions) : WaapiAnimation =
        if isNull (box target) then nullArg "target"
        new WaapiAnimation([| target |], options)

    /// <summary>Removes active WAAPI animations matching target, optional property and optional parent controller.</summary>
    let internal removeActive (target: Element option) (property: string option) (parent: WaapiAnimation option) =
        WaapiHelpers.removeWaapiAnimation target property parent

/// <summary>Timeline and Scope extensions for <see cref="T:Ranvier.Anime.WaapiAnimation"/>.</summary>
[<AutoOpen>]
module WaapiExtensions =
    type Timeline with
        /// <summary>Forces persist on a WAAPI animation, pauses it and synchronizes its playhead with this timeline.</summary>
        member this.Sync(child: WaapiAnimation, ?position: TimelinePosition) =
            if isNull (box child) then nullArg "child"
            child.Pause() |> ignore
            child.Persist <- true
            this.BorrowExternal(
                box child,
                child.Duration,
                (fun () -> child.Pause() |> ignore),
                (fun value guard -> if guard() then child.CurrentTime <- value),
                position)

        /// <summary>Pauses and synchronizes a raw DOM WebAnimation with this timeline.</summary>
        member this.Sync(child: WebAnimation, duration: float, ?position: TimelinePosition) =
            if isNull (box child) then nullArg "child"
            child.pause()
            this.BorrowExternal(
                box child,
                duration,
                child.pause,
                (fun value guard -> if guard() then child.currentTime <- Some (DoubleOrCSSNumericValue.Double value)),
                position)
