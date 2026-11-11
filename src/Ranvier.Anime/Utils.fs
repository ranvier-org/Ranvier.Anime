namespace Ranvier.Anime

open System
open System.Collections.Generic
open System.Globalization
open Ranvier.Dom

/// <summary>Composable numeric transformation pipeline matching Anime.js chainable utilities.</summary>
[<Sealed; Diagnostics.DebuggerDisplay("{ToString(),nq}")>]
type ValueChain internal (evaluate: float -> float) =
    static let jsRound (v: float) = PropertyNumbers.round 1. v

    /// <summary>Evaluates the chained numeric pipeline on an input value.</summary>
    member _.Apply(value: float) : float = evaluate value

    /// <summary>Returns this pipeline as an F# modifier function (float -> float).</summary>
    member _.ToModifier() : float -> float = evaluate

    /// <summary>Chains a [min, max] clamp stage.</summary>
    member _.Clamp(min: float, max: float) =
        ValueChain(fun v ->
            let x = evaluate v
            if x < min then min elif x > max then max else x)

    /// <summary>Chains a decimal-places rounding stage.</summary>
    member _.Round(decimalLength: int) =
        ValueChain(fun v ->
            let x = evaluate v
            if decimalLength < 0 then x
            elif decimalLength = 0 then jsRound x
            else PropertyNumbers.round (Math.Pow(10., float decimalLength)) x)

    /// <summary>Chains a uniform step-increment snap stage.</summary>
    member _.Snap(increment: float) =
        ValueChain(fun v ->
            let x = evaluate v
            if increment = 0. then x else jsRound (x / increment) * increment)

    /// <summary>Chains an array-points snap stage.</summary>
    member _.SnapTo(points: float array) =
        if isNull points || points.Length = 0 then invalidArg "points" "Expected nonempty snap points."
        let copy = Array.copy points
        ValueChain(fun v ->
            let x = evaluate v
            copy |> Array.reduce (fun closest cv -> if abs (cv - x) < abs (closest - x) then cv else closest))

    /// <summary>Chains a range wrap stage in [min, max).</summary>
    member _.Wrap(min: float, max: float) =
        ValueChain(fun v ->
            let x = evaluate v
            let span = max - min
            (((x - min) % span + span) % span) + min)

    /// <summary>Chains a linear range remapping stage.</summary>
    member _.MapRange(inLow: float, inHigh: float, outLow: float, outHigh: float) =
        ValueChain(fun v ->
            let x = evaluate v
            outLow + ((x - inLow) / (inHigh - inLow)) * (outHigh - outLow))

    /// <summary>Chains a linear interpolation stage using the current pipeline value as the interpolation factor.</summary>
    member _.Lerp(start: float, finish: float) =
        ValueChain(fun v ->
            let factor = evaluate v
            if factor = 1. then finish elif factor = 0. then start else start + (finish - start) * factor)

    /// <summary>Chains a frame-rate independent damped lerp stage using the current pipeline value as the factor.</summary>
    member _.Damp(start: float, finish: float, deltaTime: float) =
        ValueChain(fun v ->
            let factor = evaluate v
            if factor = 0. then start
            elif factor = 1. then finish
            else
                let t = 1. - Math.Exp(-factor * deltaTime * 0.1)
                if t = 1. then finish elif t = 0. then start else start + (finish - start) * t)

    /// <summary>Chains a degrees-to-radians conversion stage.</summary>
    member _.DegToRad() = ValueChain(fun v -> evaluate v * Math.PI / 180.)

    /// <summary>Chains a radians-to-degrees conversion stage.</summary>
    member _.RadToDeg() = ValueChain(fun v -> evaluate v * 180. / Math.PI)

    /// <summary>Chains a fixed-decimal string formatting stage.</summary>
    member _.RoundPad(decimalLength: int) : StringChain =
        StringChain(fun v ->
            let x = evaluate v
            let places = max 0 decimalLength
            let rounded = PropertyNumbers.round (Math.Pow(10., float places)) x
            rounded.ToString("F" + places.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture))

    /// <summary>Chains a left string-padding stage.</summary>
    member _.PadStart(totalLength: int, padString: string) : StringChain =
        StringChain(fun v -> StringChain.pad true (ValueInterpolation.number (evaluate v)) totalLength padString)

    /// <summary>Chains a right string-padding stage.</summary>
    member _.PadEnd(totalLength: int, padString: string) : StringChain =
        StringChain(fun v -> StringChain.pad false (ValueInterpolation.number (evaluate v)) totalLength padString)

    /// <summary>Invariant diagnostic of the value chain.</summary>
    override _.ToString() = "ValueChain"

