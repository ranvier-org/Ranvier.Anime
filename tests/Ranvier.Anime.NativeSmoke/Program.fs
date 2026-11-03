module Ranvier.Anime.NativeSmoke.Program

open System
open System.Diagnostics
open System.Globalization
open Ranvier
open Ranvier.Anime
open Ranvier.Css
open Ranvier.Dom
open Ranvier.View.WebCore

let private require name condition =
    if not condition then failwith name
    Console.WriteLine("PASS " + name)

let private pumpUntil (host: WebCoreHost) condition =
    let watch = Stopwatch.StartNew()
    while not(condition()) && watch.ElapsedMilliseconds < 3000L do
        host.Cycle(milliseconds = 1)
        host.Frame()
    require "native frame loop reached its target" (condition())

[<EntryPoint>]
let main args =
    try
        if Array.contains "--require-aot" args then
            require "consumer is NativeAOT" (not System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported)
#if EXPECT_TRACED
        let expectedTrace = true
#else
        let expectedTrace = false
#endif
        for assembly in [typeof<Graph>.Assembly; typeof<Document>.Assembly; typeof<WebCoreHost>.Assembly] do
            let traced =
                System.Reflection.CustomAttributeExtensions.GetCustomAttributes<System.Reflection.AssemblyMetadataAttribute>(assembly)
                |> Seq.exists(fun attribute -> attribute.Key = "RanvierTrace" && attribute.Value = "true")
            require (assembly.GetName().Name + " trace cohort") (traced = expectedTrace)
        use host = WebCoreHost.Create(320, 200)
        use scheduler = Frames.forHost host
        let element = host.Document.createElement "div" :?> HTMLElement
        host.Document.body.appendChild element |> ignore
        let style = (element :> ElementCSSInlineStyle).style
        style.setProperty("--anime-gap", "0px", priority = "important")
        style.setProperty("width", "var(--anime-gap)")
        style.setProperty("height", "20px")
        style.setProperty("background-color", "red")
        use tween = CssAnimation.customProperty scheduler style (RuntimeApi.createCustomProperty "--anime-gap") (Ranvier.Css.Css.px 0.) (Ranvier.Css.Css.px 100.) 100.
        tween.Pause()
        tween.Seek 25.
        require "typed CSS seek writes native style" (style.getPropertyValue "--anime-gap" = "25px")
        require "typed CSS retains priority" (style.getPropertyPriority "--anime-gap" = "important")
        tween.Play()
        pumpUntil host (fun () -> style.getPropertyValue "--anime-gap" = "100px")
        require "native layout consumes animated custom property" (abs(element.getBoundingClientRect().width - 100.) < 0.01)
        style.setProperty("--anime-repeat-gap", "7px", priority = "important")
        use repeatedCss =
            CssAnimation.customPropertyWithOptions
                { PlaybackOptions.Default with Repeat = PlaybackRepeat.Count 1u; Alternate = true }
                scheduler style (RuntimeApi.createCustomProperty "--anime-repeat-gap")
                (Ranvier.Css.Css.px 0.) (Ranvier.Css.Css.px 100.) 25.
        pumpUntil host (fun () -> repeatedCss.Completed)
        require "native typed CSS repeats and alternates through the shared clock"
            (repeatedCss.CurrentIteration = 1 && style.getPropertyValue "--anime-repeat-gap" = "0px" &&
             style.getPropertyPriority "--anime-repeat-gap" = "important")
        style.setProperty("--anime-timeline-gap", "0px", priority = "important")
        style.setProperty("height", "var(--anime-timeline-gap)")
        let mutable timelineValue = 0.
        let mutable timelineLoops = 0
        let timelineProperty = NumericProperty((fun () -> timelineValue), (fun value ->
            timelineValue <- value
            style.setProperty("--anime-timeline-gap", value.ToString("G17", CultureInfo.InvariantCulture) + "px", priority = "important")))
        use repeatedTimeline = new Timeline(scheduler,
            options = { PlaybackOptions.Default with Repeat = PlaybackRepeat.Count 1u; Alternate = true },
            onLoop = (fun _ -> timelineLoops <- timelineLoops + 1))
        repeatedTimeline.Animate(timelineProperty, 100., 25u) |> ignore
        repeatedTimeline.Reset()
        repeatedTimeline.Resume()
        pumpUntil host (fun () -> repeatedTimeline.Completed)
        require "native timeline repeats and alternates through the shared clock"
            (timelineValue = 0. && timelineLoops = 1 && repeatedTimeline.CurrentIteration = 1 && not repeatedTimeline.Playing &&
             style.getPropertyValue "--anime-timeline-gap" = "0px" && style.getPropertyPriority "--anime-timeline-gap" = "important")
        require "native layout consumes timeline output" (abs(element.getBoundingClientRect().height) < 0.01)
        use graph = new Graph()
        use activation = graph.Activate()
        let mutable writes = 0
        let owner, signal = Api.createRoot(fun owner ->
            let signal = Api.createSignal 0.
            Reactive.drive scheduler 50. (fun () -> signal.Value) (fun value ->
                writes <- writes + 1
                style.setProperty("opacity", value.ToString("G17", CultureInfo.InvariantCulture))) |> ignore
            owner, signal)
        graph.Flush()
        signal.Value <- 1.
        graph.Flush()
        pumpUntil host (fun () -> style.getPropertyValue "opacity" = "1")
        owner.Dispose()
        let priorWrites = writes
        signal.Value <- 0.
        graph.Flush()
        host.Cycle()
        host.Frame()
        require "Ranvier owner disposal stops native writes" (writes = priorWrites)
        let mutable timerLoops = 0
        let mutable timerCompletions = 0
        use repeatedTimer = new Timer(scheduler, 25u,
            options = { PlaybackOptions.Default with Repeat = PlaybackRepeat.Count 1u; Alternate = true },
            onLoop = (fun _ -> timerLoops <- timerLoops + 1),
            onComplete = (fun _ -> timerCompletions <- timerCompletions + 1))
        repeatedTimer.Resume()
        pumpUntil host (fun () -> repeatedTimer.Completed)
        require "native timer repeats and completes once" (timerLoops = 1 && timerCompletions = 1 && repeatedTimer.Paused)
        let mutable timerBeforeUpdates = 0
        let mutable timerUpdates = 0
        let timerOwner, ownedTimer = Api.createRoot(fun owner ->
            let timer = new Timer(scheduler, 1000u,
                onBeforeUpdate = (fun _ -> timerBeforeUpdates <- timerBeforeUpdates + 1; owner.Dispose()),
                onUpdate = (fun _ -> timerUpdates <- timerUpdates + 1))
            Timer.own timer |> ignore
            owner, timer)
        use timer = ownedTimer
        timer.Resume()
        pumpUntil host (fun () -> timerBeforeUpdates > 0)
        host.Cycle(milliseconds = 10)
        host.Frame()
        host.Cycle(milliseconds = 10)
        host.Frame()
        require "native timer owner disposal interrupts active callbacks"
            (timer.Cancelled && not timer.Completed && timerBeforeUpdates = 1 && timerUpdates = 0)
        timerOwner.Dispose()
        let mutable timelineBeforeUpdates = 0
        let mutable timelineUpdates = 0
        let mutable timelineWrites = 0
        let timelineOwner, ownedTimeline = Api.createRoot(fun owner ->
            let timeline = new Timeline(scheduler,
                onBeforeUpdate = (fun _ -> timelineBeforeUpdates <- timelineBeforeUpdates + 1; owner.Dispose()),
                onUpdate = (fun _ -> timelineUpdates <- timelineUpdates + 1))
            timeline.Animate(NumericProperty((fun () -> 0.), (fun _ -> timelineWrites <- timelineWrites + 1)), 100., 1000u) |> ignore
            Timeline.own timeline |> ignore
            owner, timeline)
        use timeline = ownedTimeline
        timeline.Reset()
        timeline.Resume()
        pumpUntil host (fun () -> timelineBeforeUpdates > 0)
        host.Cycle(milliseconds = 10)
        host.Frame()
        host.Cycle(milliseconds = 10)
        host.Frame()
        require "native timeline owner disposal interrupts active property writes"
            (timeline.Cancelled && not timeline.Completed && timelineBeforeUpdates = 1 && timelineUpdates = 0 && timelineWrites = 1)
        timelineOwner.Dispose()
        require "native host has no listener errors" (host.Errors.Count = 0)
        Console.WriteLine "NATIVE_ANIME_SMOKE_PASS"
        0
    with error ->
        Console.Error.WriteLine("NATIVE_ANIME_SMOKE_FAIL " + error.GetType().FullName + ": " + error.Message)
        1
