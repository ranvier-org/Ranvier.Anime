module Ranvier.Anime.Tests.EngineAndPromiseTests

open System
open Expecto
open Ranvier.Dom
open Ranvier.Anime

let tests =
    testList "engine public API, promises and timeline controls" [
        testCase "Engine.forScheduler canonicalizes per host queue and manages timeUnit and precision" <| fun _ ->
            use host = new ManualFrameScheduler()
            use rootA = EngineFrames.createRoot host
            use rootB = EngineFrames.createRoot host
            let eng1 = Engine.forScheduler host
            let eng2 = Engine.forScheduler rootA
            let eng3 = Engine.forScheduler rootB
            Expect.isTrue (Object.ReferenceEquals(eng1, eng2)) "canonical across root A"
            Expect.isTrue (Object.ReferenceEquals(eng1, eng3)) "canonical across root B"
            Expect.equal (eng1.TimeUnit.ToString()) "ms" "default timeUnit is ms"
            Expect.equal eng1.Precision (Some 4u) "default precision is 4"
            let origDefaultDuration = eng1.Defaults.Animation.Duration
            try
                eng1.TimeUnit <- EngineTimeUnit.Parse "s"
                Expect.isTrue eng1.Seconds "seconds enabled"
                Expect.floatClose Accuracy.veryHigh eng1.Defaults.Animation.Duration (origDefaultDuration * 0.001) "default duration scaled to seconds"
                Expect.equal eng1.TickThreshold 0.2 "tickThreshold scaled to seconds"
                eng1.Precision <- Some 1u
                Expect.equal DeclarationAnimationOptions.Default.Precision (Some 1u) "DeclarationAnimationOptions inherits engine precision"
                Expect.equal KeyframeAnimationOptions.Default.Precision (Some 1u) "KeyframeAnimationOptions inherits engine precision"
            finally
                eng1.TimeUnit <- EngineTimeUnit.Milliseconds
                eng1.Precision <- Some 4u
            Expect.floatClose Accuracy.veryHigh eng1.Defaults.Animation.Duration origDefaultDuration "default duration restored to ms"

        testCase "Ease.parse and Ease.tryParseString match upstream parser behavior" <| fun _ ->
            let linear = Ease.parse "linear"
            Expect.equal (linear 0.37) 0.37 "linear identity"
            let quad = Ease.parse "inQuad"
            Expect.floatClose Accuracy.veryHigh (quad 0.5) 0.25 "inQuad midpoint"
            let paramPower = Ease.parseStrict "in(3)"
            Expect.floatClose Accuracy.veryHigh (paramPower 0.5) 0.125 "in(3) midpoint"
            let paramBack = Ease.parseStrict "outBack(2.5)"
            let expectedBack = Ease.create (EaseFamily.Back 2.5) EaseDirection.Out
            Expect.floatClose Accuracy.veryHigh (paramBack 0.4) (expectedBack 0.4) "outBack(2.5)"
            let paramElastic = Ease.parseStrict "inOutElastic(1.5, 0.4)"
            let expectedElastic = Ease.create (EaseFamily.Elastic(1.5, 0.4)) EaseDirection.InOut
            Expect.floatClose Accuracy.veryHigh (paramElastic 0.35) (expectedElastic 0.35) "inOutElastic(1.5, 0.4)"
            let deprecatedFallback = Ease.parse "steps(4)"
            Expect.equal (deprecatedFallback 0.6) 0.6 "deprecated string syntax falls back to linear in parse"
            Expect.isNone (Ease.tryParseString "notAnEasing") "unknown string returns None"

        testCase "Timer, NumericAnimation, KeyframeAnimation and Timeline Then settle on completion and cancel cleanly" <| fun _ ->
            use host = new ManualFrameScheduler()
            let mutable timerResolved = 0.
            use timer = new Timer(host, 30u)
            let timerTask = timer.Then(fun self -> timerResolved <- self.CurrentTime)
            timer.Resume()
            host.Advance 16.; host.RunFrame()
            host.Advance 20.; host.RunFrame()
            Expect.isTrue timer.Completed "timer completed"
            Expect.equal timerResolved 30. "timer Then callback saw currentTime 30"
            Expect.isTrue timerTask.IsCompletedSuccessfully "timer task settled"

            let mutable immediateCalled = false
            let immediateTask = timer.Then(fun _ -> immediateCalled <- true)
            Expect.isTrue immediateCalled "already-completed timer resolves Then immediately"
            Expect.isTrue immediateTask.IsCompletedSuccessfully "immediate task settled"

            use cancelledTimer = new Timer(host, 100u)
            let mutable shouldNotRun = false
            let cancelledTask = cancelledTimer.Then(fun _ -> shouldNotRun <- true)
            cancelledTimer.Resume()
            cancelledTimer.Cancel()
            Expect.isFalse shouldNotRun "cancelled timer does not invoke Then callback"
            Expect.isTrue cancelledTask.IsCanceled "cancelled timer task is Canceled"

            let mutable numVal = 0.
            let mutable numResolved = 0.
            use numAnim = new NumericAnimation(host, (fun () -> numVal), (fun v -> numVal <- v), 100., 30u)
            let numTask = numAnim.Then(fun a -> numResolved <- a.CurrentTime)
            numAnim.Reset(); numAnim.Resume()
            host.Advance 20.; host.RunFrame()
            host.Advance 20.; host.RunFrame()
            Expect.equal numResolved 30. "NumericAnimation Then saw 30"
            Expect.isTrue numTask.IsCompletedSuccessfully "NumericAnimation task settled"

            let prop = NumericProperty((fun () -> numVal), (fun v -> numVal <- v))
            let mutable kfResolved = 0.
            use kfAnim = Keyframes.Numeric(host, prop, [| Keyframe.toValue 200. |], duration = 30u)
            let kfTask = kfAnim.Then(fun a -> kfResolved <- a.Playback.CurrentTime)
            host.Advance 20.; host.RunFrame()
            host.Advance 20.; host.RunFrame()
            Expect.equal kfResolved 30. "KeyframeAnimation Then saw 30"
            Expect.isTrue kfTask.IsCompletedSuccessfully "KeyframeAnimation task settled"

            let mutable tlResolved = 0.
            use tl = new Timeline(host)
            tl.Animate(prop, 50., 15u).Animate(prop, 100., 15u) |> ignore
            let tlTask = tl.Then(fun self -> tlResolved <- self.CurrentTime)
            tl.Reset(); tl.Resume()
            host.Advance 20.; host.RunFrame()
            host.Advance 20.; host.RunFrame()
            Expect.equal tlResolved 30. "Timeline Then saw 30"
            Expect.isTrue tlTask.IsCompletedSuccessfully "Timeline task settled"

        testCase "Timeline Labels, Stretch, Remove and property setters preserve proportional timing" <| fun _ ->
            use host = new ManualFrameScheduler()
            let mutable v1 = 0.
            let mutable v2 = 0.
            let p1 = NumericProperty((fun () -> v1), (fun x -> v1 <- x))
            let p2 = NumericProperty((fun () -> v2), (fun x -> v2 <- x))
            use tl = new Timeline(host)
            tl.Label("mid", TimelinePosition.At 50.)
              .Animate(p1, 100., 50u, from = 0.)
              .Animate(p2, 200., 50u, from = 0., position = TimelinePosition.Expression "mid") |> ignore
            Expect.equal tl.Duration 100. "initial duration"
            Expect.equal tl.Labels["mid"] 50. "initial label"
            tl.Stretch 200u |> ignore
            Expect.equal tl.Duration 200. "stretched duration"
            Expect.equal tl.Labels["mid"] 100. "stretched label"
            tl.Progress <- 0.25
            Expect.floatClose Accuracy.high v1 50. "p1 at stretched quarter"
            Expect.floatClose Accuracy.high v2 0. "p2 not started at 50ms"
            tl.CurrentTime <- 150.
            Expect.floatClose Accuracy.high v1 100. "p1 finished"
            Expect.floatClose Accuracy.high v2 100. "p2 at midpoint of [100..200]"

            use synced = new Timer(host, 40u)
            use tlRemove = new Timeline(host)
            tlRemove.Sync(synced, TimelinePosition.At 0.) |> ignore
            Expect.equal tlRemove.Duration 40. "synced duration"
            tlRemove.Remove(box synced) |> ignore
            Expect.isTrue tlRemove.Cancelled "removing sole child cancels timeline"
    ]
