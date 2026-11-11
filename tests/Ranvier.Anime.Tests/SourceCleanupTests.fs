module Ranvier.Anime.Tests.SourceCleanupTests

open System
open System.Collections.Generic
open System.IO
open System.Text.Json
open Expecto
open Ranvier.Anime
open Ranvier.Dom

type private Host(?late:bool,?workError:exn) =
    let mutable now=0.
    let frames=ResizeArray<(float -> unit)*bool ref>()
    let delayed=ResizeArray<bool ref>()
    let mutable requests=0
    member _.Pending=frames |> Seq.filter(fun(_,live)->live.Value) |> Seq.length
    member _.ClientWork=delayed |> Seq.filter _.Value |> Seq.length
    member _.Requests=requests
    member _.At time=
        now<-time
        let batch=frames.ToArray()
        frames.Clear()
        for callback,live in batch do if defaultArg late false || live.Value then callback now
    member private _.Work()=
        let live=ref true
        delayed.Add live
        {new IDisposable with member _.Dispose()=live.Value<-false;workError |> Option.iter raise}
    interface FrameScheduler with
        member _.Now()=now
        member _.Frame callback=
            requests<-requests+1
            let live=ref true
            frames.Add(callback,live)
            {new IDisposable with member _.Dispose()=live.Value<-false}
        member this.Timeout(_,_) = this.Work()
        member this.Microtask _ = this.Work()
        member _.Dispose()=frames.Clear();delayed.Clear()

