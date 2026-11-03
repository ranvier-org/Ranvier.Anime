module Ranvier.Anime.Tests.DeclarationTests

open System
open System.Collections.Generic
open System.IO
open System.Text.Json
open Expecto
open Ranvier
open Ranvier.Dom
open Ranvier.Css
open Ranvier.Anime

type private RecordingStyle() =
    let values=Dictionary<string,string*string>()
    let writes=ResizeArray<string*string*string>()
    member _.Writes=writes
    interface CSSStyleDeclaration with
        member _.getPropertyValue name = match values.TryGetValue name with true,(value,_) -> value | _ -> ""
        member _.getPropertyPriority name = match values.TryGetValue name with true,(_,priority) -> priority | _ -> ""
        member _.setProperty(name,value,priority) =
            let priority=defaultArg priority ""
            values[name]<-value,priority
            writes.Add(name,value,priority)
        member _.removeProperty name =
            let old=match values.TryGetValue name with true,(value,_) -> value | _ -> ""
            values.Remove name |> ignore
            old
        member _.item index = values.Keys |> Seq.tryItem(int index) |> Option.defaultValue ""
        member _.length=uint32 values.Count
        member _.cssText with get()=failwith "Whole style access" and set _=failwith "Whole style replacement"
        member _.parentRule=Unchecked.defaultof<CSSRule>
        member _.cssRules=Unchecked.defaultof<CSSRuleList>

let private cases () =
    let root=Environment.GetEnvironmentVariable "RANVIER_ANIME_REFERENCE_ROOT"
    let path=Path.Combine((if String.IsNullOrWhiteSpace root then Path.Combine(AppContext.BaseDirectory,"reference") else root),"value-interpolation.json")
    use document=JsonDocument.Parse(File.ReadAllText path)
    document.RootElement.GetProperty("cssCases").EnumerateArray() |> Seq.map _.Clone() |> Seq.toList

let private text (value:JsonElement) =
    if value.ValueKind=JsonValueKind.Number then ValueInterpolation.number(value.GetDouble()) else value.GetString()

