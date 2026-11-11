module Ranvier.Anime.Tests.TimelineKeyframeTests

open Expecto
open Ranvier.Anime
open Ranvier.Dom

let private verifyBorrowedBoundary () =
    let root=System.Environment.GetEnvironmentVariable "RANVIER_ANIME_REFERENCE_ROOT"
    let root=if System.String.IsNullOrWhiteSpace root then System.IO.Path.Combine(System.AppContext.BaseDirectory,"reference") else root
    use source=System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(root,"timeline-keyframes.json")))
    Expect.equal (source.RootElement.GetProperty("upstreamCommit").GetString()) "2c9cf8ea00329f6768c7d7902252ed977d75ce42" "pinned source"
    let fixture=source.RootElement.GetProperty("fixtures").EnumerateArray() |> Seq.find(fun item -> item.GetProperty("name").GetString()="none-mutedResetComplete")
    let expected=fixture.GetProperty("samples")[0]
    let events=ResizeArray<string>()
    use scheduler=new ManualFrameScheduler()
    let mutable value=10.
    let property=NumericProperty((fun () -> value),(fun next -> value<-next))
    let callback id phase (_:Timer)=events.Add(id+":"+phase)
    use timeline=new Timeline(scheduler,
        onBegin=(fun _ -> events.Add "root:Begin"),onBeforeUpdate=(fun _ -> events.Add "root:BeforeUpdate"),
        onRender=(fun _ -> events.Add "root:Render"),onUpdate=(fun _ -> events.Add "root:Update"),
        onLoop=(fun _ -> events.Add "root:Loop"),onPause=(fun _ -> events.Add "root:Pause"),onComplete=(fun _ -> events.Add "root:Complete"))
    let options=
        {Ranvier.Anime.KeyframeAnimationOptions.Default with Autoplay=false;Ease=id;Composition=KeyframeComposition.None;Playback={PlaybackOptions.Default with Repeat=PlaybackRepeat.Count 1u};OnBegin=callback "a" "Begin";OnBeforeUpdate=callback "a" "BeforeUpdate";OnRender=callback "a" "Render";OnUpdate=callback "a" "Update";OnLoop=callback "a" "Loop";OnPause=callback "a" "Pause";OnComplete=callback "a" "Complete"}
    use animation=Keyframes.Numeric(scheduler,property,Keyframe.values [|0.;100.;200.|],duration=200u,options=options)
    timeline.Sync(animation.Playback,TimelinePosition.At 25.) |> ignore
    timeline.Reset()
    events.Clear()
    timeline.Seek(50.,muteCallbacks=true)
    let expectedPhases=expected.GetProperty("state").GetProperty("events").EnumerateArray() |> Seq.map(fun item -> item.GetProperty("id").GetString()+":"+item.GetProperty("event").GetString()) |> Seq.toArray
    Expect.isEmpty expectedPhases "owned timeline child respects parent muting"
    Expect.sequenceEqual events ["a:Begin";"a:BeforeUpdate";"a:Render";"a:Update"] "borrowed keyframe setter retains its callbacks"
    Expect.equal value 25. "same sampled value does not establish equivalent lifecycle semantics"
    Expect.isTrue animation.Playback.Paused "borrowed scheduling remains paused"

let borrowedContractTests=testList "timeline keyframe integration boundary" [
    testCase "borrowed Sync retains callbacks that an owned child must mute" <| fun _ -> verifyBorrowedBoundary()
]
