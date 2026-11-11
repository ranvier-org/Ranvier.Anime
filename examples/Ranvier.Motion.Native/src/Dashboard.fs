/// <summary>
/// Live metrics from a core stream: a skeleton while the stream is pending, gauges that follow each sample, a drawn ring,
/// a morphing line chart, a tweened request rate, and panels that fade in and out.
/// </summary>
module Dashboard

open System
open global.Ranvier
open Ranvier.Anime
open Ranvier.Dom
open Ranvier.View
open Ranvier.View.Sugar
open Ranvier.View.Anime

/// <summary>One reading of the service; loads are in 0..1.</summary>
type Sample =
    {
        Tick: int
        Cpu: float
        Memory: float
        Latency: float
        Requests: float
    }

/// <summary>The samples the chart and the details hold.</summary>
let capacity = 24

let private clamp (value: float) =
    max 0.02 (min 0.98 value)

let private noise (tick: int) (channel: int) =
    Native.unitOf (Native.nextSeed (tick * 7919 + channel * 104729 + 1))

/// <summary>The sample at <c>tick</c>: smooth waves with seeded noise, the same on every run.</summary>
let sampleAt (tick: int) : Sample =
    let t = float tick

    {
        Tick = tick
        Cpu =
            clamp (
                0.46
                + 0.24 * sin (t / 3.)
                + 0.16 * (noise tick 1 - 0.5)
            )
        Memory =
            clamp (
                0.58
                + 0.12 * sin (t / 7.)
                + 0.06 * (noise tick 2 - 0.5)
            )
        Latency =
            clamp (
                0.32
                + 0.22 * abs (sin (t / 5.))
                + 0.1 * (noise tick 3 - 0.5)
            )
        Requests =
            Math.Round (
                1240.
                + 420. * sin (t / 4.)
                + 160. * (noise tick 4 - 0.5)
            )
    }

/// <summary>A sample at <c>tick</c> with the CPU pinned near full load.</summary>
let spikeAt (tick: int) : Sample =
    { sampleAt tick with Cpu = 0.96 }

/// <summary>The latest samples, oldest first, at most <c>capacity</c>.</summary>
let keep (samples: Sample array) (sample: Sample) =
    let all = Array.append samples [| sample |]

    if all.Length > capacity then
        all[all.Length - capacity ..]
    else
        all

/// <summary>A hot source of samples: each subscriber receives the samples pushed while it is subscribed.</summary>
type Feed() =
    let observers = ResizeArray<IObserver<Sample>>()

    member _.Push(sample: Sample) =
        for observer in observers.ToArray () do
            observer.OnNext sample

    member _.Subscribers = observers.Count

    interface IObservable<Sample> with
        member _.Subscribe(observer: IObserver<Sample>) =
            observers.Add observer

            { new IDisposable with
                member _.Dispose() =
                    observers.Remove observer |> ignore
            }

/// <summary>The dashboard's state outside its view: the feed, the stream folded from it, and the controls.</summary>
[<NoEquality; NoComparison>]
type Store =
    {
        Feed: Feed
        /// <summary>Read by the stream's start; a write restarts the stream.</summary>
        Session: Signal<int>
        Samples: StreamMemo<Sample array>
        Running: Signal<bool>
        Details: Signal<bool>
    }

/// <summary>A store over a new feed, in the current graph's scope.</summary>
let createStore () : Store =
    let feed = Feed ()
    let session = Api.createSignal 0
    let mutable history = [||]

    let append _ sample =
        history <- keep history sample
        history

    {
        Feed = feed
        Session = session
        Samples =
            Api.createStreamFold [||] append (fun _ ->
                session.Value |> ignore
                StreamSource.ofObservable feed)
        Running = Api.createSignal true
        Details = Api.createSignal false
    }

