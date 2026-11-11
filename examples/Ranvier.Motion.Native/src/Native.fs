/// <summary>
/// The host interop the native pages share, in place of the browser pages' <c>Page</c> module: Ranvier.Anime calls on
/// the installed <c>AnimeHost</c>, element queries, timers on the window loop, tweened points and seeded numbers.
/// </summary>
module Native

open System
open System.Diagnostics
open Ranvier
open Ranvier.Dom
open Ranvier.Anime
open Ranvier.View.Anime

/// <summary>The anime.js easing of that name, such as <c>outQuart</c>.</summary>
let ease (name: string) : float -> float = Ease.named name

let outQuart = ease "outQuart"
let outBack = ease "outBack"
let outCubic = ease "outCubic"
let inQuad = ease "inQuad"
let inOutQuad = ease "inOutQuad"
let inOutSine = ease "inOutSine"
let linear = ease "linear"

/// <summary>Options of a composed animation of <c>duration</c> milliseconds along <c>easing</c>, reading the host's styles.</summary>
let timing (duration: float) (easing: float -> float) =
    { ComposedAnimationOptions.Default with
        Duration = duration
        Ease = easing
        Computed = Some AnimeHost.Current.Computed }

/// <summary>Plays <c>forever</c> back and forth.</summary>
let alternating (options: ComposedAnimationOptions) =
    { options with
        Playback =
            { options.Playback with
                Repeat = PlaybackRepeat.Forever
                Alternate = true } }

/// <summary>Starts <c>options</c> after <c>delay</c> milliseconds.</summary>
let delayed (delay: float) (options: ComposedAnimationOptions) =
    { options with Playback = { options.Playback with Delay = delay } }

/// <summary>Animates <c>targets</c> on the installed host's frames.</summary>
let animateAll (targets: obj array) (options: ComposedAnimationOptions) (properties: PropertySpec array) =
    Anime.animateTargets targets properties options

/// <summary>Animates the element on the installed host's frames.</summary>
let animate (element: Element) (options: ComposedAnimationOptions) (properties: PropertySpec array) =
    animateAll [| box element |] options properties

/// <summary>A property from <c>first</c> to <c>last</c>.</summary>
let fromTo (name: string) (first: float) (last: float) = PropertySpec.Scalar(name, last, fromValue = first)

/// <summary>A property from its current value to <c>last</c>.</summary>
let toward (name: string) (last: float) = PropertySpec.Scalar(name, last)

/// <summary>A paused timeline on the installed host's frames, for children added with <c>Add</c>.</summary>
let timeline (autoplay: bool) =
    Composed.createTimeline AnimeHost.Current.Scheduler None (Some autoplay)

/// <summary>A child placed at the start of the previous one, anime's <c>"&lt;"</c>.</summary>
let withPrevious = TimelinePosition.Expression "<"

/// <summary>One delay per target: <c>step</c> milliseconds apart, spreading from <c>origin</c> over <c>grid</c>.</summary>
let stagger (step: float) (origin: StaggerOrigin) (grid: StaggerGrid) (count: int) : float array =
    Stagger.distribute (StaggerValue.Step step) { StaggerOptions.Default with Origin = origin; Grid = grid } (uint count)

/// <summary>One value per target, spread evenly from <c>first</c> to <c>last</c>.</summary>
let spread (first: float) (last: float) (count: int) : float array =
    Stagger.distribute (StaggerValue.Range(first, last)) StaggerOptions.Default (uint count)

/// <summary>Delays each target by its entry in <c>delays</c>.</summary>
let delayedBy (delays: float array) (options: ComposedAnimationOptions) =
    { options with DelayBy = Some(fun _ index _ -> delays[index]) }

/// <summary>The duration and ease of a spring of that stiffness and damping, settled at its end.</summary>
let spring (stiffness: float) (damping: float) =
    let solved = Spring(stiffness = stiffness, damping = damping)
    solved.SettlingDuration, (fun time -> solved.Ease time)

/// <summary>Animates the point <c>(x, y)</c> home to <c>(0, 0)</c> along a spring, calling <c>write</c> with every frame's point.</summary>
let springPoint (stiffness: float) (damping: float) (struct (x: float, y: float)) (write: struct (float * float) -> unit) =
    let point = AnimatedObject [ "x", AdaptedValue.Scalar x; "y", AdaptedValue.Scalar y ]
    let duration, easing = spring stiffness damping

    let options =
        { ComposedAnimationOptions.Default with
            Duration = duration
            Ease = easing
            OnUpdate = fun _ -> write (struct (point.GetNumber "x", point.GetNumber "y")) }

    Anime.animate (box point) [| toward "x" 0.; toward "y" 0. |] options

