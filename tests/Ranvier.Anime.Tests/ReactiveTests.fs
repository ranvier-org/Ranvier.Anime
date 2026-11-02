module Ranvier.Anime.Tests.ReactiveTests

open System
open Expecto
open Ranvier
open Ranvier.Dom
open Ranvier.Anime

[<System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)>]
let private disposedDriveReference clock =
    let drive = Reactive.drive clock 100. (fun () -> 0.) ignore
    drive.Dispose()
    WeakReference drive

let tests = testList "reactive drive" [
    testCase "explicitly disposed drives can be collected while their parent stays alive" <| fun _ ->
        use graph = new Graph()
        use activation = graph.Activate()
        let clock = new ManualFrameScheduler()
        let reference = disposedDriveReference clock
        GC.Collect()
        GC.WaitForPendingFinalizers()
        GC.Collect()
        Expect.isFalse reference.IsAlive "parent must release the disposed root"

    testCase "disposal by the initial reader prevents the initial write" <| fun _ ->
        use graph = new Graph()
        use activation = graph.Activate()
        let clock = new ManualFrameScheduler()
        let values = ResizeArray<float>()
        Api.createRoot(fun owner ->
            Reactive.drive clock 100. (fun () -> owner.Dispose(); 10.) values.Add |> ignore)
        clock.Advance 100.
        clock.RunFrame()
        Expect.isEmpty values "dead owner never writes"

    testCase "retargets from the rendered value and stops with its owner" <| fun _ ->
        use graph = new Graph()
        use activation = graph.Activate()
        let clock = new ManualFrameScheduler()
        let mutable value = -1.
        let mutable reads = 0
        let owner, signal = Api.createRoot(fun owner ->
            let signal = Api.createSignal 0.
            Reactive.drive clock 100. (fun () -> reads <- reads + 1; signal.Value) (fun next -> value <- next) |> ignore
            owner, signal)
        Expect.equal value 0. "initial seed is immediate"
        graph.Flush()
        signal.Value <- 100.
        graph.Flush()
        clock.Advance 25.
        clock.RunFrame()
        Expect.equal value 25. "quarter duration"
        signal.Value <- 200.
        graph.Flush()
        clock.Advance 50.
        clock.RunFrame()
        Expect.equal value 112.5 "retarget starts from 25"
        owner.Dispose()
        let previousReads = reads
        signal.Value <- 300.
        graph.Flush()
        clock.Advance 100.
        clock.RunFrame()
        Expect.equal value 112.5 "disposed owner prevents writes"
        Expect.equal reads previousReads "disposed effect stops observing"

    testCase "explicit disposal detaches the effect and pending animation" <| fun _ ->
        use graph = new Graph()
        use activation = graph.Activate()
        let clock = new ManualFrameScheduler()
        let signal = Api.createSignal 0.
        let values = ResizeArray<float>()
        use drive = Reactive.drive clock 100. (fun () -> signal.Value) values.Add
        graph.Flush()
        signal.Value <- 100.
        graph.Flush()
        drive.Dispose()
        signal.Value <- 200.
        graph.Flush()
        clock.Advance 100.
        clock.RunFrame()
        Expect.sequenceEqual values [0.] "only the seed was written"

    testCase "invalid duration fails before the initial write" <| fun _ ->
        use graph = new Graph()
        use activation = graph.Activate()
        let clock = new ManualFrameScheduler()
        let mutable writes = 0
        Expect.throwsT<ArgumentException>
            (fun () -> Reactive.drive clock nan (fun () -> 10.) (fun _ -> writes <- writes + 1) |> ignore)
            "invalid duration"
        Expect.equal writes 0 "validation precedes seeding"
]
