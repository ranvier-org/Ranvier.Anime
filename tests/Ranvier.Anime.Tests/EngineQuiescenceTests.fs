module Ranvier.Anime.Tests.EngineQuiescenceTests

open System
open System.Text.Json
open System.Collections.Generic
open Ranvier.Dom
open Ranvier.Anime
open Expecto

type private LateHost(?disposeError:exn) =
    let mutable now=0.
    let frames=ResizeArray<(float -> unit)*bool ref>()
    member _.LiveCount=frames |> Seq.filter(fun(_,live)->live.Value) |> Seq.length
    member _.Advance value=now<-now+value
    member _.Deliver()=
        let current=frames.ToArray()
        frames.Clear()
        for callback,_ in current do callback now
    interface FrameScheduler with
        member _.Now()=now
        member _.Frame callback=
            let live=ref true
            frames.Add(callback,live)
            {new IDisposable with member _.Dispose()=live.Value<-false;disposeError |> Option.iter raise}
        member _.Timeout(_,_) = failwith "Unexpected timeout"
        member _.Microtask _ = failwith "Unexpected microtask"
    interface IDisposable with member _.Dispose()=frames.Clear()

let private near label expected actual=
    if abs(expected-actual)>1e-7 then failtestf "%s expected %.12g actual %.12g" label expected actual

let private replay (fixture:JsonElement) late =
    use host=new LateHost()
    let scheduler=host:>FrameScheduler
    let engine=EngineFrames.forScheduler scheduler
    let name=fixture.GetProperty("name").GetString()
    let events=ResizeArray<string*float>()
    let roots=Dictionary<string,Timer>()
    let make name fps=
        let timer=new Timer(scheduler,1000u,options={PlaybackOptions.Default with FrameRate=fps},onUpdate=(fun timer -> events.Add(name,timer.CurrentTime)))
        timer.Reset()
        roots.Add(name,timer)
        timer
    let first=make "A" 240.
    let second=if name="cancelOnly" then None else Some(make "B" 240.)
    first.Resume();second |> Option.iter(fun timer->timer.Resume())
    let check (sample:JsonElement) =
        near "engine phase" (sample.GetProperty("phase").GetDouble()) engine.Clock.LastTick
        let expected=sample.GetProperty("events").EnumerateArray() |> Seq.map(fun item->item.GetProperty("name").GetString(),item.GetProperty("time").GetDouble()) |> Seq.toArray
        Expect.equal events.Count expected.Length "source callback count"
        for index in 0..events.Count-1 do
            let name,time=events[index]
            let wanted,wantedTime=expected[index]
            Expect.equal name wanted "source callback order"
            near "callback time" wantedTime time
        events.Clear()
        for expected in sample.GetProperty("timers").EnumerateObject() do
            let timer=roots[expected.Name]
            near "timer time" (expected.Value.GetProperty("time").GetDouble()) timer.CurrentTime
            Expect.equal timer.Paused (expected.Value.GetProperty("paused").GetBoolean()) "source paused"
            Expect.equal timer.Cancelled (expected.Value.GetProperty("cancelled").GetBoolean()) "source cancelled"
    try
        for sample in fixture.GetProperty("samples").EnumerateArray() do
            match sample.GetProperty("stage").GetString() with
            | "first" -> host.Advance 5.;host.Deliver()
            | "stopped" ->
                if name="pauseBoth" then first.Pause();second.Value.Pause()
                else first.Cancel();if name="cancelBoth" then second.Value.Cancel()
                if name="cancelOnly" || name="cancelBoth" then Expect.equal host.LiveCount 1 "Source engine cleanup remains after terminal client cancellation"
                else Expect.equal host.LiveCount 1 "Ordinary pause and mixed live roots retain the source request"
            | "idleAdvance" -> host.Advance 100.
            | "constructed" -> make "C" 30. |> ignore
            | "resumed" -> roots["C"].Resume()
            | stage ->
                let after=Double.Parse(stage.Substring(5),Globalization.CultureInfo.InvariantCulture)
                host.Advance after
                host.Deliver()
            check sample
            if late && sample.GetProperty("stage").GetString()="resumed" then Expect.equal host.LiveCount 1 "Replacement host request remains live despite a retained cancelled callback"
    finally for timer in roots.Values do timer.Cancel()

