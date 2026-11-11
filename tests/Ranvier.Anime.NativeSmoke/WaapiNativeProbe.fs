module Ranvier.Anime.NativeSmoke.WaapiNativeProbe

open System
open System.Diagnostics
open Ranvier.Anime
open Ranvier.Dom
open Ranvier.View.WebCore

let private require name condition =
    if not condition then failwith ("WAAPI_NATIVE_FAIL: " + name)
    Console.WriteLine("PASS waapi native " + name)

let private pumpUntil (host: WebCoreHost) condition =
    let watch = Stopwatch.StartNew()
    while not (condition()) && watch.ElapsedMilliseconds < 3000L do
        host.Cycle(milliseconds = 5)
        host.Frame()
    require "native WAAPI host loop reached condition" (condition())

/// <summary>Verifies Waapi.animate, individual --transform custom properties, Utils.remove and Timeline.Sync(waapi) on WebCoreHost.</summary>
let run (host: WebCoreHost) (scheduler: FrameScheduler) =
    let computed = ComputedStyles.forHost host
    use customProps = CustomProperties.forHost host
    use animSvc = Animations.forHost host
    let doc = host.Document

    // Pre-register --translateX to verify duplicate registration does not conflict (waapi.test.js line 15)
    try
        customProps.Register
            { Name = "--translateX"
              Syntax = "<length-percentage>"
              Inherits = false
              InitialValue = CustomPropertyInitialValue.Value "0px" }
    with _ -> ()

    let targets = Array.init 4 (fun i ->
        let el = doc.createElement "div"
        el.setAttribute("class", "waapi-probe-target")
        el.setAttribute("data-idx", string i)
        ((el :?> ElementCSSInlineStyle).style).setProperty("opacity", "1")
        doc.body.appendChild(el :> Node) |> ignore
        el)

    let baseOpts =
        { WaapiAnimationOptions.Default with
            Computed = Some computed
            CustomProperties = Some customProps
            Animations = Some animSvc
            Scheduler = Some scheduler }

    try
        // 1. Live scheduled completion + Then promise + no conflict with pre-registered --translateX
        do
            let mutable completedCb = false
            let mutable thenCb = false
            use anim =
                Waapi.animate targets[0]
                    { baseOpts.WithDuration(15.) with
                        Properties = [| "scale", WaapiValue.Scalar 1.5 |]
                        OnComplete = (fun self -> completedCb <- self.Completed) }
            let task = anim.Then(fun self -> thenCb <- self.Completed)
            pumpUntil host (fun () -> anim.Completed)
            // Allow extra turn if needed
            host.Cycle(milliseconds = 5); host.Frame()
            require "live scheduled WAAPI finishes and fires OnComplete and Then"
                (completedCb && thenCb && task.IsCompletedSuccessfully)
            anim.Revert() |> ignore

        // 2. Calling Complete() triggers Then on paused animation
        do
            let mutable resolved = false
            use anim =
                Waapi.animate targets[0]
                    { baseOpts.WithDuration(500.) with
                        Autoplay = false
                        Properties = [| "opacity", WaapiValue.Scalar 0.25 |] }
            let task = anim.Then(fun self -> resolved <- self.Completed)
            anim.Complete() |> ignore
            require "Complete() triggers WAAPI Then immediately" (resolved && task.IsCompletedSuccessfully)
            anim.Revert() |> ignore

        // 3. Seek + CommitStyles + Progress getter/setter + ByTarget function properties + Stagger
        do
            use anim =
                Waapi.animateTargets targets
                    ({ baseOpts with
                         Autoplay = false
                         Ease = Some (WaapiEase.Named "linear")
                         Duration = Some (fun _ i _ -> float ((i + 1) * 20))
                         Properties = [| "translate", WaapiValue.ByTarget (fun _ i _ -> WaapiValue.Css (sprintf "%dpx" ((i + 1) * 40))) |] }
                        .WithStaggerDelay(StaggerValue.Step 5.))
            require "staggered WAAPI computes max total duration" (abs (anim.Duration - 95.) < 1e-6)
            anim.Seek(20.).CommitStyles() |> ignore
            let t0 = (computed.Get(targets[0], None)).getPropertyValue "translate"
            require "Seek and CommitStyles apply interpolated WAAPI CSS" (t0 = "40px")
            anim.Progress <- 1.0
            anim.CommitStyles() |> ignore
            let t3 = (computed.Get(targets[3], None)).getPropertyValue "translate"
            require "Progress <- 1.0 reaches final WAAPI target" (t3 = "160px")
            anim.Revert() |> ignore

        // 4. Individual CSS transforms via --translateX / --translateY / --rotate / --scale and Revert()
        do
            let el = targets[0]
            let st = (el :?> ElementCSSInlineStyle).style
            st.setProperty("opacity", "0.5")
            st.setProperty("background-color", "rgb(255, 0, 0)")
            use anim =
                Waapi.animate el
                    { baseOpts.WithDuration(20.) with
                        Autoplay = false
                        Ease = Some (WaapiEase.Named "linear")
                        Properties =
                            [| "opacity", WaapiValue.Scalar 1.
                               "backgroundColor", WaapiValue.Css "#0000FF"
                               "x", WaapiValue.Scalar 100.
                               "y", WaapiValue.Scalar 200.
                               "rotate", WaapiValue.Scalar 45.
                               "scale", WaapiValue.Scalar 2. |] }
            anim.Seek(20.).CommitStyles() |> ignore
            let cs = computed.Get(el, None)
            require "individual WAAPI transforms populate registered custom properties"
                (cs.getPropertyValue "--translateX" = "100px" &&
                 cs.getPropertyValue "--translateY" = "200px" &&
                 cs.getPropertyValue "--rotate" = "45deg" &&
                 cs.getPropertyValue "--scale" = "2")
            anim.Revert() |> ignore
            let csAfter = computed.Get(el, None)
            require "WAAPI Revert restores pre-animation inline opacity and background-color"
                (csAfter.getPropertyValue "opacity" = "0.5" &&
                 csAfter.getPropertyValue "background-color" = "rgb(255, 0, 0)")
            st.removeProperty "opacity" |> ignore
            st.removeProperty "background-color" |> ignore

        // 5. Utils.remove on specific target and property
        do
            for el in targets do ((el :?> ElementCSSInlineStyle).style).setProperty("opacity", "1")
            use anim =
                Waapi.animateTargets targets
                    { baseOpts.WithDuration(15.) with
                        Properties =
                            [| "opacity", WaapiValue.Scalar 0.5
                               "width", WaapiValue.Keyframes [| WaapiValue.Css "10px"; WaapiValue.Css "80px" |] |] }
            Utils.remove (Some scheduler) [| box targets[0] |] (Some (box anim)) (Some "opacity") |> ignore
            pumpUntil host (fun () -> anim.Completed)
            host.Cycle(milliseconds = 5); host.Frame()
            let op0 = (computed.Get(targets[0], None)).getPropertyValue "opacity"
            let op1 = (computed.Get(targets[1], None)).getPropertyValue "opacity"
            let w0 = (computed.Get(targets[0], None)).getPropertyValue "width"
            require "Utils.remove cancels removed target/property WAAPI while siblings finish"
                (op0 = "1" && op1 = "0.5" && w0 = "80px")
            anim.Revert() |> ignore

        // 6. Ease cache distinguishes Curve functions while caching Named strings
        do
            use a1 =
                Waapi.animate targets[0]
                    { baseOpts.WithDuration(10.) with
                        Autoplay = false
                        Ease = Some (WaapiEase.Curve (Ease.create (EaseFamily.Power 1.) EaseDirection.Out))
                        Properties = [| "opacity", WaapiValue.Keyframes [| WaapiValue.Scalar 1.; WaapiValue.Scalar 0. |] |] }
            use a2 =
                Waapi.animate targets[1]
                    { baseOpts.WithDuration(10.) with
                        Autoplay = false
                        Ease = Some (WaapiEase.Curve (Ease.create (EaseFamily.Power 4.) EaseDirection.Out))
                        Properties = [| "opacity", WaapiValue.Keyframes [| WaapiValue.Scalar 1.; WaapiValue.Scalar 0. |] |] }
            a1.Seek 5. |> ignore
            a2.Seek 5. |> ignore
            let v1 = (computed.Get(targets[0], None)).getPropertyValue "opacity"
            let v2 = (computed.Get(targets[1], None)).getPropertyValue "opacity"
            require "WAAPI Curve functions are not conflated in string cache" (v1 <> v2)
            a1.Revert() |> ignore
            a2.Revert() |> ignore

        // 7. Looped Timeline.Sync(waapiA).Sync(waapiB, At 50.) scrubs across multiple loops (waapi.test.js line 338)
        do
            use waA =
                Waapi.animate targets[0]
                    { baseOpts.WithDuration(100.) with
                        Autoplay = false
                        Ease = Some (WaapiEase.Named "linear")
                        Properties = [| "opacity", WaapiValue.Keyframes [| WaapiValue.Scalar 0.; WaapiValue.Scalar 1. |] |] }
            use waB =
                Waapi.animate targets[1]
                    { baseOpts.WithDuration(100.) with
                        Autoplay = false
                        Ease = Some (WaapiEase.Named "linear")
                        Properties = [| "opacity", WaapiValue.Keyframes [| WaapiValue.Scalar 0.; WaapiValue.Scalar 1. |] |] }
            use tl = new Timeline(scheduler, options = { PlaybackOptions.Default with Repeat = PlaybackRepeat.Count 1u })
            tl.Sync(waA).Sync(waB, TimelinePosition.At 50.) |> ignore
            tl.Seek 50.
            let mid1A = Double.Parse((computed.Get(targets[0], None)).getPropertyValue "opacity", Globalization.CultureInfo.InvariantCulture)
            let mid1B = Double.Parse((computed.Get(targets[1], None)).getPropertyValue "opacity", Globalization.CultureInfo.InvariantCulture)
            tl.Seek 150.
            let loopStartA = Double.Parse((computed.Get(targets[0], None)).getPropertyValue "opacity", Globalization.CultureInfo.InvariantCulture)
            tl.Seek 250.
            let mid2A = Double.Parse((computed.Get(targets[0], None)).getPropertyValue "opacity", Globalization.CultureInfo.InvariantCulture)
            let mid2B = Double.Parse((computed.Get(targets[1], None)).getPropertyValue "opacity", Globalization.CultureInfo.InvariantCulture)
            require "looped Timeline.Sync scrubs persistent WAAPI animations across first and second loops"
                (abs (mid1A - 0.5) < 0.05 && abs mid1B < 0.05 &&
                 abs loopStartA < 0.05 &&
                 abs (mid2A - 1.0) < 0.05 && abs (mid2B - 0.5) < 0.05)
            waA.Revert() |> ignore
            waB.Revert() |> ignore
    finally
        for el in targets do
            doc.body.removeChild(el :> Node) |> ignore
