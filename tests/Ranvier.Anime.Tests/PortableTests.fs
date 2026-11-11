module Ranvier.Anime.Tests.PortableTests

open System
open System.Collections.Generic
open System.Reflection
open Expecto
open Ranvier
open Ranvier.Dom
open Ranvier.Css
open Ranvier.Anime

/// <summary>Dispatches the DOM interface members the portable layer calls.</summary>
type PortableProxy() =
    inherit DispatchProxy()
    /// <summary>The test fixture's member implementation.</summary>
    member val Handler: MethodInfo -> obj array -> obj = (fun methodInfo _ -> failwith methodInfo.Name) with get, set
    override this.Invoke(methodInfo, args) = this.Handler methodInfo args

let private proxy<'t> handler =
    let value = DispatchProxy.Create<'t, PortableProxy>()
    (box value :?> PortableProxy).Handler <- handler
    value

// A number in the member's own return type.
let private number (methodInfo: MethodInfo) (value: float) = Convert.ChangeType(value, methodInfo.ReturnType)

type private FakeElement(name: string) as this =
    let styles = Dictionary<string, string * string>()
    let computed = Dictionary<string, string>()
    let attributes = Dictionary<string, string>()
    let children = ResizeArray<FakeElement>()
    let mutable dom = Unchecked.defaultof<HTMLElement>

    let style =
        proxy<CSSStyleProperties> (fun methodInfo args ->
            let key () = unbox<string> args[0]

            match methodInfo.Name with
            | "setProperty" ->
                let priority =
                    match args[2] with
                    | :? string as text -> text
                    | :? (string option) as text -> defaultArg text ""
                    | _ -> ""

                styles[key ()] <- (unbox<string> args[1], priority)
                null
            | "removeProperty" ->
                let old = this.Inline(key ())
                styles.Remove(key ()) |> ignore
                box old
            | "getPropertyValue" -> box (this.Inline(key ()))
            | "getPropertyPriority" -> box (this.Priority(key ()))
            | other -> failwith ("style " + other))

    do
        dom <-
            proxy<HTMLElement> (fun methodInfo args ->
                match methodInfo.Name with
                | "get_style" -> box style
                | "getAttribute" ->
                    match attributes.TryGetValue(unbox<string> args[0]) with
                    | true, value -> box value
                    | _ -> null
                | "setAttribute" ->
                    attributes[unbox<string> args[0]] <- unbox<string> args[1]
                    null
                | "removeAttribute" ->
                    attributes.Remove(unbox<string> args[0]) |> ignore
                    null
                | "isSameNode" -> box (obj.ReferenceEquals(dom, args[0]))
                | "contains" -> box (this.Contains(unbox<Node> args[0]))
                | "get_children" ->
                    box (
                        proxy<HTMLCollection> (fun collection args ->
                            match collection.Name with
                            | "get_length" -> box (uint32 children.Count)
                            | "item" -> box (children[int (unbox<uint32> args[0])].Dom :> Element)
                            | other -> failwith ("children " + other))
                    )
                | "get_offsetTop" -> number methodInfo 20.
                | "get_offsetLeft" -> number methodInfo 10.
                | "get_offsetWidth" -> number methodInfo 200.
                | "get_offsetHeight" -> number methodInfo 40.
                | "get_scrollHeight" -> number methodInfo 320.
                | other -> failwith (name + " " + other))

    member _.Dom = dom
    member _.Element = dom :> Element
    member _.Computed = computed
    member _.Inline(property: string) = match styles.TryGetValue property with | true, (value, _) -> value | _ -> ""
    member _.Priority(property: string) = match styles.TryGetValue property with | true, (_, priority) -> priority | _ -> ""
    member _.Seed(property: string, value: string, priority: string) = styles[property] <- (value, priority)
    member _.Append(child: FakeElement) = children.Add child
    member _.Contains(node: Node) : bool = obj.ReferenceEquals(dom, node) || children.Exists(fun child -> child.Contains node)

    member _.ComputedStyle =
        { new CSSStyleDeclaration with
            member _.getPropertyValue property = match computed.TryGetValue property with | true, value -> value | _ -> ""
            member _.getPropertyPriority _ = ""
            member _.setProperty(_, _, _) = failwith "computed write"
            member _.removeProperty _ = failwith "computed remove"
            member _.item _ = ""
            member _.length = 0u
            member _.cssText with get () = "" and set _ = failwith "computed write"
            member _.parentRule = Unchecked.defaultof<CSSRule>
            member _.cssRules = Unchecked.defaultof<CSSRuleList> }

