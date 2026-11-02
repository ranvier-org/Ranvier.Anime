module Ranvier.Anime.Tests.RunnerTests

open System
open Expecto
open Ranvier
open Ranvier.Dom
open Ranvier.Anime

let tests =
    testList "numeric runner" [
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
            Expect.sequenceEqual values [25.] "only the live frame wrote"

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
            Expect.sequenceEqual values [25.] "cancel prevents requeue"

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