/// <summary>
/// A signal of the progress, 0 to 1, of a linear clock of <c>duration</c> milliseconds started now and cancelled with
/// the current owner; anime's <c>Reactive.track (Reactive.own (animate ...))</c> over a plain object.
/// </summary>
let clock (duration: float) : Signal<float> =
    let progress = Api.createSignal 0.
    let graph = Graph.Current
    let tick = AnimatedObject [ "x", AdaptedValue.Scalar 0. ]

    let options =
        { ComposedAnimationOptions.Default with
            Duration = duration
            Ease = linear
            OnUpdate = fun _ -> graph.Batch(fun () -> progress.Value <- tick.GetNumber "x") }

    let animation = Anime.animate (box tick) [| toward "x" 1. |] options
    Api.onCleanup (fun () -> animation.Cancel())
    progress

/// <summary>Seeks <c>timeline</c> to the fraction <c>read</c> returns, now and on every change; anime's <c>Reactive.scrub</c>.</summary>
let scrub (read: unit -> float) (target: ComposedTimeline) =
    Api.createEffectOn read (fun progress -> target.Seek(max 0. (min 1. progress) * target.Duration) |> ignore)

let private elementsOf (found: NodeList) =
    [| for index in 0 .. int found.length - 1 -> unbox<Element> (found.item (uint index)) |]

/// <summary>The elements under <c>root</c> matching <c>selector</c>, in document order.</summary>
let queryAll (root: Element) (selector: string) : Element array = elementsOf (root.querySelectorAll selector)

/// <summary>The first element under <c>root</c> matching <c>selector</c>.</summary>
let query (root: Element) (selector: string) : Element =
    match queryAll root selector with
    | [||] -> invalidOp ("No element matches " + selector)
    | found -> found[0]

/// <summary>The rendered width of the element, in CSS pixels; 0 before it is placed.</summary>
let widthOf (element: obj) : float =
    match element with
    | :? Element as placed -> placed.getBoundingClientRect().width
    | _ -> 0.

/// <summary>The value of the input an event fired on.</summary>
let targetValue (event: Event) : string = (unbox<HTMLInputElement> event.target).value

/// <summary>Whether the checkbox an event fired on is checked.</summary>
let targetChecked (event: Event) : bool = (unbox<HTMLInputElement> event.target).``checked``

/// <summary>The next state of the Park–Miller generator, in 1..2147483646 for a seed in that range.</summary>
let nextSeed (seed: int) = int ((float seed * 16807.) % 2147483647.)

/// <summary>A number in 0..1 drawn from <c>seed</c>.</summary>
let unitOf (seed: int) = float (seed % 10000) / 10000.

/// <summary>Callbacks due on the window loop: <c>setTimeout</c> and <c>setInterval</c> for the native pages.</summary>
type Timers() =
    let watch = Stopwatch.StartNew()
    let entries = ResizeArray<TimerEntry>()

    /// <summary>Milliseconds since the timers were created.</summary>
    member _.Now = float watch.Elapsed.TotalMilliseconds

    /// <summary>Calls <c>callback</c> once, <c>delay</c> milliseconds from now, unless the handle is disposed first.</summary>
    member this.After(delay: float, callback: unit -> unit) : IDisposable = this.Add(delay, 0., callback)

    /// <summary>Calls <c>callback</c> every <c>period</c> milliseconds until the handle is disposed.</summary>
    member this.Every(period: float, callback: unit -> unit) : IDisposable = this.Add(period, period, callback)

    member private this.Add(delay, period, callback) =
        let entry = { Due = this.Now + delay; Period = period; Callback = callback; Live = true }
        entries.Add entry

        { new IDisposable with
            member _.Dispose() =
                entry.Live <- false
                entries.Remove entry |> ignore }

    /// <summary>Runs every callback now due; the window loop calls it each turn.</summary>
    member this.Run() =
        let now = this.Now

        for entry in entries.ToArray() do
            if entry.Live && entry.Due <= now then
                if entry.Period > 0. then
                    entry.Due <- now + entry.Period
                else
                    entry.Live <- false
                    entries.Remove entry |> ignore

                entry.Callback()

    /// <summary>Milliseconds until the next callback is due, if any.</summary>
    member this.NextDelay =
        if entries.Count = 0 then None
        else Some(max 0. ((entries |> Seq.map (fun entry -> entry.Due) |> Seq.min) - this.Now))

and [<NoEquality; NoComparison>] private TimerEntry =
    { mutable Due: float
      Period: float
      Callback: unit -> unit
      mutable Live: bool }

/// <summary>The timers of the app's window loop.</summary>
let timers = Timers()

/// <summary>The window's document, set at startup; selectors such as a draggable's container resolve against it.</summary>
let document: Document ref = ref Unchecked.defaultof<Document>

/// <summary>The first element of the document matching <c>selector</c>, as an <c>HTMLElement</c>.</summary>
let select (selector: string) : HTMLElement option =
    match box (document.Value.querySelector selector) with
    | :? HTMLElement as found -> Some found
    | _ -> None