let private servicesFor (scheduler: FrameScheduler) (elements: FakeElement list) : AnimeServices =
    { Scheduler = scheduler
      Computed =
        { new ComputedStyleService with
            member _.Get(element, _) =
                (elements |> List.find (fun fake -> obj.ReferenceEquals(fake.Dom, element))).ComputedStyle } }

let private frames (scheduler: ManualFrameScheduler) count =
    for _ in 1..count do
        scheduler.Advance 10.
        scheduler.RunFrame()

// Read per test: the defaults follow the engine settings current at the call.
let private linearOptions () = { ComposedAnimationOptions.Default with Duration = 100.; Ease = id }
let private close expected actual message = Expect.floatClose Accuracy.high actual expected message
let private opacity (element: FakeElement) = Double.Parse(element.Inline "opacity", Globalization.CultureInfo.InvariantCulture)

let private hostTests =
    testList "AnimeHost" [
        testCase "Current raises until services are installed, and after they are disposed" <| fun _ ->
            Expect.throwsT<InvalidOperationException> (fun () -> AnimeHost.Current |> ignore) "nothing installed"
            use scheduler = new ManualFrameScheduler()
            let services = servicesFor scheduler []
            let handle = AnimeHost.Install services
            Expect.isTrue (obj.ReferenceEquals(AnimeHost.Current.Scheduler, scheduler)) "the installed services are current"
            handle.Dispose()
            Expect.throwsT<InvalidOperationException> (fun () -> AnimeHost.Current |> ignore) "disposal uninstalls"

        testCase "a nested install is current until its handle is disposed" <| fun _ ->
            use outerScheduler = new ManualFrameScheduler()
            use innerScheduler = new ManualFrameScheduler()
            use outer = AnimeHost.Install(servicesFor outerScheduler [])
            let inner = AnimeHost.Install(servicesFor innerScheduler [])
            Expect.isTrue (obj.ReferenceEquals(AnimeHost.Current.Scheduler, innerScheduler)) "the inner services are current"
            inner.Dispose()
            inner.Dispose()
            Expect.isTrue (obj.ReferenceEquals(AnimeHost.Current.Scheduler, outerScheduler)) "the outer services are restored once"

        testCase "services without a scheduler or computed styles are refused" <| fun _ ->
            use scheduler = new ManualFrameScheduler()
            let services = servicesFor scheduler []

            Expect.throwsT<ArgumentNullException>
                (fun () -> AnimeHost.Install { services with Scheduler = Unchecked.defaultof<FrameScheduler> } |> ignore)
                "scheduler"

            Expect.throwsT<ArgumentNullException>
                (fun () -> AnimeHost.Install { services with Computed = Unchecked.defaultof<ComputedStyleService> } |> ignore)
                "computed styles"

        testCase "drag and scroll need the interaction services" <| fun _ ->
            use scheduler = new ManualFrameScheduler()
            let element = FakeElement "target"
            use host = AnimeHost.Install(servicesFor scheduler [ element ])
            use timer = new Timer(scheduler, 100u)

            Expect.throwsT<InvalidOperationException>
                (fun () -> Drag.create element.Element NativeDraggableOptions.Default |> ignore)
                "Drag.create"

            Expect.throwsT<InvalidOperationException>
                (fun () -> Scroll.observe None element.Element ScrollOptions.Default timer (fun _ _ -> ()) |> ignore)
                "Scroll.observe"
    ]

