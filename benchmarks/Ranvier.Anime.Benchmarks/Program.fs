module Ranvier.Anime.Benchmarks.Program

open System
open System.Collections.Generic
open System.Diagnostics
open System.IO
open System.Reflection
open System.Text.Json
open Ranvier.Dom
open Ranvier.Anime

type BenchResult =
    { Name: string
      MedianMs: float
      P95Ms: float
      MedianBytes: int64 }

type BenchProxy() =
    inherit DispatchProxy()
    member val Handler: MethodInfo -> obj array -> obj = (fun _ _ -> null) with get, set
    override this.Invoke(m, args) = this.Handler m args

let private mkProxy<'T> handler : 'T =
    let p = DispatchProxy.Create<'T, BenchProxy>()
    (box p :?> BenchProxy).Handler <- handler
    p

let private measure (name: string) (warmup: int) (samplesCount: int) (action: unit -> unit) : BenchResult =
    for _ in 1 .. warmup do
        action ()
    GC.Collect()
    GC.WaitForPendingFinalizers()
    GC.Collect()

    let times = Array.zeroCreate<float> samplesCount
    let allocs = Array.zeroCreate<int64> samplesCount
    for i in 0 .. samplesCount - 1 do
        let before = GC.GetAllocatedBytesForCurrentThread()
        let sw = Stopwatch.StartNew()
        action ()
        sw.Stop()
        let after = GC.GetAllocatedBytesForCurrentThread()
        times[i] <- sw.Elapsed.TotalMilliseconds
        allocs[i] <- after - before

    Array.Sort times
    Array.Sort allocs
    let medIdx = samplesCount / 2
    let p95Idx = min (samplesCount - 1) (int (float samplesCount * 0.95))
    let res =
        { Name = name
          MedianMs = Math.Round(times[medIdx], 4)
          P95Ms = Math.Round(times[p95Idx], 4)
          MedianBytes = allocs[medIdx] }
    printfn "%-46s median %8.3f ms | p95 %8.3f ms | alloc %10d B" res.Name res.MedianMs res.P95Ms res.MedianBytes
    res

let private mkStyleElement (initialTransform: string) =
    let styles = Dictionary<string, string>(StringComparer.Ordinal)
    let priorities = Dictionary<string, string>(StringComparer.Ordinal)
    let attrs = Dictionary<string, string>(StringComparer.Ordinal)
    if not (String.IsNullOrEmpty initialTransform) then
        styles["transform"] <- initialTransform
        attrs["style"] <- "transform: " + initialTransform
    let style =
        mkProxy<CSSStyleProperties> (fun m args ->
            match m.Name with
            | "getPropertyValue" ->
                match styles.TryGetValue(string args[0]) with
                | true, v -> box v
                | _ -> box ""
            | "getPropertyPriority" ->
                match priorities.TryGetValue(string args[0]) with
                | true, v -> box v
                | _ -> box ""
            | "setProperty" ->
                let k = string args[0]
                let v = string args[1]
                styles[k] <- v
                priorities[k] <- (unbox<string option> args[2] |> Option.defaultValue "")
                null
            | "removeProperty" ->
                let k = string args[0]
                styles.Remove k |> ignore
                priorities.Remove k |> ignore
                box ""
            | "get_length" -> box (uint32 styles.Count)
            | "item" ->
                let idx = int (unbox<uint32> args[0])
                let mutable i = 0
                let mutable found = ""
                for k in styles.Keys do
                    if i = idx then found <- k
                    i <- i + 1
                box found
            | "get_cssText" -> box ""
            | _ -> null)
    let el =
        mkProxy<HTMLElement> (fun m args ->
            match m.Name with
            | "get_style" -> box style
            | "getAttribute" ->
                match attrs.TryGetValue(string args[0]) with
                | true, v -> box v
                | _ -> null
            | "setAttribute" ->
                attrs[string args[0]] <- string args[1]
                null
            | "removeAttribute" ->
                attrs.Remove(string args[0]) |> ignore
                null
            | _ -> null)
    el, style

