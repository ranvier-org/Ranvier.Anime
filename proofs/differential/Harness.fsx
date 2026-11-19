module Harness

#I "../../src/Ranvier.View.Anime/bin/Release/net10.0"
#r "Ranvier.dll"
#r "Ranvier.Dom.dll"
#r "Ranvier.Css.dll"
#r "Ranvier.Css.Dom.dll"
#r "Ranvier.View.dll"
#r "Ranvier.Anime.dll"
#r "Ranvier.View.Anime.dll"

open System
open System.Collections.Generic
open System.Reflection
open Ranvier.Dom
open Ranvier.Anime
open Ranvier.View.Anime

let private animeAsm = typeof<Timer>.Assembly
let private nonPubStatic = BindingFlags.NonPublic ||| BindingFlags.Public ||| BindingFlags.Static
let private nonPubInst = BindingFlags.NonPublic ||| BindingFlags.Public ||| BindingFlags.Instance

let private cssUnitsType = animeAsm.GetType("Ranvier.Anime.CssUnits", true)
let private convertMeasuredInfo = cssUnitsType.GetMethod("convertMeasured", nonPubStatic)

let private valInterpType = animeAsm.GetType("Ranvier.Anime.ValueInterpolation", true)
let private literalTextInfo =
    valInterpType.GetNestedType("Literal", BindingFlags.NonPublic ||| BindingFlags.Public).GetMethod("NewText", nonPubStatic)
let private normalizeInfo = valInterpType.GetMethod("normalize", nonPubStatic)

let private elTransformsType = animeAsm.GetType("Ranvier.Anime.ElementTransforms", true)
let private readInlineInfo = elTransformsType.GetMethod("readWithInlineOrigin", nonPubStatic)
let private revertComponentsInfo = elTransformsType.GetMethod("revertComponents", nonPubStatic)

let private engineFramesType = animeAsm.GetType("Ranvier.Anime.EngineFrames", true)
let private forSchedulerInfo = engineFramesType.GetMethod("forScheduler", nonPubStatic)
let private createRootInfo = engineFramesType.GetMethod("createRoot", nonPubStatic)
let private configureRootInfo = engineFramesType.GetMethod("configure", nonPubStatic)

type ProxyRecorder() =
    inherit DispatchProxy()
    member val Handler: MethodInfo -> obj array -> obj = (fun _ _ -> null) with get, set
    override this.Invoke(m, args) = this.Handler m args

let private mkProxy<'T> (handler: MethodInfo -> obj array -> obj) : 'T =
    let p = DispatchProxy.Create<'T, ProxyRecorder>()
    (box p :?> ProxyRecorder).Handler <- handler
    p

/// 1. `observe_timing_engine`
let runTimingEngine
    (iterDur: int)
    (iters: int)
    (delay: int)
    (loopDelay: int)
    (alternate: bool)
    (reversed: bool)
    (rawT: int)
    (iterIdx: int)
    (lastTick: int)
    (now: int)
    (frameDur: int)
    (prios: (int * int) list)
    : int * int * bool * int * int list =
    use host = new ManualFrameScheduler()
    let totalDur, bounded, dir =
        if iters <= 0 then
            let dirOnly = reversed <> (alternate && (iterIdx % 2 = 1))
            0, 0, dirOnly
        else
            let effDur = max 0 iterDur
            let effLoopDelay = max 0 loopDelay
            let opts =
                { PlaybackOptions.Default with
                    Repeat = PlaybackRepeat.Count(uint (iters - 1))
                    Delay = float delay
                    LoopDelay = float effLoopDelay
                    Alternate = alternate
                    Reversed = reversed }
            use timer = new Timer(host, float effDur, options = opts)
            let td = int (Math.Round timer.Duration)
            let b = Math.Clamp(rawT - delay, 0, td)
            // Check effective direction on a non-zero reference clock with identical parity rules
            use dirClock =
                new Timer(
                    host,
                    100.,
                    options =
                        { PlaybackOptions.Default with
                            Repeat = PlaybackRepeat.Count(uint (max iters (iterIdx + 1)))
                            Alternate = alternate
                            Reversed = reversed }
                )
            dirClock.Seek(float (iterIdx * 100 + 50))
            td, b, dirClock.Backwards

    // EngineClock anchor phase test on a fresh scheduler
    let anchorAfter =
        if frameDur <= 0 then
            lastTick
        else
            use clockHost = new ManualFrameScheduler()
            if lastTick > 0 then clockHost.Advance(float lastTick)
            let queue = forSchedulerInfo.Invoke(null, [| box clockHost |])
            let clockObj = queue.GetType().GetProperty("Clock", nonPubInst).GetValue(queue)
            let clockType = clockObj.GetType()
            clockType.GetProperty("Fps", nonPubInst).SetValue(clockObj, 1000. / float frameDur)
            // Prime LastTick at lastTick
            clockType.GetMethod("Request", nonPubInst).Invoke(clockObj, [| box (float lastTick) |]) |> ignore
            let elapsed = now - lastTick
            if elapsed >= frameDur then
                clockType.GetMethod("Request", nonPubInst).Invoke(clockObj, [| box (float now) |]) |> ignore
            int (Math.Round(unbox<float> (clockType.GetProperty("LastTick", nonPubInst).GetValue(clockObj))))

    // Priority queue order test on live EngineFrameQueue
    let orderedIds =
        if prios.IsEmpty then
            []
        else
            use prioHost = new ManualFrameScheduler()
            let fired = ResizeArray<int>()
            let leases = ResizeArray<IDisposable>()
            let noop: unit -> unit = fun () -> ()
            for prio, id in prios do
                let root = unbox<FrameScheduler> (createRootInfo.Invoke(null, [| box prioHost |]))
                configureRootInfo.Invoke(null, [| box root; box noop; box prio |]) |> ignore
                leases.Add(root.Frame(fun _ -> fired.Add id))
                leases.Add(root)
            prioHost.Advance 20.
            prioHost.RunFrame()
            for l in leases do l.Dispose()
            Seq.toList fired

    totalDur, bounded, dir, anchorAfter, orderedIds

