module Ranvier.Anime.Tests.ScopeAnimationDefaultsTests

open System
open System.IO
open System.Text.Json
open System.Collections.Generic
open Expecto
open Ranvier.Dom
open Ranvier.Css
open Ranvier.Anime

let private half progress=progress*0.5
let private quant value=floor(value/10.+0.5)*10.
let private overrides =
    {AnimationDefaults.Empty with Duration=Some 240.;Ease=Some half;Modifier=Some quant
                                  Composition=Some KeyframeComposition.None;Autoplay=Some false}
let private sourceCases () =
    let root=Environment.GetEnvironmentVariable "RANVIER_ANIME_REFERENCE_ROOT"
    let root=if String.IsNullOrEmpty root then Path.Combine(AppContext.BaseDirectory,"reference") else root
    use source=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"scope-animation-defaults-reference.json")))
    source.RootElement.GetProperty("cases").Clone()

type private Style(?onWrite:unit -> unit) =
    let values=Dictionary<string,string*string>()
    let names=ResizeArray<string>()
    member _.Value name=match values.TryGetValue name with true,(value,_) -> value | _ -> ""
    interface CSSStyleDeclaration with
        member this.getPropertyValue name=this.Value name
        member _.getPropertyPriority name=match values.TryGetValue name with true,(_,priority) -> priority | _ -> ""
        member _.setProperty(name,value,priority)=
            onWrite |> Option.iter(fun callback -> callback())
            if not(values.ContainsKey name) then names.Add name
            values[name]<-value,defaultArg priority ""
        member this.removeProperty name=
            let old=this.Value name
            values.Remove name |> ignore
            names.Remove name |> ignore
            old
        member _.length=uint names.Count
        member _.item index=names[int index]
        member _.cssText with get()=failwith "whole style read" and set _=failwith "whole style replace"
        member _.parentRule=Unchecked.defaultof<CSSRule>
        member _.cssRules=Unchecked.defaultof<CSSRuleList>