[<EntryPoint>]
let main argv =
    let results = ResizeArray<BenchResult>()

    // 1. Additive Blend across 16 concurrent animations & 64 channels over 60 Engine frames
    do
        let mkChannels k =
            Array.init 64 (fun i -> string ((i * 3 - 40) * k)) |> String.concat " "
        let c0 = mkChannels 1
        let targets = Array.init 16 (fun i -> mkChannels (i + 2))
        results.Add(
            measure "blend-additive-16x64-60-frames" 4 25 (fun () ->
                use host = new ManualFrameScheduler()
                let eng = Engine.forScheduler host
                eng.UseDefaultMainLoop <- false
                let bag = AnimatedObject [ "vec", AdaptedValue.Text c0; "s", AdaptedValue.Scalar 0. ]
                let opts =
                    Some
                        { ComposedAnimationOptions.Default with
                            Duration = 1000.
                            Ease = id
                            Composition = KeyframeComposition.Blend
                            Autoplay = true }
                let anims =
                    targets
                    |> Array.mapi (fun idx txt ->
                        Composed.animate
                            host
                            bag
                            [| PropertySpec.Text("vec", txt); PropertySpec.Scalar("s", float ((idx + 1) * 10)) |]
                            opts)
                for _ in 1 .. 60 do
                    host.Advance 16.
                    eng.Update()
                for a in anims do
                    a.Cancel())
        )

    // 2. ComposedTimeline + Timeline with 100 children across 120 seeks
    do
        use host = new ManualFrameScheduler()
        let bags = Array.init 100 (fun _ -> AnimatedObject [ "x", AdaptedValue.Scalar 0.; "y", AdaptedValue.Scalar 10. ])
        use tl = Composed.createTimeline host None (Some false)
        let opts = { ComposedAnimationOptions.Default with Duration = 500.; Ease = id }
        for i in 0 .. 99 do
            tl.Add(
                [| box bags[i] |],
                [| PropertySpec.Scalar("x", float (i + 50)); PropertySpec.Scalar("y", float (i * 2)) |],
                options = opts,
                position = TimelinePosition.At(float (i * 5))
            )
            |> ignore
        results.Add(
            measure "timeline-100-children-120-frames" 4 25 (fun () ->
                for f in 0 .. 119 do
                    tl.Seek(float ((f * 9) % 990)) |> ignore)
        )

    // 3. KeyframeAnimation with 32 segments across 8 properties × 200 seeks
    do
        use host = new ManualFrameScheduler()
        let vals = Array.zeroCreate<float> 8
        let targets = Array.init 8 (fun p -> NumericProperty((fun () -> vals[p]), (fun v -> vals[p] <- v)))
        let frames = Array.init 32 (fun k -> Keyframe.To(float ((k + 1) * 10)))
        use kf =
            Keyframes.Numeric(
                host,
                targets,
                frames,
                duration = 640u,
                options = { KeyframeAnimationOptions.Default with Ease = id; Autoplay = false }
            )
        results.Add(
            measure "keyframe-32-segments-8-props-200-seeks" 5 30 (fun () ->
                for s in 0 .. 199 do
                    kf.Playback.Seek(float ((s * 3) % 640)))
        )

    // 4. SVG Morph 512 points (1,024 coordinates) × 120 frames (full prepareMorph + compose)
    do
        let mkPath offset =
            Array.init 512 (fun i ->
                let cmd = if i = 0 then "M" else "L"
                let x = float (i % 211 - 105) * (0.25 + offset)
                let y = float (i % 113 - 56) * (0.125 - offset)
                cmd + ValueInterpolation.number x + " " + ValueInterpolation.number y)
            |> String.concat " "
        let pFirst = mkPath 0.0
        let pLast = mkPath 0.375
        let sampler = ValueInterpolation.prepareMorph pFirst pLast
        let guard = fun () -> true
        results.Add(
            measure "svg-morph-512-points-120-frames" 4 25 (fun () ->
                let mutable lenSum = 0
                for f in 1 .. 120 do
                    match sampler (float f / 121.) guard with
                    | Some s -> lenSum <- lenSum + s.Text.Length
                    | None -> ()
                GC.KeepAlive lenSum)
        )

    // 5. RGBA Color string interpolation × 10,000 samples
    do
        let interp =
            ValueInterpolation.prepare
                (ValueInterpolation.Text "rgba(20, 40, 180, 0.25)")
                (ValueInterpolation.Text "rgba(20, 40, 180, 0.25)")
                (ValueInterpolation.Text "rgba(240, 110, 30, 0.95)")
        let guard = fun () -> true
        results.Add(
            measure "color-rgba-string-interp-10000" 4 25 (fun () ->
                let mutable sink = 0
                for i in 0 .. 9999 do
                    match interp (float (i % 99 + 1) / 100.) 4 id guard with
                    | Some o -> sink <- sink + o.Text.Length
                    | None -> ()
                GC.KeepAlive sink)
        )

    // 6. Perceptual HCL ColorSpace.interpolate × 10,000 samples
    do
        let first = ColorSpace.rgba 0.2 0.4 0.8 0.3
        let last = ColorSpace.rgba 0.9 0.2 0.1 0.9
        results.Add(
            measure "color-hcl-perceptual-10000" 4 25 (fun () ->
                let mutable acc = 0.
                for i in 0 .. 9999 do
                    let c = ColorSpace.interpolate ColorInterpolation.Hcl GamutPolicy.Clip first last (float (i % 100) / 99.)
                    acc <- acc + c.Red
                GC.KeepAlive acc)
        )

    // 7. Stagger flat step over 4,096 targets × 200 calls
    do
        let opts = { StaggerOptions.Default with Origin = StaggerOrigin.Center; Start = Some 3. }
        let step = StaggerValue.Step -1.25
        results.Add(
            measure "stagger-flat-step-4096x200" 5 30 (fun () ->
                let mutable acc = 0.
                for i in 0 .. 199 do
                    let arr = Stagger.distribute step opts 4096u
                    acc <- acc + arr[i]
                GC.KeepAlive acc)
        )

    // 8. Stagger automatic 3D grid over 1,024 points × 50 calls
    do
        let pts =
            Array.init 1024 (fun i ->
                Some(
                    DOMPointInit(
                        x = float ((i * 37) % 320) * 1.25,
                        y = float ((i * 53) % 240) * 0.75,
                        z = float ((i * 19) % 64) * 0.5
                    )
                ))
        let opts = { StaggerOptions.Default with Grid = StaggerGrid.Automatic pts; Origin = StaggerOrigin.Center }
        let rng = StaggerValue.Range(0., 500.)
        results.Add(
            measure "stagger-automatic-grid-1024x50" 4 25 (fun () ->
                let mutable acc = 0.
                for i in 0 .. 49 do
                    let arr = Stagger.distribute rng opts 1024u
                    acc <- acc + arr[i]
                GC.KeepAlive acc)
        )

    // 9. Easing: cubicBezier + outBounce + 32-stop linear × 50,000 samples
    do
        let bez = Ease.cubicBezier 0.42 0.0 0.58 1.0
        let bnc = Ease.named "outBounce"
        let irr = Ease.irregular 32u 1.25
        results.Add(
            measure "easing-bezier-bounce-linear-50000" 4 25 (fun () ->
                let mutable acc = 0.
                for i in 1 .. 50000 do
                    let t = float (i % 999 + 1) / 1000.
                    acc <- acc + bez t + bnc t + irr t
                GC.KeepAlive acc)
        )

    // 10. Multi-target homogeneous scalar tracks (500 targets × 120 frames)
    do
        use host = new ManualFrameScheduler()
        let targets = Array.init 500 (fun i -> box (AnimatedObject [ "x", AdaptedValue.Scalar(float i) ]))
        use anim =
            Composed.animateTargets
                host
                targets
                [| PropertySpec.Scalar("x", 1000.) |]
                (Some { ComposedAnimationOptions.Default with Duration = 1000.; Ease = id; Autoplay = false })
        results.Add(
            measure "multi-target-scalar-500-targets-120-frames" 4 25 (fun () ->
                for f in 1 .. 120 do
                    anim.Seek(float (f * 8)) |> ignore)
        )

    // 11. DraggableTransformTarget + TransformCache.parse × 2,000 frames
    do
        let el, _ = mkStyleElement "perspective(800px) translateX(4px) translateY(8px) rotate(30deg) scale(1.2)"
        let dragTarget = DraggableTransformTarget(el)
        let px = dragTarget.Property "translateX"
        let py = dragTarget.Property "translateY"
        results.Add(
            measure "draggable-transform-target-2000-frames" 4 25 (fun () ->
                for i in 1 .. 2000 do
                    DeferredWrites.Enter()
                    try
                        px.Value <- float i * 0.5
                        py.Value <- float i * -0.25
                    finally
                        DeferredWrites.Exit()
                let struct (parsed, _) =
                    TransformCache.parse "perspective(800px) translate3d(12px, 24px, 0px) rotate(45deg) scale(1.25)"
                GC.KeepAlive parsed)
        )
        dragTarget.Cancel()

    // 12. CssSerialization.declarations + CssPropertyMetadata.leafNames + DomTargetHelpers.toKebab × 2,000
    do
        let cssText =
            "transform: translate3d(10px, 20px, 0px) rotate(45deg); background-color: rgba(12, 34, 56, 0.8) !important; border: 1px solid red; --custom-var: calc(100% - 20px);"
        results.Add(
            measure "css-declarations-and-metadata-2000" 4 25 (fun () ->
                let mutable count = 0
                for _ in 1 .. 2000 do
                    let decls = CssSerialization.declarations cssText
                    let leaves = CssPropertyMetadata.leafNames "border"
                    let k1 = DomTargetHelpers.toKebab "opacity"
                    let k2 = DomTargetHelpers.toKebab "backgroundColor"
                    count <- count + decls.Length + leaves.Length + k1.Length + k2.Length
                GC.KeepAlive count)
        )

    // 13. LayoutStyleSnapshot.MarkWritten (64 nodes × border shorthand × 60 frames)
    do
        let nodes =
            Array.init 64 (fun i ->
                let _, style = mkStyleElement ""
                style.setProperty("border-top-width", $"{i}px")
                LayoutStyleSnapshot(style :> CSSStyleDeclaration, "border"), style)
        results.Add(
            measure "layout-style-snapshot-64-nodes-60-frames" 4 25 (fun () ->
                for f in 1 .. 60 do
                    let v = $"{f}px"
                    for i in 0 .. nodes.Length - 1 do
                        let snap, style = nodes[i]
                        style.setProperty("border-top-width", v)
                        snap.MarkWritten())
        )

    // 14. TextScramble 256-char string × 120 frames
    do
        let text256 = String.replicate 8 " Ranvier.Anime SIMD Scramble 012"
        let bag = AnimatedObject [ ("textContent", AdaptedValue.Text text256); ("innerHTML", AdaptedValue.Text text256) ]
        let factory =
            ScrambleImpl.createFactory
                { ScrambleTextOptions.Default with
                    Text = Some(String.replicate 8 " Verified Formal Motion Engine!!")
                    Seed = 42. }
        let tween: ScrambleTextTween = factory (box bag) 0 1 None
        results.Add(
            measure "text-scramble-256-chars-120-frames" 4 25 (fun () ->
                let mutable lenSum = 0
                for f in 0 .. 119 do
                    let s = tween.Modifier(float f / 119.)
                    lenSum <- lenSum + s.Length
                GC.KeepAlive lenSum)
        )

    // 15. Utils.snapTo + ValueChain.SnapTo (1,024 snap points × 5,000 queries)
    do
        let pts = Array.init 1024 (fun i -> float (i * 7 - 3500) * 0.375)
        let chainFn = ValueChain(id).SnapTo(pts).ToModifier()
        results.Add(
            measure "utils-snap-to-1024-points-5000" 4 25 (fun () ->
                let mutable acc = 0.
                for q in 0 .. 4999 do
                    let x = float ((q * 29) % 4000 - 2000) * 0.625
                    acc <- acc + Utils.snapTo x pts + chainFn x
                GC.KeepAlive acc)
        )

    // 16. Animatable multi-target (256 NumericProperty handles × retarget + 60 frames)
    do
        use host = new ManualFrameScheduler()
        let vals = Array.init 256 float
        let props = Array.init 256 (fun i -> NumericProperty((fun () -> vals[i]), (fun v -> vals[i] <- v)))
        use group = new Animatable(host)
        let handle = group.NumericPrecise(props, 500., { AnimatablePropertyOptions.Default with Ease = id })
        results.Add(
            measure "animatable-multi-target-256-retarget-60" 4 25 (fun () ->
                handle.RetargetPrecise(1000., 500., id)
                for _ in 1 .. 30 do
                    host.Advance 16.
                    host.RunFrame()
                handle.RetargetPrecise(0., 500., id)
                for _ in 1 .. 30 do
                    host.Advance 16.
                    host.RunFrame()
                GC.KeepAlive vals[0])
        )

    // 17. Spring construction (compute settling solver) + 50,000 Ease evaluations
    do
        results.Add(
            measure "spring-construct-and-solve-500-curves" 4 25 (fun () ->
                let mutable acc = 0.
                for i in 1 .. 50 do
                    let sp = Spring(mass = 1. + float (i % 4) * 0.25, stiffness = 80. + float i * 3., damping = 8. + float (i % 10), velocity = float (i % 5) * 0.5)
                    let ease = sp.Ease
                    for k in 1 .. 1000 do
                        acc <- acc + ease (float k * 0.001)
                GC.KeepAlive acc)
        )

    // 18. Timeline with 100 OwnedAnimation children × 120 Seek scrubs + TimelinePosition expressions
    do
        use host = new ManualFrameScheduler()
        let vals = Array.zeroCreate<float> 100
        let props = Array.init 100 (fun i -> NumericProperty((fun () -> vals[i]), (fun v -> vals[i] <- v)))
        use tl = new Timeline(host)
        tl.Label("phaseA", TimelinePosition.At 50.) |> ignore
        for i in 0 .. 99 do
            tl.Animate(props[i], float (i + 1) * 10., 200., position = TimelinePosition.At(float (i * 5)), from = 0., ease = id) |> ignore
        results.Add(
            measure "timeline-owned-100-scrub-120-seeks" 4 25 (fun () ->
                let mutable acc = 0.
                for f in 1 .. 120 do
                    let t = float ((f * 47) % 695)
                    tl.Seek t
                    acc <- acc + tl.ResolvePosition(TimelinePosition.Expression "phaseA+=25") + vals[f % 100]
                GC.KeepAlive acc)
        )

    // 19. Composed non-Blend multi-target Color + Complex string tracks (64 targets × 60 frames)
    do
        use host = new ManualFrameScheduler()
        let targets =
            Array.init 64 (fun _ ->
                box (
                    AnimatedObject [
                        "tint", AdaptedValue.Text "#ff2040"
                        "shadow", AdaptedValue.Text "0px 4px 12px rgba(10, 20, 30, 0.25)"
                    ]
                ))
        use anim =
            Composed.animateTargets
                host
                targets
                [| PropertySpec.Text("tint", "#2080ff")
                   PropertySpec.Text("shadow", "8px 16px 32px rgba(40, 80, 160, 0.85)") |]
                (Some { ComposedAnimationOptions.Default with Duration = 1000.; Ease = id; Autoplay = false })
        results.Add(
            measure "composed-nonblend-color-complex-64x60" 4 25 (fun () ->
                for f in 1 .. 60 do
                    anim.Seek(float (f * 16)) |> ignore)
        )

    // 20. ValueInterpolation.decompose + CssColor.parse × 4,000
    do
        results.Add(
            measure "decompose-and-csscolor-parse-4000" 4 25 (fun () ->
                let mutable acc = 0.
                for _ in 1 .. 4000 do
                    let u1 = ValueInterpolation.decompose (ValueInterpolation.Text "125.5px")
                    let u2 = ValueInterpolation.decompose (ValueInterpolation.Text "+=48deg")
                    let cx = ValueInterpolation.decompose (ValueInterpolation.Text "translate3d(12px, 24.5px, -8px) scale(1.25)")
                    let c1 = CssColor.parse "#3b82f6"
                    let c2 = CssColor.parse "rgba(255, 128, 64, 0.75)"
                    let c3 = CssColor.parse "hsl(210deg 80% 55% / 90%)"
                    let c4 = CssColor.parse "cornflowerblue"
                    acc <- acc + u1.Number + u2.Number + cx.Components[0] + c1[0] + c2[1] + c3[2] + c4[0]
                GC.KeepAlive acc)
        )

    // 21. Waapi.convertEase + Stagger 3D Grid (16×16×4 = 1,024 cells, Reversed) × 200
    do
        let elastic = Ease.named "outElastic"
        let staggerOpts =
            { StaggerOptions.Default with
                Grid = StaggerGrid.Grid3D(16u, 16u, 4u)
                Origin = StaggerOrigin.Center
                Reversed = true }
        results.Add(
            measure "waapi-convertease-and-stagger-grid3d-200" 4 25 (fun () ->
                let mutable len = 0
                for _ in 1 .. 200 do
                    let cssLinear = Waapi.convertEase elastic (Some 64)
                    let dists = Stagger.distribute (StaggerValue.Range(0., 500.)) staggerOpts 1024u
                    len <- len + cssLinear.Length + int dists[0]
                GC.KeepAlive len)
        )

    // 22. AutoLayout 64-node tree capture + Animate + 30 Timeline frames
    do
        use host = new ManualFrameScheduler()
        let nodeStyles = Array.init 65 (fun _ -> Dictionary<string, string>(StringComparer.Ordinal))
        let nodeAttrs = Array.init 65 (fun _ -> Dictionary<string, string>(StringComparer.Ordinal))
        let nodeBounds = Array.init 65 (fun i -> if i = 0 then (0., 0., 800., 600.) else (float ((i % 8) * 90), float ((i / 8) * 70), 80., 60.))
        let classes = HashSet<string>(StringComparer.Ordinal)
        let tokens =
            mkProxy<DOMTokenList> (fun m args ->
                match m.Name with
                | "add" ->
                    for s in unbox<string array> args[0] do classes.Add s |> ignore
                    null
                | "remove" ->
                    for s in unbox<string array> args[0] do classes.Remove s |> ignore
                    null
                | "contains" -> box (classes.Contains(unbox<string> args[0]))
                | _ -> null)
        let domNodes = Array.zeroCreate<HTMLElement> 65
        let compDecls = Array.zeroCreate<CSSStyleDeclaration> 65
        for idx in 0 .. 64 do
            let sDict = nodeStyles[idx]
            let aDict = nodeAttrs[idx]
            let inlineStyle =
                mkProxy<CSSStyleProperties> (fun m args ->
                    match m.Name with
                    | "getPropertyValue" ->
                        match sDict.TryGetValue(unbox<string> args[0]) with
                        | true, v -> box v
                        | _ -> box ""
                    | "getPropertyPriority" -> box ""
                    | "setProperty" ->
                        let v = unbox<string> args[1]
                        for leaf in CssPropertyMetadata.leafNamesReadOnly (unbox<string> args[0]) do
                            sDict[leaf] <- v
                        null
                    | "removeProperty" ->
                        for leaf in CssPropertyMetadata.leafNamesReadOnly (unbox<string> args[0]) do
                            sDict.Remove leaf |> ignore
                        box ""
                    | "get_length" -> box (uint32 sDict.Count)
                    | "item" ->
                        let targetIdx = int (unbox<uint32> args[0])
                        let mutable kIdx = 0
                        let mutable found = ""
                        for k in sDict.Keys do
                            if kIdx = targetIdx then found <- k
                            kIdx <- kIdx + 1
                        box found
                    | _ -> null)
            compDecls[idx] <-
                { new CSSStyleDeclaration with
                    member _.getPropertyValue name =
                        match sDict.TryGetValue name with
                        | true, v when v <> "" -> v
                        | _ ->
                            match name with
                            | "display" -> "block"
                            | "visibility" -> "visible"
                            | "position" -> "static"
                            | "transform" -> "none"
                            | "opacity" -> "1"
                            | "font-size" -> "16px"
                            | "color" -> "rgb(0, 0, 0)"
                            | "background-color" -> "rgba(0, 0, 0, 0)"
                            | "border-radius" -> "0px"
                            | "border" -> "0px"
                            | "filter" -> "none"
                            | "clip-path" -> "none"
                            | _ -> ""
                    member _.getPropertyPriority _ = ""
                    member _.setProperty(_, _, _) = ()
                    member _.removeProperty _ = ""
                    member _.item _ = ""
                    member _.length = 0u
                    member _.cssText with get () = "" and set _ = ()
                    member _.parentRule = Unchecked.defaultof<CSSRule>
                    member _.cssRules = Unchecked.defaultof<CSSRuleList> }
            domNodes[idx] <-
                mkProxy<HTMLElement> (fun m args ->
                    let l, t, w, h = nodeBounds[idx]
                    match m.Name with
                    | "get_firstChild" ->
                        if idx = 0 then box (domNodes[1] :> Node) else null
                    | "get_nextSibling" ->
                        if idx >= 1 && idx < 64 then box (domNodes[idx + 1] :> Node) else null
                    | "get_previousSibling" ->
                        if idx > 1 then box (domNodes[idx - 1] :> Node) else null
                    | "get_parentElement" ->
                        if idx >= 1 then box (domNodes[0] :> Element) else null
                    | "get_nodeType" -> box 1us
                    | "get_textContent" -> box ""
                    | "get_style" -> box inlineStyle
                    | "get_classList" -> box tokens
                    | "get_clientLeft" | "get_clientTop" -> box 0
                    | "getAttribute" ->
                        match aDict.TryGetValue(unbox<string> args[0]) with
                        | true, v -> box v
                        | _ -> null
                    | "setAttribute" ->
                        aDict[unbox<string> args[0]] <- unbox<string> args[1]
                        null
                    | "removeAttribute" ->
                        aDict.Remove(unbox<string> args[0]) |> ignore
                        null
                    | "isSameNode" -> box (Object.ReferenceEquals(domNodes[idx], args[0]))
                    | "contains" -> box (idx = 0 || Object.ReferenceEquals(domNodes[idx], args[0]))
                    | "matches" -> box true
                    | "getBoundingClientRect" ->
                        box (
                            mkProxy<DOMRect> (fun rm _ ->
                                match rm.Name with
                                | "get_left" | "get_x" -> box l
                                | "get_top" | "get_y" -> box t
                                | "get_width" -> box w
                                | "get_height" -> box h
                                | _ -> box 0.)
                        )
                    | _ -> null)
        let computedSvc =
            { new ComputedStyleService with
                member _.Get(el, _) =
                    let mutable found = compDecls[0]
                    for k = 0 to 64 do
                        if Object.ReferenceEquals(el, domNodes[k]) then found <- compDecls[k]
                    found }
        let mutable flipToggle = false
        results.Add(
            measure "autolayout-64-nodes-capture-and-30-frames" 3 15 (fun () ->
                use layout = Layout.create domNodes[0] computedSvc host None None
                flipToggle <- not flipToggle
                let shift = if flipToggle then 24. else 0.
                for k = 1 to 64 do
                    let l, t, w, h = nodeBounds[k]
                    nodeBounds[k] <- (l + (if flipToggle then 12. else -12.), t + shift * 0.25, w, h)
                let tl = layout.Animate()
                for f = 1 to 30 do
                    tl.Seek(float (f * 10)))
        )

    if argv.Length >= 1 then
        let outFile = argv[0]
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath outFile)) |> ignore
        let json = JsonSerializer.Serialize(results, JsonSerializerOptions(WriteIndented = true))
        File.WriteAllText(outFile, json)
        printfn "Wrote %d benchmark results to %s" results.Count outFile

    0