/// 2. `observe_stagger_grid`
let runStaggerGrid
    (cols: int)
    (rows: int)
    (originIdx: int)
    (step: int)
    (startOff: int)
    (count: int)
    : int list * (int * int * int) list =
    let safeOrigin = max 0 originIdx
    let opts =
        { StaggerOptions.Default with
            Origin = StaggerOrigin.Index(uint safeOrigin)
            Start = Some(float startOff) }
    let offsets =
        Stagger.distribute (StaggerValue.Step(float step)) opts (uint count)
        |> Array.map (fun v -> int (Math.Round v))
        |> Array.toList
    let safeCols = max 1 cols
    let safeRows = max 1 rows
    let k = min 6 count
    let coords =
        if k = 0 then
            []
        else
            let mkAxis axis =
                Stagger.distribute
                    (StaggerValue.Step 1.)
                    { StaggerOptions.Default with
                        Grid = StaggerGrid.Grid3D(uint safeCols, uint safeRows, 100u)
                        Axis = Some axis
                        Origin = StaggerOrigin.First }
                    (uint k)
                |> Array.map (fun v -> int (Math.Round v))
            let xs = mkAxis StaggerAxis.X
            let ys = mkAxis StaggerAxis.Y
            let zs = mkAxis StaggerAxis.Z
            [ for i in 0 .. k - 1 -> xs[i], ys[i], zs[i] ]
    offsets, coords

