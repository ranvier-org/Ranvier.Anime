module Ranvier.Anime.Tests.ScopeFacadeClockTests

open Expecto
open Ranvier
open Ranvier.Dom
open Ranvier.Anime

let private options = { PlaybackOptions.Default with Repeat=PlaybackRepeat.Count 4u; Alternate=true }

let facadeClockTests = testList "scope facade clock forwarding" [
    testCase "numeric forwards the exact existing inner clock snapshot" <| fun _ ->
        use scheduler = new ManualFrameScheduler()
        scheduler.Advance 73.
        let mutable value = 0.
        use previous = new NumericAnimation(scheduler,(fun () -> value),(fun next -> value<-next),100.,100.,options=options)
        previous.Seek 125.
        let snapshot = previous.CaptureScopeTime()
        previous.Revert()
        scheduler.Advance 427.
        use next = new NumericAnimation(scheduler,(fun () -> value),(fun next -> value<-next),100.,200.,options=options)
        next.RestoreScopeTime snapshot
        Expect.equal next.CurrentTime 250. "changed duration"
        Expect.equal (next.CaptureScopeTime().Anchor) 73. "retained original anchor"
        Expect.equal (next.CaptureScopeTime().IterationProgress) snapshot.IterationProgress "directional progress"

    testCase "keyframe sequence forwards its installed driver clock" <| fun _ ->
        use scheduler = new ManualFrameScheduler()
        scheduler.Advance 73.
        let create duration =
            let animation = new KeyframeAnimation(0)
            let driver = new KeyframePlayback(scheduler,FrameProgram(0.,duration,0.,[||]),
                (fun _ _ _ -> ()),false,false,-1,PlaybackOptions.Default)
            animation.SetDriver driver
            animation.SetRestore(fun _ -> ())
            driver.Initialize()
            animation
        use previous = create 100.
        previous.Playback.Seek 37.
        let snapshot = previous.CaptureScopeTime()
        previous.Revert()
        scheduler.Advance 427.
        use next = create 200.
        next.RestoreScopeTime snapshot
        Expect.equal next.Playback.CurrentTime 74. "proportional setter"
        Expect.equal (next.CaptureScopeTime().Anchor) 73. "retained driver anchor"

    testCase "timeline forwards state while admitting normal child rendering" <| fun _ ->
        use scheduler = new ManualFrameScheduler()
        scheduler.Advance 73.
        use previous = new Timeline(scheduler,options=options)
        previous.AddTimer 100u |> ignore
        previous.Seek 125.
        let snapshot = previous.CaptureScopeTime()
        previous.Cancel()
        scheduler.Advance 427.
        use next = new Timeline(scheduler,options=options)
        let child = next.AddTimer 200u
        next.RestoreScopeTime snapshot
        Expect.equal next.CurrentTime 250. "timeline mapped playhead"
        Expect.equal (next.CaptureScopeTime().Anchor) 73. "timeline anchor"
        Expect.isGreaterThan child.CurrentTime 0. "child was rendered"
]
