module Ranvier.Anime.NativeSmoke.Program

open System
open System.Diagnostics
open System.Globalization
open Ranvier
open Ranvier.Anime
open Ranvier.Css
open Ranvier.Css.Dom
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
        let cache = AnimatableValue(ValueInterpolation.Number 10., None, false, None)
        let live () = true
        let double value = value*2.
        cache.Sample(0.,4,double,live) |> ignore
        require "animatable kernel getter applies its modifier to the cache" (cache.ScalarValue(double,live)=Some 40.)
        require "animatable kernel retarget remains current" (cache.RetargetScalar(100.,double,live))
        cache.Sample(0.,4,double,live) |> ignore
        let scalarOutput=cache.Sample(0.37,4,double,live) |> Option.get
        require "animatable kernel preserves repeated scalar modifier behavior"
            (scalarOutput.Scalar |> Option.exists(fun value -> abs(value-124.4)<1e-10))
        let compound=AnimatableValue(ValueInterpolation.Text "translate(10px,20px)",None,false,None)
        compound.Sample(0.,4,id,live) |> ignore
        compound.RetargetComponents([|110.;220.|],id,live) |> ignore
        compound.Sample(0.37,4,id,live) |> ignore
        compound.RetargetComponents([|210.|],id,live) |> ignore
        let compoundOutput=compound.Sample(0.37,4,id,live) |> Option.get
        let untransformed=element.getBoundingClientRect()
        StyleDeclaration.apply style (Declaration.create "transform" compoundOutput.Text)
        let transformed=element.getBoundingClientRect()
        require "animatable kernel partial components drive native geometry"
            (compoundOutput.Text="translate(107.31px,94px)" &&
             abs(transformed.left-untransformed.left-107.31)<0.02 && abs(transformed.top-untransformed.top-94.)<0.02)
        let components=compound.ComponentValues
        components[0]<-999.
        require "animatable kernel component snapshots preserve its cache" (compound.ComponentValues[0]=107.31)
        style.removeProperty "transform" |> ignore
        let retargetWrites=ref 0
        use retargetTrack=new AnimatableTrack(scheduler,[|AnimatableValue(ValueInterpolation.Text "0px",None,true,None)|],
            (fun _ output ->
                retargetWrites.Value<-retargetWrites.Value+1
                StyleDeclaration.apply style (Declaration.create "width" output.Text |> Declaration.important)),
            100.,PlaybackOptions.Default,id,id,4)
        retargetTrack.Initialize()
        let beforeRetarget=retargetWrites.Value
        retargetTrack.RetargetScalar 100.
        require "native animatable retarget updates cache without a target write"
            (retargetWrites.Value=beforeRetarget && style.getPropertyValue "width"="0px")
        pumpUntil host (fun () -> retargetTrack.Clock.Completed)
        retargetTrack.RetargetScalar(200.,newDuration=200.,newEase=Ease.named "inQuad")
        retargetTrack.Clock.Seek 100.
        require "native animatable retarget retains typed units and uses changed easing and timing"
            (style.getPropertyValue "width"="125px" && style.getPropertyPriority "width"="important" &&
             abs(element.getBoundingClientRect().width-125.)<0.01)
        retargetTrack.Cancel()
        do
            use retargetGraph=new Graph()
            use retargetActivation=retargetGraph.Activate()
            let mutable armed=false
            let mutable modifierCalls=0
            let mutable ownedWrites=0
            let owner,owned=Api.createRoot(fun owner ->
                let modifier value =
                    if armed then
                        modifierCalls<-modifierCalls+1
                        if modifierCalls=2 then owner.Dispose()
                    value
                let track=new AnimatableTrack(scheduler,[|AnimatableValue(ValueInterpolation.Number 10.,None,false,None)|],
                    (fun _ _ -> ownedWrites<-ownedWrites+1),100.,PlaybackOptions.Default,id,modifier,4)
                Timer.own track.Clock |> ignore
                track.Initialize()
                owner,track)
            use owned=owned
            armed<-true
            owned.RetargetScalar 100.
            require "native animatable owner disposal inside soft reset prevents dispatch"
                (owned.Clock.Cancelled && modifierCalls=2 && ownedWrites=1 && not owned.Clock.InternalRender)
            owner.Dispose()
        do
            let mutable groupBegins=0
            let mutable groupCompletions=0
            use group=new AnimatableGroup(scheduler,onBegin=(fun _ -> groupBegins<-groupBegins+1),
                                          onComplete=(fun _ -> groupCompletions<-groupCompletions+1))
            let width=group.Add([|AnimatableValue(ValueInterpolation.Number 0.,None,false,None)|],
                (fun _ output -> StyleDeclaration.apply style (Ranvier.Css.Css.width(Ranvier.Css.Css.px(Option.get output.Scalar)) |> Declaration.important)),
                100.,PlaybackOptions.Default,id,id,4)
            let height=group.Add([|AnimatableValue(ValueInterpolation.Number 0.,None,false,None)|],
                (fun _ output -> StyleDeclaration.apply style (Ranvier.Css.Css.height(Ranvier.Css.Css.px(Option.get output.Scalar)))),
                200.,PlaybackOptions.Default,id,id,4)
            width.RetargetScalar 100.
            height.RetargetScalar 200.
            pumpUntil host (fun () -> group.Callbacks.Completed)
            require "native animatable group callbacks finish after independent properties pause"
                (groupBegins=1 && groupCompletions=1 && width.Clock.Completed && height.Clock.Completed &&
                 group.Callbacks.CurrentTime=1000. && abs(element.getBoundingClientRect().width-100.)<0.01 &&
                 abs(element.getBoundingClientRect().height-200.)<0.01 && style.getPropertyPriority "width"="important")
        do
            use groupGraph=new Graph()
            use groupActivation=groupGraph.Activate()
            let mutable groupBegins=0
            let mutable groupUpdates=0
            let mutable groupWrites=0
            let owner,group,first,second=Api.createRoot(fun owner ->
                let group=new AnimatableGroup(scheduler,onBegin=(fun _ -> groupBegins<-groupBegins+1;owner.Dispose()),
                                              onUpdate=(fun _ -> groupUpdates<-groupUpdates+1))
                Api.getOwner().Attach(group:>IDisposable)
                let add ()=group.Add([|AnimatableValue(ValueInterpolation.Number 0.,None,false,None)|],
                                    (fun _ _ -> groupWrites<-groupWrites+1),1000.,PlaybackOptions.Default,id,id,4)
                owner,group,add(),add())
            use group=group
            first.RetargetScalar 100.
            second.RetargetScalar 100.
            pumpUntil host (fun () -> group.Cancelled)
            let stopped=groupWrites
            host.Cycle(milliseconds=10)
            host.Frame()
            require "native animatable group owner disposal interrupts callbacks and all property roots"
                (groupBegins=1 && groupUpdates=0 && groupWrites=stopped && first.Clock.Cancelled && second.Clock.Cancelled)
            owner.Dispose()
        let superseded=AnimatableValue(ValueInterpolation.Number 10.,None,false,None)
        superseded.RetargetScalar(100.,id,live) |> ignore
        let retargetAgain value = superseded.RetargetScalar(300.,id,live) |> ignore; value
        require "animatable kernel nested retarget interrupts stale sampling"
            ((superseded.Sample(0.5,4,retargetAgain,live)).IsNone &&
             (superseded.Sample(1.,4,id,live) |> Option.get).Scalar=Some 300.)
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
        tween.Stretch 200u
        require "native numeric stretch retains its last property value" (style.getPropertyValue "--anime-gap"="100px")
        tween.Seek 100.
        require "native numeric stretch samples the new iteration duration" (style.getPropertyValue "--anime-gap"="50px")
        tween.Seek 200.
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
        declarationWidth.Stretch 200u
        require "native declaration stretch retains style without immediate sampling"
            (style.getPropertyValue "width"="60px" && declarationWidth.CurrentTime=50. && declarationWidth.Progress=0.25)
        declarationWidth.Seek 100.
        require "native declaration stretch uses mutable geometry"
            (abs(element.getBoundingClientRect().width-60.)<0.01)
        declarationWidth.Stretch 0.125
        declarationWidth.Seek 0.0625
        require "native declaration stretch preserves precise fractional milliseconds"
            (abs(element.getBoundingClientRect().width-60.)<0.01)
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
        let unitParent=host.Document.createElement "div":?>HTMLElement
        host.Document.body.appendChild unitParent |> ignore
        let unitParentStyle=(unitParent:>ElementCSSInlineStyle).style
        unitParentStyle.setProperty("width","400px")
        let unitFirst=host.Document.createElement "div":?>HTMLElement
        let unitSecond=host.Document.createElement "div":?>HTMLElement
        unitParent.appendChild unitFirst |> ignore
        unitParent.appendChild unitSecond |> ignore
        let unitFirstStyle=(unitFirst:>ElementCSSInlineStyle).style
        let unitSecondStyle=(unitSecond:>ElementCSSInlineStyle).style
        unitFirstStyle.setProperty("font-size","16px")
        unitSecondStyle.setProperty("font-size","32px")
        unitFirstStyle.setProperty("width","7px",priority="important")
        use emFirst=CssAnimation.declarationOn scheduler unitFirst
                        (Ranvier.Css.Css.width(Ranvier.Css.Css.em 1.)) (Ranvier.Css.Css.width(Ranvier.Css.Css.px 100.)) 100u
        require "native unit context resolves em and retains priority"
            (unitFirstStyle.getPropertyValue "width"="16px" && unitFirstStyle.getPropertyPriority "width"="important")
        emFirst.Seek 50.
        require "native contextual em interpolation drives layout" (abs(unitFirst.getBoundingClientRect().width-58.)<0.02)
        emFirst.Cancel()
        use emSecond=CssAnimation.declarationOn scheduler unitSecond
                        (Ranvier.Css.Css.width(Ranvier.Css.Css.em 1.)) (Ranvier.Css.Css.width(Ranvier.Css.Css.px 100.)) 100u
        require "native unit contexts do not share stale font ratios" (unitSecondStyle.getPropertyValue "width"="32px")
        emSecond.Cancel()
        use percentWidth=CssAnimation.declarationOn scheduler unitFirst
                            (Ranvier.Css.Css.width(Ranvier.Css.Css.px 40.)) (Ranvier.Css.Css.width(Ranvier.Css.Css.percent 50.)) 100u
        percentWidth.Seek 50.
        require "native unit context resolves containing-block percentages"
            (unitFirstStyle.getPropertyValue "width"="30%" && abs(unitFirst.getBoundingClientRect().width-120.)<0.02)
        percentWidth.Cancel()
        use physicalWidth=CssAnimation.declarationOn scheduler unitFirst
                            (Ranvier.Css.Css.width(Ranvier.Css.Css.cm 1.)) (Ranvier.Css.Css.width(Ranvier.Css.Css.px 100.)) 100u
        require "native unit context retains source integer baseline measurements"
            (unitFirstStyle.getPropertyValue "width"="37.8px")
        physicalWidth.Cancel()
        let rootElement=host.Document.documentElement:?>HTMLElement
        let rootStyle=(rootElement:>ElementCSSInlineStyle).style
        let priorRootFont=rootStyle.getPropertyValue "font-size"
        let priorRootPriority=rootStyle.getPropertyPriority "font-size"
        try
            rootStyle.setProperty("font-size","20px")
            use remWidth=CssAnimation.declarationOn scheduler unitFirst
                            (Ranvier.Css.Css.width(Ranvier.Css.Css.rem 1.)) (Ranvier.Css.Css.width(Ranvier.Css.Css.px 100.)) 100u
            require "native unit context resolves root-relative lengths" (unitFirstStyle.getPropertyValue "width"="20px")
            remWidth.Cancel()
        finally
            if priorRootFont="" then rootStyle.removeProperty "font-size" |> ignore
            else rootStyle.setProperty("font-size",priorRootFont,priority=priorRootPriority)
        use angle=CssAnimation.declarationOn scheduler unitSecond
                    (Ranvier.Css.Css.rotate(Ranvier.Css.Css.turn 0.5)) (Ranvier.Css.Css.rotate(Ranvier.Css.Css.deg 270.)) 100u
        angle.Seek 50.
        require "native unit context converts angles without length probes" (unitSecondStyle.getPropertyValue "rotate"="225deg")
        angle.Cancel()
        require "native unit conversion removes all temporary clones" (unitParent.childNodes.length=2u)
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
        use stretchedTimer=new Timer(scheduler,100u,options={PlaybackOptions.Default with Delay=40.})
        stretchedTimer.Seek 75.
        stretchedTimer.Stretch 200u
        require "native timer stretch retains playhead and scales delay"
            (stretchedTimer.CurrentTime=75. && stretchedTimer.Progress=0.375 && stretchedTimer.Delay=80.)
        stretchedTimer.Resume()
        pumpUntil host (fun () -> stretchedTimer.Completed)
        require "native stretched timer completes through shared scheduling" (stretchedTimer.CurrentTime=200.)
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
