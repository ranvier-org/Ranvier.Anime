module Ranvier.Anime.Tests.UtilsTests

open System
open System.IO
open System.Text.Json
open Expecto
open Ranvier.Dom
open Ranvier.Anime

let private fixture () =
    let root = Environment.GetEnvironmentVariable "RANVIER_ANIME_REFERENCE_ROOT"
    let dir = if String.IsNullOrWhiteSpace root then Path.Combine(AppContext.BaseDirectory, "reference") else root
    JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "utils-reference.json")))

let private close actual expected msg =
    Expect.isLessThanOrEqual (abs (actual - expected)) 1e-11 msg

let tests =
    testList "utils family behavioral conformity" [
        testCase "math, rounding, snap, wrap, mapRange, lerp, damp, angles and padding match upstream" <| fun _ ->
            use doc = fixture()
            let math = doc.RootElement.GetProperty "mathSamples"
            for item in (math.GetProperty "clamp").EnumerateArray() do
                let actual = Utils.clamp (item.GetProperty("v").GetDouble()) (item.GetProperty("min").GetDouble()) (item.GetProperty("max").GetDouble())
                close actual (item.GetProperty("expected").GetDouble()) "clamp"
            for item in (math.GetProperty "round").EnumerateArray() do
                let actual = Utils.round (item.GetProperty("v").GetDouble()) (item.GetProperty("d").GetInt32())
                close actual (item.GetProperty("expected").GetDouble()) "round"
            for item in (math.GetProperty "snapScalar").EnumerateArray() do
                let actual = Utils.snap (item.GetProperty("v").GetDouble()) (item.GetProperty("inc").GetDouble())
                close actual (item.GetProperty("expected").GetDouble()) "snap scalar"
            for item in (math.GetProperty "snapArray").EnumerateArray() do
                let pts = (item.GetProperty "points").EnumerateArray() |> Seq.map (fun e -> e.GetDouble()) |> Seq.toArray
                let actual = Utils.snapTo (item.GetProperty("v").GetDouble()) pts
                close actual (item.GetProperty("expected").GetDouble()) "snap array"
            for item in (math.GetProperty "wrap").EnumerateArray() do
                let actual = Utils.wrap (item.GetProperty("v").GetDouble()) (item.GetProperty("min").GetDouble()) (item.GetProperty("max").GetDouble())
                close actual (item.GetProperty("expected").GetDouble()) "wrap"
            for item in (math.GetProperty "mapRange").EnumerateArray() do
                let actual =
                    Utils.mapRange
                        (item.GetProperty("v").GetDouble())
                        (item.GetProperty("inLow").GetDouble())
                        (item.GetProperty("inHigh").GetDouble())
                        (item.GetProperty("outLow").GetDouble())
                        (item.GetProperty("outHigh").GetDouble())
                close actual (item.GetProperty("expected").GetDouble()) "mapRange"
            for item in (math.GetProperty "lerp").EnumerateArray() do
                let actual = Utils.lerp (item.GetProperty("a").GetDouble()) (item.GetProperty("b").GetDouble()) (item.GetProperty("t").GetDouble())
                close actual (item.GetProperty("expected").GetDouble()) "lerp"
            for item in (math.GetProperty "damp").EnumerateArray() do
                let actual = Utils.damp (item.GetProperty("a").GetDouble()) (item.GetProperty("b").GetDouble()) (item.GetProperty("dt").GetDouble()) (item.GetProperty("f").GetDouble())
                close actual (item.GetProperty("expected").GetDouble()) "damp"
            for item in (math.GetProperty "angles").EnumerateArray() do
                let deg = item.GetProperty("deg").GetDouble()
                close (Utils.degToRad deg) (item.GetProperty("rad").GetDouble()) "degToRad"
                close (Utils.radToDeg (Utils.degToRad deg)) (item.GetProperty("roundtrip").GetDouble()) "radToDeg"
            for item in (math.GetProperty "strings").EnumerateArray() do
                let v = item.GetProperty("v").GetDouble()
                let len = item.GetProperty("len").GetInt32()
                let pad = item.GetProperty("pad").GetString()
                let actual =
                    match item.GetProperty("op").GetString() with
                    | "roundPad" -> Utils.roundPad v len
                    | "padStart" -> Utils.padStart v len pad
                    | _ -> Utils.padEnd v len pad
                Expect.equal actual (item.GetProperty("expected").GetString()) "string utility"

        testCase "chainable utility pipelines match upstream curried proxies" <| fun _ ->
            use doc = fixture()
            let chains = doc.RootElement.GetProperty "chainSamples"
            let c1 = (Utils.Chain.mapRange 0. 100. 0. 1000.).Clamp(0., 100.)
            for item in (chains.GetProperty "mapClamp").EnumerateArray() do
                close (c1.Apply (item.GetProperty("v").GetDouble())) (item.GetProperty("expected").GetDouble()) "mapClamp"
            let c2 = (Utils.Chain.clamp 0. 1.).Lerp(10., 110.).Snap(25.)
            for item in (chains.GetProperty "clampLerpSnap").EnumerateArray() do
                close (c2.Apply (item.GetProperty("v").GetDouble())) (item.GetProperty("expected").GetDouble()) "clampLerpSnap"
            let c3 = (Utils.Chain.wrap 0. 360.).DegToRad().RadToDeg().Round(2)
            for item in (chains.GetProperty "wrapAngleRound").EnumerateArray() do
                close (c3.Apply (item.GetProperty("v").GetDouble())) (item.GetProperty("expected").GetDouble()) "wrapAngleRound"
            let c4 = (Utils.Chain.mapRange 0. 10. 0. 100.).RoundPad(2).PadStart(7, "0")
            for item in (chains.GetProperty "mapRoundPadStart").EnumerateArray() do
                Expect.equal (c4.Apply (item.GetProperty("v").GetDouble())) (item.GetProperty("expected").GetString()) "mapRoundPadStart"

        testCase "createSeededRandom Mulberry32 and shuffleWith match upstream bit-for-bit" <| fun _ ->
            use doc = fixture()
            for cfg in (doc.RootElement.GetProperty "seededCases").EnumerateArray() do
                let seed = cfg.GetProperty("seed").GetDouble()
                let lo = cfg.GetProperty("min").GetDouble()
                let hi = cfg.GetProperty("max").GetDouble()
                let dec = cfg.GetProperty("dec").GetInt32()
                let rng = Utils.createSeededRandom (Some seed) (Some lo) (Some hi) (Some dec)
                let expectedDraws = (cfg.GetProperty "draws").EnumerateArray() |> Seq.map (fun e -> e.GetDouble()) |> Seq.toArray
                let actualDraws = Array.init expectedDraws.Length (fun _ -> rng None None None)
                Array.iter2 (fun a b -> close a b "seeded draw") actualDraws expectedDraws
                let expectedOverrides = (cfg.GetProperty "overrideDraws").EnumerateArray() |> Seq.map (fun e -> e.GetDouble()) |> Seq.toArray
                close (rng (Some 0.) (Some 1000.) (Some 0)) expectedOverrides[0] "override draw 0"
                close (rng (Some -5.) (Some 5.) (Some 2)) expectedOverrides[1] "override draw 1"

            let shuffleRng = Utils.createSeededRandom (Some 12345.) None None None
            let arr = [| 0 .. 9 |]
            Utils.shuffleWith (fun lo hi -> int (shuffleRng (Some (float lo)) (Some (float hi)) (Some 0))) arr |> ignore
            let expectedShuffled = (doc.RootElement.GetProperty "shuffled").EnumerateArray() |> Seq.map (fun e -> e.GetInt32()) |> Seq.toArray
            Expect.sequenceEqual arr expectedShuffled "seeded Fisher-Yates shuffle"

        testCase "linked list addChild, sortMethod, removeChild and forEachChildren match upstream" <| fun _ ->
            let parent = LinkedParent<int>()
            let c1 = LinkedChild(1, priority = 999.)
            let c2 = LinkedChild(2, priority = 42.)
            let c3 = LinkedChild(3, priority = 100.)
            let sort = Some (fun (prev: LinkedChild<int>) (child: LinkedChild<int>) -> prev.Priority > child.Priority)
            Utils.addChild parent c1 sort
            Utils.addChild parent c2 sort
            Utils.addChild parent c3 sort
            let forward = ResizeArray<int>()
            Utils.forEachChildren parent (fun n -> forward.Add n.Value) false
            Expect.sequenceEqual forward [2; 3; 1] "sorted insertion order"
            let backward = ResizeArray<int>()
            Utils.forEachChildren parent (fun n -> backward.Add n.Value) true
            Expect.sequenceEqual backward [1; 3; 2] "reverse traversal"
            Utils.removeChild parent c3
            let remaining = ResizeArray<int>()
            Utils.forEachChildren parent (fun n -> remaining.Add n.Value) false
            Expect.sequenceEqual remaining [2; 1] "after middle removal"
            Expect.isNull c3.Prev "detached prev"
            Expect.isNull c3.Next "detached next"

        testCase "Utils.sync and Utils.keepTime match upstream timekeeper traces" <| fun _ ->
            use host = new ManualFrameScheduler()
            let mutable synced = false
            use syncTimer = Utils.sync host (Some (fun _ -> synced <- true))
            host.Advance 16.; host.RunFrame()
            Expect.isTrue synced "Utils.sync ran callback on next frame"

            use doc = fixture()
            let bag = AnimatedObject [ "x", AdaptedValue.Scalar 0. ]
            let tk =
                Utils.keepTime (fun (dur: float) ->
                    Composed.animate
                        host
                        (box bag)
                        [| PropertySpec.Scalar("x", 100.) |]
                        (Some { ComposedAnimationOptions.Default with
                                  Duration = dur
                                  Ease = id
                                  Autoplay = false
                                  Playback = { PlaybackOptions.Default with Repeat = PlaybackRepeat.Forever; Alternate = true } }))
            let steps = (doc.RootElement.GetProperty "keepTimeSteps").EnumerateArray() |> Seq.toArray
            let mutable anim = tk 1000.
            close anim.CurrentTime (steps[0].GetProperty("currentTime").GetDouble()) "init1000 time"
            anim.Seek 500. |> ignore
            close anim.CurrentTime (steps[1].GetProperty("currentTime").GetDouble()) "seek500 time"
            anim <- tk 2000.
            close anim.CurrentTime (steps[2].GetProperty("currentTime").GetDouble()) "recreate2000 time"
            anim.Seek 500. |> ignore
            anim <- tk 500.
            close anim.CurrentTime (steps[3].GetProperty("currentTime").GetDouble()) "recreate500 time"
            anim.Seek 875. |> ignore
            close anim.CurrentTime (steps[4].GetProperty("currentTime").GetDouble()) "seek875 time"
            close anim.IterationProgress (steps[4].GetProperty("iterProgress").GetDouble()) "seek875 iterProgress"
            anim <- tk 1000.
            close anim.IterationProgress (steps[5].GetProperty("iterProgress").GetDouble()) "recreate1000 odd iterProgress"
            anim.Cancel()
    ]