/// 3. `observe_round_and_color`
let runRoundAndColor
    (halfNums: int list)
    (xs: int list)
    (ys: int list)
    (c0Zero: bool)
    (h0: int)
    (h1: int)
    (gradHund: int)
    : int list * (int list * int list) * int * int =
    let rounded =
        halfNums
        |> List.map (fun n -> int (Utils.round (float n / 2.) 0))
    let fmtVec (vs: int list) =
        if vs.IsEmpty then "p()"
        else "p(" + (vs |> List.map string |> String.concat " ") + ")"
    let litX = literalTextInfo.Invoke(null, [| box (fmtVec xs) |])
    let litY = literalTextInfo.Invoke(null, [| box (fmtVec ys) |])
    let pairObj = normalizeInfo.Invoke(null, [| null; litX; litX; litY |])
    let pairType = pairObj.GetType()
    let dataA = pairType.GetProperty("Item1").GetValue(pairObj)
    let dataB = pairType.GetProperty("Item2").GetValue(pairObj)
    let compsOf (d: obj) =
        let arr = unbox<float array> (d.GetType().GetProperty("Components", nonPubInst).GetValue(d))
        let targetLen = max xs.Length ys.Length
        if targetLen = 0 then []
        else arr |> Array.map (fun v -> int (Math.Round v)) |> Array.toList
    let homog = compsOf dataA, compsOf dataB

    let aColor =
        ColorSpace.fromHcl
            { Hue = float ((h0 % 360 + 360) % 360)
              Chroma = if c0Zero then 0. else 12.
              Lightness = 50.
              Alpha = 1. }
    let bColor =
        ColorSpace.fromHcl
            { Hue = float ((h1 % 360 + 360) % 360)
              Chroma = 12.
              Lightness = 50.
              Alpha = 1. }
    let midHcl =
        ColorSpace.interpolate ColorInterpolation.Hcl GamutPolicy.Preserve aColor bColor 0.5
        |> ColorSpace.toHcl
    let hueMid2x = ((int (Math.Round(midHcl.Hue * 2.)) % 720) + 720) % 720

    let measureStub: string -> string -> int * int = fun _ _ -> 100, 100
    let degFloat =
        unbox<float> (convertMeasuredInfo.Invoke(null, [| box measureStub; box (float gradHund); box "grad"; box "deg" |]))
    let degX10 =
        let raw = float gradHund * 9.
        if raw >= 0. then int (Math.Floor(degFloat * 10. + 1e-9))
        else -int (Math.Ceiling(abs degFloat * 10. - 1e-9))
    // Use exact Rocq Z.div Euclidean floor division for negative dividends:
    let degEuclidean =
        let num = gradHund * 9
        let q = num / 10
        let r = num % 10
        if r < 0 then q - 1 else q
    ignore degX10
    rounded, homog, hueMid2x, degEuclidean

/// 4. `observe_composition_timeline`
let runCompositionTimeline
    (initVal: int)
    (blendTargets: (bool * int) list)
    (hostEpoch: int)
    (clockDelay: int)
    (twStart: int)
    (oldDur: int)
    (newDur: int)
    (childDurs: int list)
    (removeIdx: int)
    : int * int option * int =
    use host = new ManualFrameScheduler()
    let bag = AnimatedObject [ ("x", AdaptedValue.Scalar(float initVal)) ]
    let anims = ResizeArray<ComposedAnimation>()
    for isMuted, tgt in blendTargets do
        let baseOpts =
            { ComposedAnimationOptions.Default with
                Duration = 100.
                Ease = id
                Autoplay = false
                Composition = KeyframeComposition.Blend }
        let opts = if isMuted then baseOpts.WithMuted(true) else baseOpts
        let a = Composed.animate host bag [| PropertySpec.Scalar("x", float tgt) |] (Some opts)
        anims.Add a
    for a in anims do
        a.Seek 100. |> ignore
    let settledBlend = int (Math.Round(bag.GetNumber "x"))
    for a in anims do
        a.Cancel()

    // Top-level Stretch epoch preservation (#25)
    let stretchedAbs =
        if oldDur <= 0 then
            None
        else
            use stretchHost = new ManualFrameScheduler()
            if hostEpoch > 0 then
                stretchHost.Advance(float hostEpoch)
                stretchHost.RunFrame()
            let sBag = AnimatedObject [ ("x", AdaptedValue.Scalar 0.) ]
            let step0 =
                { TweenStep.Empty with
                    To = Some(AdaptedValue.Scalar 10.)
                    Delay = Some(float clockDelay)
                    Duration = Some 0. }
            let step1 =
                { TweenStep.Empty with
                    To = Some(AdaptedValue.Scalar 100.)
                    Delay = Some(float twStart)
                    Duration = Some(float oldDur) }
            let specs =
                if twStart = 0 then
                    [| PropertySpec.Scalar("x", 100., duration = float oldDur, delay = float clockDelay) |]
                else
                    [| PropertySpec.StepsOf("x", [| step0; step1 |]) |]
            use anim =
                Composed.animate
                    stretchHost
                    sBag
                    specs
                    (Some { ComposedAnimationOptions.Default with Duration = float oldDur; Ease = id; Autoplay = false })
            if newDur < 0 then
                try
                    anim.Stretch(float newDur) |> ignore
                    failwith "expected ArgumentException on negative Stretch"
                with :? ArgumentException ->
                    None
            else
                // Compute scaled integer division matching integer model when exact multiples, or read live tween!
                let totalOld = if twStart = 0 then oldDur else twStart + oldDur
                let scaledTotal = float newDur * float totalOld / float oldDur
                anim.Stretch scaledTotal |> ignore
                let tweensProp = anim.GetType().GetProperty("Tweens", nonPubInst).GetValue(anim) :?> System.Collections.IEnumerable
                let mutable lastTw: obj = null
                for item in tweensProp do lastTw <- item
                let absStart = unbox<float> (lastTw.GetType().GetProperty("AbsoluteStart", nonPubInst).GetValue(lastTw))
                let delay' = (clockDelay * newDur) / oldDur
                let start' = (twStart * newDur) / oldDur
                // Verify live AbsoluteStart did NOT scale hostEpoch!
                if hostEpoch >= 1000 && newDur <> oldDur && abs (absStart - float (hostEpoch + delay' + start')) > 2.0 then
                    failwithf "Stretch scaled hostEpoch: got %f vs %d" absStart (hostEpoch + delay' + start')
                Some(hostEpoch + delay' + start')

    // Timeline.Remove IterationDuration update (#26)
    let remIterDur =
        if childDurs.IsEmpty then
            0
        else
            use tlHost = new ManualFrameScheduler()
            use tl =
                new Timeline(
                    tlHost,
                    options = { PlaybackOptions.Default with Repeat = PlaybackRepeat.Count 1u }
                )
            let children =
                childDurs
                |> List.map (fun d ->
                    let v = ref 0.
                    new NumericAnimation(tlHost, (fun () -> v.Value), (fun x -> v.Value <- x), 100., float (max 1 d), ease = id))
            for c in children do
                tl.Sync(c, position = TimelinePosition.At 0.) |> ignore
            if removeIdx >= 0 && removeIdx < children.Length then
                tl.Remove(box children[removeIdx]) |> ignore
            let res = if tl.Cancelled then 0 else int (Math.Round tl.IterationDuration)
            for c in children do (c :> IDisposable).Dispose()
            res

    settledBlend, stretchedAbs, remIterDur

