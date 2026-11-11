module Ranvier.Anime.Tests.ScrollTests

open System
open System.IO
open System.Text.Json
open Expecto
open Ranvier.Anime

let private fixture () = JsonDocument.Parse(File.ReadAllText(Path.Combine(Path.Combine(AppContext.BaseDirectory,"reference"),"scroll-reference.json")))
let private property name (value:JsonElement) = value.GetProperty(name:string)
let private number name value = (property name value).GetDouble()
let private boolean name value = (property name value).GetBoolean()
let private text name value = (property name value).GetString()
let private threshold (value:string) =
    let split = value.Split ' '
    { Container=split[0]; Target=split[1] }
let private close actual expected message = Expect.isLessThanOrEqual (abs(actual-expected)) 1e-8 message
let private geometry = { Offset=800.; TargetSize=100.; ContainerSize=600.; ScrollSize=2400. }

let tests =
    testList "scroll observation" [
        testCase "pinned source geometry including min max and relative anchors" <| fun _ ->
            use document = fixture()
            for sample in (property "geometry" document.RootElement).EnumerateArray() do
                use observer = new ScrollObserver((fun () -> 0.,false),(fun () -> {geometry with Offset=number "offset" sample}),
                    options=(fun () -> {ScrollOptions.Default with Enter=threshold(text "enter" sample); Leave=threshold(text "leave" sample); Sync=ScrollSync.Disabled}))
                observer.Refresh()
                close observer.OffsetStart (number "start" sample) "start"
                close observer.OffsetEnd (number "end" sample) "end"
                close observer.Distance (number "distance" sample) "distance"
                let coords = (property "coords" sample).EnumerateArray() |> Seq.map(fun value -> value.GetDouble()) |> Seq.toArray
                Array.iter2(fun actual expected -> close actual expected "coordinate") observer.Coordinates coords

        testCase "pinned source transitions callbacks seeks and state" <| fun _ ->
            use document = fixture()
            let mutable count = 0
            for trace in (property "traces" document.RootElement).EnumerateArray() do
                let mode = text "mode" trace
                let events = ResizeArray<string>()
                let writes = ResizeArray<float * bool>()
                let mutable scroll,prior,linkedProgress,backward = 0.,0.,0.,false
                let sync =
                    match mode with
                    | "disabled" -> ScrollSync.Disabled
                    | "methods" -> ScrollSync.Methods(Some ScrollCommand.Play,Some ScrollCommand.Pause)
                    | "directional" -> ScrollSync.DirectionalMethods(Some ScrollCommand.Play,Some ScrollCommand.Pause,Some ScrollCommand.Reverse,Some ScrollCommand.Reset)
                    | "linear" -> ScrollSync.Smooth 1.
                    | "smooth" -> ScrollSync.Smooth 0.5
                    | _ -> ScrollSync.Ease(fun p -> p*p)
                use observer = new ScrollObserver((fun () -> scroll,backward),(fun () -> geometry),
                    options=(fun () -> {ScrollOptions.Default with Sync=sync}),onEvent=(fun event _ -> events.Add(string event)))
                observer.Refresh()
                if mode <> "disabled" then
                    observer.Link {
                        Duration=(fun () -> 1000.); CurrentTime=(fun () -> linkedProgress*1000.)
                        Progress=(fun () -> linkedProgress); Completed=(fun () -> linkedProgress=1.)
                        Seek=(fun time muted -> writes.Add(time,muted); linkedProgress <- Math.Clamp(time/1000.,0.,1.))
                        Command=(fun command ->
                            let name =
                                if mode="directional" then
                                    match command with
                                    | ScrollCommand.Play -> "EnterForward" | ScrollCommand.Pause -> "LeaveForward"
                                    | ScrollCommand.Reverse -> "EnterBackward" | _ -> "LeaveBackward"
                                else string command
                            events.Add("command:"+name)) } |> ignore
                events.Clear(); writes.Clear()
                for step in (property "steps" trace).EnumerateArray() do
                    scroll <- number "scroll" step
                    if scroll <> prior then backward <- scroll < prior
                    prior <- scroll
                    events.Clear(); writes.Clear()
                    observer.HandleScroll()
                    let expectedEvents = (property "events" step).EnumerateArray() |> Seq.map(fun value -> value.GetString()) |> Seq.toArray
                    Expect.sequenceEqual events expectedEvents (mode+" events at "+string scroll)
                    let expectedWrites = (property "writes" step).EnumerateArray() |> Seq.map(fun pair -> pair[0].GetDouble(),pair[1].GetBoolean()) |> Seq.toArray
                    Expect.equal writes.Count expectedWrites.Length "seek count"
                    Seq.iter2(fun (actual,muted) (expected,expectedMuted) -> close actual expected "seek time"; Expect.equal muted expectedMuted "muting") writes expectedWrites
                    close observer.Progress (number "progress" step) "raw progress"
                    Expect.equal observer.IsInView (boolean "inView" step) "interior"
                    Expect.equal observer.Began (boolean "began" step) "began"
                    Expect.equal observer.Completed (boolean "completed" step) "completion"
                    count <- count+1
            Expect.equal count 144 "all oracle samples ran"

        testCase "a boundary enters once before the first interior update" <| fun _ ->
            let mutable scroll = 200.
            let events = ResizeArray<ScrollEvent>()
            use observer = new ScrollObserver((fun () -> scroll,false),(fun () -> geometry),onEvent=(fun event _ -> events.Add event))
            observer.Refresh(); observer.HandleScroll()
            scroll <- 201.; observer.HandleScroll()
            Expect.sequenceEqual events [ScrollEvent.Enter;ScrollEvent.EnterForward;ScrollEvent.Update] "forced entry is retained"

        testCase "zero distance uses the source positive infinity and NaN guard" <| fun _ ->
            let mutable scroll = 800.
            use observer = new ScrollObserver((fun () -> scroll,false),(fun () -> geometry),options=(fun () ->
                {ScrollOptions.Default with Enter={Container="start";Target="start"};Leave={Container="start";Target="start"};Sync=ScrollSync.Disabled}))
            observer.Refresh()
            Expect.equal observer.Progress 0. "NaN maps to zero"
            scroll <- 900.
            Expect.equal observer.Progress 0. "positive infinity maps to zero"

        testCase "dynamic options run once per refresh" <| fun _ ->
            let mutable reads = 0
            use observer = new ScrollObserver((fun () -> 0.,false),(fun () -> geometry),options=(fun () -> reads <- reads+1; ScrollOptions.Default))
            Expect.equal reads 0 "construction defers authored functions"
            observer.Refresh(); observer.Refresh()
            Expect.equal reads 2 "refresh reevaluates options"

        testCase "measurement restores linked current time with muted callbacks even when measurement throws" <| fun _ ->
            let clock = new Ranvier.Dom.ManualFrameScheduler()
            use timer = new Timer(clock,1000u)
            timer.Seek 123.
            use observer = new ScrollObserver((fun () -> 0.,false),(fun () -> failwith "measure"))
            observer.Link(ScrollLink.OfTimer timer) |> ignore
            Expect.throws (fun () -> observer.Refresh()) "measurement fault"
            Expect.equal timer.CurrentTime 123. "linked time restored"

        testCase "revert from entry prevents directional callbacks and target writes" <| fun _ ->
            let mutable writes = 0
            use observer = new ScrollObserver((fun () -> 500.,false),(fun () -> geometry),
                options=(fun () -> {ScrollOptions.Default with Sync=ScrollSync.Smooth 1.}),
                onEvent=(fun event observer -> if event=ScrollEvent.Enter then observer.Revert() else writes <- writes+1))
            observer.Refresh()
            Expect.equal writes 0 "reverted callback interrupts the sample"

        testCase "a callback exception ends observation and runs cleanup once" <| fun _ ->
            let mutable cleanup = 0
            use observer = new ScrollObserver((fun () -> 500.,false),(fun () -> geometry),
                onEvent=(fun _ _ -> failwith "callback"),onRevert=(fun () -> cleanup <- cleanup+1))
            Expect.throws (fun () -> observer.Refresh()) "callback fault"
            observer.HandleScroll(); observer.Revert()
            Expect.equal cleanup 1 "cleanup once"

        testCase "timeline resume method retains reverse playback" <| fun _ ->
            let clock = new Ranvier.Dom.ManualFrameScheduler()
            use timeline = new Timeline(clock)
            timeline.AddTimer(1000u) |> ignore
            timeline.Seek 300.
            timeline.Reverse()
            timeline.Pause()
            let link = ScrollLink.OfTimeline timeline
            link.Command ScrollCommand.Resume
            Expect.isTrue timeline.Reversed "Resume retains the reversed direction"
            timeline.Pause()
            link.Command ScrollCommand.Play
            Expect.isFalse timeline.Reversed "Play explicitly resumes forward"
    ]
