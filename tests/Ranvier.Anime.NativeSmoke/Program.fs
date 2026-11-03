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
        let timelineTimer = repeatedTimeline.AddTimer(25u, TimelinePosition.At 0.)
        repeatedTimeline.Reset()
        repeatedTimeline.Resume()
        pumpUntil host (fun () -> repeatedTimeline.Completed)
        require "native timeline repeats and alternates through the shared clock"
            (timelineValue = 0. && timelineLoops = 1 && repeatedTimeline.CurrentIteration = 1 && not repeatedTimeline.Playing &&
             style.getPropertyValue "--anime-timeline-gap" = "0px" && style.getPropertyPriority "--anime-timeline-gap" = "important")
        require "native layout consumes timeline output" (abs(element.getBoundingClientRect().height) < 0.01)
        require "native timeline samples an owned timer track"
            (timelineTimer.CurrentTime = 0. && timelineTimer.Duration = 25. && not timelineTimer.Cancelled)
        use borrowedTimer = new Timer(scheduler, 25u,
            options = { PlaybackOptions.Default with Repeat = PlaybackRepeat.Count 1u })
        use borrowedTimeline = new Timeline(scheduler,
            options = { PlaybackOptions.Default with Repeat = PlaybackRepeat.Count 1u; Alternate = true })
        borrowedTimeline.Sync(borrowedTimer, TimelinePosition.At 0.) |> ignore
        borrowedTimeline.Reset()
        borrowedTimeline.Resume()
        pumpUntil host (fun () -> borrowedTimeline.Completed)
        borrowedTimeline.Cancel()
        require "native timeline borrows repeated timer playback without disposing it"
            (borrowedTimer.CurrentTime = 0. && not borrowedTimer.Cancelled)
        use graph = new Graph()
        let mutable curveValue = 0.
        let writeCurve value =
            curveValue <- value
            style.setProperty("--anime-gap", value.ToString("G17", CultureInfo.InvariantCulture) + "px", priority = "important")
        let piecewise = Ease.linear [|LinearStop.Value 0.; LinearStop.At(1.5, 0.5); LinearStop.Value 1.|]
        use curveTween = new NumericAnimation(scheduler, (fun () -> curveValue), writeCurve, 100., 100., ease = piecewise)
        curveTween.Seek 50.
        require "native piecewise easing retains overshoot and layout"
            (style.getPropertyValue "--anime-gap" = "150px" && abs(element.getBoundingClientRect().width - 150.) < 0.01)
        curveTween.Resume()
        pumpUntil host (fun () -> curveTween.Completed)
        require "native piecewise easing finishes at its final stop" (curveValue = 100.)
        let mutable randomCalls = 0
        let irregular = Ease.irregularWith (fun () -> randomCalls <- randomCalls + 1; 0.5) 3u 1.
        use irregularTween = new NumericAnimation(scheduler, (fun () -> 0.), writeCurve, 100., 100., ease = irregular)
        irregularTween.Seek 50.
        require "native irregular easing retains construction draws"
            (curveValue = 75. && style.getPropertyValue "--anime-gap" = "75px" && randomCalls = 1)
        use activation = graph.Activate()
        use declarationWidth = CssAnimation.declaration scheduler style
                                    (Ranvier.Css.Css.width(Ranvier.Css.Css.px 0.))
                                    (Ranvier.Css.Css.width(Ranvier.Css.Css.px 120.)) 100u
        declarationWidth.Seek 50.
        require "native typed declaration drives layout"
            (abs(element.getBoundingClientRect().width - 60.) < 0.01)
        declarationWidth.Cancel()
        use declarationColor = CssAnimation.declaration scheduler style
                                    (Declaration.create "background-color" "#000")
                                    (Declaration.create "background-color" "#fff") 100u
        declarationColor.Seek 50.
        require "native color declaration uses source gamma-like interpolation"
            (style.getPropertyValue "background-color" = "rgb(180, 180, 180)")
        declarationColor.Cancel()
        let beforeTransform = element.getBoundingClientRect().left
        use declarationTransform = CssAnimation.declaration scheduler style
                                        (Declaration.create "transform" "translate(0px,0px)")
                                        (Declaration.create "transform" "translate(100px,50px)") 100u
        declarationTransform.Seek 50.
        require "native compound declaration changes transformed geometry"
            (abs(element.getBoundingClientRect().left - beforeTransform - 50.) < 0.01)
        declarationTransform.Cancel()
        style.removeProperty "transform" |> ignore
        let mutable modifierArmed = false
        let mutable modifierCalls = 0
        let mutable declarationUpdates = 0
        let declarationOwner, ownedDeclaration = Api.createRoot(fun owner ->
            let modify value =
                if modifierArmed then
                    modifierCalls <- modifierCalls + 1
                    owner.Dispose()
                value
            let settings =
                { DeclarationAnimationOptions.Default with
                    Modifier = modify
                    OnUpdate = (fun _ -> declarationUpdates <- declarationUpdates + 1) }
            let clock = CssAnimation.declarationWith settings scheduler style
                            (Declaration.create "transform" "translate(0px,0px)")
                            (Declaration.create "transform" "translate(100px,50px)") 1000u |> Timer.own
            owner, clock)
        use ownedDeclaration = ownedDeclaration
        let retainedTransform = style.getPropertyValue "transform"
        modifierArmed <- true
        pumpUntil host (fun () -> ownedDeclaration.Cancelled)
        require "native declaration owner cancellation interrupts component modifiers"
            (modifierCalls = 1 && declarationUpdates = 0 && style.getPropertyValue "transform" = retainedTransform)
        declarationOwner.Dispose()
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
        let timelineOwner, ownedTimeline, ownedTimelineTimer = Api.createRoot(fun owner ->
            let timeline = new Timeline(scheduler,
                onBeforeUpdate = (fun _ -> timelineBeforeUpdates <- timelineBeforeUpdates + 1; owner.Dispose()),
                onUpdate = (fun _ -> timelineUpdates <- timelineUpdates + 1))
            timeline.Animate(NumericProperty((fun () -> 0.), (fun _ -> timelineWrites <- timelineWrites + 1)), 100., 1000u) |> ignore
            let child = timeline.AddTimer(1000u, TimelinePosition.At 0.)
            Timeline.own timeline |> ignore
            owner, timeline, child)
        use timeline = ownedTimeline
        timeline.Reset()
        timeline.Resume()
        pumpUntil host (fun () -> timelineBeforeUpdates > 0)
        host.Cycle(milliseconds = 10)
        host.Frame()
        host.Cycle(milliseconds = 10)
        host.Frame()
        require "native timeline owner disposal interrupts active property writes"
            (timeline.Cancelled && ownedTimelineTimer.Cancelled && not timeline.Completed &&
             timelineBeforeUpdates = 1 && timelineUpdates = 0 && timelineWrites = 1)
        timelineOwner.Dispose()
        require "native host has no listener errors" (host.Errors.Count = 0)
        Console.WriteLine "NATIVE_ANIME_SMOKE_PASS"
        0
    with error ->
        Console.Error.WriteLine("NATIVE_ANIME_SMOKE_FAIL " + error.GetType().FullName + ": " + error.Message)
        1