/// <summary>The path through <c>values</c> in a 600 × 200 box: always <c>capacity</c> points, padded with the first value.</summary>
let linePath (values: float array) =
    let padded =
        if values.Length = 0 then
            Array.create capacity 0.5
        else
            Array.append (Array.create (capacity - values.Length) values[0]) values

    padded
    |> Array.mapi (fun index value ->
        let x = float index * 600. / float (capacity - 1)
        let y = 186. - value * 168.
        sprintf "%s%.1f %.1f" (if index = 0 then "M" else "L") x y)
    |> String.concat " "

/// <summary>The line's path closed along the bottom of the box.</summary>
let areaPath (values: float array) =
    linePath values + " L600 200 L0 200 Z"

let private streamingPaths (previous: float array) (current: float array) (progress: float) =
    if progress >= 1. then
        linePath current, areaPath current
    else
        let padded =
            Array.append (Array.create (capacity - previous.Length) previous[0]) previous

        let blend = min 1. (max 0. (progress / 0.4))
        let grow = (1. - cos (Math.PI * blend)) / 2.
        let step = 600. / float (capacity - 1)
        let offset = step * progress

        let y value =
            186. - value * 168.

        let old =
            padded
            |> Array.mapi (fun index value -> float index * step - offset, y value)

        let last = Array.last padded
        let next = Array.last current
        let tip = 600., y (last + (next - last) * grow)

        let line =
            Array.append old [| tip |]
            |> Array.mapi (fun index (x, y) -> sprintf "%s%.1f %.1f" (if index = 0 then "M" else "L") x y)
            |> String.concat " "

        line,
        line
        + sprintf " L%.1f 200 L%.1f 200 Z" (fst tip) (fst old[0])

let private trackChart (cpu: Memo<float array>) =
    let mutable previous = [||]
    let finished () = 1.
    let frame = Api.createSignal (previous, previous, finished)
    let mutable first = true

    Api.createEffectOn (fun () -> cpu.Value) (fun current ->
        if first then
            first <- false
            frame.Value <- current, current, finished
        else
            let progress = Native.clock 1000.
            frame.Value <- previous, current, (fun () -> progress.Value)

        previous <- current)

    Api.createMemo (fun _ ->
        let previous, current, progress = frame.Value
        streamingPaths previous current (progress ()))

/// <summary>A count-up from <c>From</c> to <c>To</c>, at the progress of the animation that drives it.</summary>
[<NoEquality; NoComparison>]
type Tween =
    {
        From: float
        To: float
        Progress: unit -> float
    }

/// <summary>The quartic ease-out the counter applies to its tween's linear progress.</summary>
let easeOut (progress: float) =
    1. - (1. - progress) ** 4.

/// <summary>The revealed content's instance.</summary>
[<NoEquality; NoComparison>]
type Panel =
    {
        Status: BoundaryStatus
        Store: Store
        Latest: Memo<Sample>
        Cpu: Memo<float[]>
        Chart: Memo<string * string>
        Tween: Signal<Tween>
    }

    member panel.Shown =
        let tween = panel.Tween.Value

        tween.From
        + (tween.To - tween.From)
          * easeOut (tween.Progress ())

let private percent (value: float) =
    sprintf "%.0f%%" (value * 100.)

let private pulse (delay: int) (_: unit) (element: Element) =
    Native.animate element (Native.timing 820. Native.inOutSine |> Native.alternating |> Native.delayed (float delay)) [| Native.fromTo "opacity" 0.35 1. |]

let private skeleton =
    Component.template
        (fun _ () -> ())
        (div (Attrs.className "skeleton", Attrs.aria ("busy", "true")) {
            for index in 0..2 do
                div (Attrs.className "skeleton-tile", Motion.on (pulse (index * 140))) {
                    div (Attrs.className "skeleton-line short") { () }
                    div (Attrs.className "skeleton-line") { () }
                }

            div (Attrs.className "skeleton-tile wide", Motion.on (pulse 420)) { div (Attrs.className "skeleton-chart") { () } }
        })

let private failed =
    Component.template
        (fun _ (failure: BoundaryFailure) -> failure)
        (div (Attrs.className "failed") {
            p () {
                fun (failure: BoundaryFailure) ->
                    "The metrics stream failed: "
                    + failure.Error.Message
            }

            button (Events.onClick (fun (failure: BoundaryFailure) _ -> failure.Retry ())) { "Retry" }
        })

