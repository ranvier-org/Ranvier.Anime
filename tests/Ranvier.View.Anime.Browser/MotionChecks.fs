/// <summary>The Motion parts mounted under jsdom on a clock advanced by the test, through motion.test.mjs.</summary>
module MotionChecks

open Fable.Core
open Fable.Core.TS.Dom
open global.Ranvier
open Fable.Ranvier.Anime
open Ranvier.Anime
open Ranvier.View
open Ranvier.View.Dom
open Ranvier.View.Dom.Scheduling
open Ranvier.View.Sugar
open Ranvier.View.Anime

/// <summary>Advances the mocked <c>Date</c> and timers by the given milliseconds.</summary>
type Clock = float -> unit

[<Global>]
let private document: Document = jsNative

[<Emit("$0.style.getPropertyValue($1)")>]
let private styleOf (element: obj) (property: string) : string = jsNative

[<Emit("$0.style.getPropertyPriority($1)")>]
let private priorityOf (element: obj) (property: string) : string = jsNative

[<Emit("$0.style.setProperty($1, $2, $3)")>]
let private setStyle (element: obj) (property: string) (value: string) (priority: string) : unit = jsNative

[<Emit("$0.getAttribute($1)")>]
let private attributeOf (element: obj) (name: string) : string = jsNative

[<Emit("$0.querySelector($1)")>]
let private find (root: obj) (selector: string) : HTMLElement = jsNative

[<Emit("Array.from($0.querySelectorAll($1), n => n.textContent)")>]
let private textsOf (root: obj) (selector: string) : string array = jsNative

[<Emit("$0.querySelectorAll($1).length")>]
let private countOf (root: obj) (selector: string) : int = jsNative

[<Emit("$0.namespaceURI")>]
let private namespaceOf (node: obj) : string = jsNative

[<Emit("$0.isConnected")>]
let private connected (node: obj) : bool = jsNative

[<Emit("globalThis.motionLog.splice(0)")>]
let private logged () : string array = jsNative

[<Emit("globalThis.motionDraggable")>]
let private lastDraggable () : Draggable = jsNative

[<Emit("globalThis.motionSets")>]
let private draggableSets () : int = jsNative

[<Emit("globalThis.motionObservers.size")>]
let private liveObservers () : int = jsNative

[<Emit("(e => { let n = 0; for (let c = e._head; c; c = c._next) n++; return n; })(globalThis.motionEngine)")>]
let private engineChildren () : int = jsNative

[<Emit("$0.reverted")>]
let private reverted (observer: ScrollObserver) : bool = jsNative

[<Emit("globalThis.motionObserver")>]
let private lastObserver () : ScrollObserver = jsNative

[<Emit("Object.defineProperty($0, 'progress', { value: $1, configurable: true })")>]
let private fixProgress (observer: ScrollObserver) (progress: float) : unit = jsNative

[<Emit("$0.onUpdate($0)")>]
let private fireUpdate (observer: ScrollObserver) : unit = jsNative

[<Emit("$0.onEnter($0)")>]
let private fireEnter (observer: ScrollObserver) : unit = jsNative

[<Emit("(() => { const r = $0.ownerDocument.createRange(); r.selectNodeContents($0); $0.replaceChildren(r.createContextualFragment($1)); })()")>]
let private loadServed (target: HTMLElement) (html: string) : unit = jsNative

let private equal expected actual =
    if expected <> actual then
        failwithf "Expected %A; got %A" expected actual

let private advance (clock: Clock) (ms: float) =
    clock ms
    Testing.step ()

let private report (errors: exn list) =
    failwithf "Unexpected report %A" [ for error in errors -> error.Message ]

let private host () =
    let target = document.createElement "div"
    document.body.appendChild target |> ignore
    target

let private mountIn (queued: bool) (graph: Graph) (target: HTMLElement) (view: Program.View) =
    let mode =
        if queued then
            QueuedCommits (CollectAndContinue, ignore)
        else
            SynchronousCommits

    let mounted =
        Mounting.mountWith mode graph None report (unbox target) None (fun context -> Program.run view context.Backend)

    mounted.Flush ()
    mounted

let private write (graph: Graph) (mounted: Mounting.Mounted) (action: unit -> unit) =
    graph.Run action
    mounted.Flush ()

/// <summary>Hands anime's engine to the test clock; called once, after <c>animejs</c> is imported.</summary>
let prepare () =
    Testing.manual ()

type Rows = { Items: Signal<int array> }

let private rows (items: int array) =
    { Items = Api.createSignal items }

let private fadeIn (_: unit -> int) (node: Element) =
    Anime.animate node [| PropertySpec.Scalar ("opacity", 1., fromValue = 0.) |] { ComposedAnimationOptions.Default with Duration = 1000.; Ease = Ease.named "linear" }

let private fadeOut (_: unit -> int) (node: Element) =
    Anime.animate node [| PropertySpec.Scalar ("opacity", 0.) |] { ComposedAnimationOptions.Default with Duration = 1000.; Ease = Ease.named "linear" }

let private entering =
    Component.template
        (fun _ (state: Rows) -> state)
        (ul () { forEach id (fun (s: Rows) -> s.Items.Value) (li (Motion.enter fadeIn) { fun (row: unit -> int) -> string (row ()) }) })

let private exiting =
    Component.template
        (fun _ (state: Rows) -> state)
        (ul () { forEach id (fun (s: Rows) -> s.Items.Value) (li (Motion.exit fadeOut) { fun (row: unit -> int) -> string (row ()) }) })

let private both =
    Component.template
        (fun _ (state: Rows) -> state)
        (ul () {
            forEach id (fun (s: Rows) -> s.Items.Value) (li (Motion.enter fadeIn, Motion.exit fadeOut) { fun (row: unit -> int) -> string (row ()) })
        })

let private present =
    Component.template
        (fun _ (state: Rows) -> state)
        (ul () { forEach id (fun (s: Rows) -> s.Items.Value) (li (Motion.presence fadeIn fadeOut) { fun (row: unit -> int) -> string (row ()) }) })

let private rowOf (target: HTMLElement) (index: int) : HTMLElement =
    find target (sprintf "li:nth-of-type(%d)" index)

let private enterValues queued (clock: Clock) =
    use graph = new Graph ()
    let state = graph.Run (fun () -> rows [| 1 |])
    let target = host ()
    let mounted = mountIn queued graph target (Component.render entering state)
    let first = styleOf (rowOf target 1) "opacity"
    write graph mounted (fun () -> state.Items.Value <- [| 1; 2 |])
    let row = rowOf target 2
    advance clock 0.
    let start = styleOf row "opacity"
    advance clock 488.
    let half = styleOf row "opacity"
    advance clock 600.
    let finish = styleOf row "opacity"
    mounted.Dispose ()
    // anime starts a resumed tickable 12 ms early, so each read shows the value 12 ms on.
    equal ("", "0", "0.5", "1") (first, start, half, finish)

