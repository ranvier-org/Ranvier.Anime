module Ranvier.Anime.Tests.NumericCompleteFaultTests

open Expecto
open Ranvier.Dom
open Ranvier.Anime

let numericCompleteFaultTests = testList "numeric complete fault" [
    testCase "a fault in Complete cannot restore through the cancellation pause callback" <| fun _ ->
        use scheduler = new ManualFrameScheduler()
        let mutable value = 10.
        let mutable writes = 0
        let mutable armed = false
        let mutable current : NumericAnimation option = None
        use animation = new NumericAnimation(scheduler,(fun () -> value),(fun next -> writes<-writes+1;value<-next),110.,100.,
            onBeforeUpdate=(fun _ ->
                if armed then
                    current.Value.Resume()
                    failwith "complete callback fault"),
            onPause=(fun _ -> current.Value.Revert()))
        current <- Some animation
        animation.Seek 50.
        animation.Resume()
        let before = writes
        armed <- true
        Expect.throws(fun () -> animation.Complete()) "complete fault"
        Expect.equal writes before "no restoration from fault pause"
        Expect.isTrue animation.Cancelled "fault remains terminal"
    testCase "successful Complete still permits later Revert" <| fun _ ->
        use scheduler = new ManualFrameScheduler()
        let mutable value = 10.
        use animation = new NumericAnimation(scheduler,(fun () -> value),(fun next -> value<-next),110.,100.)
        animation.Complete()
        animation.Revert()
        Expect.equal value 10. "successful completion preserves original snapshot"
    testCase "Revert from onComplete remains admitted before completion cancellation" <| fun _ ->
        use scheduler = new ManualFrameScheduler()
        let mutable value = 10.
        let mutable current : NumericAnimation option = None
        use animation = new NumericAnimation(scheduler,(fun () -> value),(fun next -> value<-next),110.,100.,
            onComplete=(fun _ -> current.Value.Revert()))
        current <- Some animation
        animation.Complete()
        Expect.equal value 10. "completion callback restored"
        Expect.isTrue animation.Cancelled "revert terminal"
]