let tests = testList "typed declaration playback" [
    testCase "stretch retains styles and scales typed declaration sampling" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let recorder=RecordingStyle()
        let style=recorder:>CSSStyleDeclaration
        style.setProperty("width","7px",priority="important")
        let settings={DeclarationAnimationOptions.Default with Playback={PlaybackOptions.Default with Delay=40.}}
        use clock=CssAnimation.declarationWith settings scheduler style (Css.width(Css.px 0.)) (Css.width(Css.px 100.)) 100u
        clock.Seek 50.
        let writes=recorder.Writes.Count
        clock.Stretch 200u
        Expect.equal recorder.Writes.Count writes "stretch has no immediate write"
        Expect.equal clock.CurrentTime 50. "raw playhead retained"
        Expect.equal clock.Progress 0.25 "getters use new duration"
        clock.Seek 100.
        Expect.equal (style.getPropertyValue "width") "50px" "new iteration geometry"
        Expect.equal (style.getPropertyPriority "width") "important" "priority preserved"
        clock.Reset()
        clock.Resume()
        scheduler.Advance 25.
        scheduler.RunFrame()
        Expect.equal (style.getPropertyValue "width") "0px" "scaled initial delay"
    testCase "stretch from a component modifier discards the stale style commit" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let recorder=RecordingStyle()
        let mutable active:Timer option=None
        let mutable armed=false
        let mutable calls=0
        let modifier value =
            calls<-calls+1
            if armed then
                armed<-false
                active |> Option.iter(fun clock -> clock.Stretch 200u)
            value
        let settings={DeclarationAnimationOptions.Default with Modifier=modifier}
        use clock=CssAnimation.declarationWith settings scheduler recorder
                        (Declaration.create "transform" "translate(0px,0px)") (Declaration.create "transform" "translate(100px,200px)") 100u
        active<-Some clock
        let writes=recorder.Writes.Count
        calls<-0
        armed<-true
        clock.Seek 50.
        Expect.equal recorder.Writes.Count writes "interrupted sample cannot dispatch"
        Expect.equal calls 1 "later components suppressed"
        clock.Seek 100.
        Expect.equal ((recorder:>CSSStyleDeclaration).getPropertyValue "transform") "translate(50px,100px)" "replacement geometry remains usable"
    for entry in cases() do
        testCase (entry.GetProperty("name").GetString()) <| fun _ ->
            let modifier=match entry.GetProperty("modifier").GetString() with "affine" -> (fun value -> value*0.5+2.) | "round2" -> PropertyNumbers.round 100. | _ -> id
            for sample in entry.GetProperty("samples").EnumerateArray() do
                use scheduler=new ManualFrameScheduler()
                let recorder=RecordingStyle()
                let style=recorder:>CSSStyleDeclaration
                style.setProperty("opacity",text(entry.GetProperty("base")))
                let settings={ DeclarationAnimationOptions.Default with
                                Ease=(fun _ -> sample.GetProperty("progress").GetDouble())
                                Precision=Some(entry.GetProperty("precision").GetUInt32());Modifier=modifier }
                use clock=CssAnimation.declarationPreciseWith settings scheduler style
                                (Declaration.create "opacity" (text(entry.GetProperty("first"))))
                                (Declaration.create "opacity" (text(entry.GetProperty("last")))) 100.
                clock.Seek 50.
                Expect.equal (style.getPropertyValue "opacity") (sample.GetProperty("result").GetString()) "actual declaration dispatch"
    testCase "existing typed declarations retain property and inline priority" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let recorder=RecordingStyle()
        let style=recorder:>CSSStyleDeclaration
        style.setProperty("width","7px",priority="important")
        style.setProperty("height","42px")
        use clock=CssAnimation.declaration scheduler style (Css.width(Css.px 0.)) (Css.width(Css.px 100.)) 100u
        Expect.equal (style.getPropertyValue "width") "0px" "initial declaration"
        scheduler.Advance(25.-12.)
        scheduler.RunFrame()
        Expect.equal (style.getPropertyValue "width") "25px" "typed property dispatch"
        Expect.equal (style.getPropertyPriority "width") "important" "inline priority"
        Expect.equal (style.getPropertyValue "height") "42px" "unrelated style preserved"
    testCase "an important endpoint requests importance throughout" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let style=RecordingStyle():>CSSStyleDeclaration
        use clock=CssAnimation.declaration scheduler style (Css.width(Css.px 0.)) (Css.width(Css.px 10.) |> Declaration.important) 100u
        Expect.equal (style.getPropertyPriority "width") "important" "explicit importance"
    testCase "unsigned zero and precise fractional durations are supported" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let style=RecordingStyle():>CSSStyleDeclaration
        use zero=CssAnimation.declaration scheduler style (Css.width(Css.px 0.)) (Css.width(Css.px 10.)) 0u
        Expect.equal (style.getPropertyValue "width") "10px" "zero setter endpoint"
        Expect.isTrue zero.Completed "zero completed"
        use fractional=CssAnimation.declarationPrecise scheduler style (Css.width(Css.px 0.)) (Css.width(Css.px 100.)) 0.5
        fractional.Seek 0.25
        Expect.equal (style.getPropertyValue "width") "50px" "precise fractional midpoint"
    testCase "shared playback repeats alternate with callback phases" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let style=RecordingStyle():>CSSStyleDeclaration
        let events=ResizeArray<string>()
        let settings={DeclarationAnimationOptions.Default with
                        Playback={PlaybackOptions.Default with Repeat=PlaybackRepeat.Count 1u;Alternate=true}
                        OnBegin=(fun _ -> events.Add "begin");OnBeforeUpdate=(fun _ -> events.Add "before")
                        OnRender=(fun _ -> events.Add "render");OnUpdate=(fun _ -> events.Add "update")
                        OnLoop=(fun _ -> events.Add "loop");OnComplete=(fun _ -> events.Add "complete")}
        use clock=CssAnimation.declarationWith settings scheduler style (Css.width(Css.px 0.)) (Css.width(Css.px 100.)) 100u
        Expect.isEmpty events "initialization mutes callbacks"
        clock.Seek 50.
        clock.Seek 150.
        clock.Seek 200.
        Expect.equal (style.getPropertyValue "width") "0px" "alternate final endpoint"
        Expect.sequenceEqual events ["begin";"before";"render";"update";"loop";"before";"render";"update";"before";"render";"update";"complete"] "shared lifecycle phases"
    testCase "muted seeks still write without lifecycle callbacks" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let style=RecordingStyle():>CSSStyleDeclaration
        let mutable callbacks=0
        let settings={DeclarationAnimationOptions.Default with OnRender=(fun _ -> callbacks<-callbacks+1);OnUpdate=(fun _ -> callbacks<-callbacks+1)}
        use clock=CssAnimation.declarationWith settings scheduler style (Css.width(Css.px 0.)) (Css.width(Css.px 100.)) 100u
        clock.Seek(50.,muteCallbacks=true)
        Expect.equal (style.getPropertyValue "width") "50px" "muted property render"
        Expect.equal callbacks 0 "muted callbacks"
    testCase "owner disposal inside a modifier prevents style and later callbacks" <| fun _ ->
        use graph=new Graph()
        use activation=graph.Activate()
        use scheduler=new ManualFrameScheduler()
        let recorder=RecordingStyle()
        let mutable armed=false
        let mutable updates=0
        let owner,clock=Api.createRoot(fun owner ->
            let modify value =
                if armed then owner.Dispose()
                value
            let settings={DeclarationAnimationOptions.Default with Modifier=modify;OnUpdate=(fun _ -> updates<-updates+1)}
            let clock=CssAnimation.declarationWith settings scheduler recorder (Css.width(Css.px 0.)) (Css.width(Css.px 100.)) 100u |> Timer.own
            owner,clock)
        use clock=clock
        armed<-true
        let initial=recorder.Writes.Count
        scheduler.Advance(50.-12.)
        scheduler.RunFrame()
        Expect.isTrue clock.Cancelled "owned lifetime cancelled"
        Expect.equal recorder.Writes.Count initial "interrupted modifier cannot write"
        Expect.equal updates 0 "later update suppressed"
        owner.Dispose()
    testCase "modifier seek supersedes the interrupted compound render" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let style=RecordingStyle():>CSSStyleDeclaration
        let mutable active:Timer option=None
        let mutable armed=false
        let mutable calls=0
        let modify value =
            calls<-calls+1
            if armed then
                armed<-false
                active |> Option.iter(fun timer -> timer.Seek 100.)
            value
        let settings={DeclarationAnimationOptions.Default with Modifier=modify}
        use clock=CssAnimation.declarationWith settings scheduler style
                        (Declaration.create "transform" "translate(0px,0px)") (Declaration.create "transform" "translate(100px,200px)") 100u
        active<-Some clock
        armed<-true
        calls<-0
        clock.Seek 50.
        Expect.equal (style.getPropertyValue "transform") "translate(100px,200px)" "inner seek wins"
        Expect.equal calls 3 "outer first component plus two inner components"
    testCase "onRender cancellation suppresses update and completion" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let style=RecordingStyle():>CSSStyleDeclaration
        let mutable updates=0
        let mutable completions=0
        let settings={DeclarationAnimationOptions.Default with
                        OnRender=(fun timer -> timer.Cancel());OnUpdate=(fun _ -> updates<-updates+1)
                        OnComplete=(fun _ -> completions<-completions+1)}
        use clock=CssAnimation.declarationWith settings scheduler style (Css.width(Css.px 0.)) (Css.width(Css.px 100.)) 100u
        clock.Seek 100.
        Expect.equal (style.getPropertyValue "width") "100px" "render precedes its callback"
        Expect.isTrue clock.Cancelled "render callback cancels"
        Expect.equal updates 0 "update suppressed"
        Expect.equal completions 0 "completion suppressed"
    testCase "borrowed parent cancellation is inherited through component modifiers" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let recorder=RecordingStyle()
        let mutable parent:Timeline option=None
        let mutable armed=false
        let mutable updates=0
        let mutable calls=0
        let modify value =
            calls<-calls+1
            if armed then parent |> Option.iter(fun timeline -> timeline.Cancel())
            value
        let settings={DeclarationAnimationOptions.Default with Modifier=modify;OnUpdate=(fun _ -> updates<-updates+1)}
        use child=CssAnimation.declarationWith settings scheduler recorder
                        (Declaration.create "transform" "translate(0px,0px)") (Declaration.create "transform" "translate(100px,200px)") 100u
        child.Pause()
        use timeline=new Timeline(scheduler)
        timeline.Sync(child,TimelinePosition.At 0.) |> ignore
        parent<-Some timeline
        let initial=recorder.Writes.Count
        calls<-0
        armed<-true
        timeline.Seek 50.
        Expect.equal recorder.Writes.Count initial "ancestor cancellation prevents final style commit"
        Expect.equal calls 1 "later component effects suppressed"
        Expect.equal updates 0 "borrowed lifecycle guards retained"
        Expect.isFalse child.Cancelled "borrowed controller stays reusable"
        armed<-false
        child.Seek 25.
        Expect.equal ((recorder:>CSSStyleDeclaration).getPropertyValue("transform")) "translate(25px,50px)" "ancestor scope restored"
    testCase "invalid declarations and options fail before writes" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let recorder=RecordingStyle()
        let first=Css.width(Css.px 0.)
        let last=Css.width(Css.px 10.)
        for settings in [{DeclarationAnimationOptions.Default with Precision=Some 309u}
                         {DeclarationAnimationOptions.Default with Playback={PlaybackOptions.Default with Delay=nan}}
                         {DeclarationAnimationOptions.Default with Ease=Unchecked.defaultof<_>}] do
            Expect.throwsT<ArgumentException> (fun () -> CssAnimation.declarationWith settings scheduler recorder first last 100u |> ignore) "invalid options"
        Expect.throwsT<ArgumentException> (fun () -> CssAnimation.declaration scheduler recorder first (Css.height(Css.px 10.)) 100u |> ignore) "different property"
        Expect.throwsT<ArgumentException> (fun () -> CssAnimation.declaration scheduler recorder first (Css.width(Css.em 1.)) 100u |> ignore) "contextual units"
        for duration in [-1.;nan;infinity;1e-14] do
            Expect.throwsT<ArgumentException> (fun () -> CssAnimation.declarationPrecise scheduler recorder first last duration |> ignore) "invalid precise duration"
        Expect.equal recorder.Writes.Count 0 "invalid construction does not write"
]