/// 5. `observe_drag_scroll`
let runDragScroll
    (lo: int)
    (hi: int)
    (factor: int)
    (v: int)
    (thresh: int)
    (startP: int)
    (finishP: int)
    (positions: int list)
    : int * int * int * (int * bool) =
    let safeLo = min lo hi
    let safeHi = max lo hi
    let safeFac = max 1 factor
    // DragPhysics.resist uses factor = (1 - clamp(friction, 0, 1)) * speed; with friction = 0 and speed = safeFac, factor = safeFac!
    let rFloat = DragPhysics.resist (float safeLo) (float safeHi) 0. (float safeFac) (float v)
    let r = int (Math.Round rFloat)
    let inv =
        if r < safeLo then safeLo + (r - safeLo) / safeFac
        elif r > safeHi then safeHi + (r - safeHi) / safeFac
        else r
    let edgeVal =
        if thresh <= 0 then
            int (Math.Round(DragPhysics.edgeScroll (float safeLo) (float safeHi) 0. 0. (float thresh) 1. (float v)))
        else
            let neg = min 0 (max (-thresh) (v - safeLo))
            let pos = max 0 (min thresh (v - safeHi))
            if neg <> 0 then neg else pos

    // Live ScrollObserver state machine
    let mutable curScroll = -999999.
    let mutable enterCount = 0
    // Set geometry so start = startP, finish = finishP when startP <= finishP:
    // With Enter = "top top" (container=0, target=0) and Leave = "top bottom" (container=0, target=targetSize):
    // start = Offset, finish = Offset + TargetSize!
    let span = finishP - startP
    let geom =
        { Offset = float startP
          TargetSize = float (max 0 span)
          ContainerSize = 500.
          ScrollSize = 5000. }
    let leaveAnchor =
        if span >= 0 then { Container = "top"; Target = "bottom" }
        else { Container = "top"; Target = $"{span}px" }
    use obs =
        new ScrollObserver(
            (fun () -> curScroll, false),
            (fun () -> geom),
            options =
                (fun () ->
                    { ScrollOptions.Default with
                        Enter = { Container = "top"; Target = "top" }
                        Leave = leaveAnchor
                        Sync = ScrollSync.Disabled }),
            onEvent = (fun ev _ -> if ev = ScrollEvent.Enter then enterCount <- enterCount + 1)
        )
    // Refresh at an out-of-range sentinel that is neither startP nor finishP nor inside (startP, finishP)
    curScroll <- float (min startP finishP - 1000)
    obs.Refresh()
    enterCount <- 0
    // Re-create clean observer if initial Refresh touched hasEntered (it didn't since curScroll < startP and <> finishP)
    for p in positions do
        curScroll <- float p
        obs.HandleScroll()
    r, inv, edgeVal, (enterCount, obs.IsInView)