/// <summary>String-producing terminal or further padded stage of a chainable utility pipeline.</summary>
and [<Sealed; Diagnostics.DebuggerDisplay("{ToString(),nq}")>] StringChain internal (evaluate: float -> string) =
    static member internal pad (atStart: bool) (input: string) (totalLength: int) (padString: string) =
        let padText = if isNull padString then " " else padString
        if padText.Length = 0 || input.Length >= totalLength then input
        else
            let needed = totalLength - input.Length
            let sb = Text.StringBuilder(needed)
            while sb.Length < needed do
                let take = min padText.Length (needed - sb.Length)
                sb.Append(padText, 0, take) |> ignore
            if atStart then sb.ToString() + input else input + sb.ToString()

    /// <summary>Evaluates the chained pipeline and returns the formatted string.</summary>
    member _.Apply(value: float) : string = evaluate value

    /// <summary>Chains an additional left string-padding stage.</summary>
    member _.PadStart(totalLength: int, padString: string) =
        StringChain(fun v -> StringChain.pad true (evaluate v) totalLength padString)

    /// <summary>Chains an additional right string-padding stage.</summary>
    member _.PadEnd(totalLength: int, padString: string) =
        StringChain(fun v -> StringChain.pad false (evaluate v) totalLength padString)

    /// <summary>Invariant diagnostic of the string chain.</summary>
    override _.ToString() = "StringChain"

/// <summary>Doubly linked node matching Anime.js _prev / _next child lists.</summary>
[<AllowNullLiteral; Diagnostics.DebuggerDisplay("{ToString(),nq}")>]
type LinkedChild<'Value>(value: 'Value, ?priority: float) =
    /// <summary>Payload carried by this linked node.</summary>
    member _.Value = value
    /// <summary>Optional sort priority.</summary>
    member val Priority = defaultArg priority 1. with get, set
    /// <summary>Previous node in the parent list, or null.</summary>
    member val Prev : LinkedChild<'Value> = null with get, set
    /// <summary>Next node in the parent list, or null.</summary>
    member val Next : LinkedChild<'Value> = null with get, set
    /// <summary>Invariant diagnostic of the linked child.</summary>
    override _.ToString() = "LinkedChild"

/// <summary>Doubly linked list container matching Anime.js _head / _tail parent lists.</summary>
[<Sealed; Diagnostics.DebuggerDisplay("{ToString(),nq}")>]
type LinkedParent<'Value>() =
    /// <summary>First node in the list, or null when empty.</summary>
    member val Head : LinkedChild<'Value> = null with get, set
    /// <summary>Last node in the list, or null when empty.</summary>
    member val Tail : LinkedChild<'Value> = null with get, set
    /// <summary>Invariant diagnostic reporting whether the list is nonempty.</summary>
    override this.ToString() = "LinkedParent(Empty=" + isNull(this.Head).ToString() + ")"