let private gauge (label: string) (read: Sample -> float) =
    div (Attrs.className "gauge") {
        div (Attrs.className "gauge-head") {
            span () { label }
            strong () { fun (panel: Panel) -> percent (read panel.Latest.Value) }
        }

        div (Attrs.className "gauge-track") {
            div (
                Attrs.className "gauge-fill",
                Motion.follow "scaleX" { ComposedAnimationOptions.Default with Duration = 700.; Ease = Native.outQuart } (fun (panel: Panel) -> read panel.Latest.Value)
            ) {
                ()
            }
        }
    }

let private content (store: Store) =
    Component.template
        (fun _ (status: BoundaryStatus) ->
            let latest = Api.createMemo (fun _ -> Array.last store.Samples.Value)

            let tween =
                Api.createSignal
                    {
                        From = 0.
                        To = 0.
                        Progress = fun () -> 1.
                    }

            let cpu =
                Api.createMemo (fun _ ->
                    store.Samples.Value
                    |> Array.map (fun sample -> sample.Cpu))

            let panel =
                {
                    Status = status
                    Store = store
                    Latest = latest
                    Cpu = cpu
                    Chart = trackChart cpu
                    Tween = tween
                }

            Api.createEffectOn (fun () -> latest.Value.Requests) (fun target ->
                let from = Api.untrack (fun () -> panel.Shown)
                let progress = Native.clock 900.

                tween.Value <-
                    {
                        From = from
                        To = target
                        Progress = fun () -> progress.Value
                    })

            panel)
        (div (Attrs.className "metrics", Attrs.data ("refreshing", fun (panel: Panel) -> if panel.Status.IsPending () then Some "true" else None)) {
            article (Attrs.className "tile requests") {
                h3 () { "Requests / s" }
                strong (Attrs.className "counter") { fun (panel: Panel) -> sprintf "%.0f" panel.Shown }
                span (Attrs.className "note") { fun (panel: Panel) -> sprintf "sample %d" panel.Latest.Value.Tick }
            }

            article (Attrs.className "tile ring") {
                svg (Attrs.className "ring-svg", Attrs.attribute ("viewBox", "0 0 100 100")) {
                    circle (Attrs.className "ring-track", Attrs.attribute ("cx", "50"), Attrs.attribute ("cy", "50"), Attrs.attribute ("r", "42")) {
                        ()
                    }

                    circle (
                        Attrs.className "ring-value",
                        Attrs.attribute ("cx", "50"),
                        Attrs.attribute ("cy", "50"),
                        Attrs.attribute ("r", "42"),
                        Motion.draw { ComposedAnimationOptions.Default with Duration = 700.; Ease = Native.outQuart } (fun (panel: Panel) -> panel.Latest.Value.Cpu)
                    ) {
                        ()
                    }
                }

                div (Attrs.className "ring-label") {
                    strong () { fun (panel: Panel) -> percent panel.Latest.Value.Cpu }
                    span () { "CPU" }
                }
            }

            article (Attrs.className "tile gauges") {
                gauge "CPU" (fun sample -> sample.Cpu)
                gauge "Memory" (fun sample -> sample.Memory)
                gauge "Latency" (fun sample -> sample.Latency)
            }

            article (Attrs.className "tile chart") {
                div (Attrs.className "chart-head") {
                    h3 () { "CPU, last 24 samples" }
                    span (Attrs.className "note") { fun (panel: Panel) -> sprintf "%d of %d" panel.Cpu.Value.Length capacity }
                }

                svg (Attrs.className "chart-svg", Attrs.attribute ("viewBox", "0 0 600 200"), Attrs.attribute ("preserveAspectRatio", "none")) {
                    defs () {
                        linearGradient (
                            Attrs.attribute ("id", "cpu-fill"),
                            Attrs.attribute ("x1", "0"),
                            Attrs.attribute ("y1", "0"),
                            Attrs.attribute ("x2", "0"),
                            Attrs.attribute ("y2", "1")
                        ) {
                            stop (Attrs.attribute ("offset", "0%"), Attrs.className "fill-top") { () }
                            stop (Attrs.attribute ("offset", "100%"), Attrs.className "fill-bottom") { () }
                        }
                    }

                    path (Attrs.className "chart-area", Attrs.attribute ("d", fun (panel: Panel) -> Some (snd panel.Chart.Value))) { () }

                    path (Attrs.className "chart-line", Attrs.attribute ("d", fun (panel: Panel) -> Some (fst panel.Chart.Value))) { () }
                }
            }

            show
                (fun (panel: Panel) -> panel.Latest.Value.Cpu > 0.85)
                (div (Attrs.className "alert", Attrs.role "status", Motion.fade) {
                    strong () { "CPU above 85%" }
                    span () { fun (panel: Panel) -> sprintf " at sample %d" panel.Latest.Value.Tick }
                })

            show
                (fun (panel: Panel) -> panel.Store.Details.Value)
                (aside (Attrs.className "details", Motion.fade) {
                    h3 () { "Latest sample" }

                    dl () {
                        dt () { "Tick" }
                        dd () { fun (panel: Panel) -> string panel.Latest.Value.Tick }
                        dt () { "CPU" }
                        dd () { fun (panel: Panel) -> percent panel.Latest.Value.Cpu }
                        dt () { "Memory" }
                        dd () { fun (panel: Panel) -> percent panel.Latest.Value.Memory }
                        dt () { "Latency" }
                        dd () { fun (panel: Panel) -> sprintf "%.0f ms" (panel.Latest.Value.Latency * 400.) }
                        dt () { "Requests" }
                        dd () { fun (panel: Panel) -> sprintf "%.0f / s" panel.Latest.Value.Requests }
                    }
                })
        })

