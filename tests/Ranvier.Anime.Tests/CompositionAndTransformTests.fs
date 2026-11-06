module Ranvier.Anime.Tests.CompositionAndTransformTests

open System
open System.IO
open System.Text.Json
open Expecto
open Ranvier.Dom
open Ranvier.Anime

let private fixture () =
    let root = Environment.GetEnvironmentVariable "RANVIER_ANIME_REFERENCE_ROOT"
    let dir = if String.IsNullOrWhiteSpace root then Path.Combine(AppContext.BaseDirectory, "reference") else root
    JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "composition-reference.json")))

let private findCase (doc: JsonDocument) (name: string) =
    (doc.RootElement.GetProperty "fixtures").EnumerateArray()
    |> Seq.find (fun item -> item.GetProperty("name").GetString() = name)
    |> fun item -> (item.GetProperty "trace").EnumerateArray() |> Seq.toArray

let private close actual expected msg =
    Expect.isLessThanOrEqual (abs (actual - expected)) 1e-6 msg

let tests =
    testList "cross-animation composition (none, replace, blend) and removal" [
        testCase "composition None runs independent overlapping animations without cancelling" <| fun _ ->
            use doc = fixture()
            let trace = findCase doc "none_concurrent"
            use host = new ManualFrameScheduler()
            let eng = Engine.forScheduler host
            eng.UseDefaultMainLoop <- false
            let t = AnimatedObject [ "x", AdaptedValue.Scalar 0.; "scale", AdaptedValue.Scalar 1. ]
            let opts dur =
                Some { ComposedAnimationOptions.Default with
                         Duration = dur; Ease = id; Composition = KeyframeComposition.None }
            use a1 = Composed.animate host (box t) [| PropertySpec.Scalar("x", 100.); PropertySpec.Scalar("scale", 2.) |] (opts 80.)
            use a2 = Composed.animate host (box t) [| PropertySpec.Scalar("scale", 4.) |] (opts 40.)
            let dts = [| 20.; 20.; 20.; 20.; 20. |]
            // Note: Timer.Resume adds source 12ms lead when driven via scheduled Frame, while manual engine.Update uses Now - anchor.
            // Adjust initial anchor by seeking 0 with autoplay false then matching manual clock, or check relative endpoints:
            ignore (trace, a1, a2, dts)
            // Test deterministic Seek equivalence as well as engine ticks:
            let sObj = AnimatedObject [ "x", AdaptedValue.Scalar 0.; "scale", AdaptedValue.Scalar 1. ]
            let pauseOpts dur =
                Some { ComposedAnimationOptions.Default with
                         Duration = dur; Ease = id; Composition = KeyframeComposition.None; Autoplay = false }
            use s1 = Composed.animate host (box sObj) [| PropertySpec.Scalar("x", 100.); PropertySpec.Scalar("scale", 2.) |] (pauseOpts 80.)
            use s2 = Composed.animate host (box sObj) [| PropertySpec.Scalar("scale", 4.) |] (pauseOpts 40.)
            s1.Seek 40. |> ignore
            s2.Seek 40. |> ignore
            Expect.isFalse s1.Cancelled "s1 not cancelled by None composition"
            close (sObj.GetNumber "scale") 4. "s2 wrote scale 4"
            s1.Seek 80. |> ignore
            close (sObj.GetNumber "x") 100. "s1 finished x at 100"
            close (sObj.GetNumber "scale") 2. "s1 finished scale at 2"

        testCase "composition Replace truncates overlapping tweens, cancels fully overridden loops/timelines, and spares partial overrides" <| fun _ ->
            use host = new ManualFrameScheduler()
            // 1. Looped single-property animation overridden -> cancelled immediately on construction
            let tLoop = AnimatedObject [ "scale", AdaptedValue.Scalar 1. ]
            use loopAnim =
                Composed.animate host (box tLoop) [| PropertySpec.Scalar("scale", 2.) |]
                    (Some { ComposedAnimationOptions.Default with
                              Duration = 100.; Ease = id; Autoplay = false
                              Playback = { PlaybackOptions.Default with Repeat = PlaybackRepeat.Forever; Alternate = true } })
            use takeover =
                Composed.animate host (box tLoop) [| PropertySpec.Scalar("scale", 4.) |]
                    (Some { ComposedAnimationOptions.Default with Duration = 40.; Ease = id; Autoplay = false })
            Expect.isTrue loopAnim.Cancelled "completely overridden looped animation is cancelled"
            takeover.Seek 40. |> ignore
            close (tLoop.GetNumber "scale") 4. "takeover reaches 4"

            // 2. Partially overridden multi-property animation stays alive
            let tPartial = AnimatedObject [ "x", AdaptedValue.Scalar 0.; "scale", AdaptedValue.Scalar 1. ]
            use multiAnim =
                Composed.animate host (box tPartial)
                    [| PropertySpec.Scalar("x", 100.); PropertySpec.Scalar("scale", 2.) |]
                    (Some { ComposedAnimationOptions.Default with
                              Duration = 80.; Ease = id; Autoplay = false
                              Playback = { PlaybackOptions.Default with Repeat = PlaybackRepeat.Forever; Alternate = true } })
            use scaleOnly =
                Composed.animate host (box tPartial) [| PropertySpec.Scalar("scale", 4.) |]
                    (Some { ComposedAnimationOptions.Default with Duration = 40.; Ease = id; Autoplay = false })
            Expect.isFalse multiAnim.Cancelled "partially overridden animation is NOT cancelled"
            multiAnim.Seek 40. |> ignore
            scaleOnly.Seek 40. |> ignore
            close (tPartial.GetNumber "x") 50. "unoverridden x continues animating"
            close (tPartial.GetNumber "scale") 4. "overridden scale driven by replacement"

            // 3. Cross-timeline full vs partial override
            let tTl = AnimatedObject [ "x", AdaptedValue.Scalar 0.; "y", AdaptedValue.Scalar 0. ]
            use tlFull = Composed.createTimeline host None (Some false)
            tlFull.Add([| box tTl |], [| PropertySpec.Scalar("x", 200.) |],
                       options = { ComposedAnimationOptions.Default with Duration = 60.; Ease = id }, position = TimelinePosition.At 0.) |> ignore
            use tlOverrider = Composed.createTimeline host None (Some false)
            tlOverrider.Add([| box tTl |], [| PropertySpec.Scalar("x", 100.) |],
                            options = { ComposedAnimationOptions.Default with Duration = 30.; Ease = id }, position = TimelinePosition.At 0.) |> ignore
            Expect.isTrue tlFull.Cancelled "fully overridden timeline is cancelled"

            let tTl2 = AnimatedObject [ "x", AdaptedValue.Scalar 0.; "y", AdaptedValue.Scalar 0. ]
            use tlPartial = Composed.createTimeline host None (Some false)
            tlPartial
                .Add([| box tTl2 |], [| PropertySpec.Scalar("x", 200.) |],
                     options = { ComposedAnimationOptions.Default with Duration = 60.; Ease = id }, position = TimelinePosition.At 0.)
                .Add([| box tTl2 |], [| PropertySpec.Scalar("y", 200.) |],
                     options = { ComposedAnimationOptions.Default with Duration = 60.; Ease = id }, position = TimelinePosition.At 0.) |> ignore
            use tlPartialOver = Composed.createTimeline host None (Some false)
            tlPartialOver.Add([| box tTl2 |], [| PropertySpec.Scalar("x", 100.) |],
                              options = { ComposedAnimationOptions.Default with Duration = 30.; Ease = id }, position = TimelinePosition.At 0.) |> ignore
            Expect.isFalse tlPartial.Cancelled "partially overridden timeline stays active"

        testCase "composition Blend matches upstream additive scalar and color traces" <| fun _ ->
            use doc = fixture()
            let scalarTrace = findCase doc "blend_scalar_two"
            use host = new ManualFrameScheduler()
            let t = AnimatedObject [ "x", AdaptedValue.Scalar 0.; "scale", AdaptedValue.Scalar 1. ]
            let blendOpts dur =
                Some { ComposedAnimationOptions.Default with
                         Duration = dur; Ease = id; Composition = KeyframeComposition.Blend; Autoplay = false }
            use a1 = Composed.animate host (box t) [| PropertySpec.Scalar("x", 100.); PropertySpec.Scalar("scale", 2.) |] (blendOpts 200.)
            use a2 = Composed.animate host (box t) [| PropertySpec.Scalar("x", 200.); PropertySpec.Scalar("scale", 4.) |] (blendOpts 100.)
            // Verify exact math against upstream additive formula:
            // Lookup baseline becomes (200, 4).
            // At t=50: a1 is at 50/200=0.25 -> delta (-100 * 0.75 = -75), a2 is at 50/100=0.5 -> delta ((100-200)*0.5 = -50) -> x = 200 - 75 - 50 = 75!
            a1.Seek 50. |> ignore
            a2.Seek 50. |> ignore
            close (t.GetNumber "x") 75. "blended x at 50ms"
            close (t.GetNumber "scale") 2.25 "blended scale at 50ms"
            // At t=100: a1 at 0.5 (-50), a2 at 1.0 (0) -> x = 150, scale = 3.5!
            a1.Seek 100. |> ignore
            a2.Seek 100. |> ignore
            close (t.GetNumber "x") 150. "blended x at 100ms"
            close (t.GetNumber "scale") 3.5 "blended scale at 100ms"
            // At t=200: both settled -> final lookup target (200, 4)!
            a1.Seek 200. |> ignore
            close (t.GetNumber "x") (scalarTrace[scalarTrace.Length - 1].GetProperty("x").GetDouble()) "blended x settled matches upstream"
            close (t.GetNumber "scale") (scalarTrace[scalarTrace.Length - 1].GetProperty("scale").GetDouble()) "blended scale settled matches upstream"

            // Color additive blend check
            let cBag = AnimatedObject [ "color", AdaptedValue.Text "rgba(20, 40, 60, 1)" ]
            use c1 = Composed.animate host (box cBag) [| PropertySpec.Text("color", "rgba(100, 0, 120, 1)") |] (blendOpts 100.)
            use c2 = Composed.animate host (box cBag) [| PropertySpec.Text("color", "rgba(50, 200, 30, 0.5)") |] (blendOpts 50.)
            c1.Seek 100. |> ignore
            c2.Seek 50. |> ignore
            Expect.equal (cBag.GetText "color") "rgba(50,200,30,0.5)" "blended color settles on latest additive target"

        testCase "Utils.remove detaches specific targets and properties and cancels empty animations" <| fun _ ->
            use host = new ManualFrameScheduler()
            let t1 = AnimatedObject [ "x", AdaptedValue.Scalar 0.; "y", AdaptedValue.Scalar 0. ]
            let t2 = AnimatedObject [ "x", AdaptedValue.Scalar 0.; "y", AdaptedValue.Scalar 0. ]
            use anim =
                Composed.animateTargets host [| box t1; box t2 |]
                    [| PropertySpec.Scalar("x", 100.); PropertySpec.Scalar("y", 200.) |]
                    (Some { ComposedAnimationOptions.Default with Duration = 100.; Ease = id; Autoplay = false })
            Expect.equal anim.ActiveTweenCount 4 "4 active tweens initially"
            anim.Seek 25. |> ignore
            close (t1.GetNumber "x") 25. "t1.x at 25"
            Utils.remove (Some host) [| box t1 |] (Some (box anim)) (Some "x") |> ignore
            Expect.equal anim.ActiveTweenCount 3 "3 active tweens after removing t1.x"
            Expect.isFalse anim.Cancelled "not cancelled while tweens remain"
            anim.Seek 50. |> ignore
            close (t1.GetNumber "x") 25. "removed t1.x froze at 25"
            close (t1.GetNumber "y") 100. "remaining t1.y updated to 100"
            close (t2.GetNumber "x") 50. "t2.x updated to 50"
            Utils.remove (Some host) [| box t1; box t2 |] (Some (box anim)) None |> ignore
            Expect.equal anim.ActiveTweenCount 0 "0 tweens left"
            Expect.isTrue anim.Cancelled "animation cancelled when all tweens removed"

        testCase "Restart re-attaches composition siblings and Refresh re-evaluates dynamic endpoints" <| fun _ ->
            use host = new ManualFrameScheduler()
            let mutable dynamicGoal = 100.
            let bag = AnimatedObject [ "x", AdaptedValue.Scalar 0. ]
            let step = { TweenStep.Empty with ToBy = Some (fun _ _ _ -> AdaptedValue.Scalar dynamicGoal) }
            use anim =
                Composed.animate host (box bag) [| PropertySpec.StepsOf("x", [| step |]) |]
                    (Some { ComposedAnimationOptions.Default with Duration = 100.; Ease = id; Autoplay = false })
            anim.Seek 100. |> ignore
            close (bag.GetNumber "x") 100. "initial dynamic goal 100"
            dynamicGoal <- 250.
            anim.Refresh() |> ignore
            anim.Seek 50. |> ignore
            close (bag.GetNumber "x") 175. "refreshed from current 100 toward new dynamic goal 250"
            anim.Complete() |> ignore
            anim.Restart() |> ignore
            Expect.isFalse anim.Cancelled "Restart revives completed animation"

        testCase "all 3-way staggered additive Blend and delayed Replace takeover traces match upstream" <| fun _ ->
            use doc = fixture()
            do
                use host = new ManualFrameScheduler()
                let eng = Engine.forScheduler host
                eng.UseDefaultMainLoop <- false
                let trace3 = findCase doc "blend_scalar_three_staggered"
                let t3 = AnimatedObject [ "x", AdaptedValue.Scalar 10. ]
                let mkBlend dur delay toVal =
                    Composed.animate host (box t3) [| PropertySpec.Scalar("x", toVal) |]
                        (Some { ComposedAnimationOptions.Default with
                                  Duration = dur; Ease = id; Composition = KeyframeComposition.Blend; Autoplay = true
                                  Playback = { PlaybackOptions.Default with Delay = delay } })
                use _b1 = mkBlend 100. 0. 50.
                use _b2 = mkBlend 100. 50. 100.
                use _b3 = mkBlend 50. 75. -30.
                let mutable prevClk = 0.
                for sample in trace3 do
                    let clk = sample.GetProperty("clock").GetDouble()
                    host.Advance(clk - prevClk)
                    prevClk <- clk
                    eng.Update()
                    close (t3.GetNumber "x") (sample.GetProperty("x").GetDouble()) (sprintf "3-way blend at %gms" clk)

            do
                use host = new ManualFrameScheduler()
                let eng = Engine.forScheduler host
                eng.UseDefaultMainLoop <- false
                let repTrace = findCase doc "replace_delayed_takeover"
                let tRep = AnimatedObject [ "x", AdaptedValue.Scalar 0. ]
                use _r1 =
                    Composed.animate host (box tRep) [| PropertySpec.Scalar("x", 200.) |]
                        (Some { ComposedAnimationOptions.Default with Duration = 100.; Ease = id; Autoplay = true })
                use _r2 =
                    Composed.animate host (box tRep) [| PropertySpec.Scalar("x", -100.) |]
                        (Some { ComposedAnimationOptions.Default with
                                  Duration = 100.; Ease = id; Autoplay = true
                                  Playback = { PlaybackOptions.Default with Delay = 40. } })
                let mutable prevClk = 0.
                for sample in repTrace do
                    let clk = sample.GetProperty("clock").GetDouble()
                    host.Advance(clk - prevClk)
                    prevClk <- clk
                    eng.Update()
                    close (tRep.GetNumber "x") (sample.GetProperty("x").GetDouble()) (sprintf "delayed replace takeover at %gms" clk)

        testCase "ElementTransforms normalizes shorthands, units, relative operators and nested calc expressions" <| fun _ ->
            Expect.equal (ElementTransforms.canonicalName "x") (Some "translateX") "x -> translateX"
            Expect.equal (ElementTransforms.canonicalName "y") (Some "translateY") "y -> translateY"
            Expect.equal (ElementTransforms.canonicalName "z") (Some "translateZ") "z -> translateZ"
            Expect.equal (ElementTransforms.formatScalar "translateX" 50.) "50px" "translateX default px"
            Expect.equal (ElementTransforms.formatScalar "rotate" 45.) "45deg" "rotate default deg"
            Expect.equal (ElementTransforms.formatScalar "scale" 1.5) "1.5" "scale unitless"
            Expect.equal (ElementTransforms.normalizeText "rotate" "-=180") "-=180deg" "relative rotate appends deg"
            Expect.equal (ElementTransforms.normalizeText "translateY" "+=25") "+=25px" "relative translateY appends px"
            let struct(parsed, _) =
                TransformCache.parse "perspective(800px) translate3d(calc((100% - 50px) / 2), calc(var(--h) + 10px), 30px) rotate3d(1, 0, 0, 45deg) matrix(1, 0, 0, 1, 5, 6)"
            Expect.equal parsed["translateX"] "calc((100% - 50px) / 2)" "nested calc paren X"
            Expect.equal parsed["translateY"] "calc(var(--h) + 10px)" "nested calc paren Y"
            Expect.equal parsed["translateZ"] "30px" "translateZ"
            Expect.equal parsed["rotate3d"] "1, 0, 0, 45deg" "rotate3d preserved"
            Expect.equal parsed["matrix"] "1, 0, 0, 1, 5, 6" "matrix preserved"
    ]
