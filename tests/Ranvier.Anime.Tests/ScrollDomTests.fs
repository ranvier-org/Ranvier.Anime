module Ranvier.Anime.Tests.ScrollDomTests

open System
open Expecto
open Ranvier.Dom
open Ranvier.Anime

/// <summary>Dispatches the DOM interface members exercised by scroll contracts.</summary>
type ScrollDomProxy() =
    inherit Reflection.DispatchProxy()
    let mutable invoke:Reflection.MethodInfo -> obj array -> obj = fun methodInfo _ -> failwith methodInfo.Name
    /// <summary>Supplies the test fixture's member implementation.</summary>
    member _.InvokeMember with set value = invoke <- value
    override _.Invoke(methodInfo,args) = invoke methodInfo args

let private proxy<'T> invoke =
    let result = Reflection.DispatchProxy.Create<'T,ScrollDomProxy>()
    (box result :?> ScrollDomProxy).InvokeMember <- invoke
    result

let private fixture () =
    let events = ResizeArray<string>()
    let mutable scrollY = 0
    let mutable listener:EventListenerObject option = None
    let mutable resizeCallback: (ResizeObserverEntry array -> unit) option = None
    let rect left top width height = proxy<DOMRect>(fun methodInfo _ ->
        match methodInfo.Name with
        | "get_left" -> box left | "get_top" -> box top | "get_width" -> box width | "get_height" -> box height
        | name -> failwith name)
    let style = proxy<CSSStyleProperties>(fun methodInfo args ->
        match methodInfo.Name with
        | "getPropertyValue" -> box (if unbox<string> args[0]="position" then "static" else "0px")
        | name -> failwith name)
    let mutable container:HTMLElement = Unchecked.defaultof<_>
    let document = proxy<Document>(fun methodInfo _ ->
        match methodInfo.Name with "get_body" -> box container | name -> failwith name)
    container <- proxy<HTMLElement>(fun methodInfo args ->
        match methodInfo.Name with
        | "getBoundingClientRect" -> box(rect 10. 20. 600. 400.)
        | "get_clientWidth" -> box 600 | "get_clientHeight" -> box 400
        | "get_scrollWidth" -> box 2400 | "get_scrollHeight" -> box 2000
        | "get_scrollLeft" -> box 0 | "get_scrollTop" -> box scrollY
        | "addEventListener" -> listener <- Some(unbox args[1]); events.Add "add"; null
        | "removeEventListener" ->
            if listener |> Option.exists(fun listener -> Object.ReferenceEquals(listener,args[1])) then listener <- None
            else failwith "listener identity changed"
            events.Add "remove"; null
        | name -> failwith name)
    let target = proxy<HTMLElement>(fun methodInfo _ ->
        match methodInfo.Name with
        | "getBoundingClientRect" -> box(rect 160. (520.-float scrollY) 50. 200.)
        | "get_ownerDocument" -> box document
        | "get_parentElement" -> box container
        | name -> failwith name)
    let computed = proxy<ComputedStyleService>(fun methodInfo _ ->
        match methodInfo.Name with "Get" -> box style | name -> failwith name)
    let resizeObserver = proxy<ResizeObserver>(fun methodInfo _ ->
        match methodInfo.Name with
        | "Observe" -> events.Add "observe"; null
        | "Disconnect" -> events.Add "disconnect"; null
        | name -> failwith name)
    let resize = proxy<ResizeObservationService>(fun methodInfo args ->
        match methodInfo.Name with
        | "Create" -> resizeCallback <- Some(unbox args[0]); box resizeObserver
        | name -> failwith name)
    let scroll value = scrollY <- value; listener |> Option.iter(fun listener -> listener.handleEvent Unchecked.defaultof<Event>)
    let resized () = resizeCallback |> Option.iter(fun callback -> callback [||])
    container,target,computed,resize,events,scroll,resized,(fun () -> listener.IsSome)