/// <summary>The dashboard's component over <c>store</c>; build it once per store.</summary>
let view (store: Store) : Component<unit> =
    let revealed = content store

    Component.template
        (fun _ () -> store)
        (section (Attrs.className "dashboard") {
            header (Attrs.className "toolbar") {
                button (Attrs.className "primary pause", Events.onClick (fun (s: Store) _ -> s.Running.Value <- not s.Running.Value)) {
                    fun (s: Store) -> if s.Running.Value then "Pause" else "Resume"
                }

                button (Attrs.className "reconnect", Events.onClick (fun (s: Store) _ -> s.Session.Value <- s.Session.Value + 1)) { "Reconnect" }

                button (Attrs.className "details-toggle", Events.onClick (fun (s: Store) _ -> s.Details.Value <- not s.Details.Value)) {
                    fun (s: Store) -> if s.Details.Value then "Hide details" else "Show details"
                }

                span (Attrs.className "meta") { fun (s: Store) -> if s.Running.Value then "live · 1 sample / s" else "paused" }
            }

            div (Attrs.className "stage") { boundary skeleton failed revealed }
        })

/// <summary>Pushes the sample <c>sample</c> builds for the next tick into the store's feed.</summary>
type Pump(graph: Graph, store: Store) =
    let mutable tick = 0

    /// <summary>Pushes the next tick's sample.</summary>
    member _.Push(sample: int -> Sample) =
        tick <- tick + 1
        graph.Run (fun () -> store.Feed.Push (sample tick))

/// <summary>Starts the sample feed on the window loop: a first sample after 600 ms, then one a second while running.</summary>
let activate (graph: Graph) (store: Store) : Pump * System.IDisposable =
    let pump = Pump (graph, store)

    let next () =
        if graph.Run (fun () -> Api.untrack (fun () -> store.Running.Value)) then
            pump.Push sampleAt

    let mutable timer: IDisposable = null

    let first =
        Native.timers.After (
            600.,
            fun () ->
                next ()
                timer <- Native.timers.Every (1000., next)
        )

    pump,
    { new IDisposable with
        member _.Dispose() =
            first.Dispose ()

            if not (isNull timer) then
                timer.Dispose () }