let private animeTests =
    testList "Anime" [
        testCase "animate plays on the installed scheduler" <| fun _ ->
            use scheduler = new ManualFrameScheduler()
            use host = AnimeHost.Install(servicesFor scheduler [])
            let target = AnimatedObject [ "x", AdaptedValue.Scalar 0. ]
            use animation = Anime.animate (box target) [| PropertySpec.Scalar("x", 100.) |] (linearOptions ())
            frames scheduler 20
            close 100. (target.GetNumber "x") "the animation ran to its end"
            Expect.isTrue animation.Completed "and completed"

        testCase "animateTargets drives every target" <| fun _ ->
            use scheduler = new ManualFrameScheduler()
            use host = AnimeHost.Install(servicesFor scheduler [])
            let first = AnimatedObject [ "x", AdaptedValue.Scalar 0. ]
            let second = AnimatedObject [ "x", AdaptedValue.Scalar 50. ]

            use animation =
                Anime.animateTargets [| box first; box second |] [| PropertySpec.Scalar("x", 100.) |] { linearOptions () with Autoplay = false }

            animation.Seek 50. |> ignore
            close 50. (first.GetNumber "x") "first target halfway"
            close 75. (second.GetNumber "x") "second target halfway"

        testCase "animate reads starting values through the host's computed styles" <| fun _ ->
            use scheduler = new ManualFrameScheduler()
            let element = FakeElement "target"
            element.Computed["opacity"] <- "0.5"
            use host = AnimeHost.Install(servicesFor scheduler [ element ])
            use animation = Anime.animate (box element.Dom) [| PropertySpec.Scalar("opacity", 0.) |] { linearOptions () with Autoplay = false }
            animation.Seek 50. |> ignore
            close 0.25 (opacity element) "halfway from the computed 0.5"

        testCase "computed styles on the options win over the host's" <| fun _ ->
            use scheduler = new ManualFrameScheduler()
            let element = FakeElement "target"
            element.Computed["opacity"] <- "0.5"
            let other = FakeElement "other"
            other.Computed["opacity"] <- "0.8"
            use host = AnimeHost.Install(servicesFor scheduler [ element ])

            let own =
                { new ComputedStyleService with
                    member _.Get(_, _) = other.ComputedStyle }

            use animation =
                Anime.animate (box element.Dom) [| PropertySpec.Scalar("opacity", 0.) |] { linearOptions () with Autoplay = false; Computed = Some own }

            animation.Seek 50. |> ignore
            close 0.4 (opacity element) "halfway from the options' 0.8"

        testCase "a timeline's children read starting values through the host's computed styles" <| fun _ ->
            use scheduler = new ManualFrameScheduler()
            let element = FakeElement "target"
            element.Computed["opacity"] <- "0.5"
            use host = AnimeHost.Install(servicesFor scheduler [ element ])
            use timeline = Anime.timeline ()
            timeline.Pause() |> ignore
            timeline.Add([| box element.Dom |], [| PropertySpec.Scalar("opacity", 0.) |], options = linearOptions ()) |> ignore
            timeline.Seek 50. |> ignore
            close 0.25 (opacity element) "halfway from the computed 0.5"
            close 100. timeline.Duration "the child's duration"
    ]