let private exitRemovesAtCompletion queued (clock: Clock) =
    use graph = new Graph ()
    let state = graph.Run (fun () -> rows [| 1; 2 |])
    let target = host ()
    let mounted = mountIn queued graph target (Component.render exiting state)
    let row = rowOf target 2
    write graph mounted (fun () -> state.Items.Value <- [| 1 |])
    let held = List.ofArray (textsOf target "li")
    advance clock 0.
    advance clock 987.
    let before = connected row, styleOf row "opacity"
    // The engine skips a tick shorter than its frame, so the clock moves 1 ms at a time until the node leaves; while
    // the node is connected the animation has not rendered its end value.
    let early = ResizeArray ()
    let mutable steps = 0

    while connected row && steps < 50 do
        advance clock 1.
        steps <- steps + 1

        if connected row && styleOf row "opacity" = "0" then
            early.Add steps

    let after = connected row, List.ofArray (textsOf target "li"), List.ofSeq early
    mounted.Dispose ()
    equal ([ "1"; "2" ], (true, "0.001"), (false, [ "1" ], [])) (held, before, after)

let private onRevertsOnDisposal (clock: Clock) =
    use graph = new Graph ()
    let target = host ()

    let view =
        Component.template
            (fun _ () -> ())
            (div (Motion.on (fun _ node -> Fable.Ranvier.Anime.Anime.animate (node, Anim (opacity = 0.5, duration = 1000, ease = Ease.linear)))) { "on" })

    let mounted = mountIn false graph target (Component.render view ())
    let element = find target "div"
    advance clock 0.
    advance clock 488.
    let running = styleOf element "opacity"
    mounted.Dispose ()
    equal ("0.75", "") (running, styleOf element "opacity")

let private typedCssOnReverts queued (clock: Clock) =
    use graph = new Graph ()
    let target = host ()

    let gap: Ranvier.Css.CustomProperty<unit> =
        Ranvier.Css.CustomProperty.create "--gap"

    let px value : Ranvier.Css.Value<unit> =
        Ranvier.Css.Generated.number value "px"

    let view =
        Component.template
            (fun _ () -> ())
            (div (
                Attrs.className "user-class",
                Motion.on (fun _ node -> Fable.Ranvier.Anime.Anime.animate (node, Anim(duration = 1000, ease = Ease.linear).With(gap, px 0., px 100.)))
            ) {
                "typed"
            })

    let mounted = mountIn queued graph target (Component.render view ())
    let element = find target "div"
    advance clock 0.
    advance clock 488.
    equal "50px" (styleOf element "--gap")
    equal "user-class" (attributeOf element "class")
    mounted.Dispose ()
    equal "" (styleOf element "--gap")

let private typedCssFollow queued (clock: Clock) =
    use graph = new Graph ()
    let target = host ()

    let gap: Ranvier.Css.CustomProperty<unit> =
        Ranvier.Css.CustomProperty.create "--gap"

    let px value : Ranvier.Css.Value<unit> =
        Ranvier.Css.Generated.number value "px"

    let state = graph.Run (fun () -> Api.createSignal 0.)

    let existing =
        Hooks.motion (
            System.Func<_, _, _, _>(fun _ node _ ->
                setStyle (box node) "--gap" "calc(1px + 2px)" "important"
                fun () -> ())
        )

    let view =
        Component.template
            (fun _ (value: Signal<float>) -> value)
            (div (
                Attrs.className "user-class",
                existing,
                Motion.followCss gap { ComposedAnimationOptions.Default with Duration = 1000.; Ease = id } (fun (value: Signal<float>) -> px value.Value)
            ) {
                "typed"
            })

    let mounted = mountIn queued graph target (Component.render view state)
    let element = find target "div"
    equal "0px" (styleOf element "--gap")
    write graph mounted (fun () -> state.Value <- 100.)
    advance clock 0.
    advance clock 488.
    equal "50px" (styleOf element "--gap")
    equal "user-class" (attributeOf element "class")
    mounted.Dispose ()
    equal "calc(1px + 2px)" (styleOf element "--gap")
    equal "important" (priorityOf element "--gap")

let private pausedWhenExitFollows (view: Component<Rows>) queued (clock: Clock) =
    use graph = new Graph ()
    let state = graph.Run (fun () -> rows [| 1 |])
    let target = host ()
    let mounted = mountIn queued graph target (Component.render view state)
    write graph mounted (fun () -> state.Items.Value <- [| 1; 2 |])
    let row = rowOf target 2
    advance clock 0.
    advance clock 488.
    let entered = styleOf row "opacity"
    write graph mounted (fun () -> state.Items.Value <- [| 1 |])
    let paused = styleOf row "opacity"
    advance clock 0.
    advance clock 488.
    let leaving = styleOf row "opacity"
    advance clock 600.
    let left = connected row
    mounted.Dispose ()
    equal ("0.5", "0.5", "0.25", false) (entered, paused, leaving, left)

type Toggle = { Shown: Signal<bool> }

let private toggled =
    Component.template
        (fun _ (state: Toggle) -> state)
        (div () {
            show
                (fun (s: Toggle) -> s.Shown.Value)
                (p (
                    Motion.presence
                        (fun _ node -> Fable.Ranvier.Anime.Anime.animate (node, Anim (opacity = Value.fromTo (0, 1), duration = 1000, ease = Ease.linear)))
                        (fun _ node -> Fable.Ranvier.Anime.Anime.animate (node, Anim (opacity = 0, duration = 1000, ease = Ease.linear)))
                ) {
                    "panel"
                })
        })

let private armPresence queued (clock: Clock) =
    use graph = new Graph ()

    let state =
        {
            Shown = graph.Run (fun () -> Api.createSignal false)
        }

    let target = host ()
    let mounted = mountIn queued graph target (Component.render toggled state)
    write graph mounted (fun () -> state.Shown.Value <- true)
    let panel = find target "p"
    advance clock 0.
    advance clock 488.
    let entered = styleOf panel "opacity"
    write graph mounted (fun () -> state.Shown.Value <- false)
    let held = connected panel, styleOf panel "opacity"
    advance clock 0.
    advance clock 488.
    let leaving = styleOf panel "opacity"
    advance clock 600.
    let left = connected panel
    mounted.Dispose ()
    equal ("0.5", (true, "0.5"), "0.25", false) (entered, held, leaving, left)

let private presencePins (clock: Clock) =
    use graph = new Graph ()
    let state = graph.Run (fun () -> rows [| 1; 2 |])
    let target = host ()
    let mounted = mountIn false graph target (Component.render present state)
    let row = rowOf target 2
    let before = styleOf row "position"
    write graph mounted (fun () -> state.Items.Value <- [| 1 |])
    let pinned = styleOf row "position"
    mounted.Dispose ()
    equal ("", "absolute", false) (before, pinned, connected row)

let private disposalEndsExits queued (clock: Clock) =
    use graph = new Graph ()
    let state = graph.Run (fun () -> rows [| 1; 2 |])
    let target = host ()
    let mounted = mountIn queued graph target (Component.render exiting state)
    let row = rowOf target 2
    write graph mounted (fun () -> state.Items.Value <- [| 1 |])
    advance clock 0.
    advance clock 488.
    mounted.Dispose ()
    let gone = connected row
    let value = styleOf row "opacity"
    advance clock 600.
    equal (false, "0.5", "0.5") (gone, value, styleOf row "opacity")