let tests = testList "scroll DOM contracts" [
    testCase "explicit linked target preserves source deferred refresh" <| fun _ ->
        let element,target,computed,resize,_,_,_,_ = fixture()
        use scheduler = new ManualFrameScheduler()
        use container = new ScrollContainer(scheduler,element,computed,resize)
        use timer = new Timer(scheduler,1000u)
        use observer = container.ObserveLinked(ScrollLink.OfTimer timer,Seq.empty,target=target)
        Expect.isFalse observer.Ready "source link with an authored target skips synchronous refresh"
        scheduler.RunFrame()
        Expect.isTrue observer.Ready "constructor frame resolves the authored target"
        Expect.equal observer.Offset 500. "explicit target takes precedence"

    testCase "registry reuses native identity and evicts the last disposed observer" <| fun _ ->
        let element,target,computed,resize,events,_,_,listening = fixture()
        use scheduler = new ManualFrameScheduler()
        use registry = new ScrollContainerRegistry(scheduler,computed,resize)
        let first = registry.ForElement element
        let second = registry.ForElement element
        Expect.isTrue (Object.ReferenceEquals(first,second)) "one native container per element"
        use a = first.Observe target
        use b = second.Observe target
        a.Revert()
        Expect.isTrue (listening()) "remaining observer retains shared subscription"
        b.Revert()
        Expect.equal registry.Count 0 "final observer evicts container"
        Expect.isFalse (listening()) "final observer releases shared subscription"
        let replacement = registry.ForElement element
        Expect.isFalse (Object.ReferenceEquals(first,replacement)) "later observation receives a live replacement"
        registry.Dispose()
        Expect.equal registry.Count 0 "registry disposal clears every entry"
        Expect.sequenceEqual events ["add";"observe";"disconnect";"remove";"add";"observe";"disconnect";"remove"] "two lifetimes"
        Expect.throws (fun () -> registry.ForElement element |> ignore) "disposed registry is terminal"

    testCase "typed linked targets initialize immediately without duplicate next-frame measurement" <| fun _ ->
        let element,target,computed,resize,_,_,_,_ = fixture()
        use scheduler = new ManualFrameScheduler()
        use container = new ScrollContainer(scheduler,element,computed,resize)
        use timer = new Timer(scheduler,1000u)
        timer.Seek 123.
        let mutable refreshes = 0
        use observer = container.ObserveLinked(ScrollLink.OfTimer timer,[Unchecked.defaultof<HTMLElement>;target],
            options=(fun () -> refreshes <- refreshes+1; ScrollOptions.Default))
        Expect.isTrue observer.Ready "link computes the inferred target immediately"
        Expect.equal observer.Offset 500. "first typed DOM target selected"
        Expect.equal timer.CurrentTime 123. "muted measurement restores borrowed playhead"
        scheduler.RunFrame()
        Expect.equal refreshes 1 "queued initialization skips an already linked observer"
        observer.Revert()
        Expect.isFalse timer.Cancelled "linked lifetime remains borrowed"

    testCase "native interface geometry initializes on the next frame and shared cleanup releases listeners" <| fun _ ->
        let element,target,computed,resize,events,_,_,listening = fixture()
        use scheduler = new ManualFrameScheduler()
        use container = new ScrollContainer(scheduler,element,computed,resize)
        use observer = container.Observe target
        Expect.isFalse observer.Ready "link may be configured before the next frame"
        scheduler.RunFrame()
        Expect.equal observer.Offset 500. "target offset subtracts container origin"
        Expect.equal observer.OffsetStart 100. "container end meets target start"
        Expect.equal observer.OffsetEnd 700. "container start meets target end"
        observer.Revert()
        Expect.isFalse (listening()) "last child removes scroll listener"
        Expect.sequenceEqual events ["add";"observe";"disconnect";"remove"] "native subscriptions released once"

    testCase "queued initialization is cancelled by owner disposal" <| fun _ ->
        let element,target,computed,resize,events,_,_,_ = fixture()
        use scheduler = new ManualFrameScheduler()
        use graph = new Ranvier.Graph()
        use activation = graph.Activate()
        use container = new ScrollContainer(scheduler,element,computed,resize)
        let mutable observer:ScrollObserver option = None
        let owner = Ranvier.Api.createRoot(fun owner -> observer <- Some(container.Observe(target).Own()); owner)
        owner.Dispose()
        scheduler.RunFrame()
        Expect.isFalse observer.Value.Ready "disposed owner cancels deferred measurement"
        Expect.sequenceEqual events ["add";"observe";"disconnect";"remove"] "ownership released the shared container"

    testCase "resize refresh is debounced by the existing timer and emits once per observer" <| fun _ ->
        let element,target,computed,resize,_,_,resized,_ = fixture()
        use scheduler = new ManualFrameScheduler()
        use container = new ScrollContainer(scheduler,element,computed,resize)
        let mutable resizes = 0
        use observer = container.Observe(target,onEvent=(fun event _ -> if event=ScrollEvent.Resize then resizes <- resizes+1))
        scheduler.RunFrame()
        resized(); scheduler.Advance 100.; scheduler.RunFrame()
        Expect.equal resizes 0 "before debounce duration"
        resized(); scheduler.Advance 250.; scheduler.RunFrame()
        Expect.equal resizes 1 "one refresh after restarted timer"

    testCase "scroll events drive the existing linked timer and cleanup leaves borrowed playback live" <| fun _ ->
        let element,target,computed,resize,_,scroll,_,_ = fixture()
        use scheduler = new ManualFrameScheduler()
        use timer = new Timer(scheduler,1000u)
        use container = new ScrollContainer(scheduler,element,computed,resize)
        use observer = container.Observe(target,options=(fun () -> {ScrollOptions.Default with Sync=ScrollSync.Smooth 1.}))
        observer.Link(ScrollLink.OfTimer timer) |> ignore
        scheduler.RunFrame()
        scroll 400
        // The wake timer resumes the ticker on its first frame; the ticker samples the scroll on the following frame.
        scheduler.Advance 20.; scheduler.RunFrame(); scheduler.RunFrame()
        Expect.isLessThanOrEqual (abs(timer.Progress-0.5)) 1e-8 "linked midpoint"
        observer.Revert()
        Expect.isFalse timer.Cancelled "observer borrows playback lifetime"
]