let private followTests =
    testList "Follow" [
        testCase "property sets the first value at once, animates the next and restores on Revert" <| fun _ ->
            use scheduler = new ManualFrameScheduler()
            let element = FakeElement "target"
            element.Seed("opacity", "0.9", "important")
            use host = AnimeHost.Install(servicesFor scheduler [ element ])
            let follower = Follow.property element.Element "opacity" (linearOptions ())
            Expect.equal (element.Inline "opacity") "0.9" "nothing is written before the first value"
            follower.To 0.4
            close 0.4 (opacity element) "the first value is applied at once"
            follower.To 1.
            close 0.4 (opacity element) "the second value starts where the property is"
            frames scheduler 6
            let midway = opacity element
            Expect.isTrue (midway > 0.4 && midway < 1.) "and animates"
            frames scheduler 20
            close 1. (opacity element) "to its end"
            follower.Revert()
            Expect.equal (element.Inline "opacity") "0.9" "the inline value from before the follower"
            Expect.equal (element.Priority "opacity") "important" "and its priority"
            follower.To 0.2
            Expect.equal (element.Inline "opacity") "0.9" "a reverted follower writes nothing"

        testCase "property retargets from the running value" <| fun _ ->
            use scheduler = new ManualFrameScheduler()
            let element = FakeElement "target"
            use host = AnimeHost.Install(servicesFor scheduler [ element ])
            let follower = Follow.property element.Element "opacity" (linearOptions ())
            follower.To 0.
            follower.To 1.
            frames scheduler 6
            let interrupted = opacity element
            follower.To 0.
            frames scheduler 2
            Expect.isTrue (opacity element < interrupted && opacity element > 0.) "the retarget leaves from the interrupted value"
            follower.Pause()
            let paused = opacity element
            frames scheduler 20
            close paused (opacity element) "a paused follower stays where it is"
            follower.Revert()
            Expect.equal (element.Inline "opacity") "" "a property with no inline value is removed again"

        testCase "property keeps one track for a transform component" <| fun _ ->
            use scheduler = new ManualFrameScheduler()
            let element = FakeElement "target"
            use host = AnimeHost.Install(servicesFor scheduler [ element ])
            let follower = Follow.property element.Element "x" (linearOptions ())
            follower.To 10.
            Expect.equal (element.Inline "transform") "translateX(10px)" "the first value is applied at once"
            follower.To 50.
            frames scheduler 30
            Expect.equal (element.Inline "transform") "translateX(50px)" "the retarget reaches its value"
            follower.Revert()
            Expect.equal (element.Inline "transform") "" "the transform from before the follower"

        testCase "css follows a typed custom property in one unit" <| fun _ ->
            use scheduler = new ManualFrameScheduler()
            let element = FakeElement "target"
            use host = AnimeHost.Install(servicesFor scheduler [ element ])
            let gap = RuntimeApi.createCustomProperty "--gap"
            let follower = Follow.css element.Element gap (linearOptions ()) (Css.px 0.)
            Expect.equal (element.Inline "--gap") "" "the initial value is not written"
            follower.To(Css.px 20.)
            Expect.equal (element.Inline "--gap") "20px" "the first value is applied at once"
            follower.To(Css.px 60.)
            frames scheduler 30
            Expect.equal (element.Inline "--gap") "60px" "later values animate to their end"
            Expect.throwsT<ArgumentException> (fun () -> follower.To(Css.rem 2.)) "another unit is refused"
            follower.Revert()
            Expect.equal (element.Inline "--gap") "" "the inline value from before the follower"
    ]

let private nodeSetTests =
    testList "NodeSet" [
        testCase "holds each node once, by identity" <| fun _ ->
            let first = FakeElement "first"
            let second = FakeElement "second"
            let nodes = NodeSet()
            Expect.equal nodes.Count 0 "empty"
            nodes.Add first.Dom
            nodes.Add first.Dom
            Expect.equal nodes.Count 1 "a node is added once"
            Expect.isTrue (nodes.Has first.Dom) "has the node added"
            Expect.isFalse (nodes.Has second.Dom) "and not another"
            nodes.Remove second.Dom
            Expect.equal nodes.Count 1 "removing an absent node changes nothing"
            nodes.Remove first.Dom
            Expect.equal nodes.Count 0 "removed"

        testCase "AnyUnder finds a node at or under the root" <| fun _ ->
            let root = FakeElement "root"
            let child = FakeElement "child"
            let stranger = FakeElement "stranger"
            root.Append child
            let nodes = NodeSet()
            Expect.isFalse (nodes.AnyUnder root.Dom) "empty"
            nodes.Add stranger.Dom
            Expect.isFalse (nodes.AnyUnder root.Dom) "a node elsewhere"
            nodes.Add child.Dom
            Expect.isTrue (nodes.AnyUnder root.Dom) "a child of the root"
            Expect.isTrue (nodes.AnyUnder child.Dom) "the node itself"
    ]

