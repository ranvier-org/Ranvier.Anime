module Ranvier.Anime.Tests.ScopeClockDefaultsTests

open System
open Expecto
open Ranvier.Anime
open Ranvier.Dom

let private authored =
    { PlaybackOptions.Default with Repeat=PlaybackRepeat.Count 4u;Delay=20.;LoopDelay=30.
                                   Reversed=true;Alternate=true;Speed=3.;FrameRate=60. }

let scopeClockDefaultsTests = testSequenced <| testList "scope clock defaults" [
    testCase "absent fields inherit and explicit false and zero survive application" <| fun _ ->
        Expect.equal (PlaybackDefaults.Empty.Apply authored) authored "all inherited"
        let overrides =
            { PlaybackDefaults.Empty with Repeat=Some(PlaybackRepeat.Count 0u);Delay=Some 0.;LoopDelay=Some 0.
                                          Reversed=Some false;Alternate=Some false;Speed=Some 0.;FrameRate=Some 0. }
        Expect.equal (overrides.Apply authored) {PlaybackOptions.Default with Speed=0.;FrameRate=0.} "all seven present"

    testCase "scope omitted Timer options receive all seven authored fields" <| fun _ ->
        use graph = new Ranvier.Graph()
        use active = graph.Activate()
        use frames = new ManualFrameScheduler()
        use scope = new Scope(defaults=authored)
        scope.Execute(fun _ ->
            use clock = new Timer(frames,100.)
            Expect.equal (clock.IterationCount,clock.Delay,clock.LoopDelay,clock.Reversed,clock.Speed,clock.Fps)
                (5.,20.,30.,true,3.,60.) "clock construction"
            Expect.isTrue (clock.CaptureScopeTime().Alternate) "alternate authored")

    testCase "partial scope defaults merge inherited fields and explicit zeros normalize only in Timer" <| fun _ ->
        use graph = new Ranvier.Graph()
        use active = graph.Activate()
        use frames = new ManualFrameScheduler()
        use outer = new Scope(defaults=authored)
        outer.Execute(fun _ ->
            use inner = new Scope(clockDefaults={PlaybackDefaults.Empty with Delay=Some 0.;Reversed=Some false;Speed=Some 0.;FrameRate=Some 0.})
            inner.Execute(fun _ ->
                Expect.equal inner.ClockDefaults.Options.Speed 0. "authored zero retained"
                use clock = new Timer(frames,100.)
                Expect.equal (clock.Delay,clock.Reversed,clock.Speed,clock.Fps) (0.,false,1e-11,1e-11) "existing normalization"
                Expect.equal (clock.IterationCount,clock.LoopDelay) (5.,30.) "remaining inherited"))

    testCase "no-override descendants share identity and overrides including empty create copies" <| fun _ ->
        use graph = new Ranvier.Graph()
        use active = graph.Activate()
        use outer = new Scope(defaults=authored)
        outer.Execute(fun _ ->
            use inherited = new Scope()
            use copied = new Scope(clockDefaults=PlaybackDefaults.Empty)
            Expect.isTrue (Object.ReferenceEquals(outer.ClockDefaults,inherited.ClockDefaults)) "same retained object"
            Expect.isFalse (Object.ReferenceEquals(outer.ClockDefaults,copied.ClockDefaults)) "present empty override copies"
            Expect.isTrue (Object.ReferenceEquals(inherited.ParentScope.Value,outer)) "captured construction parent"
            outer.ClockDefaults.Options <- { authored with Speed=4. }
            Expect.equal inherited.ClockDefaults.Options.Speed 4. "shared later mutation"
            Expect.equal copied.ClockDefaults.Options.Speed 3. "copy captured fallback")

    testCase "top-level omitted defaults share global mutations while overridden scopes retain copies" <| fun _ ->
        use graph = new Ranvier.Graph()
        use active = graph.Activate()
        let globalCell = Scope.GlobalClockDefaults
        let previous = globalCell.Options
        try
            globalCell.Options <- authored
            use direct = new Scope()
            use copied = new Scope(clockDefaults={PlaybackDefaults.Empty with Delay=Some 0.})
            globalCell.Options <- { authored with Speed=4. }
            Expect.isTrue (Object.ReferenceEquals(direct.ClockDefaults,globalCell)) "global identity"
            Expect.equal direct.ClockDefaults.Options.Speed 4. "global later mutation"
            Expect.equal copied.ClockDefaults.Options.Speed 3. "captured global fallback"
        finally globalCell.Options <- previous

    testCase "nested execution and exceptions restore the previous defaults" <| fun _ ->
        use graph = new Ranvier.Graph()
        use active = graph.Activate()
        let before = PlaybackDefaults.current()
        use outer = new Scope(defaults=authored)
        outer.Execute(fun _ ->
            use inner = new Scope(clockDefaults={PlaybackDefaults.Empty with Speed=Some 4.})
            Expect.throws(fun () -> inner.Execute(fun _ ->
                Expect.equal (PlaybackDefaults.current().Speed) 4. "inner active"
                failwith "callback")) "expected callback failure"
            Expect.equal (PlaybackDefaults.current()) authored "outer restored")
        Expect.equal (PlaybackDefaults.current()) before "global restored"

    testCase "refresh methods and revert retain the same mutable defaults cell" <| fun _ ->
        use graph = new Ranvier.Graph()
        use active = graph.Activate()
        use frames = new ManualFrameScheduler()
        use scope = new Scope(defaults=authored)
        let speeds = ResizeArray<float>()
        scope.Add(fun _ ->
            let clock = new Timer(frames,100.)
            speeds.Add clock.Speed
            None) |> ignore
        scope.AddMethod("speed",fun () -> PlaybackDefaults.current().Speed) |> ignore
        scope.ClockDefaults.Options <- {authored with Speed=4.}
        scope.Refresh() |> ignore
        Expect.sequenceEqual speeds [3.;4.] "reconstruction read retained values"
        Expect.equal (scope.Invoke<unit,float>("speed",())) 4. "named method context"
        let cell=scope.ClockDefaults
        scope.Revert()
        Expect.isTrue (Object.ReferenceEquals(scope.ClockDefaults,cell)) "revert retains object"
        Expect.equal scope.Defaults (Some {authored with Speed=4.}) "retained public view"

    testCase "explicit full factory options override scope values without guessing absent fields" <| fun _ ->
        use graph = new Ranvier.Graph()
        use active = graph.Activate()
        use frames = new ManualFrameScheduler()
        use scope = new Scope(defaults=authored)
        scope.Execute(fun _ ->
            use explicitClock = new Timer(frames,100.,options=PlaybackOptions.Default)
            Expect.equal (explicitClock.Speed,explicitClock.Reversed,explicitClock.IterationCount) (1.,false,1.) "fully authored options"
            use partialClock = new Timer(frames,100.,options=PlaybackDefaults.resolve {PlaybackDefaults.Empty with Delay=Some 0.;Reversed=Some false})
            Expect.equal (partialClock.Delay,partialClock.Reversed,partialClock.Speed) (0.,false,3.) "typed partial factory override")

    testCase "numeric direct and default Animation helper use the active defaults" <| fun _ ->
        use graph = new Ranvier.Graph()
        use active = graph.Activate()
        use frames = new ManualFrameScheduler()
        use scope = new Scope(defaults=authored)
        let mutable value=0.
        scope.Execute(fun _ ->
            use numeric = new NumericAnimation(frames,(fun () -> value),(fun next -> value<-next),100.,100.)
            use helper = Animation.animate frames (fun () -> value) (fun next -> value<-next) 100. 100.
            Expect.equal (numeric.Speed,numeric.Fps,numeric.Reversed) (3.,60.,true) "direct constructor"
            Expect.equal (helper.Speed,helper.Fps,helper.Reversed) (3.,60.,true) "default facade")

    testCase "Timeline omitted options use active defaults and explicit options stay explicit" <| fun _ ->
        use graph = new Ranvier.Graph()
        use active = graph.Activate()
        use frames = new ManualFrameScheduler()
        use scope = new Scope(defaults=authored)
        scope.Execute(fun _ ->
            use timeline = new Timeline(frames)
            use explicitTimeline = new Timeline(frames,options=PlaybackOptions.Default)
            Expect.equal (timeline.Speed,timeline.Fps,timeline.Reversed) (3.,60.,true) "hidden Timer inheritance"
            Expect.equal explicitTimeline.Speed 1. "explicit options")

    testCase "default keyframe and declaration option records resolve active clock values" <| fun _ ->
        use graph = new Ranvier.Graph()
        use active = graph.Activate()
        use scope = new Scope(defaults=authored)
        scope.Execute(fun _ ->
            Expect.equal KeyframeAnimationOptions.Default.Playback authored "keyframe default factory"
            Expect.equal DeclarationAnimationOptions.Default.Playback authored "CSS default factory")

    testCase "keyframe and animatable property factories construct clocks from inherited defaults" <| fun _ ->
        use graph = new Ranvier.Graph()
        use active = graph.Activate()
        use frames = new ManualFrameScheduler()
        use scope = new Scope(defaults=authored)
        let mutable value=0.
        scope.Execute(fun _ ->
            let property=NumericProperty((fun () -> value),(fun next -> value<-next))
            use keyframes=Keyframes.Numeric(frames,property,[|Keyframe.toValue 100.|],duration=100u)
            use animatable=new Animatable(frames)
            let handle=animatable.Numeric(property,duration=100u)
            Expect.equal (keyframes.Playback.Speed,keyframes.Playback.Fps,keyframes.Playback.Reversed) (3.,60.,true) "keyframe clock"
            Expect.equal (handle.Playback.Speed,handle.Playback.Fps,handle.Playback.Reversed) (3.,60.,true) "animatable property clock"
            Expect.equal animatable.Callbacks.Speed 3. "independent callback animation")

    testCase "invalid inherited fields reject in existing controllers and restore ambient defaults" <| fun _ ->
        use graph = new Ranvier.Graph()
        use active = graph.Activate()
        use frames = new ManualFrameScheduler()
        let before=PlaybackDefaults.current()
        use scope=new Scope(clockDefaults={PlaybackDefaults.Empty with Speed=Some nan})
        Expect.throwsT<ArgumentException>(fun () -> scope.Execute(fun _ -> new Timer(frames,100.) |> ignore)) "existing finite validation"
        Expect.equal (PlaybackDefaults.current()) before "failed construction restores context"

    testCase "unscoped construction remains graph-free with shared defaults" <| fun _ ->
        use frames = new ManualFrameScheduler()
        use clock = new Timer(frames,100.)
        Expect.equal clock.Speed (Scope.GlobalClockDefaults.Options.Speed |> max 1e-11) "outside ambient graph"

    testCase "pure root routing omits querying for absent selector and resets to document" <| fun _ ->
        let mutable queried=false
        let selected=ScopeRootRouting.resolve "document" "outer" (fun _ _ -> queried<-true;Some "inner") None
        Expect.equal selected "document" "nested omitted root"
        Expect.isFalse queried "omitted root does not query"

    testCase "pure root routing searches active root and uses first result or document fallback" <| fun _ ->
        let calls=ResizeArray<string*string>()
        let query root selector = calls.Add(root,selector);if selector="inside" then Some "inner" else None
        Expect.equal (ScopeRootRouting.resolve "document" "outer" query (Some "inside")) "inner" "selected"
        Expect.equal (ScopeRootRouting.resolve "document" "outer" query (Some "outside")) "document" "unmatched fallback"
        Expect.sequenceEqual calls ["outer","inside";"outer","outside"] "query receiver"

    testCase "root routing preserves native invalid-selector exceptions" <| fun _ ->
        let fault=InvalidOperationException "native selector"
        let mutable seen:exn option=None
        try ScopeRootRouting.resolve "document" "outer" (fun _ _ -> raise fault) (Some "[") |> ignore
        with error -> seen<-Some error
        Expect.isTrue (seen |> Option.exists(fun error -> Object.ReferenceEquals(error,fault))) "native error identity"
]