let scopeAnimationDefaultsTests = testSequenced <| testList "scope constant animation defaults" [
    testCase "default diagnostics preserve explicit values without invoking callbacks" <| fun _ ->
        let fail _ = failwith "Diagnostic invoked a callback."
        let animation={AnimationDefaults.Empty with Duration=Some 0.;Ease=Some fail;Modifier=Some fail;Autoplay=Some false}
        Expect.equal (animation.ToString()) "AnimationDefaults(Duration=0; Composition=inherit; Autoplay=False)" "constant override presence"
        let playback={PlaybackDefaults.Empty with Delay=Some 0.;Reversed=Some false}
        Expect.equal (playback.ToString())
            "PlaybackDefaults(Repeat=inherit; Delay=0; LoopDelay=inherit; Reversed=False; Alternate=inherit; Speed=inherit; FrameRate=inherit)"
            "clock override presence"

    for factory in ["numeric";"declaration";"custom property"] do
        for autoplay in [false;true] do
            testCase (sprintf "%s captures autoplay %b before initialization callbacks" factory autoplay) <| fun _ ->
                use graph=new Ranvier.Graph()
                use active=graph.Activate()
                use scheduler=new ManualFrameScheduler()
                use scope=new Scope(animationDefaults={AnimationDefaults.Empty with Autoplay=Some autoplay})
                let mutate () = scope.ClockDefaults.Animation <- {scope.ClockDefaults.Animation with Autoplay=not autoplay}
                scope.Execute(fun _ ->
                    let style=Style(onWrite=mutate)
                    let playing =
                        match factory with
                        | "numeric" ->
                            use animation=Animation.animate scheduler (fun () -> mutate();0.) (fun _ -> mutate()) 100. 100.
                            animation.Playing
                        | "declaration" ->
                            use clock=CssAnimation.declaration scheduler style (Declaration.create "width" "0px") (Declaration.create "width" "100px") 100u
                            not clock.Paused
                        | _ ->
                            use animation=CssAnimation.customProperty scheduler style (RuntimeApi.createCustomProperty "--startup")
                                              (Generated.number 0. "px") (Generated.number 100. "px") 100.
                            animation.Playing
                    Expect.equal scope.ClockDefaults.Animation.Autoplay (not autoplay) "initialization callback changed future defaults"
                    Expect.equal playing autoplay "current construction retained its captured startup setting")

    testCase "optional fields preserve explicit duration0 autoplayfalse and function identity" <| fun _ ->
        let inherited={AnimationDefaultOptions.Default with Duration=240.;Ease=half;Modifier=quant}
        let applied={AnimationDefaults.Empty with Duration=Some 0.;Autoplay=Some false;Composition=Some KeyframeComposition.None}.Apply inherited
        Expect.equal (applied.Duration,applied.Autoplay,applied.Composition) (0.,false,KeyframeComposition.None) "presence retained"
        Expect.isTrue (Object.ReferenceEquals(applied.Ease,inherited.Ease)) "easing callback retained"
        Expect.isTrue (Object.ReferenceEquals(applied.Modifier,inherited.Modifier)) "modifier callback retained"

    testCase "actual keyframe factory agrees with source five-field inherited playback" <| fun _ ->
        let expected=(sourceCases()).GetProperty "inherited"
        use graph=new Ranvier.Graph()
        use active=graph.Activate()
        use scheduler=new ManualFrameScheduler()
        use scope=new Scope(animationDefaults=overrides)
        let mutable value=0.
        scope.Execute(fun _ ->
            let property=NumericProperty((fun () -> value),(fun next -> value<-next))
            use animation=Keyframes.Numeric(scheduler,property,[|Keyframe.toValue 100.|])
            Expect.equal animation.Playback.IterationDuration (expected.GetProperty("iterationDuration").GetDouble()) "omitted duration"
            Expect.equal animation.Playback.Paused (expected.GetProperty("paused").GetBoolean()) "autoplayfalse"
            animation.Playback.Seek 120.
            Expect.equal value (expected.GetProperty("value120").GetDouble()) "easing then modifier"
            Expect.equal KeyframeAnimationOptions.Default.Composition KeyframeComposition.None "typed composition"
            let settings=AnimationDefaults.current()
            Expect.equal (settings.Ease 0.5,settings.Modifier 25.) (0.25,30.) "retained functions")

    testCase "constant duration flows through the precise path without truncating fractions" <| fun _ ->
        use graph=new Ranvier.Graph()
        use active=graph.Activate()
        use scheduler=new ManualFrameScheduler()
        use scope=new Scope(animationDefaults={overrides with Duration=Some 240.5})
        scope.Execute(fun _ ->
            let mutable value=0.
            let property=NumericProperty((fun () -> value),(fun next -> value<-next))
            use animation=Keyframes.Numeric(scheduler,property,[|Keyframe.toValue 100.|])
            Expect.equal animation.Playback.IterationDuration 240.5 "precise inherited duration"
            use explicitAnimation=Keyframes.Numeric(scheduler,property,[|Keyframe.toValue 100.|],duration=120u)
            Expect.equal explicitAnimation.Playback.IterationDuration 120. "unsigned explicit duration wins")

    testCase "explicit zero remains present through existing zero-duration normalization" <| fun _ ->
        let expected=(sourceCases()).GetProperty "zero"
        use graph=new Ranvier.Graph()
        use active=graph.Activate()
        use scheduler=new ManualFrameScheduler()
        use scope=new Scope(animationDefaults=overrides)
        scope.Execute(fun _ ->
            let mutable value=0.
            let property=NumericProperty((fun () -> value),(fun next -> value<-next))
            use animation=Keyframes.Numeric(scheduler,property,[|Keyframe.toValue 100.|],duration=0u,
                options={KeyframeAnimationOptions.Default with Autoplay=true})
            Expect.equal animation.Playback.IterationDuration (expected.GetProperty("iterationDuration").GetDouble()) "zero does not inherit240"
            Expect.equal animation.Playback.Paused (expected.GetProperty("paused").GetBoolean()) "instant playback completes")

    testCase "nooverride shares all fields and any scalar clock override copies animation fields" <| fun _ ->
        let expected=(sourceCases()).GetProperty "mutation"
        use graph=new Ranvier.Graph()
        use active=graph.Activate()
        use outer=new Scope(animationDefaults=overrides)
        outer.Execute(fun _ ->
            use inherited=new Scope()
            use copied=new Scope(clockDefaults={PlaybackDefaults.Empty with Speed=Some 2.})
            outer.ClockDefaults.Animation<-{outer.ClockDefaults.Animation with Duration=300.}
            Expect.equal inherited.ClockDefaults.Animation.Duration (expected.GetProperty("inherited").GetDouble()) "shared animation mutation"
            Expect.equal copied.ClockDefaults.Animation.Duration (expected.GetProperty("copied").GetDouble()) "clock override copied animation fallback"
            Expect.isTrue (Object.ReferenceEquals(inherited.ClockDefaults,outer.ClockDefaults)) "same full defaults cell"
            Expect.isFalse (Object.ReferenceEquals(copied.ClockDefaults,outer.ClockDefaults)) "copied full defaults cell")

    testCase "an animation-only override also snapshots clock defaults" <| fun _ ->
        use graph=new Ranvier.Graph()
        use active=graph.Activate()
        use outer=new Scope(clockDefaults={PlaybackDefaults.Empty with Speed=Some 3.},animationDefaults=overrides)
        outer.Execute(fun _ ->
            use child=new Scope(animationDefaults={AnimationDefaults.Empty with Autoplay=Some true})
            outer.ClockDefaults.Options<-{outer.ClockDefaults.Options with Speed=4.}
            Expect.equal child.ClockDefaults.Options.Speed 3. "whole defaults object copied"
            Expect.equal child.ClockDefaults.Animation.Duration 240. "animation fallback captured"
            Expect.isTrue child.ClockDefaults.Animation.Autoplay "explicit true")

    testCase "refresh named methods and revert retain mutated animation defaults" <| fun _ ->
        let cases=sourceCases()
        use graph=new Ranvier.Graph()
        use active=graph.Activate()
        use scheduler=new ManualFrameScheduler()
        use scope=new Scope(animationDefaults={overrides with Duration=Some 300.})
        let durations=ResizeArray<float>()
        scope.Add(fun _ ->
            let mutable value=0.
            let animation=Keyframes.Numeric(scheduler,NumericProperty((fun () -> value),(fun next -> value<-next)),[|Keyframe.toValue 100.|])
            durations.Add animation.Playback.IterationDuration
            None) |> ignore
        scope.AddMethod("duration",fun () -> AnimationDefaults.duration None) |> ignore
        scope.ClockDefaults.Animation<-{scope.ClockDefaults.Animation with Duration=360.}
        scope.Refresh() |> ignore
        Expect.sequenceEqual durations (cases.GetProperty("refresh").EnumerateArray() |> Seq.map _.GetDouble()) "source retained mutation"
        Expect.equal (scope.Invoke<unit,float>("duration",())) 360. "method active defaults"
        scope.Revert()
        Expect.equal scope.ClockDefaults.Animation.Duration (cases.GetProperty("afterRevert").GetProperty("duration").GetDouble()) "revert retains defaults"

    testCase "explicit full options retain authored easing modifier composition and autoplay" <| fun _ ->
        let expected=(sourceCases()).GetProperty "explicit"
        use graph=new Ranvier.Graph()
        use active=graph.Activate()
        use scheduler=new ManualFrameScheduler()
        use scope=new Scope(animationDefaults=overrides)
        scope.Execute(fun _ ->
            let mutable value=0.
            let options={KeyframeAnimationOptions.Default with Ease=id;Modifier=id;Composition=KeyframeComposition.Replace;Autoplay=true}
            use animation=Keyframes.Numeric(scheduler,NumericProperty((fun () -> value),(fun next -> value<-next)),[|Keyframe.toValue 100.|],duration=120u,options=options)
            animation.Playback.Seek 60.
            Expect.equal value (expected.GetProperty("value60").GetDouble()) "explicit fields"
            Expect.equal animation.Playback.Paused (expected.GetProperty("paused").GetBoolean()) "explicit autoplay")

    testCase "animatable property and independent callback clocks inherit constant duration" <| fun _ ->
        use graph=new Ranvier.Graph()
        use active=graph.Activate()
        use scheduler=new ManualFrameScheduler()
        use scope=new Scope(animationDefaults=overrides)
        scope.Execute(fun _ ->
            let mutable value=0.
            use group=new Animatable(scheduler)
            let handle=group.Numeric(NumericProperty((fun () -> value),(fun next -> value<-next)))
            Expect.equal (handle.Playback.IterationDuration,group.Callbacks.IterationDuration) (240.,240.) "both source-created animations"
            Expect.isTrue handle.Playback.Paused "source property explicitly constructs paused"
            handle.To 100.
            handle.Playback.Seek 120.
            Expect.equal value 30. "property ease and modifier")

    testCase "legacy declaration applies typed scope easing modifier and autoplay" <| fun _ ->
        use graph=new Ranvier.Graph()
        use active=graph.Activate()
        use scheduler=new ManualFrameScheduler()
        use scope=new Scope(animationDefaults=overrides)
        let style=Style()
        scope.Execute(fun _ ->
            use clock=CssAnimation.declaration scheduler style (Declaration.create "width" "0px") (Declaration.create "width" "100px") 240u
            Expect.isTrue clock.Paused "autoplayfalse"
            clock.Seek 120.
            Expect.equal (style.Value "width") "30px" "ease before modifier")

    testCase "linear numeric facade stays explicit while omitted constructor easing inherits" <| fun _ ->
        use graph=new Ranvier.Graph()
        use active=graph.Activate()
        use scheduler=new ManualFrameScheduler()
        use scope=new Scope(animationDefaults=overrides)
        scope.Execute(fun _ ->
            let mutable directValue=0.
            use direct=new NumericAnimation(scheduler,(fun () -> directValue),(fun next -> directValue<-next),100.,240.)
            direct.Seek 120.
            Expect.equal directValue 25. "omitted ease inherits; raw numeric has no modifier field"
            let mutable linearValue=0.
            use linear=Animation.animate scheduler (fun () -> linearValue) (fun next -> linearValue<-next) 100. 240.
            Expect.isFalse linear.Playing "source scope autoplayfalse"
            linear.Seek 120.
            Expect.equal linearValue 50. "existing explicitly linear facade")

    testCase "global cell mutation affects nooverride scopes while override copies retain constants" <| fun _ ->
        use graph=new Ranvier.Graph()
        use active=graph.Activate()
        let cell=Scope.GlobalClockDefaults
        let previous=cell.Animation
        try
            cell.Animation<-{previous with Duration=240.}
            use shared=new Scope()
            use copied=new Scope(clockDefaults=PlaybackDefaults.Empty)
            cell.Animation<-{previous with Duration=300.}
            Expect.equal shared.ClockDefaults.Animation.Duration 300. "global identity"
            Expect.equal copied.ClockDefaults.Animation.Duration 240. "combined snapshot"
        finally cell.Animation<-previous

    testCase "invalid inherited duration rejects before target access" <| fun _ ->
        use graph=new Ranvier.Graph()
        use active=graph.Activate()
        use scheduler=new ManualFrameScheduler()
        use scope=new Scope(animationDefaults={AnimationDefaults.Empty with Duration=Some nan})
        let mutable reads=0
        scope.Execute(fun _ ->
            Expect.throwsT<ArgumentException>(fun () -> Keyframes.Numeric(scheduler,NumericProperty((fun () -> reads<-reads+1;0.),ignore),[|Keyframe.toValue 100.|]) |> ignore) "existing validation"
            Expect.equal reads 0 "invalid default rejected before target reads")

    testCase "sparse numeric sequence and timeline-owned factories inherit omitted duration" <| fun _ ->
        use graph=new Ranvier.Graph()
        use active=graph.Activate()
        use scheduler=new ManualFrameScheduler()
        use scope=new Scope(animationDefaults=overrides)
        scope.Execute(fun _ ->
            let mutable value=0.
            let property=NumericProperty((fun () -> value),(fun next -> value<-next))
            let sequence=KeyframeSequence.Rows [|KeyframeRow.values [|"x",100.|]|]
            use sequenceAnimation=Keyframes.Numeric(scheduler,[|"x",[|property|]|],sequence)
            Expect.equal sequenceAnimation.Playback.IterationDuration 240. "sparse sequence"
            use timeline=new Timeline(scheduler)
            let child=timeline.AddKeyframes(property,[|Keyframe.toValue 100.|])
            Expect.equal child.Playback.IterationDuration 240. "owned scalar frames"
            use sequenceTimeline=new Timeline(scheduler)
            let sequenceChild=sequenceTimeline.AddKeyframes([|"x",[|property|]|],sequence)
            Expect.equal sequenceChild.Playback.IterationDuration 240. "owned sparse sequence")

    testCase "typed CSS keyframe sequence and timeline factories inherit omitted duration" <| fun _ ->
        use graph=new Ranvier.Graph()
        use active=graph.Activate()
        use scheduler=new ManualFrameScheduler()
        use scope=new Scope(animationDefaults=overrides)
        let style=Style() :> CSSStyleDeclaration
        let first=Declaration.create "width" "0px"
        let last=Declaration.create "width" "100px"
        scope.Execute(fun _ ->
            use animation=Keyframes.Declaration(scheduler,style,first,[|Keyframe.toValue last|])
            Expect.equal animation.Playback.IterationDuration 240. "CSS scalar frames"
            let sequence=KeyframeSequence.Rows [|KeyframeRow.declarations [|last|]|]
            use sequenceAnimation=Keyframes.Declaration(scheduler,style,[|first|],sequence)
            Expect.equal sequenceAnimation.Playback.IterationDuration 240. "CSS sparse sequence"
            use timeline=new Timeline(scheduler)
            let child=timeline.AddKeyframes(style,first,[|Keyframe.toValue last|])
            Expect.equal child.Playback.IterationDuration 240. "owned CSS scalar frames"
            use sequenceTimeline=new Timeline(scheduler)
            let sequenceChild=sequenceTimeline.AddKeyframes(style,[|first|],sequence)
            Expect.equal sequenceChild.Playback.IterationDuration 240. "owned CSS sparse sequence"
            use group=new Animatable(scheduler)
            let property=group.Declaration(style,first)
            Expect.equal property.Playback.IterationDuration 240. "retargetable CSS property")

    for phase in ["ease";"modifier"] do
        testCase ("inherited "+phase+" reentry cannot write through a disposed owner") <| fun _ ->
            use graph=new Ranvier.Graph()
            use active=graph.Activate()
            use scheduler=new ManualFrameScheduler()
            let mutable current:Scope option=None
            let mutable owner:Ranvier.Owner option=None
            let mutable closed=false
            let mutable lateWrites=0
            let interrupt value =
                closed<-true
                current.Value.Revert()
                value
            let settings={overrides with Ease=Some(if phase="ease" then interrupt else id)
                                         Modifier=Some(if phase="modifier" then interrupt else id)}
            use scope=new Scope(animationDefaults=settings)
            current<-Some scope
            try
                scope.Add(fun _ ->
                    owner<-Some(ScopeRegistration.resourceOwner())
                    Keyframes.Numeric(scheduler,NumericProperty((fun () -> 0.),(fun _ -> if owner.Value.IsDisposed then lateWrites<-lateWrites+1)),[|Keyframe.toValue 100.|]) |> ignore
                    None) |> ignore
            with
            | :? ObjectDisposedException -> ()
            | :? InvalidOperationException -> ()
            Expect.isTrue closed "inherited user callback ran"
            Expect.isTrue scope.IsDisposed "actual scope lifetime closed"
            Expect.equal lateWrites 0 "existing constructor/render guards excluded writes"

    testCase "unscoped keyframe factories observe shared global constant mutations" <| fun _ ->
        let expected=(sourceCases()).GetProperty "globalFactory"
        use scheduler=new ManualFrameScheduler()
        let cell=Scope.GlobalClockDefaults
        let previous=cell.Animation
        try
            cell.Animation<-{Duration=180.;Ease=half;Modifier=quant;Composition=KeyframeComposition.None;Autoplay=false}
            let mutable value=0.
            use animation=Keyframes.Numeric(scheduler,NumericProperty((fun () -> value),(fun next -> value<-next)),[|Keyframe.toValue 100.|])
            animation.Playback.Seek 90.
            Expect.equal animation.Playback.IterationDuration (expected.GetProperty("iterationDuration").GetDouble()) "global duration"
            Expect.equal animation.Playback.Paused (expected.GetProperty("paused").GetBoolean()) "global autoplay"
            Expect.equal value (expected.GetProperty("value90").GetDouble()) "global easing and modifier"
        finally cell.Animation<-previous
]