let engineQuiescenceTests=testList "Source-owned terminal engine cleanup" [
    testCase "source successor phases and values retain bounded engine cleanup" <| fun _ ->
        use document=JsonDocument.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine((match Environment.GetEnvironmentVariable "RANVIER_ANIME_REFERENCE_ROOT" with null | "" -> System.IO.Path.Combine(AppContext.BaseDirectory,"reference") | root -> root),"engine-quiescence-reference.json")))
        for fixture in document.RootElement.GetProperty("fixtures").EnumerateArray() do
            try replay fixture true with error -> failtestf "%s: %s" (fixture.GetProperty("name").GetString()) error.Message
    testCase "cancelled client stays silent while bounded source engine cleanup drains" <| fun _ ->
        use host=new LateHost()
        let mutable calls=0
        use timer=new Timer(host,1000u,onUpdate=(fun _ -> calls<-calls+1))
        timer.Resume();timer.Cancel()
        Expect.equal host.LiveCount 1 "Shared source cleanup remains"
        host.Advance 100.;host.Deliver()
        Expect.equal (host.LiveCount,calls) (1,0) "Cleanup only prequeues its final drain"
        host.Advance 25.;host.Deliver()
        Expect.equal (host.LiveCount,calls) (0,0) "No client callback or infinite polling"
    testCase "fault preserves its primary error while bounded source engine cleanup drains" <| fun _ ->
        use host=new LateHost()
        let original=InvalidOperationException "primary"
        use timer=new Timer(host,1000u,onUpdate=(fun _ -> raise original),onPause=(fun _ -> raise(FormatException "secondary")))
        timer.Resume();host.Advance 5.
        let mutable caught:exn option=None
        try host.Deliver() with error -> caught<-Some error
        Expect.isTrue (caught |> Option.exists(fun error->Object.ReferenceEquals(error,original))) "Original callback exception"
        Expect.isTrue timer.Cancelled "Existing native terminal-fault contract"
        Expect.equal host.LiveCount 1 "Only source engine cleanup remains"
        host.Advance 100.;host.Deliver()
        Expect.equal host.LiveCount 1 "Cleanup prequeues its final drain"
        host.Advance 25.;host.Deliver()
        Expect.equal host.LiveCount 0 "No repeated client fault or infinite polling"
    testCase "cancellation retains its first error before explicit engine hard-pause disposal" <| fun _ ->
        let primary=InvalidOperationException "pause primary"
        use host=new LateHost(disposeError=FormatException "host secondary")
        let timer=new Timer(host,1000u,onPause=(fun _ -> raise primary))
        let mutable cleanup=0
        timer.OnCancel<-(fun()->cleanup<-cleanup+1;raise(ArgumentException "cleanup tertiary"))
        timer.Resume()
        let mutable caught:exn option=None
        try timer.Cancel() with error -> caught<-Some error
        timer.OnCancel<-ignore
        timer.Cancel()
        Expect.isTrue (caught |> Option.exists(fun error->Object.ReferenceEquals(error,primary))) "First cancellation error wins"
        Expect.equal cleanup 1 "Later native lifetime cleanup still runs"
        Expect.isTrue timer.Cancelled "Cancellation remains terminal"
        Expect.equal host.LiveCount 1 "Client cancellation retains shared source cleanup"
        Expect.throwsT<FormatException> (fun()->(EngineFrames.forScheduler host).Pause()) "Global engine pause owns physical lease disposal"
        Expect.equal host.LiveCount 0 "Hard pause released the physical lease"
    testCase "frame callback fault remains primary before explicit engine hard-pause disposal" <| fun _ ->
        let primary=InvalidOperationException "frame primary"
        use host=new LateHost(disposeError=FormatException "host secondary")
        use timer=new Timer(host,1000u,onUpdate=(fun _ -> raise primary),onPause=(fun _ -> raise(ArgumentException "pause tertiary")))
        let mutable cleanup=0
        timer.OnFault<-(fun()->cleanup<-cleanup+1;raise(ArgumentException "fault cleanup"))
        timer.Resume();host.Advance 5.
        let mutable caught:exn option=None
        try host.Deliver() with error -> caught<-Some error
        Expect.isTrue (caught |> Option.exists(fun error->Object.ReferenceEquals(error,primary))) "Original frame error wins"
        Expect.equal cleanup 1 "Fault cleanup attempted"
        Expect.equal host.LiveCount 1 "Failed client retains bounded source cleanup"
        Expect.throwsT<FormatException> (fun()->(EngineFrames.forScheduler host).Pause()) "Physical hard-pause disposal is separate"
        Expect.equal host.LiveCount 0 "Global hard pause releases the prequeue"
]