let private flipTests =
    testList "Flip" [
        testCase "children leaves out the nodes of either set" <| fun _ ->
            let root = FakeElement "root"
            let rows = [ for index in 0..3 -> FakeElement("row" + string index) ]
            rows |> List.iter root.Append
            let leaving = NodeSet()
            let entering = NodeSet()
            Expect.equal (Flip.children root.Element leaving entering).Length 4 "every child"
            leaving.Add rows[1].Dom
            entering.Add rows[3].Dom
            let live = Flip.children root.Element leaving entering
            Expect.equal live.Length 2 "two remain"
            Expect.isTrue (obj.ReferenceEquals(live[0], rows[0].Dom) && obj.ReferenceEquals(live[1], rows[2].Dom)) "in document order"
    ]

let private domKitTests =
    testList "DomKit" [
        testCase "setStyle and removeStyle write the inline declaration" <| fun _ ->
            let element = FakeElement "target"
            DomKit.setStyle element.Element "overflow" "hidden"
            Expect.equal (element.Inline "overflow") "hidden" "set"
            DomKit.removeStyle element.Element "overflow"
            Expect.equal (element.Inline "overflow") "" "removed"

        testCase "pinOutOfFlow pins the element at its offset box" <| fun _ ->
            let element = FakeElement "target"
            DomKit.pinOutOfFlow element.Element

            let declared =
                [ for property in [ "position"; "top"; "left"; "width"; "height"; "margin"; "box-sizing" ] -> element.Inline property ]

            Expect.equal declared [ "absolute"; "20px"; "10px"; "200px"; "40px"; "0"; "border-box" ] "the pinned box"

        testCase "scrollHeight reads the content height" <| fun _ ->
            close 320. (DomKit.scrollHeight (FakeElement "target").Element) "content height"
    ]

let private reactiveTests =
    testList "Reactive.own" [
        testCase "reverts when the owner is disposed" <| fun _ ->
            use graph = new Graph()
            use activation = graph.Activate()
            let mutable reverts = 0

            let revertible =
                { new IRevertible with
                    member _.Revert() = reverts <- reverts + 1 }

            let owner =
                Api.createRoot (fun owner ->
                    Reactive.own revertible
                    owner)

            Expect.equal reverts 0 "live while the owner is"
            owner.Dispose()
            Expect.equal reverts 1 "reverted with the owner"
    ]

let private scrollFixture () =
    let mutable scrollY = 0
    let mutable listener: EventListenerObject option = None

    let rect left top width height =
        proxy<DOMRect> (fun methodInfo _ ->
            match methodInfo.Name with
            | "get_left" -> box left
            | "get_top" -> box top
            | "get_width" -> box width
            | "get_height" -> box height
            | other -> failwith other)

    let style =
        proxy<CSSStyleProperties> (fun methodInfo args ->
            match methodInfo.Name with
            | "getPropertyValue" -> box (if unbox<string> args[0] = "position" then "static" else "0px")
            | other -> failwith other)

    let mutable container: HTMLElement = Unchecked.defaultof<_>

    let document =
        proxy<Document> (fun methodInfo _ ->
            match methodInfo.Name with
            | "get_body" -> box container
            | other -> failwith other)

    container <-
        proxy<HTMLElement> (fun methodInfo args ->
            match methodInfo.Name with
            | "getBoundingClientRect" -> box (rect 10. 20. 600. 400.)
            | "get_clientWidth" -> box 600
            | "get_clientHeight" -> box 400
            | "get_scrollWidth" -> box 2400
            | "get_scrollHeight" -> box 2000
            | "get_scrollLeft" -> box 0
            | "get_scrollTop" -> box scrollY
            | "addEventListener" ->
                listener <- Some (unbox args[1])
                null
            | "removeEventListener" ->
                listener <- None
                null
            | other -> failwith other)

    let target =
        proxy<HTMLElement> (fun methodInfo _ ->
            match methodInfo.Name with
            | "getBoundingClientRect" -> box (rect 160. (520. - float scrollY) 50. 200.)
            | "get_ownerDocument" -> box document
            | "get_parentElement" -> box container
            | other -> failwith other)

    let computed =
        proxy<ComputedStyleService> (fun methodInfo _ ->
            match methodInfo.Name with
            | "Get" -> box style
            | other -> failwith other)

    let resizeObserver = proxy<ResizeObserver> (fun _ _ -> null)

    let resize =
        proxy<ResizeObservationService> (fun methodInfo _ ->
            match methodInfo.Name with
            | "Create" -> box resizeObserver
            | other -> failwith other)

    let scroll value =
        scrollY <- value
        listener |> Option.iter (fun listener -> listener.handleEvent Unchecked.defaultof<Event>)

    container, target, computed, resize, scroll, (fun () -> listener.IsSome)