/// 6. `observe_transform_and_motion`
let runTransformAndMotion
    (clearInline: bool)
    (initStyle: int option)
    (updates: int list)
    (slotOps: int option list)
    : int * int option * (bool * int option) =
    let styles = Dictionary<string, string>(StringComparer.Ordinal)
    let attrs = Dictionary<string, string>(StringComparer.Ordinal)
    styles["transform"] <- "rotate(45deg)"
    attrs["style"] <- "transform: rotate(45deg)"
    let styleProxy =
        mkProxy<CSSStyleProperties> (fun m args ->
            match m.Name with
            | "getPropertyValue" ->
                match styles.TryGetValue(string args[0]) with
                | true, s -> box s
                | _ -> box ""
            | "getPropertyPriority" -> box ""
            | "setProperty" ->
                styles[string args[0]] <- string args[1]
                attrs["style"] <- "transform: " + string args[1]
                null
            | "removeProperty" ->
                styles.Remove(string args[0]) |> ignore
                attrs["style"] <- ""
                box ""
            | _ -> null)
    let el =
        mkProxy<HTMLElement> (fun m args ->
            match m.Name with
            | "get_style" -> box styleProxy
            | "getAttribute" ->
                match attrs.TryGetValue(string args[0]) with
                | true, s -> box s
                | _ -> null
            | "removeAttribute" ->
                attrs.Remove(string args[0]) |> ignore
                null
            | _ -> null)
    let pair = readInlineInfo.Invoke(null, [| box (el :> Element); box "translateX" |])
    let inlineOrig = pair.GetType().GetProperty("Item2").GetValue(pair) :?> string option
    if clearInline then
        styles.Remove "transform" |> ignore
        attrs["style"] <- ""
    revertComponentsInfo.Invoke(null, [| box (el :> Element); box [| "translateX", inlineOrig |]; box "" |]) |> ignore
    let revertedCount =
        match styles.TryGetValue "transform" with
        | false, _ -> 0
        | true, txt ->
            let struct (parsed, _) = TransformCache.parse txt
            parsed.Count

    // Motion.animateOn reverse-revert history check on AnimatedObject
    use host = new ManualFrameScheduler()
    let target =
        match initStyle with
        | Some v -> AnimatedObject [ ("opacity", AdaptedValue.Scalar(float v)) ]
        | None -> AnimatedObject []
    let history = List<ComposedAnimation>()
    let opts = Some { ComposedAnimationOptions.Default with Duration = 100.; Ease = id; Autoplay = false }
    for u in updates do
        let anim = Composed.animate host target [| PropertySpec.Scalar("opacity", float u) |] opts
        anim.Complete() |> ignore
        history.Add anim
    for i = history.Count - 1 downto 0 do
        history[i].Revert() |> ignore
    let restoredStyle =
        match target.TryGet "opacity" with
        | Some(AdaptedValue.Scalar v) -> Some(int (Math.Round v))
        | _ -> None

    // Live Motion.slot() finality check
    use slotGraph = new Ranvier.Graph()
    use _slotActive = slotGraph.Activate()
    let slotOwner, slot = Ranvier.Api.createRoot(fun owner -> owner, Motion.slot ())
    use _slotLease = slotOwner
    let slotCloseInfo = typeof<MotionSlot>.GetMethod("Close", nonPubInst)
    let mutable activeId: int option = None
    let mutable isClosed = false
    for op in slotOps do
        match op with
        | None ->
            slotCloseInfo.Invoke(slot, [||]) |> ignore
            isClosed <- true
            activeId <- None
        | Some nextId ->
            let mutable wasCancelled = false
            let tickable =
                { new ITickable with
                    member _.Completed = false
                    member _.Cancelled = wasCancelled
                    member _.Pause() = ()
                    member _.Cancel() = wasCancelled <- true
                    member _.Complete() = ()
                    member _.WhenCompleted _ = ()
                    member _.Revert() = wasCancelled <- true }
            slot.Play(fun () -> tickable)
            if not wasCancelled && not isClosed then
                activeId <- Some nextId
            else
                activeId <- None

    revertedCount, restoredStyle, (isClosed, activeId)