let private reducedPolicy queued (clock: Clock) =
    use graph = new Graph ()
    let state = graph.Run (fun () -> rows [| 1 |])
    graph.Run (fun () -> (Reactive.policy ()).Value <- Reactive.Reduced)
    let target = host ()
    let mounted = mountIn queued graph target (Component.render both state)
    write graph mounted (fun () -> state.Items.Value <- [| 1; 2 |])
    let row = rowOf target 2
    advance clock 0.
    advance clock 100.
    let entered = styleOf row "opacity"
    write graph mounted (fun () -> state.Items.Value <- [| 1 |])
    let left = connected row
    mounted.Dispose ()
    equal ("", false) (entered, left)

let private flipped =
    Component.template
        (fun _ (state: Rows) -> state)
        (ul (Motion.flip (LayoutOptions.Default.WithDuration 300u)) {
            forEach id (fun (s: Rows) -> s.Items.Value) (li (Motion.exit fadeOut) { fun (row: unit -> int) -> string (row ()) })
        })

let private flipOrder queued (clock: Clock) =
    logged () |> ignore
    use graph = new Graph ()
    let state = graph.Run (fun () -> rows [| 1; 2; 3 |])
    let target = host ()
    let mounted = mountIn queued graph target (Component.render flipped state)
    let mounting = logged ()
    write graph mounted (fun () -> state.Items.Value <- [| 3; 1; 2 |])
    let created = logged ()
    write graph mounted (fun () -> state.Items.Value <- [| 3; 1 |])
    let removed = logged ()
    let held = List.ofArray (textsOf target "li")
    graph.Run (fun () -> (Reactive.policy ()).Value <- Reactive.Reduced)
    write graph mounted (fun () -> state.Items.Value <- [| 1; 3 |])
    let reduced = logged ()
    mounted.Dispose ()

    equal
        ([||], [| "animate 3 1 2 fade" |], [| "record 3 1 2 fade"; "animate 3 1" |], [ "3"; "1"; "2" ], [||])
        (mounting, created, removed, held, reduced)

type Level = { Level: Signal<float> }

let private followReaches (clock: Clock) =
    use graph = new Graph ()
    let target = host ()

    let state =
        {
            Level = graph.Run (fun () -> Api.createSignal 0.2)
        }

    let view =
        Component.template
            (fun _ (s: Level) -> s)
            (div (Motion.follow "opacity" { ComposedAnimationOptions.Default with Duration = 1000.; Ease = id } (fun (s: Level) -> s.Level.Value)) { "level" })

    let mounted = mountIn false graph target (Component.render view state)
    let element = find target "div"
    let first = styleOf element "opacity"
    write graph mounted (fun () -> state.Level.Value <- 1.)
    advance clock 0.
    advance clock 488.
    let half = styleOf element "opacity"
    advance clock 600.
    let reached = styleOf element "opacity"
    mounted.Dispose ()
    equal ("0.2", "0.6", "1", "") (first, half, reached, styleOf element "opacity")

let private replayed =
    Component.template
        (fun _ (s: Level) -> s)
        (div (
            Motion.animateOn (fun (s: Level) -> s.Level.Value) (fun _ node level ->
                Fable.Ranvier.Anime.Anime.animate (node, Anim (opacity = level, duration = 1000, ease = Ease.linear)))
        ) {
            "level"
        })

let private animateOnReplaces (clock: Clock) =
    use graph = new Graph ()
    let target = host ()

    let state =
        {
            Level = graph.Run (fun () -> Api.createSignal 0.2)
        }

    let mounted = mountIn false graph target (Component.render replayed state)
    let element = find target "div"
    let first = styleOf element "opacity"
    write graph mounted (fun () -> state.Level.Value <- 1.)
    advance clock 0.
    advance clock 488.
    let half = styleOf element "opacity"
    write graph mounted (fun () -> state.Level.Value <- 0.4)
    advance clock 0.
    advance clock 488.
    let replaced = styleOf element "opacity"
    advance clock 600.
    let reached = styleOf element "opacity"
    advance clock 1000.
    let settled = styleOf element "opacity"
    mounted.Dispose ()
    equal ("0.2", "0.6", "0.5", "0.4", "0.4") (first, half, replaced, reached, settled)

let private stroked =
    Component.template
        (fun _ (s: Level) -> s)
        (svg () { path (Motion.draw { ComposedAnimationOptions.Default with Duration = 1000.; Ease = id } (fun (s: Level) -> s.Level.Value)) { () } })

let private drawReaches queued (clock: Clock) =
    use graph = new Graph ()
    let target = host ()

    let state =
        {
            Level = graph.Run (fun () -> Api.createSignal 0.25)
        }

    let mounted = mountIn queued graph target (Component.render stroked state)
    let path = find target "path"
    let first = attributeOf path "stroke-dasharray", namespaceOf path
    write graph mounted (fun () -> state.Level.Value <- 0.75)
    advance clock 0.
    advance clock 488.
    let half = attributeOf path "stroke-dasharray"
    advance clock 600.
    let reached = attributeOf path "stroke-dasharray"
    mounted.Dispose ()
    equal (("250 760", "http://www.w3.org/2000/svg"), "500 510", "750 260") (first, half, reached)

type Outline = { Shape: Signal<string> }

let private morphed =
    Component.template
        (fun _ (s: Outline) -> s)
        (svg () { path (Motion.morph { ComposedAnimationOptions.Default with Duration = 1000.; Ease = id } (fun (s: Outline) -> s.Shape.Value)) { () } })

let private morphReaches queued (clock: Clock) =
    use graph = new Graph ()
    let target = host ()

    let state =
        {
            Shape = graph.Run (fun () -> Api.createSignal "M0 0 L10 0")
        }

    let mounted = mountIn queued graph target (Component.render morphed state)
    let path = find target "path"
    let first = attributeOf path "d", namespaceOf path
    write graph mounted (fun () -> state.Shape.Value <- "M0 10 L10 10")
    advance clock 0.
    advance clock 488.
    let half = attributeOf path "d"
    advance clock 600.
    let reached = attributeOf path "d"
    mounted.Dispose ()
    equal (("M0 0 L10 0", "http://www.w3.org/2000/svg"), true, "M0 10 L10 10") (first, half <> fst first && half <> reached, reached)

type Strokes = { Fractions: Signal<float array> }

let private strokes =
    Component.template
        (fun _ (s: Strokes) -> s)
        (svg () {
            forEach
                id
                (fun (s: Strokes) -> s.Fractions.Value)
                (path (Motion.draw { ComposedAnimationOptions.Default with Duration = 1000.; Ease = id } (fun (current: unit -> float) -> current ())) { () })
        })

[<Emit("Array.from($0.querySelectorAll($1), n => n.namespaceURI + ' ' + n.getAttribute('stroke-dasharray'))")>]
let private strokesOf (root: obj) (selector: string) : string array = jsNative

let private svgRows queued (clock: Clock) =
    use graph = new Graph ()
    let target = host ()

    let state =
        {
            Fractions = graph.Run (fun () -> Api.createSignal [| 0.25 |])
        }

    let mounted = mountIn queued graph target (Component.render strokes state)
    write graph mounted (fun () -> state.Fractions.Value <- [| 0.25; 0.5 |])
    let rows = List.ofArray (strokesOf target "svg > *")
    mounted.Dispose ()
    let svg = "http://www.w3.org/2000/svg"
    equal [ svg + " 250 760"; svg + " 500 510" ] rows

