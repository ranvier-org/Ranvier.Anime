namespace Ranvier.Anime

open Fable.Core

/// <summary>The conversion behind the erased binding types.</summary>
[<System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)>]
module Erased =
    /// <summary>
    /// <c>value</c> as <c>'U</c>: the same JavaScript value under Fable, and <c>Unchecked.defaultof</c> on .NET, where
    /// erased values are placeholders.
    /// </summary>
    let inline cast<'T, 'U> (value: 'T) : 'U =
#if FABLE_COMPILER
        unbox<'U> value
#else
        ignore value
        Unchecked.defaultof<'U>
#endif

/// <summary>The targets of an animation: an element, an element array, a CSS selector or a plain JS object.</summary>
[<Erase>]
type Targets =
    static member inline op_Implicit(element: TS.Dom.Element) : Targets =
        Erased.cast element

    static member inline op_Implicit(elements: TS.Dom.Element array) : Targets =
        Erased.cast elements

    /// <summary>The elements matched by the CSS selector <c>css</c> when the animation is created.</summary>
    static member inline selector(css: string) : Targets =
        Erased.cast css

    /// <summary>A plain JS object whose numeric properties are tweened in place.</summary>
    static member inline object(target: obj) : Targets =
        Erased.cast target

/// <summary>An easing: a named curve, a parameterised curve, a spring or an F# function of progress.</summary>
[<Erase>]
type Ease =
    static member inline linear: Ease = Erased.cast "linear"
    static member inline none: Ease = Erased.cast "none"
    static member inline ``in``: Ease = Erased.cast "in"
    static member inline out: Ease = Erased.cast "out"
    static member inline inOut: Ease = Erased.cast "inOut"
    static member inline outIn: Ease = Erased.cast "outIn"
    static member inline inQuad: Ease = Erased.cast "inQuad"
    static member inline outQuad: Ease = Erased.cast "outQuad"
    static member inline inOutQuad: Ease = Erased.cast "inOutQuad"
    static member inline outInQuad: Ease = Erased.cast "outInQuad"
    static member inline inCubic: Ease = Erased.cast "inCubic"
    static member inline outCubic: Ease = Erased.cast "outCubic"
    static member inline inOutCubic: Ease = Erased.cast "inOutCubic"
    static member inline outInCubic: Ease = Erased.cast "outInCubic"
    static member inline inQuart: Ease = Erased.cast "inQuart"
    static member inline outQuart: Ease = Erased.cast "outQuart"
    static member inline inOutQuart: Ease = Erased.cast "inOutQuart"
    static member inline outInQuart: Ease = Erased.cast "outInQuart"
    static member inline inQuint: Ease = Erased.cast "inQuint"
    static member inline outQuint: Ease = Erased.cast "outQuint"
    static member inline inOutQuint: Ease = Erased.cast "inOutQuint"
    static member inline outInQuint: Ease = Erased.cast "outInQuint"
    static member inline inSine: Ease = Erased.cast "inSine"
    static member inline outSine: Ease = Erased.cast "outSine"
    static member inline inOutSine: Ease = Erased.cast "inOutSine"
    static member inline outInSine: Ease = Erased.cast "outInSine"
    static member inline inCirc: Ease = Erased.cast "inCirc"
    static member inline outCirc: Ease = Erased.cast "outCirc"
    static member inline inOutCirc: Ease = Erased.cast "inOutCirc"
    static member inline outInCirc: Ease = Erased.cast "outInCirc"
    static member inline inExpo: Ease = Erased.cast "inExpo"
    static member inline outExpo: Ease = Erased.cast "outExpo"
    static member inline inOutExpo: Ease = Erased.cast "inOutExpo"
    static member inline outInExpo: Ease = Erased.cast "outInExpo"
    static member inline inBounce: Ease = Erased.cast "inBounce"
    static member inline outBounce: Ease = Erased.cast "outBounce"
    static member inline inOutBounce: Ease = Erased.cast "inOutBounce"
    static member inline outInBounce: Ease = Erased.cast "outInBounce"
    static member inline inBack: Ease = Erased.cast "inBack"
    static member inline outBack: Ease = Erased.cast "outBack"
    static member inline inOutBack: Ease = Erased.cast "inOutBack"
    static member inline outInBack: Ease = Erased.cast "outInBack"
    static member inline inElastic: Ease = Erased.cast "inElastic"
    static member inline outElastic: Ease = Erased.cast "outElastic"
    static member inline inOutElastic: Ease = Erased.cast "inOutElastic"
    static member inline outInElastic: Ease = Erased.cast "outInElastic"

    /// <summary>The power curve <c>in(power)</c>; anime's default power is 1.675.</summary>
    [<Emit("'in(' + $0 + ')'")>]
    static member inPower(power: float) : Ease = jsNative

    /// <summary>The power curve <c>out(power)</c>; anime's default power is 1.675.</summary>
    [<Emit("'out(' + $0 + ')'")>]
    static member outPower(power: float) : Ease = jsNative

    /// <summary>The power curve <c>inOut(power)</c>; anime's default power is 1.675.</summary>
    [<Emit("'inOut(' + $0 + ')'")>]
    static member inOutPower(power: float) : Ease = jsNative

    /// <summary>The back curve <c>inBack(overshoot)</c>; anime's default overshoot is 1.7.</summary>
    [<Emit("'inBack(' + $0 + ')'")>]
    static member inBackBy(overshoot: float) : Ease = jsNative

    /// <summary>The back curve <c>outBack(overshoot)</c>; anime's default overshoot is 1.7.</summary>
    [<Emit("'outBack(' + $0 + ')'")>]
    static member outBackBy(overshoot: float) : Ease = jsNative

    /// <summary>The back curve <c>inOutBack(overshoot)</c>; anime's default overshoot is 1.7.</summary>
    [<Emit("'inOutBack(' + $0 + ')'")>]
    static member inOutBackBy(overshoot: float) : Ease = jsNative

    /// <summary>The elastic curve <c>inElastic(amplitude, period)</c>; anime's defaults are 1 and 0.3.</summary>
    [<Emit("'inElastic(' + $0 + ',' + $1 + ')'")>]
    static member inElasticBy(amplitude: float, period: float) : Ease = jsNative

    /// <summary>The elastic curve <c>outElastic(amplitude, period)</c>; anime's defaults are 1 and 0.3.</summary>
    [<Emit("'outElastic(' + $0 + ',' + $1 + ')'")>]
    static member outElasticBy(amplitude: float, period: float) : Ease = jsNative

    /// <summary>The elastic curve <c>inOutElastic(amplitude, period)</c>; anime's defaults are 1 and 0.3.</summary>
    [<Emit("'inOutElastic(' + $0 + ',' + $1 + ')'")>]
    static member inOutElasticBy(amplitude: float, period: float) : Ease = jsNative

    /// <summary>A physical spring. Given a <c>duration</c> or <c>bounce</c>, it replaces the stiffness and damping.</summary>
    /// <remarks>As an animation's <c>ease</c>, the spring also sets the animation's duration to its settling time.</remarks>
    [<Import("spring", "animejs"); ParamObject>]
    static member spring(?mass: float, ?stiffness: float, ?damping: float, ?velocity: float, ?bounce: float, ?duration: float) : Ease = jsNative

    /// <summary>A CSS-style cubic Bézier curve through the control points (x1, y1) and (x2, y2).</summary>
    [<Import("cubicBezier", "animejs")>]
    static member cubicBezier(x1: float, y1: float, x2: float, y2: float) : Ease = jsNative

    /// <summary>A staircase of <c>count</c> equal steps; <c>fromStart</c> jumps at the start of each step.</summary>
    [<Import("steps", "animejs")>]
    static member steps(count: int, ?fromStart: bool) : Ease = jsNative

    /// <summary>A piecewise linear curve through evenly spaced <c>stops</c>.</summary>
    [<Import("linear", "animejs")>]
    static member linearStops([<System.ParamArray>] stops: float array) : Ease = jsNative

    /// <summary>A random-walk curve of <c>length</c> segments; <c>randomness</c> scales each segment's deviation.</summary>
    [<Import("irregular", "animejs")>]
    static member irregular(?length: int, ?randomness: float) : Ease = jsNative

    /// <summary>A curve computed by <c>f</c>, mapping progress in 0..1 to eased progress.</summary>
    static member inline fn(f: float -> float) : Ease =
        Erased.cast f

    /// <summary>Any other anime or WAAPI ease string, such as <c>"cubic-bezier(.2,0,0,1)"</c>.</summary>
    static member inline named(ease: string) : Ease =
        Erased.cast ease

/// <summary>The origin of a stagger.</summary>
[<Erase>]
type StaggerFrom =
    static member inline first: StaggerFrom = Erased.cast "first"
    static member inline center: StaggerFrom = Erased.cast "center"
    static member inline last: StaggerFrom = Erased.cast "last"
    static member inline random: StaggerFrom = Erased.cast "random"

    /// <summary>The target at <c>index</c>.</summary>
    static member inline index(index: int) : StaggerFrom =
        Erased.cast index

    /// <summary>
    /// The grid point at fractions <c>x</c> and <c>y</c> of the grid's width and height, each in 0..1, for a stagger with
    /// <c>grid</c>: <c>(0.5, 0.5)</c> is the centre.
    /// </summary>
    [<Emit("[$0, $1]")>]
    static member at(x: float, y: float) : StaggerFrom = jsNative

/// <summary>The grid of a stagger: measured from the targets' positions, or given as columns and rows.</summary>
[<Erase>]
type Grid =
    /// <summary>The grid read from the targets' layout.</summary>
    static member inline auto: Grid = Erased.cast true

    [<Emit("[$0, $1]")>]
    static member size(columns: int, rows: int) : Grid = jsNative

/// <summary>A spatial axis: x, y or z.</summary>
[<Erase>]
type Axis =
    static member inline x: Axis = Erased.cast "x"
    static member inline y: Axis = Erased.cast "y"
    static member inline z: Axis = Erased.cast "z"

/// <summary>A setting given as a boolean, a string or a number, as anime's options of that shape take.</summary>
[<Erase>]
type Switch =
    static member inline op_Implicit(value: bool) : Switch =
        Erased.cast value

    static member inline op_Implicit(value: string) : Switch =
        Erased.cast value

    static member inline op_Implicit(value: int) : Switch =
        Erased.cast value

/// <summary>How an animation combines with others animating the same property of the same target.</summary>
[<Erase>]
type Composition =
    /// <summary>The new animation replaces the running one on the property.</summary>
    static member inline replace: Composition = Erased.cast "replace"

    /// <summary>The animations run independently and the last rendered wins.</summary>
    static member inline none: Composition = Erased.cast "none"

    /// <summary>The animations add their values together.</summary>
    static member inline blend: Composition = Erased.cast "blend"

/// <summary>
/// A property value or timing parameter: a number, a string with a unit, operator or colour, a from/to pair, a keyframe
/// array, a per-target function or a stagger.
/// </summary>
[<Erase>]
type Value =
    static member inline op_Implicit(value: float) : Value =
        Erased.cast value

    static member inline op_Implicit(value: int) : Value =
        Erased.cast value

    /// <summary>A string value such as <c>"50%"</c>, <c>"+=20"</c>, <c>"#f0f"</c> or <c>"1rem"</c>.</summary>
    static member inline op_Implicit(value: string) : Value =
        Erased.cast value

    /// <summary>Animates from <c>start</c> to <c>finish</c>.</summary>
    static member inline fromTo(start: Value, finish: Value) : Value =
        Erased.cast [| start; finish |]

    /// <summary>Animates from <c>start</c> to the target's current value.</summary>
    [<Emit("{from:$0}")>]
    static member from(start: Value) : Value = jsNative

    /// <summary>Animates from the target's current value to <c>finish</c>.</summary>
    [<Emit("{to:$0}")>]
    static member ``to``(finish: Value) : Value = jsNative

    /// <summary>A tween object with its own timing, overriding the animation's for this property.</summary>
    [<ParamObject; Emit("$0")>]
    static member tween
        (
            ?``to``: Value,
            ?from: Value,
            ?fromTo: Value,
            ?duration: Value,
            ?delay: Value,
            ?ease: Ease,
            ?modifier: float -> float,
            ?composition: Composition
        ) : Value =
        jsNative

    /// <summary>Keyframes played in sequence, each taking an equal share of the duration.</summary>
    static member inline keys(frames: Value array) : Value =
        Erased.cast frames

    /// <summary>A value computed per target from the target, its index and every target.</summary>
    static member inline per(f: TS.Dom.Element -> int -> TS.Dom.Element array -> Value) : Value =
        Erased.cast (System.Func<_, _, _, _> f)

    /// <summary>
    /// A per-target value growing by <c>each</c> from target to target. <c>each</c> given as <c>Value.fromTo</c>
    /// distributes the range across the targets.
    /// </summary>
    /// <remarks><c>use</c> names the property or attribute of each target that holds its stagger index.</remarks>
    [<Import("stagger", "animejs"); ParamObject(1)>]
    static member stagger
        (
            each: Value,
            ?start: Value,
            ?from: StaggerFrom,
            ?reversed: bool,
            ?grid: Grid,
            ?axis: Axis,
            ?``use``: string,
            ?total: int,
            ?ease: Ease,
            ?modifier: float -> float,
            ?jitter: float,
            ?seed: int
        ) : Value =
        jsNative

/// <summary>A position on a timeline.</summary>
[<Erase>]
type At =
    /// <summary><c>ms</c> milliseconds from the timeline's start.</summary>
    static member inline ms(ms: float) : At =
        Erased.cast ms

    /// <summary><c>ms</c> milliseconds after the end of the last child (<c>"+=ms"</c>).</summary>
    [<Emit("'+=' + $0")>]
    static member after(ms: float) : At = jsNative

    /// <summary><c>ms</c> milliseconds before the end of the last child (<c>"-=ms"</c>).</summary>
    [<Emit("'-=' + $0")>]
    static member before(ms: float) : At = jsNative

    /// <summary>The timeline's current duration multiplied by <c>factor</c> (<c>"*=factor"</c>).</summary>
    [<Emit("'*=' + $0")>]
    static member times(factor: float) : At = jsNative

    /// <summary>Sequential: the end of the previous child (<c>"&lt;"</c>).</summary>
    static member inline afterPrevious: At = Erased.cast "<"

    /// <summary>Simultaneous: the start of the previous child (<c>"&lt;&lt;"</c>).</summary>
    static member inline withPrevious: At = Erased.cast "<<"

    /// <summary><c>ms</c> milliseconds after the start of the previous child (<c>"&lt;&lt;+=ms"</c>).</summary>
    [<Emit("'<<+=' + $0")>]
    static member afterPreviousStart(ms: float) : At = jsNative

    /// <summary>The position of the label <c>name</c>.</summary>
    static member inline label(name: string) : At =
        Erased.cast name

    /// <summary>A per-target position from a stagger, for a child animating several targets.</summary>
    static member inline stagger(stagger: Value) : At =
        Erased.cast stagger

/// <summary>A repeat count: a number of extra iterations, or forever.</summary>
[<Erase>]
type Loop =
    static member inline op_Implicit(times: int) : Loop =
        Erased.cast times

    static member inline op_Implicit(forever: bool) : Loop =
        Erased.cast forever

    static member inline forever: Loop = Erased.cast true

/// <summary>An object built from key-value pairs, for parameters keyed by arbitrary names.</summary>
[<Erase>]
type Pairs<'T> =
    /// <summary>The object with one property per pair.</summary>
    [<Emit("Object.fromEntries($0)")>]
    static member ofArray(pairs: (string * 'T) array) : Pairs<'T> = jsNative