let cleanupPolicyTests=testList "Source-owned bounded engine cleanup" [
    testCase "cancelled sole client is silent while exactly two shared cleanup deliveries drain" <| fun _ ->
        use host=new Host()
        let mutable updates=0
        use timer=new Timer(host,1000u,onUpdate=(fun _->updates<-updates+1))
        timer.Resume();timer.Cancel()
        Expect.equal host.Pending 1 "Source retains the pending engine callback"
        host.At 25.
        Expect.equal (host.Pending,updates) (1,0) "Cleanup retains only its prequeued drain"
        host.At 50.
        Expect.equal (host.Pending,updates,host.Requests) (0,0,2) "No infinite idle polling"
        host.At 75.
        Expect.equal host.Requests 2 "Idle host frames cannot restart the engine"
    testCase "disposed root releases delayed client work immediately and retains source cleanup membership" <| fun _ ->
        use host=new Host()
        let root=EngineFrames.createRoot host
        let mutable calls=0
        root.Frame(fun _->calls<-calls+1) |> ignore
        root.Timeout(100.,fun()->calls<-calls+1) |> ignore
        root.Microtask(fun()->calls<-calls+1) |> ignore
        root.Dispose()
        Expect.equal (host.Pending,host.ClientWork) (1,0) "Client resources release before engine cleanup"
        host.At 25.
        Expect.equal (host.Pending,calls) (1,0) "Closed root remains until the admitted traversal"
        host.At 50.
        Expect.equal (host.Pending,calls) (0,0) "Bounded drain"
    testCase "terminate releases delayed client work without closing reusable root adapter" <| fun _ ->
        use host=new Host()
        use root=EngineFrames.createRoot host
        root.Frame ignore |> ignore
        root.Timeout(100.,ignore) |> ignore
        root.Microtask ignore |> ignore
        EngineFrames.terminate root
        Expect.equal (host.Pending,host.ClientWork) (1,0) "Terminal client work is separate from engine cleanup"
        host.At 25.;host.At 50.
        let mutable calls=0
        root.Frame(fun _->calls<-calls+1) |> ignore
        host.At 75.
        Expect.equal calls 1 "Internal adapter remains usable for retained controller revival"
    testCase "global pause rejects late cancelled physical callbacks before replacement pending state" <| fun _ ->
        use host=new Host(late=true)
        let engine=EngineFrames.forScheduler host
        let mutable updates=0
        use timer=new Timer(host,1000u,onUpdate=(fun _->updates<-updates+1))
        timer.Resume();engine.Pause()
        Expect.equal host.Pending 0 "Global engine pause owns hard physical cancellation"
        engine.Resume()
        host.At 25.
        Expect.equal (updates,host.Pending) (1,1) "Late old callback cannot clear or duplicate replacement work"
        timer.Cancel();host.At 50.;host.At 75.
        Expect.equal (updates,host.Pending) (1,0) "Terminal client and bounded source drain"
    testCase "cancel preserves primary pause error while cancelling throwing delayed leases and later cleanup" <| fun _ ->
        let primary=InvalidOperationException "pause primary"
        use host=new Host(workError=FormatException "client lease secondary")
        use root=EngineFrames.createRoot host
        let timer=new Timer(root,1000u,onPause=(fun _->raise primary))
        let mutable cleanups=0
        timer.OnCancel<-(fun()->cleanups<-cleanups+1;raise(ArgumentException "cleanup tertiary"))
        root.Timeout(100.,ignore) |> ignore
        root.Microtask ignore |> ignore
        timer.Resume()
        let mutable caught=None
        try timer.Cancel() with error -> caught<-Some error
        timer.OnCancel<-ignore
        Expect.isTrue (caught |> Option.exists(fun error->Object.ReferenceEquals(error,primary))) "Primary error identity"
        Expect.equal (host.ClientWork,host.Pending,cleanups) (0,1,1) "Every client cleanup attempted while engine cleanup remains"
        host.At 25.;host.At 50.
        Expect.equal host.Pending 0 "Source cleanup is finite after a cancellation fault"
        timer.Cancel()
    testCase "frame fault remains primary after throwing delayed-lease and owner cleanup" <| fun _ ->
        let primary=InvalidOperationException "callback primary"
        use host=new Host(workError=FormatException "client lease secondary")
        use root=EngineFrames.createRoot host
        use timer=new Timer(root,1000u,onUpdate=(fun _->raise primary))
        timer.OnFault<-(fun()->raise(ArgumentException "fault cleanup tertiary"))
        root.Timeout(100.,ignore) |> ignore
        root.Microtask ignore |> ignore
        timer.Resume()
        let mutable caught=None
        try host.At 25. with error -> caught<-Some error
        Expect.isTrue (caught |> Option.exists(fun error->Object.ReferenceEquals(error,primary))) "Original callback error survives abandonment"
        Expect.equal (timer.Cancelled,host.ClientWork,host.Pending) (true,0,1) "Only source engine cleanup remains"
        host.At 50.;host.At 75.
        Expect.equal host.Pending 0 "No repeated callback fault or infinite cleanup"
    testCase "cancelled root never interrupts a live peer or takes its shared subscription" <| fun _ ->
        use host=new Host()
        let mutable firstCalls=0
        let mutable peerCalls=0
        use first=new Timer(host,1000u,onUpdate=(fun _->firstCalls<-firstCalls+1))
        use peer=new Timer(host,1000u,onUpdate=(fun _->peerCalls<-peerCalls+1))
        first.Resume();peer.Resume();first.Cancel();host.At 25.;host.At 50.
        Expect.equal (firstCalls,peerCalls,host.Pending) (0,2,1) "One shared engine continues the peer"
        peer.Cancel();host.At 75.;host.At 100.
        Expect.equal (firstCalls,peerCalls,host.Pending) (0,2,0) "Cleanup drains after the last client"
    testCase "idle host RAF successor phase callbacks values and pending counts exactly equal source" <| fun _ ->
        use source=JsonDocument.Parse(File.ReadAllText(Path.Combine((match Environment.GetEnvironmentVariable "RANVIER_ANIME_REFERENCE_ROOT" with null | "" -> Path.Combine(AppContext.BaseDirectory,"reference") | root -> root),"engine-idle-phase-reference.json")))
        use host=new Host()
        let engine=EngineFrames.forScheduler host
        let events=ResizeArray<float>()
        use first=new Timer(host,1000u)
        first.Reset();first.Resume()
        let mutable successor:Timer option=None
        for sample in source.RootElement.GetProperty("samples").EnumerateArray() do
            let stage=sample.GetProperty("stage").GetString()
            let time=sample.GetProperty("now").GetDouble()
            match stage with
            | "cancel" -> first.Cancel()
            | "constructed" ->
                let timer=new Timer(host,1000u,options={PlaybackOptions.Default with FrameRate=30.},onUpdate=(fun self->events.Add self.CurrentTime))
                timer.Reset();successor<-Some timer
            | "resumed" -> successor.Value.Resume()
            | _ -> host.At time
            Expect.floatClose Accuracy.veryHigh engine.Clock.LastTick (sample.GetProperty("phase").GetDouble()) (stage+" source engine phase")
            Expect.equal host.Pending (sample.GetProperty("pending").GetInt32()) (stage+" source pending")
            match successor with
            | Some timer ->
                Expect.equal (timer.CurrentTime,timer.Paused,timer.Cancelled) (sample.GetProperty("time").GetDouble(),sample.GetProperty("paused").GetBoolean(),sample.GetProperty("cancelled").GetBoolean()) (stage+" source controller")
            | None -> ()
            Expect.equal (events |> Seq.toList) (sample.GetProperty("events").EnumerateArray() |> Seq.map _.GetDouble() |> Seq.toList) (stage+" source update order and values")
            events.Clear()
        successor |> Option.iter(fun timer->timer.Cancel())
]
