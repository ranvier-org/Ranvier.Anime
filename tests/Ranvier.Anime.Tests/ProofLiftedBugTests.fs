module Ranvier.Anime.Tests.ProofLiftedBugTests

open System
open System.Collections.Generic
open Expecto
open Ranvier
open Ranvier.Dom
open Ranvier.Anime

let private close actual expected msg =
    Expect.isLessThanOrEqual (abs (actual - expected)) 1e-6 msg

let tests =
    testList
        "proof-lifted regression suite (#24-#29)"
        [
            testCase "#24: clock-muted ComposedAnimation does not hijack Replace writers or corrupt Blend baselines, and Alternate/Complete guard fullyReplaced"
            <| fun _ ->
                use host = new ManualFrameScheduler()
                let target = AnimatedObject [ "x", AdaptedValue.Scalar 0. ]
                let baseOpts = { ComposedAnimationOptions.Default with Duration = 100.; Ease = id; Autoplay = false }
                use liveReplace = Composed.animate host target [| PropertySpec.Scalar("x", 100.) |] (Some baseOpts)
                use mutedReplace =
                    Composed.animate host target [| PropertySpec.Scalar("x", 500.) |] (Some (baseOpts.WithMuted(true)))
                Expect.isFalse liveReplace.Cancelled "muted Replace animation must not cancel active unmuted writer"
                liveReplace.Seek 50. |> ignore
                mutedReplace.Seek 50. |> ignore
                close (target.GetNumber "x") 50. "unmuted writer retains ownership while muted sibling stays muted"
                mutedReplace.Muted <- false
                mutedReplace.Seek 50. |> ignore
                close (target.GetNumber "x") 250. "unmuting attaches sibling and renders its authored midpoint"
                Expect.isTrue liveReplace.Cancelled "earlier Replace writer cancelled once sibling unmutes"
                // Alternate and Complete on fullyReplaced animation must be safe no-ops rather than throwing ObjectDisposedException.
                liveReplace.Alternate() |> ignore
                liveReplace.Complete() |> ignore

                let blendTarget = AnimatedObject [ "x", AdaptedValue.Scalar 0. ]
                let blendOpts = { baseOpts with Composition = KeyframeComposition.Blend }
                use _mutedBlend =
                    Composed.animate
                        host
                        blendTarget
                        [| PropertySpec.Scalar("x", 500., fromValue = 125.) |]
                        (Some (blendOpts.WithMuted(true)))
                use liveBlend = Composed.animate host blendTarget [| PropertySpec.Scalar("x", 100.) |] (Some blendOpts)
                liveBlend.Seek 100. |> ignore
                close (blendTarget.GetNumber "x") 100. "clock-muted Blend animation does not corrupt unmuted baseline"

            testCase "#25: ComposedAnimation.Stretch validates input atomically and preserves top-level host epoch offset"
            <| fun _ ->
                use host = new ManualFrameScheduler()
                host.Advance 10000.
                host.RunFrame()
                let target = AnimatedObject [ "x", AdaptedValue.Scalar 0. ]
                let opts = Some { ComposedAnimationOptions.Default with Duration = 100.; Ease = id; Autoplay = false }
                use anim = Composed.animate host target [| PropertySpec.Scalar("x", 100.) |] opts
                Expect.throwsT<ArgumentException> (fun () -> anim.Stretch nan |> ignore) "Stretch(nan) rejected"
                Expect.throwsT<ArgumentException> (fun () -> anim.Stretch -10. |> ignore) "Stretch(-10) rejected"
                anim.Seek 50. |> ignore
                close (target.GetNumber "x") 50. "failed Stretch leaves tween state intact"
                anim.Stretch 200. |> ignore
                Expect.equal anim.Duration 200. "stretched duration"
                // A later animation at host epoch 10050ms must still properly order against stretched animation's 10000ms epoch (not 20000ms!).
                host.Advance 50.
                use later = Composed.animate host target [| PropertySpec.Scalar("x", 300.) |] opts
                later.Seek 100. |> ignore
                close (target.GetNumber "x") 300. "later Replace animation wins without epoch inflation"

            testCase "#26: Timeline.Remove updates IterationDuration on repeating timelines and 0ms repeating Timer handles Reverse/Alternate/KeepTime"
            <| fun _ ->
                use host = new ManualFrameScheduler()
                let mutable aVal = 0.
                let mutable bVal = 0.
                use animA = new NumericAnimation(host, (fun () -> aVal), (fun v -> aVal <- v), 100., 100., ease = id)
                use animB = new NumericAnimation(host, (fun () -> bVal), (fun v -> bVal <- v), 500., 500., ease = id)
                use tl =
                    new Timeline(
                        host,
                        options = { PlaybackOptions.Default with Repeat = PlaybackRepeat.Count 1u; Alternate = true }
                    )
                tl.Sync(animA, position = TimelinePosition.At 0.) |> ignore
                tl.Sync(animB, position = TimelinePosition.At 0.) |> ignore
                tl.Remove(box animB) |> ignore
                Expect.equal tl.IterationDuration 100. "IterationDuration updated after Remove"
                Expect.equal tl.Duration 200. "Total Duration updated for 2 iterations"
                tl.Seek 150.
                Expect.equal tl.CurrentIteration 1 "second iteration at 150ms"
                close aVal 50. "alternating second iteration at 50ms backward"

                use zeroLoop =
                    new Timer(
                        host,
                        0.,
                        options = { PlaybackOptions.Default with Repeat = PlaybackRepeat.Forever; Alternate = true }
                    )
                zeroLoop.Reverse()
                zeroLoop.Alternate()
                Expect.isTrue (Double.IsFinite zeroLoop.CurrentTime) "0ms repeating Reverse/Alternate stays finite"

                use graph = new Graph()
                use _active = graph.Activate()
                use scope = new Scope()
                scope.Add(fun s ->
                    s.KeepTime(
                        (fun _ ->
                            new Timer(
                                host,
                                0.,
                                options = { PlaybackOptions.Default with Repeat = PlaybackRepeat.Count 2u; LoopDelay = 25. }
                            )),
                        (fun t -> t.CaptureScopeTime()),
                        (fun t snap -> t.RestoreScopeTime snap),
                        (fun t -> t.RewindForRevert())
                    )
                    |> ignore
                    None)
                |> ignore
                scope.Refresh() |> ignore

            testCase "#27: TransformCache.parse skips non-space ASCII whitespace and ElementTransforms.revertComponents clears stale cache"
            <| fun _ ->
                let struct (parsed, _) = TransformCache.parse "translateX(10px)\n\trotate(45deg)\r\nscale(1.5)"
                Expect.equal parsed["translateX"] "10px" "translateX"
                Expect.equal parsed["rotate"] "45deg" "rotate after newline and tab"
                Expect.equal parsed["scale"] "1.5" "scale after CRLF"
                Expect.equal (TransformCache.build parsed) "translateX(10px) rotate(45deg) scale(1.5) " "canonical build"
                Expect.equal (TransformCache.buildDeclaration parsed null) "translateX(10px) rotate(45deg) scale(1.5)" "canonical declaration"

                let styles = Dictionary<string, string>(StringComparer.Ordinal)
                let attrs = Dictionary<string, string>(StringComparer.Ordinal)
                styles["transform"] <- "rotate(45deg)"
                attrs["style"] <- "transform: rotate(45deg)"
                let styleProxy =
                    SvgTests.proxy<CSSStyleProperties> (fun m args ->
                        match m.Name with
                        | "getPropertyValue" ->
                            match styles.TryGetValue(string args[0]) with
                            | true, v -> box v
                            | _ -> box ""
                        | "getPropertyPriority" -> box ""
                        | "setProperty" ->
                            styles[string args[0]] <- string args[1]
                            attrs["style"] <- "transform: " + string args[1]
                            null
                        | "removeProperty" ->
                            styles.Remove(string args[0]) |> ignore
                            attrs["style"] <- ""
                            box ""
                        | _ -> null)
                let el =
                    SvgTests.proxy<HTMLElement> (fun m args ->
                        match m.Name with
                        | "get_style" -> box styleProxy
                        | "getAttribute" ->
                            match attrs.TryGetValue(string args[0]) with
                            | true, v -> box v
                            | _ -> null
                        | "removeAttribute" ->
                            attrs.Remove(string args[0]) |> ignore
                            null
                        | _ -> null)
                let _, inlineOrig = ElementTransforms.readWithInlineOrigin (el :> Element) "translateX"
                styles.Remove "transform" |> ignore
                attrs["style"] <- ""
                ElementTransforms.revertComponents (el :> Element) [| "translateX", inlineOrig |] ""
                Expect.isFalse (styles.ContainsKey "transform") "cleared inline transform is not resurrected from stale cache"

            testCase "#28: DragPhysics.edgeScroll guards zero threshold, ScrollObserver rejects negative Smooth sync, and CssUnits converts grad"
            <| fun _ ->
                Expect.equal (DragPhysics.edgeScroll 100. 500. 0. 0. 0. 1.5 100.) 0. "zero threshold returns 0 instead of NaN"
                Expect.equal (DragPhysics.edgeScroll 100. 500. 0. 0. -5. 1.5 80.) 0. "negative threshold returns 0"
                let geom = { Offset = 100.; TargetSize = 50.; ContainerSize = 400.; ScrollSize = 1000. }
                use obs =
                    new ScrollObserver(
                        (fun () -> 0., false),
                        (fun () -> geom),
                        options = (fun () -> { ScrollOptions.Default with Sync = ScrollSync.Smooth -0.5 })
                    )
                Expect.throwsT<ArgumentException> (fun () -> obs.Refresh()) "negative Smooth amount rejected"
                close (CssUnits.convertMeasured (fun _ _ -> 100, 100) 200. "grad" "deg") 180. "200grad = 180deg"
                close (CssUnits.convertMeasured (fun _ _ -> 100, 100) 1. "turn" "grad") 400. "1turn = 400grad"

            testCase "#29: CssColor preserves chroma at L=100% and ColorSpace.interpolate Lab with GamutPolicy.Reject succeeds on D50 white"
            <| fun _ ->
                let okChromatic = Utils.parseColor "oklab(1 0.3 0)"
                Expect.isLessThan okChromatic[1] 200. "oklab(1 0.3 0) green channel is not collapsed to 255 white"
                let okWhite = Utils.parseColor "oklab(1 0 0)"
                Expect.equal okWhite [| 255.; 255.; 255.; 1. |] "achromatic oklab(1 0 0) remains exact white"
                let white = ColorSpace.rgba 1. 1. 1. 1.
                let midWhite = ColorSpace.interpolate ColorInterpolation.Lab GamutPolicy.Reject white white 0.5
                close midWhite.Red 1. "white Red"
                close midWhite.Green 1. "white Green"
                close midWhite.Blue 1. "white Blue"
        ]
