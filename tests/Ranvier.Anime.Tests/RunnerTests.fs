module Ranvier.Anime.Tests.RunnerTests

open System
open Expecto
open Ranvier
open Ranvier.Dom
open Ranvier.Anime

let tests =
    testList "numeric runner" [
        testCase "a quadratic easing changes the rendered midpoint" <| fun _ ->
            let clock = new ManualFrameScheduler()
            let mutable value = 0.
            use animation = Animation.animateWith (Ease.named "inQuad") clock (fun () -> value) (fun next -> value <- next) 100. 100.
            clock.Advance 50.
            clock.RunFrame()
            Expect.equal value 25. "quadratic midpoint"

        testCase "custom easing is applied at initialization and completion like upstream" <| fun _ ->
            let clock = new ManualFrameScheduler()
            let mutable value = 0.
            use animation = Animation.animateWith (fun _ -> 1.25) clock (fun () -> value) (fun next -> value <- next) 100. 100.
            Expect.equal value 125. "upstream initial custom easing"
            clock.Advance 50.
            clock.RunFrame()
            Expect.equal value 125. "custom overshoot"
            clock.Advance 50.
            clock.RunFrame()
            Expect.equal value 125. "upstream completion custom easing"

        testCase "cancellation inside easing prevents the interrupted sample write" <| fun _ ->
            let clock = new ManualFrameScheduler()
            let values = ResizeArray<float>()
            let mutable current: NumericAnimation option = None
            let ease time =
                current |> Option.iter(fun animation -> animation.Cancel())
                time
            use animation = Animation.animateWith ease clock (fun () -> 0.) values.Add 100. 100.
            current <- Some animation
            let countBeforeFrame = values.Count
            clock.Advance 50.
            clock.RunFrame()
            Expect.equal values.Count countBeforeFrame "cancelled easing cannot write"

        testCase "a seek inside easing supersedes the interrupted frame" <| fun _ ->
            let clock = new ManualFrameScheduler()
            let mutable value = 0.
            let mutable current: NumericAnimation option = None
            let mutable seekOnce = true
            let ease time =
                if time = 0.5 && seekOnce then
                    seekOnce <- false
                    current |> Option.iter(fun animation -> animation.Seek 100.)
                time
            use animation = Animation.animateWith ease clock (fun () -> value) (fun next -> value <- next) 100. 100.
            current <- Some animation
            clock.Advance 50.
            clock.RunFrame()
            Expect.equal value 100. "outer frame must preserve the inner seek result"
            Expect.isTrue animation.Completed "seek completed the animation"

        testCase "finite opposite endpoints have a finite midpoint" <| fun _ ->
            let clock = new ManualFrameScheduler()
            let mutable value = -1e308
            use animation = Animation.animate clock (fun () -> value) (fun next -> value <- next) 1e308 100.
            clock.Advance 50.
            clock.RunFrame()
            Expect.equal value 0. "opposite finite endpoints meet at zero"

        testCase "linear frames render the midpoint and finish exactly" <| fun _ ->
            let clock = new ManualFrameScheduler()
            let mutable value = 0.
            use animation = Animation.animate clock (fun () -> value) (fun next -> value <- next) 100. 100.
            clock.Advance 25.
            clock.RunFrame()
            Expect.equal value 25. "quarter duration"
            clock.Advance 75.
            clock.RunFrame()
            Expect.equal value 100. "endpoint"
            Expect.isTrue animation.Completed "finished"
            Expect.isFalse animation.Playing "frame loop ended"

        testCase "pause and resume preserve elapsed time" <| fun _ ->
            let clock = new ManualFrameScheduler()
            let mutable value = 0.
            use animation = Animation.animate clock (fun () -> value) (fun next -> value <- next) 100. 100.
            clock.Advance 25.
            clock.RunFrame()
            animation.Pause()
            clock.Advance 200.
            clock.RunFrame()
            Expect.equal value 25. "paused"
            animation.Play()
            clock.Advance 25.
            clock.RunFrame()
            Expect.equal value 50. "resumed remaining time"

        testCase "seek renders immediately and retarget starts at the rendered value" <| fun _ ->
            let clock = new ManualFrameScheduler()
            let mutable value = 0.
            use animation = Animation.animate clock (fun () -> value) (fun next -> value <- next) 100. 100.
            animation.Seek 25.
            Expect.equal value 25. "seek"
            animation.Retarget 125.
            clock.Advance 50.
            clock.RunFrame()
            Expect.equal value 75. "new target uses current value"

        testCase "owner disposal stops later frames" <| fun _ ->
            use graph = new Graph()
            use activation = graph.Activate()
            let clock = new ManualFrameScheduler()
            let values = ResizeArray<float>()
            let owner = Api.createRoot(fun owner ->
                Animation.animate clock (fun () -> 0.) values.Add 100. 100.
                |> Animation.own |> ignore
                owner)
            clock.Advance 25.
            clock.RunFrame()
            owner.Dispose()
            clock.Advance 50.
            clock.RunFrame()
            Expect.sequenceEqual values [0.; 25.] "initialization and the live frame wrote"

        testCase "a setter can cancel during its frame" <| fun _ ->
            let clock = new ManualFrameScheduler()
            let values = ResizeArray<float>()
            let mutable current : NumericAnimation option = None
            let write value =
                values.Add value
                current |> Option.iter(fun animation -> animation.Cancel())
            use animation = Animation.animate clock (fun () -> 0.) write 100. 100.
            current <- Some animation
            clock.Advance 25.
            clock.RunFrame()
            clock.Advance 25.
            clock.RunFrame()
            Expect.sequenceEqual values [0.; 25.] "cancel prevents requeue"

        testCase "invalid input fails before mutation" <| fun _ ->
            let clock = new ManualFrameScheduler()
            let mutable writes = 0
            for target, duration in [100., nan; 100., -1.; infinity, 100.] do
                Expect.throwsT<ArgumentException>
                    (fun () -> Animation.animate clock (fun () -> 0.) (fun _ -> writes <- writes + 1) target duration |> ignore)
                    "invalid timing or endpoint"
            clock.Advance 100.
            clock.RunFrame()
            Expect.equal writes 0 "invalid animation never writes"
    ]