type Curved =
    {
        Drawn: Signal<bool>
        Curve: Signal<string>
    }

let private curvedArm =
    Component.template
        (fun _ (s: Curved) -> s)
        (svg () {
            show
                (fun (s: Curved) -> s.Drawn.Value)
                (path (Motion.morph { ComposedAnimationOptions.Default with Duration = 1000.; Ease = id } (fun (s: Curved) -> s.Curve.Value)) { () })
        })

let private svgArm queued (clock: Clock) =
    use graph = new Graph ()
    let target = host ()

    let state =
        graph.Run (fun () ->
            {
                Drawn = Api.createSignal false
                Curve = Api.createSignal "M0 0 L10 0"
            })

    let mounted = mountIn queued graph target (Component.render curvedArm state)
    write graph mounted (fun () -> state.Drawn.Value <- true)
    let arm = find target "path"
    let first = namespaceOf arm, attributeOf arm "d"
    write graph mounted (fun () -> state.Curve.Value <- "M0 10 L10 10")
    advance clock 0.
    advance clock 488.
    let half = attributeOf arm "d"
    advance clock 600.
    let reached = attributeOf arm "d"
    mounted.Dispose ()
    equal (("http://www.w3.org/2000/svg", "M0 0 L10 0"), true, "M0 10 L10 10") (first, half <> snd first && half <> reached, reached)

type Puck =
    {
        Position: Signal<struct (float * float)>
        Dragging: Signal<bool>
    }

let private dragged =
    Component.template
        (fun _ (p: Puck) -> p)
        (div (
            Motion.drag { NativeDraggableOptions.Default with Callbacks = { DraggableCallbacks.Default with OnUpdate = fun _ -> () } } (fun (p: Puck) ->
                {
                    Position = p.Position
                    Dragging = ValueSome p.Dragging
                    Slot = ValueNone
                })
        ) {
            "puck"
        })

let private dragEchoGuard (clock: Clock) =
    use graph = new Graph ()
    let target = host ()

    let puck =
        graph.Run (fun () ->
            {
                Position = Api.createSignal (struct (0., 0.))
                Dragging = Api.createSignal false
            })

    let mounted = mountIn false graph target (Component.render dragged puck)
    let draggable = lastDraggable ()
    let initial = draggableSets ()
    write graph mounted (fun () -> puck.Position.Value <- struct (100., 50.))
    advance clock 0.
    advance clock 16.

    let moved =
        draggableSets () - initial, struct (draggable.x, draggable.y), graph.Run (fun () -> puck.Position.Value)

    draggable.x <- 30.
    advance clock 16.

    let afterDrag =
        draggableSets () - initial, graph.Run (fun () -> puck.Position.Value)

    mounted.Dispose ()
    equal ((2, struct (100., 50.), struct (100., 50.)), (3, struct (30., 50.)), false) (moved, afterDrag, draggable.enabled)

type Scrolled =
    {
        Progress: Signal<float>
        InView: Signal<bool>
    }

let private scrolled =
    Component.template
        (fun _ (s: Scrolled) -> s)
        (section (
            Motion.scroll
                ScrollParams.Default
                (fun (s: Scrolled) ->
                    {
                        Progress = ValueSome s.Progress
                        InView = ValueSome s.InView
                    })
                (fun _ node -> Fable.Ranvier.Anime.Anime.animate (node, Anim (opacity = Value.fromTo (0, 1), duration = 1000, ease = Ease.linear)))
        ) {
            "story"
        })

let private scrollWrites (clock: Clock) =
    use graph = new Graph ()
    let target = host ()

    let state =
        graph.Run (fun () ->
            {
                Progress = Api.createSignal 0.
                InView = Api.createSignal false
            })

    let mounted = mountIn false graph target (Component.render scrolled state)
    let observer = lastObserver ()
    fixProgress observer 0.25
    fireUpdate observer
    fireEnter observer
    let written = graph.Run (fun () -> state.Progress.Value, state.InView.Value)
    mounted.Dispose ()
    equal ((0.25, true), true) (written, reverted observer)

let private splitted =
    Component.template
        (fun _ () -> ())
        (h1 (
            Motion.split { TextSplitterOptions.Default with Chars = SplitMode.Enabled } (fun _ splitter ->
                Anime.animateTargets (Array.map box splitter.Chars) [| PropertySpec.Scalar ("opacity", 1., fromValue = 0.) |] { ComposedAnimationOptions.Default with Duration = 100. })
        ) {
            "Hi"
        })

let private splitReverts (clock: Clock) =
    use graph = new Graph ()
    let target = host ()
    let observing = liveObservers ()
    let mounted = mountIn false graph target (Component.render splitted ())
    let heading = find target "h1"
    let parts = countOf heading "*"
    mounted.Dispose ()
    equal (true, "Hi", observing) (parts >= 2, heading.innerHTML, liveObservers ())

let private appeared =
    Component.template
        (fun _ () -> ())
        (p (Motion.appear (fun _ node -> Fable.Ranvier.Anime.Anime.animate (node, Anim (opacity = Value.fromTo (0, 1), duration = 1000)))) { "served" })

let private appearSkipsHydrated (clock: Clock) =
    use graph = new Graph ()
    let served = host ()
    loadServed served (Server.render Server.Options.defaults appeared ())

    let hydrated =
        Hydration.hydrate graph None report { IdPrefix = "r"; Strict = true } (unbox served) appeared ()

    advance clock 0.
    let adopted = styleOf (find served "p") "opacity"
    hydrated.Dispose ()
    let fresh = host ()
    let mounted = mountIn false graph fresh (Component.render appeared ())
    advance clock 0.
    let created = styleOf (find fresh "p") "opacity"
    mounted.Dispose ()
    equal ("", "0") (adopted, created)

type Lane = { Lanes: Signal<int array> }

let private lanes (items: int array) =
    { Lanes = Api.createSignal items }

let private livelyRows =
    Component.template
        (fun _ (s: Lane) -> s)
        (ul () {
            forEach
                id
                (fun (s: Lane) -> s.Lanes.Value)
                (li (
                    Motion.exit fadeOut,
                    Motion.drag NativeDraggableOptions.Default (fun (_: unit -> int) ->
                        {
                            Position = Api.createSignal (struct (0., 0.))
                            Dragging = ValueNone
                            Slot = ValueNone
                        })
                ) {
                    span (
                        Motion.split { TextSplitterOptions.Default with Chars = SplitMode.Enabled } (fun _ split ->
                            Anime.animateTargets (Array.map box split.Chars) [| PropertySpec.Scalar ("opacity", 1., fromValue = 0.) |] { ComposedAnimationOptions.Default with Duration = 100. })
                    ) {
                        "ab"
                    }

                    b (Motion.follow "opacity" { ComposedAnimationOptions.Default with Duration = 100. } (fun (_: unit -> int) -> 0.5)) { "f" }
                })
        })