let private scrollTests =
    testList "Scroll.observe" [
        testCase "drives the linked timer, reports the observer's progress and ends on Revert" <| fun _ ->
            let container, target, computed, resize, scroll, listening = scrollFixture ()
            use scheduler = new ManualFrameScheduler()
            let services = { Scheduler = scheduler; Computed = computed }

            let interaction =
                { Draggable =
                    { Scheduler = scheduler
                      Document = Unchecked.defaultof<Document>
                      Computed = computed
                      Geometry = Unchecked.defaultof<GeometryService>
                      Resize = resize
                      Media = Unchecked.defaultof<MediaQueryService>
                      Viewport = Unchecked.defaultof<ViewportService>
                      WindowScroll = Unchecked.defaultof<ViewportScrollService> }
                  Viewport = Unchecked.defaultof<ViewportProvider> }

            let host = AnimeHost.Install(services, interaction)
            use timer = new Timer(scheduler, 1000u)
            let updates = ResizeArray<float>()

            let observer =
                Scroll.observe (Some container) (target :> Element) { ScrollOptions.Default with Sync = ScrollSync.Smooth 1. } timer
                    (fun event progress -> if event = ScrollEvent.Update then updates.Add progress)

            Expect.isTrue (listening ()) "the container listens for scrolling"
            scheduler.RunFrame()
            scroll 400
            scheduler.Advance 20.
            scheduler.RunFrame()
            scheduler.RunFrame()
            Expect.isLessThanOrEqual (abs (timer.Progress - 0.5)) 1e-8 "the linked timer is at the midpoint"
            Expect.isNonEmpty updates "updates were reported"
            close 0.5 (Seq.last updates) "with the observer's progress"
            observer.Revert()
            Expect.isFalse (listening ()) "the last observer releases the container"
            Expect.isFalse timer.Cancelled "the linked timer is borrowed"
            host.Dispose()

        testCase "refuses a tickable it cannot link" <| fun _ ->
            let container, target, computed, resize, _, listening = scrollFixture ()
            use scheduler = new ManualFrameScheduler()

            let interaction =
                { Draggable =
                    { Scheduler = scheduler
                      Document = Unchecked.defaultof<Document>
                      Computed = computed
                      Geometry = Unchecked.defaultof<GeometryService>
                      Resize = resize
                      Media = Unchecked.defaultof<MediaQueryService>
                      Viewport = Unchecked.defaultof<ViewportService>
                      WindowScroll = Unchecked.defaultof<ViewportScrollService> }
                  Viewport = Unchecked.defaultof<ViewportProvider> }

            use host = AnimeHost.Install({ Scheduler = scheduler; Computed = computed }, interaction)

            let other =
                { new ITickable with
                    member _.Revert() = ()
                    member _.Completed = false
                    member _.Cancelled = false
                    member _.Pause() = ()
                    member _.Cancel() = ()
                    member _.Complete() = ()
                    member _.WhenCompleted _ = () }

            Expect.throwsT<ArgumentException>
                (fun () -> Scroll.observe (Some container) (target :> Element) ScrollOptions.Default other (fun _ _ -> ()) |> ignore)
                "an unknown tickable"

            Expect.isFalse (listening ()) "and nothing was observed"
    ]

let tests =
    testList "portable surface" [ hostTests; animeTests; followTests; nodeSetTests; flipTests; domKitTests; reactiveTests; scrollTests ]