/// <summary>Anime.js utility family: math, chainables, seeded random, timekeepers, DOM/object queries and target removal.</summary>
[<RequireQualifiedAccess>]
module Utils =
    let mutable private autoSeed = 0.

    /// <summary>Clamps a value between min and max bounds.</summary>
    let clamp (value: float) (min: float) (max: float) : float =
        if value < min then min elif value > max then max else value

    /// <summary>Rounds a number to decimalLength decimal places using JavaScript half-up rounding.</summary>
    let round (value: float) (decimalLength: int) : float =
        if decimalLength < 0 then value
        elif decimalLength = 0 then PropertyNumbers.round 1. value
        else PropertyNumbers.round (Math.Pow(10., float decimalLength)) value

    /// <summary>Snaps a value to the nearest multiple of increment.</summary>
    let snap (value: float) (increment: float) : float =
        if increment = 0. then value
        else PropertyNumbers.round 1. (value / increment) * increment

    /// <summary>Snaps a value to the closest entry in points (ties keep the earlier entry).</summary>
    let snapTo (value: float) (points: float array) : float =
        if isNull points || points.Length = 0 then invalidArg "points" "Expected nonempty snap points."
        points |> Array.reduce (fun closest cv -> if abs (cv - value) < abs (closest - value) then cv else closest)

    /// <summary>Wraps a value into the half-open interval [min, max).</summary>
    let wrap (value: float) (min: float) (max: float) : float =
        let span = max - min
        (((value - min) % span + span) % span) + min

    /// <summary>Linearly interpolates between start and finish by factor.</summary>
    let lerp (start: float) (finish: float) (factor: float) : float =
        if factor = 1. then finish elif factor = 0. then start else start + (finish - start) * factor

    /// <summary>Frame-rate independent damped interpolation matching Anime.js utils.damp.</summary>
    let damp (start: float) (finish: float) (deltaTime: float) (factor: float) : float =
        if factor = 0. then start
        elif factor = 1. then finish
        else lerp start finish (1. - Math.Exp(-factor * deltaTime * 0.1))

    /// <summary>Linearly maps a value from [inLow, inHigh] to [outLow, outHigh].</summary>
    let mapRange (value: float) (inLow: float) (inHigh: float) (outLow: float) (outHigh: float) : float =
        outLow + ((value - inLow) / (inHigh - inLow)) * (outHigh - outLow)

    /// <summary>Converts degrees to radians.</summary>
    let degToRad (degrees: float) : float = degrees * Math.PI / 180.

    /// <summary>Converts radians to degrees.</summary>
    let radToDeg (radians: float) : float = radians * 180. / Math.PI

    /// <summary>Rounds a number and formats fixed decimal places like Number.prototype.toFixed.</summary>
    let roundPad (value: float) (decimalLength: int) : string =
        let places = max 0 decimalLength
        let rounded = round value places
        rounded.ToString("F" + places.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture)

    /// <summary>Pads the start of a formatted number to totalLength using padString.</summary>
    let padStart (value: float) (totalLength: int) (padString: string) : string =
        StringChain.pad true (ValueInterpolation.number value) totalLength padString

    /// <summary>Pads the end of a formatted number to totalLength using padString.</summary>
    let padEnd (value: float) (totalLength: int) (padString: string) : string =
        StringChain.pad false (ValueInterpolation.number value) totalLength padString

    /// <summary>Entry points for curried chainable utility pipelines.</summary>
    [<RequireQualifiedAccess>]
    module Chain =
        /// <summary>Starts a chainable pipeline with a [min, max] clamp.</summary>
        let clamp min max = ValueChain(id).Clamp(min, max)
        /// <summary>Starts a chainable pipeline with decimal rounding.</summary>
        let round decimalLength = ValueChain(id).Round(decimalLength)
        /// <summary>Starts a chainable pipeline with uniform step snapping.</summary>
        let snap increment = ValueChain(id).Snap(increment)
        /// <summary>Starts a chainable pipeline with point-array snapping.</summary>
        let snapTo points = ValueChain(id).SnapTo(points)
        /// <summary>Starts a chainable pipeline with [min, max) wrapping.</summary>
        let wrap min max = ValueChain(id).Wrap(min, max)
        /// <summary>Starts a chainable pipeline with linear range mapping.</summary>
        let mapRange inLow inHigh outLow outHigh = ValueChain(id).MapRange(inLow, inHigh, outLow, outHigh)
        /// <summary>Starts a chainable pipeline with linear interpolation.</summary>
        let lerp start finish = ValueChain(id).Lerp(start, finish)
        /// <summary>Starts a chainable pipeline with damped interpolation.</summary>
        let damp start finish deltaTime = ValueChain(id).Damp(start, finish, deltaTime)
        /// <summary>Starts a chainable pipeline with degrees-to-radians conversion.</summary>
        let degToRad () = ValueChain(id).DegToRad()
        /// <summary>Starts a chainable pipeline with radians-to-degrees conversion.</summary>
        let radToDeg () = ValueChain(id).RadToDeg()
        /// <summary>Starts a chainable pipeline with fixed-decimal formatting.</summary>
        let roundPad decimalLength = ValueChain(id).RoundPad(decimalLength)
        /// <summary>Starts a chainable pipeline with left string padding.</summary>
        let padStart totalLength padString = ValueChain(id).PadStart(totalLength, padString)
        /// <summary>Starts a chainable pipeline with right string padding.</summary>
        let padEnd totalLength padString = ValueChain(id).PadEnd(totalLength, padString)

    /// <summary>Generates a random number in [min, max] (inclusive) with optional decimal precision.</summary>
    let random (min: float) (max: float) (decimalLength: int) : float =
        let m = Math.Pow(10., float decimalLength)
        Math.Floor((Random.Shared.NextDouble() * (max - min + (1. / m)) + min) * m) / m

    /// <summary>Creates a deterministic Mulberry32 pseudorandom generator matching Anime.js createSeededRandom.</summary>
    let createSeededRandom (seed: float option) (seededMin: float option) (seededMax: float option) (seededDecimalLength: int option)
                           : (float option -> float option -> int option -> float) =
        let defaultMin = defaultArg seededMin 0.
        let defaultMax = defaultArg seededMax 1.
        let defaultDecimals = defaultArg seededDecimalLength 0
        let mutable t =
            match seed with
            | Some s -> s
            | None ->
                let s = autoSeed
                autoSeed <- autoSeed + 1.
                s
        let toUint32 (v: float) =
            let rem = Math.Truncate(v) % 4294967296.
            uint32 (if rem < 0. then rem + 4294967296. else rem)
        fun minOpt maxOpt decOpt ->
            let lo = defaultArg minOpt defaultMin
            let hi = defaultArg maxOpt defaultMax
            let dec = defaultArg decOpt defaultDecimals
            let u0 = toUint32 (t + 1831565813.)
            let u1 = (u0 ^^^ (u0 >>> 15)) * (u0 ||| 1u)
            let u2 = u1 ^^^ (u1 + ((u1 ^^^ (u1 >>> 7)) * (u1 ||| 61u)))
            t <- float (int32 u2)
            let normalized = float (u2 ^^^ (u2 >>> 14)) / 4294967296.
            let m = Math.Pow(10., float dec)
            Math.Floor((normalized * (hi - lo + (1. / m)) + lo) * m) / m

    /// <summary>Picks a random element from a nonempty array.</summary>
    let randomPick (items: 'T array) : 'T =
        if isNull items || items.Length = 0 then invalidArg "items" "Expected a nonempty array."
        items[int (random 0. (float (items.Length - 1)) 0)]

    /// <summary>Picks a random character from a nonempty string.</summary>
    let randomPickChar (items: string) : char =
        if String.IsNullOrEmpty items then invalidArg "items" "Expected a nonempty string."
        items[int (random 0. (float (items.Length - 1)) 0)]

    /// <summary>Shuffles an array in-place using Fisher-Yates and an optional inclusive integer RNG (lo, hi -> int).</summary>
    let shuffleWith (rng: int -> int -> int) (items: 'T array) : 'T array =
        if isNull items then nullArg "items"
        if isNull (box rng) then nullArg "rng"
        let mutable m = items.Length
        while m > 0 do
            m <- m - 1
            let i = rng 0 m
            let tmp = items[m]
            items[m] <- items[i]
            items[i] <- tmp
        items

    /// <summary>Shuffles an array in-place using Fisher-Yates and Random.Shared.</summary>
    let shuffle (items: 'T array) : 'T array =
        shuffleWith (fun lo hi -> int (random (float lo) (float hi) 0)) items

    /// <summary>Appends or inserts a child into a doubly linked parent list using an optional sort predicate.</summary>
    let addChild (parent: LinkedParent<'Value>) (child: LinkedChild<'Value>)
                 (sortMethod: (LinkedChild<'Value> -> LinkedChild<'Value> -> bool) option) : unit =
        if isNull (box parent) then nullArg "parent"
        if isNull child then nullArg "child"
        let mutable prev = parent.Tail
        match sortMethod with
        | Some pred ->
            while not (isNull prev) && pred prev child do
                prev <- prev.Prev
        | None -> ()
        let next = if not (isNull prev) then prev.Next else parent.Head
        if not (isNull prev) then prev.Next <- child else parent.Head <- child
        if not (isNull next) then next.Prev <- child else parent.Tail <- child
        child.Prev <- prev
        child.Next <- next

    /// <summary>Detaches a child from a doubly linked parent list.</summary>
    let removeChild (parent: LinkedParent<'Value>) (child: LinkedChild<'Value>) : unit =
        if isNull (box parent) then nullArg "parent"
        if isNull child then nullArg "child"
        let prev = child.Prev
        let next = child.Next
        if not (isNull prev) then prev.Next <- next else parent.Head <- next
        if not (isNull next) then next.Prev <- prev else parent.Tail <- prev
        child.Prev <- null
        child.Next <- null

    /// <summary>Iterates a doubly linked parent list forward or in reverse, caching next before callback invocation.</summary>
    let forEachChildren (parent: LinkedParent<'Value>) (callback: LinkedChild<'Value> -> unit) (reverse: bool) : unit =
        if isNull (box parent) then nullArg "parent"
        if isNull (box callback) then nullArg "callback"
        let mutable cursor = if reverse then parent.Tail else parent.Head
        while not (isNull cursor) do
            let next = if reverse then cursor.Prev else cursor.Next
            callback cursor
            cursor <- next

    /// <summary>Schedules a 1-unit timer on the next frame to synchronize callback execution with the animation loop.</summary>
    let sync (scheduler: FrameScheduler) (callback: (Timer -> unit) option) : Timer =
        let dur = 1. * EngineDefaults.timeScale()
        let timer = new Timer(scheduler, dur, ?onComplete = callback)
        timer.Resume()
        timer

    /// <summary>Wraps a Timer-producing constructor so repeated invocations revert the previous instance and preserve iteration progress.</summary>
    let keepTimeTimer (constructor: 'Arg -> Timer) : ('Arg -> Timer) =
        let mutable tracked : Timer option = None
        fun arg ->
            let snapshot = tracked |> Option.map (fun t -> t.CaptureScopeTime())
            tracked |> Option.iter (fun t -> t.Revert())
            let next = constructor arg
            tracked <- Some next
            snapshot |> Option.iter (fun snap -> next.RestoreScopeTime snap)
            next

    /// <summary>Wraps a ComposedAnimation-producing constructor so repeated invocations revert the previous instance and preserve loop/alternate progress.</summary>
    let keepTime (constructor: 'Arg -> ComposedAnimation) : ('Arg -> ComposedAnimation) =
        let mutable tracked : ComposedAnimation option = None
        fun arg ->
            let snapshot = tracked |> Option.map (fun a -> a.Clock.CaptureScopeTime())
            tracked |> Option.iter (fun a -> a.Revert() |> ignore)
            let next = constructor arg
            tracked <- Some next
            snapshot |> Option.iter (fun snap -> next.Clock.RestoreScopeTime snap)
            next

    /// <summary>Selects matching DOM elements within the active Scope root or the supplied document ($ equivalent).</summary>
    let select (document: Document) (selector: string) : Element array =
        if isNull (box document) then nullArg "document"
        if String.IsNullOrWhiteSpace selector then [||]
        else
            let list = ScopeTargets.select document selector
            Array.init (int list.length) (fun i -> list.item (uint i) :?> Element)

    /// <summary>Reads a target property (CSS, transform, attribute, adapted or AnimatedObject), optionally converting or stripping units.</summary>
    let get (target: obj) (propName: string) (unitOption: Choice<unit, bool, string>) (computed: ComputedStyleService option) : AdaptedValue =
        if isNull target then nullArg "target"
        if String.IsNullOrWhiteSpace propName then invalidArg "propName" "A property name is required."
        let kind, canonical, adapter = DomTargetHelpers.classify Adapters.DefaultRegistry target propName
        let rawLit, _ = DomTargetHelpers.readOriginal computed target kind canonical adapter
        match unitOption with
        | Choice1Of3 () ->
            match rawLit with
            | ValueInterpolation.Number n -> AdaptedValue.Scalar n
            | ValueInterpolation.Text s -> AdaptedValue.Text s
        | Choice2Of3 false ->
            let decomposed = ValueInterpolation.decompose rawLit
            if decomposed.Kind = ValueInterpolation.ValueKind.Number || decomposed.Kind = ValueInterpolation.ValueKind.Unit then
                AdaptedValue.Scalar decomposed.Number
            else
                match rawLit with
                | ValueInterpolation.Number n -> AdaptedValue.Scalar n
                | ValueInterpolation.Text s -> AdaptedValue.Text s
        | Choice2Of3 true ->
            match rawLit with
            | ValueInterpolation.Number n -> AdaptedValue.Text (ValueInterpolation.number n)
            | ValueInterpolation.Text s -> AdaptedValue.Text s
        | Choice3Of3 targetUnit ->
            let decomposed = ValueInterpolation.decompose rawLit
            if decomposed.Kind = ValueInterpolation.ValueKind.Number || decomposed.Kind = ValueInterpolation.ValueKind.Unit then
                let converted =
                    match target with
                    | :? HTMLElement as htmlEl ->
                        CssUnits.convert htmlEl decomposed.Number decomposed.Unit targetUnit
                    | _ -> decomposed.Number
                let prec = match EngineDefaults.precision() with Some p -> int p | None -> 4
                let rounded = round converted prec
                AdaptedValue.Text (ValueInterpolation.number rounded + targetUnit)
            else
                match rawLit with
                | ValueInterpolation.Number n -> AdaptedValue.Text (ValueInterpolation.number n)
                | ValueInterpolation.Text s -> AdaptedValue.Text s

    /// <summary>Immediately sets properties on targets using a minValue-duration None-composition animation (matching utils.set).</summary>
    let set (scheduler: FrameScheduler) (targets: obj array) (specs: PropertySpec array) : ComposedAnimation =
        let opts =
            { ComposedAnimationOptions.Default with
                Duration = 1e-11
                Composition = KeyframeComposition.None
                Autoplay = false }
        let anim = Composed.animateTargets scheduler targets specs (Some opts)
        anim.Seek(1e-11, muteCallbacks = true)

    /// <summary>Removes targets (and optional property) from active ComposedAnimations, ComposedTimelines, Timelines or WaapiAnimations.</summary>
    let remove (scheduler: FrameScheduler option) (targets: obj array) (renderable: obj option) (propertyName: string option) : obj array =
        if isNull targets then nullArg "targets"
        let copy = Array.copy targets
        let targetSet = HashSet<obj>(copy, HashIdentity.Reference)
        for t in copy do
            match t with
            | :? Element as el ->
                match renderable with
                | Some (:? WaapiAnimation as wa) -> Waapi.removeActive (Some el) propertyName (Some wa)
                | None -> Waapi.removeActive (Some el) propertyName None
                | _ -> ()
            | _ -> ()
        match renderable with
        | Some (:? ComposedAnimation as anim) ->
            anim.RemoveMatching(targetSet, propertyName) |> ignore
        | Some (:? ComposedTimeline as tl) ->
            tl.RemoveTargets(copy, ?propertyName = propertyName) |> ignore
        | Some (:? Timeline as tl) ->
            for t in copy do tl.Remove t |> ignore
        | Some (:? WaapiAnimation) -> ()
        | _ ->
            match scheduler with
            | Some sched ->
                let manager = CompositionManagers.forScheduler sched
                for anim in manager.ActiveAnimations do
                    anim.RemoveMatching(targetSet, propertyName) |> ignore
            | None -> ()
        copy

    /// <summary>Pauses a ComposedAnimation and cleans only its inline CSS/transform mutations, preserving object/attribute values.</summary>
    let cleanInlineStyles (animation: ComposedAnimation) : ComposedAnimation =
        if isNull (box animation) then nullArg "animation"
        animation.RevertValues true
        animation