let private nothingLiveAfterExit queued (clock: Clock) =
    use graph = new Graph ()
    let observing = liveObservers ()
    let state = graph.Run (fun () -> lanes [| 1; 2 |])
    let target = host ()
    let mounted = mountIn queued graph target (Component.render livelyRows state)
    let draggable = lastDraggable ()
    let perRow = (liveObservers () - observing) / 2
    advance clock 0.
    advance clock 300.
    write graph mounted (fun () -> state.Lanes.Value <- [| 1 |])
    advance clock 0.
    advance clock 1100.

    for _ in 1..50 do
        advance clock 1.

    let afterExit =
        countOf target "li", draggable.enabled, liveObservers () - observing, engineChildren ()

    mounted.Dispose ()
    equal ((1, false, perRow, 0), observing) (afterExit, liveObservers ())

type Panel =
    {
        Rows: Signal<int array>
        Open: Signal<bool>
    }

let private guardedRows =
    Component.template
        (fun _ (s: Panel) -> s)
        (div () {
            ul () {
                forEach
                    id
                    (fun (s: Panel) -> s.Rows.Value)
                    (li (Motion.presence fadeIn fadeOut) {
                        b (Motion.follow "opacity" { ComposedAnimationOptions.Default with Duration = 1000. } (fun (_: unit -> int) -> 0.3)) { "f" }
                        i (Motion.on (fun _ el -> Fable.Ranvier.Anime.Anime.animate (el, Anim (opacity = 0.5, duration = 1000, ease = Ease.linear)))) { "o" }

                        em (
                            Motion.animateOn (fun (_: unit -> int) -> 0.7) (fun _ el value ->
                                Fable.Ranvier.Anime.Anime.animate (el, Anim (opacity = value, duration = 100)))
                        ) {
                            "a"
                        }
                    })
            }

            show
                (fun (s: Panel) -> s.Open.Value)
                (section (Motion.fade) { b (Motion.follow "opacity" { ComposedAnimationOptions.Default with Duration = 1000. } (fun (_: Panel) -> 0.3)) { "g" } })
        })

let private descendantsPause queued (clock: Clock) =
    use graph = new Graph ()

    let state =
        graph.Run (fun () ->
            {
                Rows = Api.createSignal [| 1; 2 |]
                Open = Api.createSignal true
            })

    let target = host ()
    let mounted = mountIn queued graph target (Component.render guardedRows state)
    let row = rowOf target 2
    let gauge = find target "section b"
    advance clock 0.
    advance clock 488.
    write graph mounted (fun () -> state.Rows.Value <- [| 1 |])
    write graph mounted (fun () -> state.Open.Value <- false)

    let kept =
        styleOf (find row "b") "opacity", styleOf (find row "i") "opacity", styleOf (find row "em") "opacity", styleOf gauge "opacity"

    let held = connected row, connected gauge
    mounted.Dispose ()
    equal (("0.3", "0.75", "0.7", "0.3"), (true, true)) (kept, held)

/// <summary>The entries the <c>Hooks.both</c> fixtures wrote, in order.</summary>
let private bothLog = ResizeArray<string>()

let private logged' () =
    let entries = List.ofSeq bothLog
    bothLog.Clear ()
    entries

let private named (name: string) : Part<'native, 'inst> =
    Hooks.motion (
        System.Func<_, _, _, _>(fun _ _ _ ->
            bothLog.Add (name + " run")
            fun () -> bothLog.Add (name + " cleanup"))
    )

let private mountBoth (part: Part<Ranvier.Dom.HTMLDivElement, unit>) =
    use graph = new Graph ()
    let target = host ()
    let view = Component.template (fun _ () -> ()) (div part { "x" })

    let failure =
        try
            let mounted = mountIn false graph target (Component.render view ())

            try
                mounted.Dispose ()
                ""
            with error ->
                error.Message
        with error ->
            error.Message

    failure, logged' ()

let private bothRules (clock: Clock) =
    bothLog.Clear ()
    let ordered = mountBoth (Hooks.both (named "first", named "second"))

    let secondRaises =
        mountBoth (Hooks.both (named "first", Hooks.motion (System.Func<_, _, _, _>(fun _ _ _ -> failwith "second failed"))))

    let cleanupRaises =
        mountBoth (Hooks.both (named "first", Hooks.motion (System.Func<_, _, _, _>(fun _ _ _ -> fun () -> failwith "cleanup failed"))))

    let rejected =
        try
            Hooks.both (Hooks.mounted (fun _ (_: HTMLDivElement) -> ignore), Hooks.lend (fun _ (_: HTMLDivElement) -> ignore))
            |> ignore

            "accepted"
        with error when error.Message.Contains "Hooks.both combines" ->
            "rejected"

    equal
        (("", [ "first run"; "second run"; "second cleanup"; "first cleanup" ]),
         [ "first run"; "first cleanup" ],
         [ "first run"; "first cleanup" ],
         "rejected")
        (ordered, snd secondRaises, snd cleanupRaises, rejected)

    if not ((fst secondRaises).Contains "second failed") then
        failwithf "Expected the hook failure; got %s" (fst secondRaises)

type Plot = { Ticks: Signal<int array> }

