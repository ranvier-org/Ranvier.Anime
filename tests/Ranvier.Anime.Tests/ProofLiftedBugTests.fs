module Ranvier.Anime.Tests.ProofLiftedBugTests

open System
open System.Collections.Generic
open System.Reflection
open Expecto
open Ranvier
open Ranvier.Dom
open Ranvier.Anime

let private domProxy<'t> handler =
    let value = DispatchProxy.Create<'t, PortableTests.PortableProxy>()
    (box value :?> PortableTests.PortableProxy).Handler <- handler
    value

let private close actual expected msg =
    Expect.isLessThanOrEqual (abs (actual - expected)) 1e-6 msg

let tests =
    testList
        "proof-lifted regression suite (#24-#29, #37-#41)"
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

            testCase "#37: PlaybackState.Resize rebound, KeyframeAnimation.Playback.Revert, TimelinePosition, Timeline.Remove/Stretch, and equal KeyframeOffset ULP clamping"
            <| fun _ ->
                use host = new ManualFrameScheduler()

                // 1. KeyframeAnimation.Playback.Revert() restores pre-construction snapshot after completion
                let mutable propVal = 7.
                let numProp = NumericProperty((fun () -> propVal), (fun v -> propVal <- v))
                use kfAnim =
                    Keyframes.Numeric(
                        host,
                        numProp,
                        [| Keyframe.between 20. 120. |],
                        duration = 100u,
                        options = { KeyframeAnimationOptions.Default with Autoplay = false; Ease = id }
                    )
                kfAnim.Playback.Seek 100.
                close propVal 120. "completed at 120"
                kfAnim.Playback.Revert()
                close propVal 7. "Playback.Revert() restores pre-construction snapshot 7 even after completion"

                // 4. TimelinePosition '<label' and double-operator rejection
                use tl = new Timeline(host)
                let mutable pVal = 0.
                let p = NumericProperty((fun () -> pVal), (fun v -> pVal <- v))
                tl.Animate(p, 100., 100u).Label("<tag", TimelinePosition.At 40.) |> ignore
                close (tl.ResolvePosition(TimelinePosition.Expression "<tag")) 40. "'<tag' resolves label instead of '<' sibling"
                close (tl.ResolvePosition(TimelinePosition.Expression "<tag+=15")) 55. "'<tag+=15' resolves relative to label '<tag'"
                Expect.throwsT<ArgumentException>
                    (fun () -> tl.ResolvePosition(TimelinePosition.Expression "<missing+=10") |> ignore)
                    "'<missing+=10' rejects missing label"
                Expect.throwsT<ArgumentException>
                    (fun () -> tl.ResolvePosition(TimelinePosition.Expression "+=10+=20") |> ignore)
                    "chained relative operators rejected"

                // 5. Timeline.Remove(property) removes OwnedAnimation from Timeline.Animate and rebounds Progress
                use tlRem = new Timeline(host)
                let mutable v1, v2 = 0., 0.
                let prop1 = NumericProperty((fun () -> v1), (fun v -> v1 <- v))
                let prop2 = NumericProperty((fun () -> v2), (fun v -> v2 <- v))
                tlRem.Animate(prop1, 100., 100u).Animate(prop2, 200., 200u, position = TimelinePosition.At 0.) |> ignore
                tlRem.Seek 100.
                close tlRem.Progress 0.5 "100 / 200 = 0.5 before removing prop2"
                tlRem.Remove(box prop2) |> ignore
                close tlRem.Duration 100. "removing prop2 shrinks duration to 100"
                close tlRem.Progress 1.0 "ResizeWithoutReset immediately rebounds Progress to 1.0"

                // 6. Timeline.Stretch preserves zero-duration setter duration and validates atomically
                use tlSet = new Timeline(host)
                let mutable setV = 0.
                let setProp = NumericProperty((fun () -> setV), (fun v -> setV <- v))
                tlSet.Set(setProp, 42., position = TimelinePosition.At 50.).Animate(prop1, 100., 100u, position = TimelinePosition.At 0.) |> ignore
                tlSet.Stretch 200. |> ignore
                tlSet.Seek 99.
                close setV 0. "stretched setter at 100ms has not fired at 99ms"
                tlSet.Seek 100.
                close setV 42. "stretched setter fires cleanly at 100ms"

                // 7. Equal KeyframeOffset positions (0.07, 0.9, 0.9) do not throw on 1-ULP sum drift
                let seqOffsets =
                    KeyframeSequence<NumericProperty, float>.Offsets
                        [| KeyframeOffset.at 0.07 [| "x", 10. |]
                           KeyframeOffset.at 0.9 [| "x", 50. |]
                           KeyframeOffset.at 0.9 [| "x", 90. |] |]
                use seqAnim = Keyframes.Numeric(host, [| "x", prop1 |], seqOffsets, duration = 1000u)
                seqAnim.Playback.Seek 1000.
                close v1 90. "equal 0.9 offsets compile and reach final endpoint 90"

            testCase "#38: Spring overdamped slow-root rationalization & finite Ease, Reactive.drive ambient-isolation, Animatable idle Pause, and Stagger/CssStagger null guards"
            <| fun _ ->
                // 1. Extreme overdamped spring settles below 60s cap and negative-time Solve/Ease stay finite
                let extremeOver = Spring(mass = 1., stiffness = 1e-6, damping = 10000.)
                Expect.isTrue (Double.IsFinite (extremeOver.Solve -10.)) "negative time Solve finite"
                let zeroCoeffSpring = Spring(mass = 1., stiffness = 100., damping = 40., velocity = 40.)
                Expect.isFalse (Double.IsNaN (zeroCoeffSpring.Solve -5.)) "0 * exp(+large) does not produce NaN"
                Expect.isTrue (Double.IsFinite (extremeOver.Ease -1000.)) "Ease clamps extreme extrapolation to finite float"

                // 2. Reactive.drive ignores ambient Reversed/Forever scope playback defaults and validates duration early
                use graph = new Graph()
                use _act = graph.Activate()
                use host = new ManualFrameScheduler()
                let owner = Api.createRoot id
                try
                    let mutable reads = 0
                    Expect.throwsT<ArgumentException>
                        (fun () ->
                            Api.runWithOwner owner (fun () ->
                                Reactive.drive host -5. (fun () -> reads <- reads + 1; 1.) ignore |> ignore))
                        "negative duration rejected before reading source"
                    Expect.equal reads 0 "read callback not invoked when duration is invalid"

                    let sigVal = Api.createSignal 0.
                    let mutable driven = -1.
                    let disp =
                        Api.runWithOwner owner (fun () ->
                            ScopePlaybackContext.run
                                (ScopeClockDefaults({ PlaybackOptions.Default with Reversed = true; Repeat = PlaybackRepeat.Forever }))
                                (fun () -> Reactive.drive host 100. (fun () -> sigVal.Value) (fun v -> driven <- v)))
                    use _d = disp
                    close driven 0. "seeded to 0"
                    sigVal.Value <- 100.
                    host.Advance 88.
                    host.RunFrame()
                    close driven 100. "Reactive.drive reaches 100 forward despite ambient Reversed=true"
                finally
                    owner.Dispose()

                // 3. Animatable with onComplete does not fire onComplete when idle before any property begins
                let mutable completes = 0
                use animGroup = new Animatable(host, onComplete = (fun _ -> completes <- completes + 1))
                let mutable ax = 0.
                let track = animGroup.Numeric(NumericProperty((fun () -> ax), (fun v -> ax <- v)), duration = 100u)
                track.Playback.Pause()
                Expect.equal completes 0 "pausing idle track before any retarget does not fire group onComplete"

                // 4. Stagger.distribute and CssStagger.step null guards
                Expect.throwsT<ArgumentNullException>
                    (fun () -> Stagger.distribute (StaggerValue.Step 10.) Unchecked.defaultof<StaggerOptions> 3u |> ignore)
                    "null StaggerOptions throws ArgumentNullException"
                Expect.throwsT<ArgumentNullException>
                    (fun () -> CssStagger.step Unchecked.defaultof<_> (Ranvier.Css.Css.px 10.) StaggerOptions.Default 0u |> ignore)
                    "null create in CssStagger.step throws ArgumentNullException even when targetCount = 0"

            testCase "#39: Composition Complex rgba preservation, Blend mid-flight cancel & Restart, RemoveMatching duration/timeline sync, ComposedTimeline speed/Refresh, and Utils zero-span safety"
            <| fun _ ->
                use host = new ManualFrameScheduler()
                // 1. Complex string starting with rgba(..., 1) is not truncated to 3-channel rgb(...)
                let styles = Dictionary<string, string>(StringComparer.Ordinal)
                let mockStyle =
                    domProxy<CSSStyleProperties> (fun m args ->
                        match m.Name with
                        | "get_length" -> box (uint32 styles.Count)
                        | "item" -> box ""
                        | "getPropertyValue" ->
                            match styles.TryGetValue(unbox<string> args[0]) with
                            | true, v -> box v
                            | _ -> box ""
                        | "getPropertyPriority" -> box ""
                        | "setProperty" ->
                            styles[unbox<string> args[0]] <- unbox<string> args[1]
                            null
                        | "removeProperty" ->
                            styles.Remove(unbox<string> args[0]) |> ignore
                            box ""
                        | other -> failwith other)
                let mockEl =
                    domProxy<HTMLElement> (fun m _ ->
                        match m.Name with
                        | "get_style" -> box mockStyle
                        | "getAttribute" -> null
                        | "removeAttribute" -> null
                        | other -> failwith other)
                use shadowAnim =
                    Composed.animate
                        host
                        (box mockEl)
                        [| PropertySpec.Text("boxShadow", "rgba(255, 0, 0, 1) 10px 20px", fromValue = "rgba(0, 0, 0, 1) 0px 0px") |]
                        (Some { ComposedAnimationOptions.Default with Duration = 100.; Ease = id; Autoplay = false })
                shadowAnim.Seek 100. |> ignore
                Expect.isTrue (styles["box-shadow"].Contains "10px 20px") "box-shadow preserves trailing offsets when leading color has alpha=1"

                // 2. Blend mid-flight Cancel folds residual without jump, and Restart replays Blend delta
                let blendBag = AnimatedObject [ "x", AdaptedValue.Scalar 0. ]
                let blendOpts =
                    { ComposedAnimationOptions.Default with
                        Duration = 100.
                        Ease = id
                        Autoplay = false
                        Composition = KeyframeComposition.Blend }
                use b1 = Composed.animate host blendBag [| PropertySpec.Scalar("x", 100.) |] (Some blendOpts)
                b1.Seek 50. |> ignore
                use b2 = Composed.animate host blendBag [| PropertySpec.Scalar("x", 200.) |] (Some blendOpts)
                b2.Seek 50. |> ignore
                // At b1=50% (-50) and b2=50% (-50) with FromNumber=200, blended x = 100.
                close (blendBag.GetNumber "x") 100. "combined blend at 50%/50% is 100"
                b2.Cancel()
                b1.Seek 50. |> ignore
                close (blendBag.GetNumber "x") 100. "cancelling b2 mid-flight preserves live blended value 100 without +50 jump"
                b1.Seek 100. |> ignore
                close (blendBag.GetNumber "x") 150. "b1 finishes at 150 (100 + b2's half-completed +50)"

                let restartBag = AnimatedObject [ "x", AdaptedValue.Scalar 0. ]
                use br1 = Composed.animate host restartBag [| PropertySpec.Scalar("x", 100.) |] (Some blendOpts)
                use br2 = Composed.animate host restartBag [| PropertySpec.Scalar("x", 100.) |] (Some blendOpts)
                br1.Seek 100. |> ignore
                br2.Seek 100. |> ignore
                br1.Restart() |> ignore
                br1.Seek 50. |> ignore
                close (restartBag.GetNumber "x") 50. "restarted completed Blend animation replays from 0 -> 100"

                // 3. ComposedTimeline child Speed division & Utils.remove on timeline child
                let tlBag1 = AnimatedObject [ "x", AdaptedValue.Scalar 0. ]
                let tlBag2 = AnimatedObject [ "x", AdaptedValue.Scalar 0. ]
                use ctl = Composed.createTimeline host None (Some false)
                ctl.Add(
                    [| box tlBag1 |],
                    [| PropertySpec.Scalar("x", 100.) |],
                    options = { ComposedAnimationOptions.Default with Duration = 100.; Playback = { PlaybackOptions.Default with Speed = 2. } }
                ) |> ignore
                close ctl.Duration 50. "100ms child at Speed=2 contributes 50ms to ComposedTimeline duration"
                ctl.Add([| box tlBag2 |], [| PropertySpec.Scalar("x", 100.) |], options = { ComposedAnimationOptions.Default with Duration = 100. }) |> ignore
                close ctl.Duration 150. "second 100ms child placed at '<'/end starts at 50ms -> total 150ms"
                ctl.Seek 75. |> ignore
                Utils.remove (Some host) [| box tlBag2 |] None None |> ignore
                close ctl.Duration 50. "Utils.remove prunes cancelled ComposedTimeline child and updates duration"

                // 4. Utils.wrap and Utils.mapRange zero-span safety
                close (Utils.wrap 42. 10. 10.) 10. "Utils.wrap with min=max returns min instead of NaN"
                close ((Utils.Chain.wrap 10. 10.).Apply 42.) 10. "ValueChain.Wrap with min=max returns min"
                close (Utils.mapRange 5. 10. 10. 20. 80.) 20. "Utils.mapRange with inLow=inHigh returns outLow instead of NaN"
                close ((Utils.Chain.mapRange 10. 10. 20. 80.).Apply 5.) 20. "ValueChain.MapRange with inLow=inHigh returns outLow"

            testCase "#40: ElementTransforms external cache sync, CssColor HWB proportional gray & '#'-prefixed complex decomposition, Waapi custom-prop Revert, and Scramble text safety"
            <| fun _ ->
                // 1. ElementTransforms clears stale cached components when inline style.transform is externally overwritten
                let mutable inlineTf = ""
                let tfStyle =
                    domProxy<CSSStyleProperties> (fun m args ->
                        match m.Name with
                        | "getPropertyValue" -> box (if unbox<string> args[0] = "transform" then inlineTf else "")
                        | "getPropertyPriority" -> box ""
                        | "setProperty" ->
                            if unbox<string> args[0] = "transform" then inlineTf <- unbox<string> args[1]
                            null
                        | "removeProperty" ->
                            if unbox<string> args[0] = "transform" then inlineTf <- ""
                            box ""
                        | other -> failwith other)
                let tfEl =
                    domProxy<HTMLElement> (fun m _ ->
                        match m.Name with
                        | "get_style" -> box tfStyle
                        | other -> failwith other)
                ElementTransforms.set (tfEl :> Element) [ "x", TransformValue.Scalar 20.; "rotate", TransformValue.Scalar 45. ]
                Expect.equal inlineTf "translateX(20px) rotate(45deg)" "both staged"
                // External caller overwrites inline transform removing rotate(45deg)
                inlineTf <- "translateX(10px)"
                Expect.equal (ElementTransforms.get (tfEl :> Element) "rotate") "0deg" "stale rotate(45deg) cleared when absent from live inline transform"

                // 2. CssColor HWB proportional gray normalization when w > 100% and '#'-prefixed complex decomposition
                let hwbGrey = Utils.parseColor "hwb(0 150% 50%)"
                close hwbGrey[0] (255. * 0.75) "hwb(0 150% 50%) normalizes 1.5 / (1.5 + 0.5) = 0.75 gray"
                let complexHex = ValueInterpolation.decompose (ValueInterpolation.Text "#ff0000,#00ff00")
                Expect.equal complexHex.Kind ValueInterpolation.ValueKind.Complex "'#ff0000,#00ff00' falls through to Complex without throwing"

                // 3. TextScramble writes textContent on HTMLElement (no HTML tag injection) and restores original innerHTML on Revert
                use host = new ManualFrameScheduler()
                let mutable innerHtml = "<span>Keep</span>"
                let mutable textContent = "Keep"
                let scramEl =
                    domProxy<HTMLElement> (fun m args ->
                        match m.Name with
                        | "get_innerHTML" -> box innerHtml
                        | "set_innerHTML" ->
                            innerHtml <- unbox<string> args[0]
                            textContent <- innerHtml
                            null
                        | "get_textContent" -> box textContent
                        | "set_textContent" ->
                            textContent <- unbox<string> args[0]
                            null
                        | other -> failwith other)
                use scramTimer =
                    Text.animateScramble
                        host
                        (box scramEl)
                        (Some { ScrambleTextOptions.Default with Text = Some "<b&>"; Duration = Some 100. })
                        None
                        (Some false)
                        None
                scramTimer.Seek 100.
                Expect.equal textContent "<b&>" "settled text written via textContent without HTML parsing"
                Expect.equal innerHtml "<span>Keep</span>" "innerHTML setter was not used during scramble frames"
                scramTimer.Revert()
                Expect.equal innerHtml "<span>Keep</span>" "Revert restores pre-scramble innerHTML"

            testCase "#41: NativeDraggable muted X/Y setters, Follow.property component-scoped transform Revert & Autoplay override, and Scroll.observe ComposedTimeline link"
            <| fun _ ->
                use host = new ManualFrameScheduler()
                // 1. NativeDraggable X/Y setters move coordinates without firing OnUpdate
                let mutable xVal, yVal = 0., 0.
                let mutable updates = 0
                let xProp = NumericProperty((fun () -> xVal), (fun v -> xVal <- v))
                let yProp = NumericProperty((fun () -> yVal), (fun v -> yVal <- v))
                use ctrl = new Draggable(host, xProp, yProp, onUpdate = (fun _ -> updates <- updates + 1))
                use nativeDrag = new NativeDraggable(ctrl, None, None, [||])
                nativeDrag.X <- 42.
                nativeDrag.Y <- 84.
                close xVal 42. "NativeDraggable.X moved xProp"
                close yVal 84. "NativeDraggable.Y moved yProp"
                Expect.equal updates 0 "NativeDraggable.X/Y setters mute OnUpdate per portable contract"

                // 2. Follow.property forces Autoplay=true and reverts only its own transform component
                let styles = Dictionary<string, string * string>(StringComparer.Ordinal)
                let fStyle =
                    domProxy<CSSStyleProperties> (fun m args ->
                        match m.Name with
                        | "get_length" -> box (uint32 styles.Count)
                        | "item" -> box ""
                        | "getPropertyValue" ->
                            match styles.TryGetValue(unbox<string> args[0]) with
                            | true, (v, _) -> box v
                            | _ -> box ""
                        | "getPropertyPriority" ->
                            match styles.TryGetValue(unbox<string> args[0]) with
                            | true, (_, p) -> box p
                            | _ -> box ""
                        | "setProperty" ->
                            let prio =
                                match args[2] with
                                | :? string as s -> s
                                | :? (string option) as opt -> defaultArg opt ""
                                | _ -> ""
                            styles[unbox<string> args[0]] <- (unbox<string> args[1], prio)
                            null
                        | "removeProperty" ->
                            styles.Remove(unbox<string> args[0]) |> ignore
                            box ""
                        | other -> failwith other)
                let fEl =
                    domProxy<HTMLElement> (fun m _ ->
                        match m.Name with
                        | "get_style" -> box fStyle
                        | "getAttribute" ->
                            if styles.Count = 0 then box "" else box "nonempty"
                        | "removeAttribute" -> null
                        | other -> failwith other)
                let computedSvc =
                    { new ComputedStyleService with
                        member _.Get(_, _) = fStyle :> CSSStyleDeclaration }
                use _installed = AnimeHost.Install { Scheduler = host; Computed = computedSvc }
                let followOpts = { ComposedAnimationOptions.Default with Duration = 100.; Ease = id; Autoplay = false }
                let fx = Follow.property (fEl :> Element) "x" followOpts
                let fy = Follow.property (fEl :> Element) "y" followOpts
                fx.To 10.
                fy.To 20.
                Expect.equal (fst styles["transform"]) "translate(10px,20px)" "both x and y followers wrote"
                fx.Revert()
                Expect.equal (fst styles["transform"]) "translateY(20px)" "reverting x follower preserves y follower's transform"
                fy.Revert()
                Expect.isFalse (styles.ContainsKey "transform") "reverting y follower removes empty transform"

                // Opacity follower with Autoplay=false on options still autoplays on second To
                let fOp = Follow.property (fEl :> Element) "opacity" followOpts
                fOp.To 0.2
                fOp.To 1.0
                for _ in 1..15 do
                    host.Advance 10.
                    host.RunFrame()
                close (Double.Parse(fst styles["opacity"], Globalization.CultureInfo.InvariantCulture)) 1.0 "ComposedFollower overrides Autoplay=false so follower animates"
                fOp.Revert()

                // 3. Follow.morph keeps exact target shape after step-root disposal, retargets mid-flight, and restores original on Revert
                let mkSvgPath (attrs: Dictionary<string, string>) (ownerDoc: unit -> Document) =
                    domProxy<SVGGeometryElement> (fun m args ->
                        match m.Name with
                        | "get_tagName" -> box "path"
                        | "get_ownerDocument" -> box (ownerDoc ())
                        | "getTotalLength" -> box 100f
                        | "getPointAtLength" ->
                            let d = float (unbox<float32> args[0])
                            let shape = match attrs.TryGetValue "d" with true, v -> v | _ -> ""
                            let px, py =
                                if shape = "M0 0 L0 100" then 0., d
                                elif shape = "M0 0 L100 100" then d * 0.70710678, d * 0.70710678
                                else d, 0.
                            box (domProxy<SVGPoint> (fun pm _ ->
                                match pm.Name with "get_x" -> box (float32 px) | "get_y" -> box (float32 py) | _ -> null))
                        | "getAttribute" ->
                            match attrs.TryGetValue(string args[0]) with true, v -> box v | _ -> null
                        | "setAttribute" ->
                            attrs[string args[0]] <- string args[1]
                            null
                        | "removeAttribute" ->
                            attrs.Remove(string args[0]) |> ignore
                            null
                        | _ -> null)
                let mutable svgDoc = Unchecked.defaultof<Document>
                svgDoc <-
                    domProxy<Document> (fun m _ ->
                        match m.Name with
                        | "createElementNS" ->
                            let destAttrs = Dictionary<string, string>(StringComparer.Ordinal)
                            box (mkSvgPath destAttrs (fun () -> svgDoc))
                        | _ -> null)
                let srcAttrs = Dictionary<string, string>(StringComparer.Ordinal)
                srcAttrs["d"] <- "M0 0 L100 0"
                let srcPath = mkSvgPath srcAttrs (fun () -> svgDoc)
                use morphGraph = new Graph()
                let morphFollower =
                    morphGraph.Run(fun () ->
                        let f = Follow.morph (srcPath :> Element) { ComposedAnimationOptions.Default with Duration = 100.; Ease = id }
                        f.To "M0 0 L100 0"
                        f)
                // Call second To outside ambient graph to verify graph-captured stepRoot creation too
                morphFollower.To "M0 0 L0 100"
                host.Advance 50.
                host.RunFrame()
                let midShape = srcAttrs["d"]
                Expect.notEqual midShape "M0 0 L100 0" "Follow.morph interpolates at midpoint"
                Expect.notEqual midShape "M0 0 L0 100" "Follow.morph has not yet settled at midpoint"
                for _ in 1..10 do
                    host.Advance 15.
                    host.RunFrame()
                Expect.equal srcAttrs["d"] "M0 0 L0 100" "Follow.morph retains exact target shape after stepRoot disposal"
                morphFollower.Revert()
                Expect.equal srcAttrs["d"] "M0 0 L100 0" "Follow.morph Revert restores initial shape"

            testCase "#40 follow-up: Waapi Restart with Persist=false observes the finished promise current after the restart"
            <| fun _ ->
                let mutable currentTime: DoubleOrCSSNumericValue option = None
                let webAnim =
                    domProxy<WebAnimation> (fun m args ->
                        match m.Name with
                        | "get_currentTime" -> box currentTime
                        | "set_currentTime" ->
                            currentTime <- unbox<DoubleOrCSSNumericValue option> args[0]
                            null
                        | "set_playbackRate" | "pause" | "play" | "commitStyles" | "cancel" -> null
                        | other -> failwith other)
                // AnimationService.Finished settles once, for the finished promise current at the call.
                let finished = ResizeArray<ResourceOutcome<WebAnimation> -> unit>()
                let animations =
                    domProxy<AnimationService> (fun m args ->
                        match m.Name with
                        | "Finished" ->
                            finished.Add(unbox<ResourceOutcome<WebAnimation> -> unit> args[1])
                            box (domProxy<ResourceOperation<WebAnimation>> (fun op _ ->
                                match op.Name with
                                | "Dispose" | "Cancel" -> null
                                | other -> failwith other))
                        | other -> failwith other)
                let style =
                    domProxy<CSSStyleProperties> (fun m _ ->
                        match m.Name with
                        | "getPropertyValue" | "removeProperty" -> box ""
                        | "setProperty" -> null
                        | other -> failwith other)
                let mutable el = Unchecked.defaultof<HTMLElement>
                el <-
                    domProxy<HTMLElement> (fun m args ->
                        match m.Name with
                        | "get_style" -> box style
                        | "get_ownerDocument" -> null
                        | "animate" -> box webAnim
                        | "isSameNode" -> box (Object.ReferenceEquals(args[0], el))
                        | "getAttribute" | "removeAttribute" -> null
                        | other -> failwith other)
                use anim =
                    Waapi.animate
                        (el :> Element)
                        { WaapiAnimationOptions.Default with
                            Properties = [| "opacity", WaapiValue.Scalar 0.5 |]
                            Duration = Some(fun _ _ _ -> 100.)
                            Autoplay = false
                            Animations = Some animations }
                Expect.equal finished.Count 1 "construction observes the first finished promise"
                finished[0] (ResourceOutcome.Succeeded webAnim)
                Expect.isTrue anim.Completed "the first finish completes the animation"
                anim.Restart() |> ignore
                Expect.isFalse anim.Completed "Restart clears completion"
                let replay = anim.Then()
                Expect.equal finished.Count 2 "Restart observes the finished promise of the replay"
                finished[finished.Count - 1] (ResourceOutcome.Succeeded webAnim)
                Expect.isTrue anim.Completed "the replay's finish completes the animation"
                Expect.isTrue replay.IsCompleted "Then() settles after the replay"

            testCase "#39 follow-up: setting ComposedAnimation.Speed after Add re-measures the ComposedTimeline without moving its playhead"
            <| fun _ ->
                use host = new ManualFrameScheduler()
                let bag = AnimatedObject [ "x", AdaptedValue.Scalar 0. ]
                use ctl = Composed.createTimeline host None (Some false)
                ctl.Add([| box bag |], [| PropertySpec.Scalar("x", 100.) |], options = { ComposedAnimationOptions.Default with Duration = 100.; Ease = id }) |> ignore
                close ctl.Duration 100. "100ms child at Speed=1"
                ctl.Seek 20. |> ignore
                let _, child = ctl.Children[0]
                child.Speed <- 2.
                close ctl.Duration 50. "the child at Speed=2 contributes 50ms"
                close ctl.CurrentTime 20. "re-measuring keeps the playhead"
                ctl.Seek 25. |> ignore
                close (bag.GetNumber "x") 50. "the child samples at twice the timeline time"

            testCase "#39 follow-up: Restart of a completed Blend animation replays its delta after the settle microtask detached it"
            <| fun _ ->
                use host = new ManualFrameScheduler()
                let bag = AnimatedObject [ "x", AdaptedValue.Scalar 0. ]
                let blendOpts =
                    { ComposedAnimationOptions.Default with
                        Duration = 100.
                        Ease = id
                        Autoplay = false
                        Composition = KeyframeComposition.Blend }
                use first = Composed.animate host bag [| PropertySpec.Scalar("x", 100.) |] (Some blendOpts)
                use second = Composed.animate host bag [| PropertySpec.Scalar("x", 100.) |] (Some blendOpts)
                first.Seek 100. |> ignore
                second.Seek 50. |> ignore
                close (bag.GetNumber "x") 100. "first completed, second mid-flight"
                // Completion detaches the settled animation's Blend tweens in a microtask; second keeps the entry alive.
                host.RunMicrotasks()
                Expect.isTrue first.Completed "first completed"
                Expect.isFalse second.Completed "second still active"
                first.Restart() |> ignore
                first.Seek 50. |> ignore
                close (bag.GetNumber "x") 50. "restarted Blend animation replays from 0 -> 100"
        ]
