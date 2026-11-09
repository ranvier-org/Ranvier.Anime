namespace Ranvier.Anime

open Fable.Core

/// <summary>The constructors of anime's objects, each imported from <c>animejs</c> on its own.</summary>
[<Erase>]
type Anime =
    /// <summary>Animates <c>targets</c> with <c>parameters</c>, playing at once unless <c>autoplay</c> is false.</summary>
    [<Import("animate", "animejs")>]
    static member animate(targets: Targets, parameters: Anim) : JSAnimation = jsNative

    /// <summary>An empty timeline; children are added with <c>add</c>, <c>set</c>, <c>call</c> and <c>sync</c>.</summary>
    [<Import("createTimeline", "animejs")>]
    static member timeline(?parameters: TimelineParams) : Timeline = jsNative

    /// <summary>A timer running <c>parameters</c>' callbacks on anime's clock.</summary>
    [<Import("createTimer", "animejs")>]
    static member timer(?parameters: TimerParams) : Timer = jsNative

    /// <summary>The properties of <c>targets</c> named in <c>parameters</c>, each animating to every value set on it.</summary>
    [<Import("createAnimatable", "animejs")>]
    static member animatable(targets: Targets, parameters: AnimatableParams) : Animatable = jsNative

    /// <summary>Makes <c>target</c> draggable.</summary>
    /// <remarks>Requires <c>DOMPoint</c>, <c>DOMMatrix</c> and <c>ResizeObserver</c>, which jsdom lacks.</remarks>
    [<Import("createDraggable", "animejs")>]
    static member draggable(target: Targets, ?parameters: DraggableParams) : Draggable = jsNative

    /// <summary>A scroll observer; passed as a tickable's <c>autoplay</c>, it drives that tickable.</summary>
    /// <remarks>Requires <c>ResizeObserver</c>, which jsdom lacks.</remarks>
    [<Import("onScroll", "animejs")>]
    static member onScroll(?parameters: ScrollParams) : ScrollObserver = jsNative

    /// <summary>A layout recorder over the children of <c>root</c>.</summary>
    /// <remarks>Transforms animate through WAAPI, outside anime's engine clock.</remarks>
    [<Import("createLayout", "animejs")>]
    static member layout(root: TS.Dom.Element, ?parameters: LayoutParams) : AutoLayout = jsNative

    /// <summary>A scope for the anime objects created by its constructors.</summary>
    [<Import("createScope", "animejs")>]
    static member scope(?parameters: ScopeParams) : Scope = jsNative

    /// <summary>Splits the text of <c>target</c> into line, word and character elements.</summary>
    /// <remarks>Requires <c>ResizeObserver</c>, which jsdom lacks.</remarks>
    [<Import("splitText", "animejs")>]
    static member splitText(target: TS.Dom.Element, ?parameters: SplitParams) : TextSplitter = jsNative

    /// <summary>A per-target value that scrambles the text of each target towards its final text.</summary>
    [<Import("scrambleText", "animejs")>]
    static member scramble(?parameters: ScrambleParams) : Value = jsNative

    /// <summary>Resolves once <c>tickable</c> completes.</summary>
    [<Emit("$0.then()")>]
    static member completion(tickable: #ITickable) : JS.Promise<unit> = jsNative

/// <summary>SVG helpers: morphing, line drawing and motion paths.</summary>
[<Erase>]
type Svg =
    /// <summary>A <c>d</c> or <c>points</c> value morphing to the shape of <c>path</c>.</summary>
    /// <remarks><c>precision</c> is the number of sampled points; 0 morphs point to point.</remarks>
    [<Import("morphTo", "animejs")>]
    static member morphTo(path: Targets, ?precision: int) : Value = jsNative

    /// <summary>The SVG geometry elements of <c>targets</c> with a <c>draw</c> property, visible from <c>start</c> to <c>finish</c>.</summary>
    [<Import("createDrawable", "animejs")>]
    static member drawable(targets: Targets, ?start: float, ?finish: float) : TS.Dom.Element array = jsNative

    /// <summary>The translations and rotation following <c>path</c>, starting <c>offset</c> along it in 0..1.</summary>
    [<Import("createMotionPath", "animejs")>]
    static member motionPath(path: Targets, ?offset: float) : MotionPath = jsNative

/// <summary>Text helpers.</summary>
[<Erase>]
type Text =
    /// <summary>Splits the text of <c>target</c>; the same as <c>Anime.splitText</c>.</summary>
    [<Import("splitText", "animejs")>]
    static member split(target: TS.Dom.Element, ?parameters: SplitParams) : TextSplitter = jsNative

    /// <summary>A scrambling text value; the same as <c>Anime.scramble</c>.</summary>
    [<Import("scrambleText", "animejs")>]
    static member scramble(?parameters: ScrambleParams) : Value = jsNative

/// <summary>anime's WAAPI module.</summary>
[<AllowNullLiteral>]
type WaapiModule =
    abstract animate: targets: Targets * parameters: WaapiParams -> WAAPIAnimation
    abstract convertEase: ease: (float -> float) * ?samples: int -> string

/// <summary>Animations run by the browser's Web Animations API, for transforms and opacity off the main thread.</summary>
[<Erase>]
type Waapi =
    /// <summary>The module object behind <c>animate</c> and <c>convertEase</c>.</summary>
    [<Import("waapi", "animejs")>]
    static member api: WaapiModule = jsNative

    /// <summary>Animates <c>targets</c> through WAAPI.</summary>
    static member inline animate(targets: Targets, parameters: WaapiParams) : WAAPIAnimation =
        Waapi.api.animate (targets, parameters)

    /// <summary>The CSS <c>linear()</c> easing approximating <c>ease</c> with <c>samples</c> points.</summary>
    static member inline convertEase(ease: float -> float, ?samples: int) : string =
        Waapi.api.convertEase (ease, ?samples = samples)

    /// <summary>Resolves once <c>animation</c> completes.</summary>
    [<Emit("$0.then()")>]
    static member completion(animation: WAAPIAnimation) : JS.Promise<unit> = jsNative

/// <summary>anime's utilities, each imported on its own.</summary>
[<Erase>]
type Utils =
    /// <summary>Sets the properties in <c>parameters</c> on <c>targets</c> at once.</summary>
    [<Import("set", "animejs")>]
    static member set(targets: Targets, parameters: Anim) : JSAnimation = jsNative

    /// <summary>The current value of <c>property</c> on the first of <c>targets</c>, with its unit.</summary>
    [<Import("get", "animejs")>]
    static member get(targets: Targets, property: string) : string = jsNative

    /// <summary>The current value of <c>property</c> on the first of <c>targets</c>, converted to <c>unit</c>.</summary>
    [<Import("get", "animejs")>]
    static member getIn(targets: Targets, property: string, unit: string) : string = jsNative

    /// <summary>The current value of <c>property</c> on the first of <c>targets</c> as a number, without its unit.</summary>
    static member inline getNumber(targets: Targets, property: string) : float =
        (JsInterop.import "get" "animejs": System.Func<Targets, string, bool, float>).Invoke(targets, property, false)

    /// <summary>Removes <c>targets</c> from every animation, or from <c>animation</c> only, or only their <c>property</c> tweens.</summary>
    [<Import("remove", "animejs")>]
    static member remove(targets: Targets, ?animation: ITickable, ?property: string) : unit = jsNative

    /// <summary>Removes the inline styles <c>tickable</c> set on its targets.</summary>
    [<Import("cleanInlineStyles", "animejs")>]
    static member cleanInlineStyles(tickable: ITickable) : unit = jsNative

    /// <summary>A random number in <c>min</c>..<c>max</c>, rounded to <c>decimals</c> places.</summary>
    [<Import("random", "animejs")>]
    static member random(min: float, max: float, ?decimals: int) : float = jsNative

    /// <summary>A random number generator seeded with <c>seed</c>, repeating its sequence for the same seed.</summary>
    [<Import("createSeededRandom", "animejs")>]
    static member seededRandom(seed: int) : System.Func<float, float, int, float> = jsNative

    [<Import("clamp", "animejs")>]
    static member clamp(value: float, min: float, max: float) : float = jsNative

    /// <summary>Maps <c>value</c> from the range <c>inLow</c>..<c>inHigh</c> to <c>outLow</c>..<c>outHigh</c>.</summary>
    [<Import("mapRange", "animejs")>]
    static member mapRange(value: float, inLow: float, inHigh: float, outLow: float, outHigh: float) : float = jsNative

    /// <summary>Rounds <c>value</c> to the nearest multiple of an increment, or to the nearest of <c>Snap.points</c>.</summary>
    [<Import("snap", "animejs")>]
    static member snap(value: float, increment: Snap) : float = jsNative

    /// <summary>Interpolates from <c>start</c> to <c>finish</c> by <c>amount</c> in 0..1.</summary>
    [<Import("lerp", "animejs")>]
    static member lerp(start: float, finish: float, amount: float) : float = jsNative

    /// <summary>A frame-rate independent <c>lerp</c> over <c>deltaTime</c> milliseconds.</summary>
    [<Import("damp", "animejs")>]
    static member damp(start: float, finish: float, deltaTime: float, factor: float) : float = jsNative

    /// <summary>Rounds <c>value</c> to <c>decimals</c> places.</summary>
    [<Import("round", "animejs")>]
    static member round(value: float, decimals: int) : float = jsNative

    /// <summary>Wraps <c>value</c> into <c>min</c>..<c>max</c>.</summary>
    [<Import("wrap", "animejs")>]
    static member wrap(value: float, min: float, max: float) : float = jsNative

    [<Import("degToRad", "animejs")>]
    static member degToRad(degrees: float) : float = jsNative

    [<Import("radToDeg", "animejs")>]
    static member radToDeg(radians: float) : float = jsNative

    /// <summary>A timer that runs <c>callback</c> once on the next engine tick.</summary>
    [<Import("sync", "animejs")>]
    static member sync(callback: Timer -> unit) : Timer = jsNative

/// <summary>The settings of anime's engine, the clock every tickable runs on.</summary>
[<AllowNullLiteral>]
type EngineSettings =
    /// <summary>Whether the engine ticks on <c>requestAnimationFrame</c>; false leaves ticking to <c>update</c>.</summary>
    abstract useDefaultMainLoop: bool with get, set
    /// <summary>Whether the engine pauses while the document is hidden.</summary>
    abstract pauseOnDocumentHidden: bool with get, set
    /// <summary>The global playback rate.</summary>
    abstract speed: float with get, set
    /// <summary>The engine's frame rate cap.</summary>
    abstract fps: float with get, set
    /// <summary>The time unit of every duration, delay and time: <c>"ms"</c> or <c>"s"</c>.</summary>
    abstract timeUnit: string with get, set
    /// <summary>The number of decimals in rendered values.</summary>
    abstract precision: int with get, set
    abstract paused: bool
    /// <summary>Advances every running tickable to the current time.</summary>
    abstract update: unit -> unit
    abstract pause: unit -> unit
    abstract resume: unit -> unit

/// <summary>anime's engine.</summary>
[<Erase>]
type Engine =
    /// <summary>The engine's main-loop, speed, frame-rate and time-unit settings, and its manual <c>update</c>.</summary>
    [<Import("engine", "animejs")>]
    static member settings: EngineSettings = jsNative

    /// <summary>Advances every running tickable to the current time.</summary>
    static member inline update() =
        Engine.settings.update ()
