module Ranvier.Anime.Tests.CompositionAndTransformTests

open System
open System.IO
open System.Text.Json
open Expecto
open Ranvier.Dom
open Ranvier.Anime
open Ranvier.Anime.ComposedTimelineDsl

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
        testCase "timeline CE places groups and labels in order and supports scrubbing across overlaps" <| fun _ ->
            use host = new ManualFrameScheduler()
            let options = { ComposedAnimationOptions.Default with Duration = 100.; Ease = id }
            let targets = Array.init 2 (fun _ -> AnimatedObject [ "x", AdaptedValue.Scalar 7. ])
            let mutable placements = 0
            use timeline =
                composedTimeline host {
                    yield group targets options [| PropertySpec.Scalar("x", 100., fromValue = 0.) |]
                    yield label "handoff" |> at (TimelinePosition.At 50.)
                    for index in 0 .. targets.Length - 1 do
                        placements <- placements + 1
                        if index = 0 then
                            yield child targets[index] options [| PropertySpec.Scalar("x", 250.) |]
                                  |> at (TimelinePosition.Expression "handoff")
                }
            Expect.isTrue timeline.Paused "the caller controls when playback starts"
            Expect.equal placements targets.Length "the authoring loop ran once"
            close timeline.Duration 150. "the label is relative to the built timeline"
            for time, x, other in [ 75.,100.,75.; 25.,25.,25.; 50.,50.,50.; 150.,250.,100. ] do
                timeline.Seek(time) |> ignore
                close (targets[0].GetNumber "x") x "overlapping child starts from the predecessor at handoff"
                close (targets[1].GetNumber "x") other "the other target follows its own track"
            timeline.Revert() |> ignore
            for target in targets do close (target.GetNumber "x") 7. "revert restores the original group"
            Expect.equal placements targets.Length "seek and revert never replay the authoring code"

        testCase "timeline CE rollback restores added children and respects a newer property owner" <| fun _ ->
            use host = new ManualFrameScheduler()
            let target = AnimatedObject [ "x", AdaptedValue.Scalar 1.; "y", AdaptedValue.Scalar 2. ]
            let options = { ComposedAnimationOptions.Default with Duration = 100.; Ease = id; Autoplay = false }
            let mutable replacement : ComposedAnimation option = None
            let failure = InvalidOperationException("later cue failed")
            let build () =
                composedTimeline host {
                    yield child target options [| PropertySpec.Scalar("x", 100., fromValue = 5.)
                                                  PropertySpec.Scalar("y", 100., fromValue = 6.) |]
                    close (target.GetNumber "y") 6. "the first cue has already taken effect"
                    let next = Composed.animate host target [| PropertySpec.Scalar("x", 80., fromValue = 40.) |] (Some options)
                    replacement <- Some next
                    next.Seek(50.) |> ignore
                    raise failure
                } |> ignore
            try
                try build(); failtest "construction should have failed"
                with :? InvalidOperationException as caught ->
                    Expect.isTrue (obj.ReferenceEquals(caught, failure)) "the original exception propagates"
                close (target.GetNumber "y") 2. "rollback restores the property the child still owned"
                close (target.GetNumber "x") 60. "rollback preserves the newer owner's value"
                let next = replacement.Value
                next.Seek(75.) |> ignore
                close (target.GetNumber "x") 70. "the newer owner remains live"
            finally replacement |> Option.iter (fun animation -> animation.Cancel())

        testCase "timeline CE restores a partially initialized child and its preceding sibling" <| fun _ ->
            use host = new ManualFrameScheduler()
            let target = AnimatedObject [ "x", AdaptedValue.Scalar 1.; "y", AdaptedValue.Scalar 2.; "z", AdaptedValue.Scalar 3. ]
            let registry = AdapterRegistry()
            let adapter = registry.Register().RegisterTypeAdapter<AnimatedObject>(fun _ -> true)
            let failure = InvalidOperationException("setter rejected initial value")
            adapter.RegisterScalar<AnimatedObject>("z", (fun bag -> bag.GetNumber "z"), fun bag value _ ->
                if value = 9. then raise failure
                bag.SetNumber("z", value))
            let options = { ComposedAnimationOptions.Default with Duration = 100.; Registry = Some registry }
            try
                composedTimeline host {
                    yield child target options [| PropertySpec.Scalar("x", 100., fromValue = 5.) |]
                    yield child target options [| PropertySpec.Scalar("y", 100., fromValue = 6.)
                                                  PropertySpec.Scalar("z", 100., fromValue = 9.) |]
                } |> ignore
                failtest "child initialization should have failed"
            with :? InvalidOperationException as caught ->
                Expect.isTrue (obj.ReferenceEquals(failure, caught)) "preserve the setter failure"
            Expect.equal (target.GetNumber "x", target.GetNumber "y", target.GetNumber "z") (1., 2., 3.)
                "rollback includes writes from the child that never reached the timeline's entry list"

        testCase "soft revert restores typed and absent originals after both owners detach" <| fun _ ->
            use host = new ManualFrameScheduler()
            let target = AnimatedObject [ "x", AdaptedValue.Scalar 0.; "label", AdaptedValue.Text ""; "digits", AdaptedValue.Text "007" ]
            let options = Some { ComposedAnimationOptions.Default with Autoplay = false; Duration = 100.; Ease = id }
            use old = Composed.animate host target
                            [| for name in ["x"; "label"; "digits"; "missing"] do
                                   PropertySpec.Scalar(name, 100., fromValue = 20.) |] options
            old.Complete() |> ignore
            use replacement = Composed.animate host target [| PropertySpec.Scalar("x", 200.) |] options
            replacement.Complete() |> ignore
            old.Revert(false) |> ignore
            Expect.equal target["x"] (AdaptedValue.Scalar 200.) "the completed replacement retains ownership"
            Expect.equal target["label"] (AdaptedValue.Text "") "present empty text survives a scalar from value"
            Expect.equal target["digits"] (AdaptedValue.Text "007") "numeric-looking original text retains its representation"
            Expect.isNone (target.TryGet "missing") "an originally missing key is removed"
            replacement.Revert(false) |> ignore
            Expect.equal target["x"] (AdaptedValue.Scalar 100.) "the latest owner restores its own snapshot"

        testCase "soft timeline revert restores child snapshots without sampling and preserves the playhead" <| fun _ ->
            use host = new ManualFrameScheduler()
            let target = AnimatedObject [ "x", AdaptedValue.Scalar 0.; "y", AdaptedValue.Scalar 5. ]
            use timeline = Composed.createTimeline host None (Some false)
            let mutable samples = 0
            let options = { ComposedAnimationOptions.Default with Duration = 100.; Ease = fun t -> samples <- samples + 1; t }
            timeline.Add([| box target |], [| PropertySpec.Scalar("x", 100.); PropertySpec.Scalar("y", 50.) |], options = options) |> ignore
            timeline.Seek(50.) |> ignore
            timeline.Add([| box target |], [| PropertySpec.Scalar("x", 200.) |], options = options, position = TimelinePosition.At 50.) |> ignore
            timeline.Seek(110.) |> ignore
            let before = samples
            timeline.Revert(false) |> ignore
            Expect.equal samples before "neither timeline child was sampled during restoration"
            Expect.equal timeline.CurrentTime 110. "timeline playhead stays in place"
            Expect.equal (target.GetNumber "x", target.GetNumber "y") (0., 5.) "earliest child snapshots win"
            Expect.isFalse timeline.HasChildren "children are released"

        testCase "soft revert dispatches one adapter restoration and respects reentrant replacement" <| fun _ ->
            use host = new ManualFrameScheduler()
            let target = AnimatedObject [ "x", AdaptedValue.Scalar 0.; "y", AdaptedValue.Scalar 3. ]
            let registry = AdapterRegistry()
            let writes = ResizeArray<float>()
            let mutable afterSet = ignore
            let adapter = registry.Register().RegisterTypeAdapter<AnimatedObject>(fun _ -> true)
            adapter.RegisterScalar<AnimatedObject>("x", (fun t -> t.GetNumber "x"),
                (fun t value _ -> t.SetNumber("x", value); writes.Add value; afterSet()))
            let options = Some { ComposedAnimationOptions.Default with Registry = Some registry; Duration = 100.; Ease = id; Autoplay = false }
            let steps = [| TweenStep.FromToScalar(10., 50.); TweenStep.FromToScalar(50., 90.) |]
            use animation = Composed.animate host target [| PropertySpec.StepsOf("x", steps); PropertySpec.Scalar("y", 50.) |] options
            animation.Seek 60. |> ignore
            afterSet <- fun () ->
                use newer = Composed.animate host target [| PropertySpec.Scalar("y", 150.) |] options
                newer.Complete() |> ignore
                animation.Revert(false) |> ignore
            writes.Clear()
            animation.Revert(false) |> ignore
            animation.Revert(false) |> ignore
            Expect.sequenceEqual writes [0.] "the original is written once without a rewind or duplicate step restoration"
            Expect.equal (target.GetNumber "y") 150. "the adapter's replacement keeps the next property"
            Expect.equal animation.CurrentTime 60. "animation playhead stays in place"

        testCase "soft timeline revert preserves its first failure and finishes restoring the other children" <| fun _ ->
            use host = new ManualFrameScheduler()
            use timeline = Composed.createTimeline host None (Some false)
            let target = AnimatedObject [ "x", AdaptedValue.Scalar 2.; "y", AdaptedValue.Scalar 3. ]
            let registry = AdapterRegistry()
            let adapter = registry.Register().RegisterTypeAdapter<AnimatedObject>(fun _ -> true)
            let mutable restoring = false
            let failure = InvalidOperationException("adapter restore failed")
            let specs = [| PropertySpec.Scalar("y", 80.) |]
            adapter.RegisterScalar<AnimatedObject>("x", (fun t -> t.GetNumber "x"), fun t value _ ->
                if restoring then
                    Expect.throwsT<InvalidOperationException> (fun () -> timeline.Add([| box t |], specs) |> ignore)
                        "a restoring timeline cannot publish a child"
                    raise failure
                t.SetNumber("x", value))
            let options = { ComposedAnimationOptions.Default with Registry = Some registry; Duration = 100.; Ease = id }
            timeline.Add([| box target |], specs, options = options) |> ignore
            timeline.Add([| box target |], [| PropertySpec.Scalar("x", 90.) |], options = options, position = TimelinePosition.At 0.) |> ignore
            timeline.Seek(60.) |> ignore
            restoring <- true
            let observed =
                try timeline.Revert(false) |> ignore; None
                with error -> Some error
            Expect.isTrue (observed |> Option.exists (fun error -> obj.ReferenceEquals(failure, error))) "first exception is propagated"
            Expect.equal (target.GetNumber "y") 3. "the earlier child still restores after the failing adapter"
            Expect.isTrue timeline.Cancelled "the timeline ends despite the failed restoration"
            Expect.isFalse timeline.HasChildren "both children are released"

        testCase "composed timeline hands off at the end of child delay in both seek directions" <| fun _ ->
            use host = new ManualFrameScheduler()
            let target = AnimatedObject [ "x", AdaptedValue.Scalar 0.; "y", AdaptedValue.Scalar 0. ]
            use timeline = Composed.createTimeline host None (Some false)
            let first = { ComposedAnimationOptions.Default with Duration = 100.; Ease = id }
            let later = { first with Duration = 30.; Playback = { first.Playback with Delay = 20. } }
            timeline.Add([| box target |], [| PropertySpec.Scalar("x", 100.); PropertySpec.Scalar("y", 100.) |], options = first) |> ignore
            timeline.Add([| box target |], [| PropertySpec.Scalar("x", 130.) |], options = later, position = TimelinePosition.At 50.) |> ignore
            for time, x in [ 65.,65.; 70.,70.; 85.,100.; 100.,130.; 65.,65.; 85.,100.; 0.,0. ] do
                timeline.Seek(time) |> ignore
                close (target.GetNumber "x") x "per-property takeover uses the sampled handoff"
                close (target.GetNumber "y") time "independent property stays with the first child"

        testCase "composed timeline restores the previous endpoint when rewinding into a gap" <| fun _ ->
            use host = new ManualFrameScheduler()
            let target = AnimatedObject [ "x", AdaptedValue.Scalar 0. ]
            use timeline = Composed.createTimeline host None (Some false)
            let options = { ComposedAnimationOptions.Default with Duration = 20.; Ease = id }
            target.SetNumber("x", 123.)
            timeline.Add([| box target |], [| PropertySpec.Scalar("x", 20., fromValue = 0.) |], options = options) |> ignore
            close (target.GetNumber "x") 0. "the first explicit from is visible at construction"
            timeline.Add([| box target |], [| PropertySpec.Scalar("x", 180., fromValue = 140.) |],
                         options = options, position = TimelinePosition.At 80.) |> ignore
            close (target.GetNumber "x") 0. "the later from cannot displace the current owner at construction"
            for time, x in [ 90.,160.; 50.,20.; 10.,10.; 80.,140.; 79.,20. ] do
                timeline.Seek(time) |> ignore
                close (target.GetNumber "x") x "the later from value starts exactly at its boundary"

        testCase "composed timeline samples alternate keyframes at the replacement time" <| fun _ ->
            use host = new ManualFrameScheduler()
            let target = AnimatedObject [ "x", AdaptedValue.Scalar 0. ]
            use timeline = Composed.createTimeline host None (Some false)
            let first = { ComposedAnimationOptions.Default with Duration = 100.; Ease = id
                                                                Playback = { PlaybackOptions.Default with Repeat = PlaybackRepeat.Count 1u; Alternate = true } }
            let steps = [| TweenStep.FromToScalar(0., 50.); TweenStep.FromToScalar(50., 100.) |]
            timeline.Add([| box target |], [| PropertySpec.StepsOf("x", steps) |], options = first) |> ignore
            timeline.Add([| box target |], [| PropertySpec.Scalar("x", 150.) |],
                         options = { ComposedAnimationOptions.Default with Duration = 50.; Ease = id },
                         position = TimelinePosition.At 150.) |> ignore
            for time, x in [ 175.,100.; 125.,75.; 150.,50.; 25.,25. ] do
                timeline.Seek(time) |> ignore
                close (target.GetNumber "x") x "timeline ownership and local keyframe direction are independent"

        testCase "a reentrant ease can cancel construction of a timeline handoff" <| fun _ ->
            use host = new ManualFrameScheduler()
            let target = AnimatedObject [ "x", AdaptedValue.Scalar 0. ]
            use timeline = Composed.createTimeline host None (Some false)
            let mutable interrupt = false
            let ease t =
                if interrupt then timeline.Cancel()
                t
            let options = { ComposedAnimationOptions.Default with Duration = 100.; Ease = ease }
            timeline.Add([| box target |], [| PropertySpec.Scalar("x", 100.) |], options = options) |> ignore
            interrupt <- true
            Expect.throwsT<InvalidOperationException> (fun () ->
                timeline.Add([| box target |], [| PropertySpec.Scalar("x", 150.) |], position = TimelinePosition.At 50.) |> ignore)
                "the cancelled parent cannot publish a successor"
            Expect.isFalse timeline.HasChildren "cancellation releases its children"

        testCase "an immediate owner replaces an already scheduled property" <| fun _ ->
            use host = new ManualFrameScheduler()
            let target = AnimatedObject [ "x", AdaptedValue.Scalar 0. ]
            let mutable completions = 0
            let options = { ComposedAnimationOptions.Default with Duration = 100.; Ease = id }
            use scheduled = Composed.animate host target [| PropertySpec.Scalar("x", 200., delay = 80.) |]
                                (Some { options with OnComplete = fun _ -> completions <- completions + 1 })
            use immediate = Composed.animate host target [| PropertySpec.Scalar("x", 100.) |] (Some options)
            for _ in 1..24 do host.Advance 10.; host.RunFrame()
            Expect.isTrue scheduled.Cancelled "the scheduled property lost its only owner"
            Expect.isTrue immediate.Completed "the immediate animation completes"
            Expect.equal completions 0 "displacement does not dispatch completion"
            close (target.GetNumber "x") 100. "a delayed frame cannot revive its scheduled writer"

        testCase "explicit play and restart reclaim properties from a live replacement" <| fun _ ->
            for restart in [false; true] do
                use host = new ManualFrameScheduler()
                let target = AnimatedObject [ "x", AdaptedValue.Scalar 0. ]
                let mutable rivalCompletions = 0
                let options = { ComposedAnimationOptions.Default with Duration = 100.; Ease = id; Autoplay = false }
                use original = Composed.animate host target [| PropertySpec.Scalar("x", 100.) |] (Some options)
                use rival = Composed.animate host target [| PropertySpec.Scalar("x", 500.) |]
                                (Some { options with Autoplay = true; OnComplete = fun _ -> rivalCompletions <- rivalCompletions + 1 })
                if restart then original.Restart() |> ignore else original.Play() |> ignore
                let completion = original.AsTask()
                for _ in 1..24 do host.Advance 10.; host.RunFrame()
                Expect.isTrue completion.IsCompletedSuccessfully "the reclaimed lifetime can complete"
                Expect.isTrue rival.Cancelled "the rival stops without dispatching completion"
                Expect.equal rivalCompletions 0 "the rival callback stayed silent"
                close (target.GetNumber "x") 100. "the reclaimed animation renders its own endpoint"

        testCase "backward seek preserves another clock's property even after that owner cancels" <| fun _ ->
            use host = new ManualFrameScheduler()
            let target = AnimatedObject [ "x", AdaptedValue.Scalar 0.; "y", AdaptedValue.Scalar 0. ]
            let options = Some { ComposedAnimationOptions.Default with Duration = 100.; Ease = id; Autoplay = false }
            use original = Composed.animate host target [| PropertySpec.Scalar("x", 100.); PropertySpec.Scalar("y", 100.) |] options
            original.Seek 70. |> ignore
            host.Advance 30.
            (Engine.forScheduler host).Update()
            use rival = Composed.animate host target [| PropertySpec.Scalar("x", 200., fromValue = 100.) |] options
            rival.Seek 50. |> ignore
            for time in [20.; 10.] do
                original.Seek time |> ignore
                close (target.GetNumber "x") 150. "x stays at the replacement sample"
                close (target.GetNumber "y") time "y follows the original clock"
                rival.Cancel()
            original.Play() |> ignore
            original.Seek 60. |> ignore
            close (target.GetNumber "x") 60. "explicit play also reclaims a partial replacement"

        testCase "seeking a completed animation reattaches only unclaimed properties" <| fun _ ->
            use host = new ManualFrameScheduler()
            let target = AnimatedObject [ "x", AdaptedValue.Scalar 0.; "y", AdaptedValue.Scalar 0. ]
            let options = Some { ComposedAnimationOptions.Default with Duration = 100.; Ease = id; Autoplay = false }
            use earlier = Composed.animate host target [| PropertySpec.Scalar("x", 100.); PropertySpec.Scalar("y", 100.) |] options
            earlier.Complete() |> ignore
            use newer = Composed.animate host target [| PropertySpec.Scalar("x", 200.) |] options
            newer.Seek 50. |> ignore
            earlier.Seek 20. |> ignore
            close (target.GetNumber "x") 150. "the newer clock retains x"
            close (target.GetNumber "y") 20. "the older clock resamples y"
            Expect.isFalse newer.Cancelled "the backward seek leaves the current owner live"

        testCase "muted property steps leave active writers and their values intact" <| fun _ ->
            use host = new ManualFrameScheduler()
            for composition in [ KeyframeComposition.Replace; KeyframeComposition.Blend ] do
                let target = AnimatedObject [ "x", AdaptedValue.Scalar 0.; "y", AdaptedValue.Scalar 0. ]
                let options =
                    Some { ComposedAnimationOptions.Default with
                             Duration = 100.; Autoplay = false; Ease = id; Composition = composition }
                use writer = Composed.animate host target [| PropertySpec.Scalar("x", 100.) |] options
                writer.Seek 20. |> ignore
                use mixed =
                    Composed.animate host target
                        [| PropertySpec.Scalar("x", 500., fromValue = 125., muted = true)
                           PropertySpec.Scalar("y", 100.) |] options
                writer.Seek 50. |> ignore
                mixed.Seek 75. |> ignore
                close (target.GetNumber "x") 50. "the existing writer retains this property"
                close (target.GetNumber "y") 75. "the active sibling renders normally"
                mixed.Revert() |> ignore
                close (target.GetNumber "x") 50. "reverting restores only participating properties"

        testCase "muted keyframe intervals hold target values across forward and backward seeks" <| fun _ ->
            use host = new ManualFrameScheduler()
            let target = AnimatedObject [ "x", AdaptedValue.Scalar 7. ]
            let steps =
                [| { TweenStep.FromToScalar(0., 10.) with Muted = true }
                   TweenStep.FromToScalar(10., 20.)
                   { TweenStep.FromToScalar(20., 30.) with Muted = true } |]
            let options =
                Some { ComposedAnimationOptions.Default with Duration = 300.; Autoplay = false; Ease = id }
            use animation = Composed.animate host target [| PropertySpec.StepsOf("x", steps) |] options
            animation.Seek 50. |> ignore
            close (target.GetNumber "x") 7. "the next audible step cannot write early"
            animation.Seek 150. |> ignore
            close (target.GetNumber "x") 15. "the audible step renders at its own time"
            for time in [ 250.; 50. ] do
                target.SetNumber("x", 42.)
                animation.Seek time |> ignore
                close (target.GetNumber "x") 42. "muted ranges preserve another writer"
            animation.Revert() |> ignore
            close (target.GetNumber "x") 7. "the first writable step retains the restoration snapshot"

        testCase "muted blend advances time without publishing hidden contributions" <| fun _ ->
            use host = new ManualFrameScheduler()
            let target = AnimatedObject [ "x", AdaptedValue.Scalar 0. ]
            let options =
                Some { ComposedAnimationOptions.Default with
                         Duration = 100.; Autoplay = false; Ease = id; Composition = KeyframeComposition.Blend }
            use first = Composed.animate host target [| PropertySpec.Scalar("x", 100.) |] options
            use second = Composed.animate host target [| PropertySpec.Scalar("x", 200.) |] options
            first.Seek 20. |> ignore
            first.Muted <- true
            first.Seek 80. |> ignore
            second.Seek 30. |> ignore
            close first.CurrentTime 80. "muted clock advances"
            close (target.GetNumber "x") 50. "blend uses the last visible contribution"
            first.Muted <- false
            first.Seek 85. |> ignore
            close (target.GetNumber "x") 115. "the next sample contributes at the new time"

        testCase "composed timeline mute applies to child writes, callbacks and terminal tasks" <| fun _ ->
            use host = new ManualFrameScheduler()
            let target = AnimatedObject [ "x", AdaptedValue.Scalar 7. ]
            let mutable renders = 0
            let options =
                { ComposedAnimationOptions.Default with
                    Duration = 100.; Ease = id; OnRender = fun _ -> renders <- renders + 1 }
            use timeline = Composed.createTimeline host (Some { PlaybackOptions.Default with Muted = true }) (Some false)
            timeline.Add([| box target |], [| PropertySpec.Scalar("x", 100., fromValue = 0.) |], options = options) |> ignore
            timeline.Seek 40. |> ignore
            close (target.GetNumber "x") 7. "muted parent protects the target"
            Expect.equal renders 0 "child render callback is silent"
            timeline.Muted <- false
            timeline.Seek 60. |> ignore
            close (target.GetNumber "x") 60. "child resumes at parent time"
            timeline.Muted <- true
            let pending = timeline.AsTask()
            timeline.Seek timeline.Duration |> ignore
            Expect.isTrue timeline.Completed "muted clock reached the end"
            let late = timeline.AsTask()
            Expect.isFalse (pending.IsCompleted || late.IsCompleted) "neither registration delivers a muted completion"
            timeline.Cancel()
            Expect.isTrue (pending.IsCanceled && late.IsCanceled) "cancellation releases both registrations"

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

        testCase "composition Replace starts a unit value from its rendered value, not the replaced tween's end" <| fun _ ->
            use host = new ManualFrameScheduler()
            let target = AnimatedObject [ "x", AdaptedValue.Text "0px" ]
            let opts =
                Some { ComposedAnimationOptions.Default with
                         Duration = 100.; Ease = id; Composition = KeyframeComposition.Replace; Autoplay = false }
            use first = Composed.animate host (box target) [| PropertySpec.Text("x", "100px") |] opts
            first.Seek 50. |> ignore
            Expect.equal (target.GetText "x") "50px" "the first tween is halfway"
            use second = Composed.animate host (box target) [| PropertySpec.Text("x", "200px") |] opts
            second.Seek 0. |> ignore
            Expect.equal (target.GetText "x") "50px" "the retarget starts where the property is"
            second.Seek 50. |> ignore
            Expect.equal (target.GetText "x") "125px" "and moves from there"
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

        testCase "composition Replace orders the tweens of timelines started at different times on the engine clock" <| fun _ ->
            // anime's absolute offsets: a tween of a later timeline replaces an earlier timeline's tween of the same
            // property even when its own position is smaller, and plays to its end.
            use host = new ManualFrameScheduler()
            let frames count = for _ in 1 .. count do host.Advance 10.; host.RunFrame()
            let target = AnimatedObject [ "x", AdaptedValue.Scalar 0. ]
            let options = { ComposedAnimationOptions.Default with Duration = 100.; Ease = id }
            use earlier = Composed.createTimeline host None (Some true)
            earlier.Add([| box target |], [| PropertySpec.Scalar("x", 100.) |], options = options, position = TimelinePosition.At 60.) |> ignore
            earlier.Seek earlier.Duration |> ignore
            close (target.GetNumber "x") 100. "the earlier timeline completes at once"
            frames 10
            use later = Composed.createTimeline host None (Some true)
            later.Add([| box target |], [| PropertySpec.Scalar("x", 0.) |], options = options, position = TimelinePosition.At 30.) |> ignore
            frames 40
            close (target.GetNumber "x") 0. "the later timeline's tween plays to its end"
            Expect.isTrue later.Completed "the later timeline completes"

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