let private plotted =
    Component.template
        (fun _ (s: Plot) -> s)
        (div () {
            svg (Attrs.attribute ("viewBox", "0 0 10 10"), Attrs.className "chart") {
                g (Attrs.className "axis", Events.onClick (fun (_: Plot) _ -> ())) {
                    forEach id (fun (s: Plot) -> s.Ticks.Value) (text' () { fun (tick: unit -> int) -> string (tick ()) })
                }

                forEach id (fun (s: Plot) -> s.Ticks.Value) (fragment [| text (fun (tick: unit -> int) -> "t" + string (tick ())); path () { () } |])

                foreignObject () { p () { "html" } }
            }

            ol () { forEach id (fun (s: Plot) -> s.Ticks.Value) (g () { () }) }
        })

[<Emit("Array.from($0.querySelectorAll($1), n => n.namespaceURI === 'http://www.w3.org/2000/svg' ? 'svg' : 'html')")>]
let private namespacesOf (root: obj) (selector: string) : string array = jsNative

let private svgAuthoring queued (clock: Clock) =
    use graph = new Graph ()
    let target = host ()

    let state =
        {
            Ticks = graph.Run (fun () -> Api.createSignal [| 1 |])
        }

    let mounted = mountIn queued graph target (Component.render plotted state)
    write graph mounted (fun () -> state.Ticks.Value <- [| 1; 2 |])
    let chart = find target "svg"

    let read =
        attributeOf chart "viewBox",
        attributeOf (find chart "g") "class",
        List.ofArray (namespacesOf chart "text, path, foreignObject"),
        List.ofArray (namespacesOf chart "foreignObject p"),
        List.ofArray (namespacesOf target "ol > g")

    mounted.Dispose ()

    equal ("0 0 10 10", "axis", [ "svg"; "svg"; "svg"; "svg"; "svg" ], [ "html" ], [ "svg"; "svg" ]) read

let private mixedRoots =
    Component.template (fun _ (s: Plot) -> s) (svg () { forEach id (fun (s: Plot) -> s.Ticks.Value) (fragment [| g () { () }; p () { "p" } |]) })

let private mixedRootsRaise (clock: Clock) =
    use graph = new Graph ()
    let target = host ()

    let state =
        {
            Ticks = graph.Run (fun () -> Api.createSignal [| 1 |])
        }

    let failure =
        try
            (mountIn false graph target (Component.render mixedRoots state)).Dispose()
            ""
        with error ->
            error.Message

    if not (failure.Contains "mixes SVG and HTML root elements") then
        failwithf "Expected the mixed-root failure; got %s" failure

let private svgCleanupRestores (clock: Clock) =
    use graph = new Graph ()
    let target = host ()

    let drawn =
        {
            Level = graph.Run (fun () -> Api.createSignal 0.25)
        }

    let mounted = mountIn false graph target (Component.render stroked drawn)
    let stroke = find target "path"
    let drawing = attributeOf stroke "pathLength"
    mounted.Dispose ()

    let morphedState =
        {
            Shape = graph.Run (fun () -> Api.createSignal "M0 0 L10 0")
        }

    let shapes = host ()
    let morphing = mountIn false graph shapes (Component.render morphed morphedState)
    let shape = find shapes "path"
    let first = attributeOf shape "d"
    morphing.Dispose ()

    equal
        (("1000", true, true), ("M0 0 L10 0", true))
        ((drawing, isNull (attributeOf stroke "pathLength"), isNull (attributeOf stroke "stroke-dasharray")), (first, isNull (attributeOf shape "d")))

let private enteringUnderFlip (enter: Part<Ranvier.Dom.HTMLLIElement, unit -> int>) =
    Component.template
        (fun _ (state: Rows) -> state)
        (ul (Motion.flip { LayoutOptions.Default.WithDuration 300u with Ease = id }) {
            forEach id (fun (s: Rows) -> s.Items.Value) (li enter { fun (row: unit -> int) -> string (row ()) })
        })

let private opacityEnter (_: unit -> int) (node: Element) =
    Fable.Ranvier.Anime.Anime.animate (node, Anim (opacity = Value.fromTo (0, 1), duration = 100, ease = Ease.linear))

let private flippedEnter = enteringUnderFlip (Motion.enter opacityEnter)

let private flippedPresence =
    enteringUnderFlip (Motion.presence opacityEnter fadeOut)

let private flipKeepsEnters queued (clock: Clock) =
    let ends (view: Component<Rows>) =
        use graph = new Graph ()
        let state = graph.Run (fun () -> rows [| 1 |])
        let target = host ()
        let mounted = mountIn queued graph target (Component.render view state)
        write graph mounted (fun () -> state.Items.Value <- [| 1; 2 |])
        let row = rowOf target 2

        for _ in 1..10 do
            advance clock 16.

        let entered = styleOf row "opacity"

        for _ in 1..15 do
            advance clock 16.

        let settled = styleOf row "opacity"
        mounted.Dispose ()
        entered, settled

    equal (("1", "1"), ("1", "1")) (ends flippedEnter, ends flippedPresence)

[<Emit("$0.animate[$0.xProp]($1, $2)")>]
let private releaseTo (draggable: Draggable) (x: float) (duration: float) : unit = jsNative

[<Emit("$0.onGrab($0)")>]
let private fireGrab (draggable: Draggable) : unit = jsNative

[<Emit("$0.onSettle($0)")>]
let private fireSettle (draggable: Draggable) : unit = jsNative

let private dragWritesSettle (clock: Clock) =
    use graph = new Graph ()
    let target = host ()

    let puck =
        graph.Run (fun () ->
            {
                Position = Api.createSignal (struct (0., 0.))
                Dragging = Api.createSignal false
            })

    let mounted = mountIn false graph target (Component.render dragged puck)
    let draggable = lastDraggable ()
    // anime reports the settle while the release animation still runs, then moves the element with no callback.
    fireGrab draggable
    releaseTo draggable 42. 100.
    advance clock 0.
    advance clock 48.
    fireSettle draggable
    let early = draggable.x
    let sets = draggableSets ()

    for _ in 1..10 do
        advance clock 16.

    let settled =
        graph.Run (fun () -> puck.Position.Value), draggable.x, draggableSets () - sets

    mounted.Dispose ()
    equal (true, (struct (42., 0.), 42., 0)) (early < 42., settled)

[<Emit("(l => [l.properties, l.recordedProperties].every(s => s instanceof Set && s.has('opacity') && s.has('color') && s.has('filter')))($0)")>]
let private layoutShape (layout: AutoLayout) : bool = jsNative

let private layoutFieldsPinned (clock: Clock) =
    let root = host ()

    root.appendChild (document.createElement "p")
    |> ignore

    let layout = Fable.Ranvier.Anime.Anime.layout root
    layout.animate () |> ignore
    let shaped = layoutShape layout
    layout.revert ()
    equal true shaped

let private failureOf queued (build: unit -> Component<unit>) =
    try
        let view = build ()
        use graph = new Graph ()
        (mountIn queued graph (host ()) (Component.render view ())).Dispose()
        ""
    with error ->
        error.Message

let private mixedCaseHandlerRaises queued (clock: Clock) =
    let failures =
        [
            failureOf queued (fun () -> Component.template (fun _ () -> ()) (div (Attrs.attribute ("oNclick", "alert(1)")) { "x" }))
            failureOf queued (fun () -> Component.template (fun _ () -> ()) (div (Attrs.attribute ("refX refY", "1")) { "x" }))
            failureOf queued (fun () -> Component.define (fun _ () -> ()) (fun t -> t.element (HtmlTag "feBlend feColorMatrix") [||] [||]))
        ]

    equal [ true; true; true ] [ for failure in failures -> failure.Contains "names must" ]

/// <summary>Each element templates reject on every path, with the phrase its failure holds.</summary>
let private refused: (string * (TemplateBuilder<unit> -> TemplateView<unit>)) list =
    [
        "Script elements", (fun t -> t.div [||] [| t.element (HtmlTag "script") [||] [| t.text "globalThis.pwned = 1" |] |])
        "Script elements",
        (fun t ->
            t.div
                [||]
                [|
                    t.element (HtmlTag "script") [| t.Attrs.attribute ("src", "data:text/javascript,window.pwnedSrc=1") |] [||]
                |])
        "Script elements",
        (fun t ->
            t.element
                (HtmlTag "svg")
                [||]
                [|
                    t.element (HtmlTag "script") [| t.Attrs.attribute ("href", "data:text/javascript,window.pwnedSrc=2") |] [||]
                |])
        "Script elements", (fun t -> t.div [||] [| t.script [||] [||] |])
        "Raw-text", (fun t -> t.element (HtmlTag "style") [||] [| t.text "p {}" |])
        "Raw-text", (fun t -> t.element (HtmlTag "iframe") [||] [| t.text "<p>" |])
        "Template elements", (fun t -> t.element (HtmlTag "template") [||] [| t.span [||] [||] |])
        "void element", (fun t -> t.element (HtmlTag "br") [||] [| t.span [||] [||] |])
    ]

[<Emit("(globalThis.pwned ?? globalThis.window.pwned ?? globalThis.window.pwnedSrc)")>]
let private pwned () : obj = jsNative

/// <summary>Elements the server rejects and the client mounts as built: RCDATA with element children and case twins.</summary>
let private clientAccepted: (TemplateBuilder<unit> -> TemplateView<unit>) list =
    [
        (fun t -> t.element (HtmlTag "textarea") [||] [| t.span [||] [| t.text "globalThis.pwned = 1" |] |])
        (fun t -> t.element (HtmlTag "title") [||] [| t.span [||] [||] |])
        (fun t ->
            t.element
                (HtmlTag "svg")
                [|
                    t.Attrs.attribute ("viewBox", "0 0 1 1")
                    t.Attrs.attribute ("viewbox", "0 0 2 2")
                |]
                [||])
    ]

let private clientMountsServerChecks queued (clock: Clock) =
    let failures =
        [
            for view in clientAccepted -> failureOf queued (fun () -> Component.define (fun _ () -> ()) view)
        ]

    equal (List.replicate clientAccepted.Length "", true) (failures, isNull (pwned ()))

let private refusedElements queued (clock: Clock) =
    let failures =
        [
            for phrase, view in refused ->
                let failure = failureOf queued (fun () -> Component.define (fun _ () -> ()) view)
                failure.ToLowerInvariant().Contains(phrase.ToLowerInvariant ())
        ]

    equal (List.replicate refused.Length true, true) (failures, isNull (pwned ()))

type Captions = { Captions: Signal<string array> }

let private captioned =
    Component.template
        (fun _ (s: Captions) -> s)
        (svg () { forEach id (fun (s: Captions) -> s.Captions.Value) (title' () { fun (caption: unit -> string) -> caption () }) })

let private titleRows queued (clock: Clock) =
    use graph = new Graph ()
    let target = host ()

    let state =
        {
            Captions = graph.Run (fun () -> Api.createSignal [| "a" |])
        }

    let mounted = mountIn queued graph target (Component.render captioned state)
    let spaces = List.ofArray (namespacesOf target "svg > title")
    mounted.Dispose ()
    // A title row is an SVG element when created under its svg parent, and an HTML title when parsed on its own.
    equal (if queued then [ "svg" ] else [ "html" ]) spaces

let private failingExit =
    Component.template
        (fun _ (state: Rows) -> state)
        (ul () {
            forEach
                id
                (fun (s: Rows) -> s.Items.Value)
                (li (Motion.exit (fun _ _ -> failwith "play failed")) { fun (row: unit -> int) -> string (row ()) })
        })

let private failingExitUnmarks queued (clock: Clock) =
    use graph = new Graph ()
    let marked = Motion.departures ()
    let state = graph.Run (fun () -> rows [| 1; 2 |])
    let target = host ()
    let mounted = mountIn queued graph target (Component.render failingExit state)

    // The seam finishes the failed exit and reports the failure with the pass.
    try
        write graph mounted (fun () -> state.Items.Value <- [| 1 |])
    with _ ->
        ()

    let after = Motion.departures () - marked, List.ofArray (textsOf target "li")
    mounted.Dispose ()
    equal (0, [ "1" ]) after

type Thrown =
    {
        At: Signal<struct (float * float)>
        Spring: MotionSlot
    }

[<Emit("({ x: 0 })")>]
let private tweened () : obj = jsNative

[<Emit("$0.x")>]
let private xOf (target: obj) : float = jsNative

let private thrown =
    Component.template
        (fun _ (publish: Thrown -> unit) ->
            let created =
                {
                    At = Api.createSignal (struct (0., 0.))
                    Spring = Motion.slot ()
                }

            publish created
            created)
        (div (
            Motion.drag NativeDraggableOptions.Default (fun (t: Thrown) ->
                {
                    Position = t.At
                    Dragging = ValueNone
                    Slot = ValueSome t.Spring
                })
        ) {
            "puck"
        })

let private slotOwnsAnimations (clock: Clock) =
    use graph = new Graph ()
    let instance: Thrown ref = ref Unchecked.defaultof<Thrown>

    let mounted =
        mountIn false graph (host ()) (Component.render thrown (fun t -> instance.Value <- t))

    let spring = instance.Value.Spring
    let draggable = lastDraggable ()
    let starts = ref 0

    let play (target: obj) =
        spring.Play (fun () ->
            starts.Value <- starts.Value + 1
            Fable.Ranvier.Anime.Anime.animate (Targets.object target, Anim (x = 100, duration = 1000, ease = Ease.linear)))

    let first = tweened ()
    play first
    advance clock 0.
    advance clock 300.
    let second = tweened ()
    play second
    let replaced = xOf first
    advance clock 300.
    let firstHeld = xOf first = replaced && xOf second > 0.
    fireGrab draggable
    let grabbed = xOf second, spring.Playing
    advance clock 300.
    let secondHeld = xOf second = fst grabbed
    let third = tweened ()
    play third
    let playing = spring.Playing
    advance clock 100.
    mounted.Dispose ()
    let disposed = xOf third
    advance clock 300.
    play (tweened ())

    equal
        ((true, false, true), (true, true, false), 3)
        ((replaced > 0. && firstHeld, snd grabbed, secondHeld), (playing, xOf third = disposed, spring.Playing), starts.Value)

[<Emit("$0.motionRect = { left: $1, top: $2, width: 300, height: 90 }")>]
let private placeAt (element: obj) (left: float) (top: float) : unit = jsNative

[<Emit("delete $0.motionRect")>]
let private unplace (element: obj) : unit = jsNative

[<Emit("$0.style.translate")>]
let private translateOf (element: obj) : string = jsNative

[<Emit("$0.style.width")>]
let private widthOf (element: obj) : string = jsNative

[<Emit("$0.motionFrom === undefined")>]
let private unhinted (element: obj) : bool = jsNative

// Under a `data-rects` parent, motion.test.mjs places the n-th child at x = 200 n.
let private laned =
    Component.template
        (fun _ (state: Rows) -> state)
        (ul (Attrs.data ("rects", ""), Motion.flip { LayoutOptions.Default.WithDuration 300u with Ease = id }) {
            forEach id (fun (s: Rows) -> s.Items.Value) (li () { fun (row: unit -> int) -> string (row ()) })
        })

let private flipStartsFromDrop queued (clock: Clock) =
    use graph = new Graph ()
    let state = graph.Run (fun () -> rows [| 1; 2; 3 |])
    let target = host ()
    let mounted = mountIn queued graph target (Component.render laned state)

    let rowNamed (name: int) =
        find
            target
            (sprintf
                "li:nth-of-type(%d)"
                (Array.findIndex ((=) name) (graph.Run (fun () -> Api.untrack (fun () -> state.Items.Value)))
                 + 1))

    let drop (name: int) (left: float) (top: float) (order: int array) =
        let row = rowNamed name
        placeAt row left top
        Motion.flipFrom row
        unplace row
        write graph mounted (fun () -> state.Items.Value <- order)
        row

    let dropped = drop 2 380. 12. [| 1; 3; 2 |]

    let first =
        translateOf dropped, translateOf (rowNamed 3), unhinted dropped, widthOf dropped

    for _ in 1..30 do
        advance clock 16.

    let again = drop 1 150. 30. [| 3; 1; 2 |]
    let second = translateOf again, translateOf (rowNamed 3), unhinted again
    mounted.Dispose ()
    equal (("380px 12px", "400px 0px", true, "100px"), ("150px 30px", "200px 0px", true)) (first, second)

let private flipHintUnderReduced queued (clock: Clock) =
    use graph = new Graph ()
    let state = graph.Run (fun () -> rows [| 1; 2; 3 |])
    let target = host ()
    let mounted = mountIn queued graph target (Component.render laned state)
    let row = rowOf target 2
    graph.Run (fun () -> (Reactive.policy ()).Value <- Reactive.Reduced)
    placeAt row 380. 12.
    graph.Run (fun () -> Motion.flipFrom row)
    unplace row
    write graph mounted (fun () -> state.Items.Value <- [| 1; 3; 2 |])
    graph.Run (fun () -> (Reactive.policy ()).Value <- Reactive.Full)
    write graph mounted (fun () -> state.Items.Value <- [| 2; 1; 3 |])
    let start = translateOf row, unhinted row
    mounted.Dispose ()
    equal ("400px 0px", true) start

type Offset = { Offset: Signal<float> }

let private followUnit (clock: Clock) =
    use graph = new Graph ()
    let target = host ()

    let state =
        {
            Offset = graph.Run (fun () -> Api.createSignal 50.)
        }

    let view =
        Component.template
            (fun _ (s: Offset) -> s)
            (div () {
                span (Motion.follow "x" { ComposedAnimationOptions.Default with Unit = Some "%"; Duration = 100.; Ease = id } (fun (s: Offset) -> s.Offset.Value)) {
                    "shared"
                }

                b (Motion.follow "x" { ComposedAnimationOptions.Default with Unit = Some "%"; Extra = AnimatableParams (x = AnimatableProp (unit = "px")) } (fun (s: Offset) -> s.Offset.Value)) { "own" }

                i (Motion.follow "x" { ComposedAnimationOptions.Default with Unit = Some "%"; Ease = id; Extra = AnimatableParams (x = 400) } (fun (s: Offset) -> s.Offset.Value)) { "timed" }
            })

    let mounted = mountIn false graph target (Component.render view state)
    let shared = find target "span"
    let own = find target "b"
    let first = styleOf shared "transform", styleOf own "transform"
    write graph mounted (fun () -> state.Offset.Value <- 100.)
    advance clock 0.
    advance clock 200.
    let reached = styleOf shared "transform"
    let timedEarly = styleOf (find target "i") "transform"
    advance clock 300.
    let timedReached = styleOf (find target "i") "transform"
    mounted.Dispose ()

    equal
        (("translateX(50%)", "translateX(50px)"), "translateX(100%)", (true, "translateX(100%)"))
        (first, reached, (timedEarly <> timedReached, timedReached))

let private tileSpringsHome (clock: Clock) =
    use graph = new Graph ()
    let target = host ()
    let mounted = mountIn false graph target (Component.render Fixtures.tile ())
    let draggable = lastDraggable ()
    draggable.x <- 100.
    advance clock 16.
    (find target "button").click()
    mounted.Flush ()
    advance clock 0.

    for _ in 1..5 do
        advance clock 16.

    let moving = draggable.x

    for _ in 1..300 do
        advance clock 16.

    let settled = draggable.x
    mounted.Dispose ()
    equal (true, true) (moving < 100. && moving > 0., abs settled < 0.5)

let private modes (name: string) (check: bool -> Clock -> unit) =
    [|
        for queued in [ false; true ] do
            yield sprintf "%s (%s commits)" name (if queued then "queued" else "synchronous"), check queued
    |]

/// <summary>Each check by name; a check receives the test clock.</summary>
let cases: (string * (Clock -> unit)) array =
    Array.concat
        [
            modes "enter reads its values at 0, half and end, and first-pass rows stay still" enterValues
            modes "typed CSS variables use Motion ownership and preserve user classes" typedCssOnReverts
            modes "typed CSS variables follow reactive values through Motion attributes" typedCssFollow
            modes "exit removes the row's node at the tick its animation completes" exitRemovesAtCompletion
            [|
                "on reverts its animation when the instance is disposed", onRevertsOnDisposal
            |]
            modes "enter and exit on one element: the exit starts from the paused enter's values" (pausedWhenExitFollows both)
            modes "presence: the exit starts from the paused enter's values" (pausedWhenExitFollows present)
            modes "an arm with presence enters when shown and leaves from its paused values when hidden" armPresence
            [| "presence pins the leaving node out of flow", presencePins |]
            modes "disposing the mount mid-exit removes the node and stops the exit" disposalEndsExits
            modes "under Reduced, enter does not play and exit finishes at once" reducedPolicy
            modes "draw animates the stroke of an SVG path to each fraction" drawReaches
            modes "morph animates an SVG path to each shape and ends on it exactly" morphReaches
            modes "list rows under an svg element are SVG elements and draw their strokes" svgRows
            modes "an arm rooted at an SVG tag under svg is an SVG element and morphs" svgArm
            modes "after a row's exit completes, none of its drag, split or follow objects is live" nothingLiveAfterExit
            modes "parts inside an exiting row or a fading arm keep their values while it leaves" descendantsPause
            [|
                "Hooks.both runs hooks in order, cleanups in reverse, and keeps the first cleanup on a failure", bothRules
            |]
            modes "SVG tags take camelCase attributes, shared parts, text rows and foreignObject content" svgAuthoring
            [| "a synchronous row mixing SVG and HTML roots raises", mixedRootsRaise |]
            [|
                "draw and morph cleanups restore the element's attributes", svgCleanupRestores
            |]
            modes "under flip, an opacity enter or presence enter ends at its last frame" flipKeepsEnters
            [|
                "drag writes its position once a release's motion ends, without echo", dragWritesSettle
            |]
            [| "anime's layout keeps the property Sets flip edits", layoutFieldsPinned |]
            modes "an attribute name that lowercases to an event handler, or joins listed SVG names, raises" mixedCaseHandlerRaises
            modes "raw-text, template and void-with-children elements raise on the client" refusedElements
            modes "an exit whose play raises leaves no departing mark" failingExitUnmarks
            modes "the client mounts RCDATA elements with element children and case-twin attributes without running script" clientMountsServerChecks
            modes "a title row under svg follows the commit mode's documented namespace" titleRows
            modes "flip records before and animates after each pass, without leaving rows, and fades rows with no running Motion part" flipOrder
            modes "a row given flipFrom starts its FLIP at the dropped box, on the creating pass and on later passes" flipStartsFromDrop
            modes "flipFrom under Reduced keeps no box for a later pass" flipHintUnderReduced
            [| "the README tile springs home through its slot", tileSpringsHome |]
            [|
                "a slot replaces its animation, a grab cancels it, and disposal cancels it and refuses later plays", slotOwnsAnimations
                "follow applies the shared unit unless the property names its own", followUnit
            |]
            [|
                "follow sets its first value and animates to each write", followReaches
                "animateOn replaces a running animation and settles on the last value", animateOnReplaces
                "drag writes its position and moves on writes without echo", dragEchoGuard
                "scroll writes the observer's progress and in-view state", scrollWrites
                "split splits the text and its cleanup restores it", splitReverts
                "appear skips hydrated elements and plays on created ones", appearSkipsHydrated
            |]
        ]
