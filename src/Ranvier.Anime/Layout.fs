namespace Ranvier.Anime

open System
open System.Collections.Generic
open System.Globalization
open Ranvier
open Ranvier.Dom
open Ranvier.Css
open Ranvier.Css.Dom

exception private LayoutConstructionInterrupted

/// <summary>Context for a layout timing function; indices count targets in the same transition group.</summary>
type LayoutContext =
    { Element: HTMLElement
      Index: uint
      Count: uint }

/// <summary>Per-element timing and typed declarations used at a layout state boundary.</summary>
[<NoEquality; NoComparison>]
type LayoutEffect =
    { Properties: Declaration array
      /// <summary>Resolves typed boundary values; names must be declared in the layout's captured Properties.</summary>
      PropertiesBy: (LayoutContext -> Declaration array) option
      Duration: float option
      DurationBy: (LayoutContext -> float) option
      Delay: float option
      DelayBy: (LayoutContext -> float) option
      Ease: (float -> float) option
      EaseBy: (LayoutContext -> float -> float) option }
    /// <summary>Fades the node at the boundary while inheriting the layout timing.</summary>
    static member Default =
        { Properties=[| Declaration.create "opacity" "0" |];PropertiesBy=None
          Duration=None;DurationBy=None;Delay=None;DelayBy=None;Ease=None;EaseBy=None }
    /// <summary>Replaces duration with unsigned whole milliseconds.</summary>
    member this.WithDuration(duration:uint)={this with Duration=Some(float duration);DurationBy=None}
    /// <summary>Uses the spring's curve and precise settling duration.</summary>
    member this.WithSpring(spring:Spring)={this with Duration=Some spring.SettlingDuration;DurationBy=None;Ease=Some spring.Ease;EaseBy=None}

/// <summary>Callbacks for the layout's shared playback controller.</summary>
[<NoEquality; NoComparison>]
type LayoutCallbacks =
    { OnBegin: Timeline -> unit
      OnBeforeUpdate: Timeline -> unit
      OnRender: Timeline -> unit
      OnUpdate: Timeline -> unit
      OnLoop: Timeline -> unit
      OnPause: Timeline -> unit
      OnComplete: Timeline -> unit }
    /// <summary>Silent playback callbacks.</summary>
    static member Default =
        { OnBegin=ignore;OnBeforeUpdate=ignore;OnRender=ignore;OnUpdate=ignore
          OnLoop=ignore;OnPause=ignore;OnComplete=ignore }

/// <summary>Layout timing, target selection and boundary effects. Values use precise milliseconds.</summary>
[<NoEquality; NoComparison>]
type LayoutOptions =
    { Playback: PlaybackOptions
      /// <summary>Remaps all timeline children and swaps; controller time stays raw and iteration time exposes the mapped value.</summary>
      PlaybackEase: (float -> float) option
      Callbacks: LayoutCallbacks
      Autoplay: bool
      /// <summary>Keeps layout declarations on completion for linked scroll playback.</summary>
      RetainLayoutOnComplete: bool
      Duration: LayoutContext -> float
      Delay: LayoutContext -> float
      Ease: float -> float
      EaseBy: (LayoutContext -> float -> float) option
      Children: string
      /// <summary>Explicit targets, including matching layout-id twins outside the root.</summary>
      Targets: HTMLElement array option
      /// <summary>Selector scope for non-wildcard children; defaults to the owning document.</summary>
      SelectionRoot: ParentNode option
      Properties: string array
      EnterFrom: LayoutEffect
      LeaveTo: LayoutEffect
      SwapAt: LayoutEffect }
    /// <summary>350 ms, no delay, source inOut(3.5), all descendants and source boundary fades.</summary>
    static member Default =
        { Playback=PlaybackOptions.Default;PlaybackEase=None;Callbacks=LayoutCallbacks.Default;Autoplay=true;RetainLayoutOnComplete=false
          Duration=(fun _ -> 350.); Delay=(fun _ -> 0.); Ease=Ease.create (EaseFamily.Power 3.5) EaseDirection.InOut;EaseBy=None
          Children="*";Targets=None;SelectionRoot=None
          Properties=[|"opacity";"font-size";"color";"background-color";"border-radius";"border";"filter";"clip-path"|]
          EnterFrom=LayoutEffect.Default; LeaveTo=LayoutEffect.Default
          SwapAt={LayoutEffect.Default with Ease=Some(Ease.create (EaseFamily.Power 1.75) EaseDirection.InOut)} }
    /// <summary>Replaces duration with unsigned whole milliseconds.</summary>
    member this.WithDuration(duration:uint) = {this with Duration=(fun _ -> float duration)}
    /// <summary>Replaces delay with unsigned whole milliseconds.</summary>
    member this.WithDelay(delay:uint) = {this with Delay=(fun _ -> float delay)}
    /// <summary>Uses the spring's curve and precise settling duration.</summary>
    member this.WithSpring(spring:Spring)={this with Duration=(fun _ -> spring.SettlingDuration);Ease=spring.Ease;EaseBy=None}

module private LayoutStyleKeyComparer =
    let instance =
        { new IEqualityComparer<struct(HTMLElement * string)> with
            member _.Equals(struct(a, pa), struct(b, pb)) =
                Object.ReferenceEquals(a, b) && StringComparer.Ordinal.Equals(pa, pb)
            member _.GetHashCode(struct(e, p)) =
                HashCode.Combine(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode e, StringComparer.Ordinal.GetHashCode p) }

module private LayoutConst =
    let isAnimatedTokens = [| "is-animated" |]
    let zeroOffsetProps = [| "left"; "top"; "margin-left"; "margin-top" |]
    let minSizeProps = [| "min-width"; "min-height" |]
    let maxSizeProps = [| "max-width"; "max-height" |]

    [<AbstractClass; Sealed>]
    type ScratchTls =
        [<ThreadStatic; DefaultValue>]
        static val mutable private chars: char array
        static member Get() =
            let mutable b = ScratchTls.chars
            if isNull b then
                b <- Array.zeroCreate<char> 128
                ScratchTls.chars <- b
            b

    let formatPx (value: float) : string =
        let buf = ScratchTls.Get()
        let mutable written = 0
        if value.TryFormat(Span<char>(buf, 0, 64), &written, "G17", CultureInfo.InvariantCulture) then
            buf[written] <- 'p'
            buf[written + 1] <- 'x'
            String(ReadOnlySpan<char>(buf, 0, written + 2))
        else
            value.ToString("G17", CultureInfo.InvariantCulture) + "px"

    let formatTranslatePx (x: float) (y: float) : string =
        let buf = ScratchTls.Get()
        let mutable w1 = 0
        let mutable w2 = 0
        if x.TryFormat(Span<char>(buf, 0, 56), &w1, "G17", CultureInfo.InvariantCulture) then
            buf[w1] <- 'p'
            buf[w1 + 1] <- 'x'
            buf[w1 + 2] <- ' '
            let p2 = w1 + 3
            if y.TryFormat(Span<char>(buf, p2, 56), &w2, "G17", CultureInfo.InvariantCulture) then
                buf[p2 + w2] <- 'p'
                buf[p2 + w2 + 1] <- 'x'
                String(ReadOnlySpan<char>(buf, 0, p2 + w2 + 2))
            else
                formatPx x + " " + formatPx y
        else
            formatPx x + " " + formatPx y

type private LayoutMeasurement =
    { Id: string
      Element: HTMLElement
      Measure: HTMLElement
      RenderHidden: bool
      Parent: HTMLElement option
      ParentId: string voption
      Left: float
      Top: float
      BorderLeft: float
      BorderTop: float
      X: float
      Y: float
      Width: float
      Height: float
      Hidden: bool
      Inline: bool
      Target: bool
      Display: string
      Position: string
      Values: string array }

type private LayoutTransformSampler(element:HTMLElement, values:string array, easing:string) =
    let mutable cancelled=false
    let frames=values |> Array.mapi(fun index value ->
        let property=Ranvier.Dom.KeyframeProperty(name="transform",value=value)
        Ranvier.Dom.Keyframe(offset=Some(float index/float(values.Length-1)),properties=[|property|]))
    let options=Ranvier.Dom.KeyframeAnimationOptions(
                    duration=UnrestrictedDoubleOrCSSNumericValueOrDOMString.UnrestrictedDouble 1.,
                    fill=FillMode.Both,easing=easing)
    let animation=element.animate(frames,options)
    do
        try animation.pause()
        with _ ->
            try animation.cancel() with _ -> ()
            reraise()
    member _.Sample(progress:float) =
        if not(Double.IsFinite progress) then invalidArg "progress" "Expected a finite interpolation progress."
        if not cancelled then
            animation.currentTime<-Some(DoubleOrCSSNumericValue.Double progress)
    member _.Element=element
    member _.Commit()=if not cancelled then animation.commitStyles()
    member _.Cancel()=
        if not cancelled then
            cancelled<-true
            animation.cancel()

type private LayoutStyleWrite =
    { Element: HTMLElement
      Style: CSSStyleDeclaration
      Name: string
      Snapshot: LayoutStyleSnapshot }

/// <summary>Captures native DOM layout and animates mutations using the shared owned playback clock.</summary>
/// <remarks>Call on the host thread. Revert restores owned inline declarations, retaining DOM mutations and concurrent edits.</remarks>
// Layout behavior derives from Anime.js v4.5.0, copyright (c) 2025 Julian Garnier; see LICENSE-AnimeJS.
type AutoLayout(root:HTMLElement, computed:ComputedStyleService, scheduler:FrameScheduler,
                ?options:LayoutOptions, ?viewport:ViewportScrollService) as this =
    let options=defaultArg options LayoutOptions.Default
    let ownerLive=ScopeRegistration.captureWriteGuard()
    let styles=ResizeArray<LayoutStyleWrite>()
    let styleMap=Dictionary<struct(HTMLElement * string), LayoutStyleWrite>(LayoutStyleKeyComparer.instance)
    let knownStyleElements=HashSet<HTMLElement>(HashIdentity.Reference)
    let styleElementAliases=Dictionary<HTMLElement, HTMLElement>(HashIdentity.Reference)
    let transforms=ResizeArray<LayoutTransformSampler>()
    let deferred=ResizeArray<IDisposable>()
    let identities=ResizeArray<HTMLElement*string>()
    let identityPrefix=Guid.NewGuid().ToString("N")
    let mutable nextIdentity=0u
    let mutable clock:Timeline option=None
    let mutable driver:NumericAnimation option=None
    let mutable disposed=false
    let mutable generation=0L
    let mutable measurements:LayoutMeasurement array=[||]
    let mutable scroll:ScrollPosition option=None
    let mutable entering:HTMLElement array=[||]
    let mutable leaving:HTMLElement array=[||]
    let mutable animating:HTMLElement array=[||]
    let mutable swapping:HTMLElement array=[||]
    let finite name value =
        if not(Double.IsFinite value) || value<0. then invalidArg name "Expected finite nonnegative milliseconds."
    let inlineStyle (e:HTMLElement)=(e:>ElementCSSInlineStyle).style
    let identity (e:HTMLElement) =
        let existing=e.getAttribute "data-layout-id"
        if not(String.IsNullOrWhiteSpace existing) then existing
        else
            let value="ranvier-layout-"+identityPrefix+"-"+nextIdentity.ToString(CultureInfo.InvariantCulture)
            nextIdentity<-nextIdentity+1u
            e.setAttribute("data-layout-id",value)
            identities.Add(e,value)
            value
    let same (a:HTMLElement) (b:HTMLElement)=Object.ReferenceEquals(a,b) || (a:>Node).isSameNode(b:>Node)
    let canonicalStyleElement (e:HTMLElement) =
        if knownStyleElements.Contains e then e
        else
            let mutable alias = Unchecked.defaultof<HTMLElement>
            if styleElementAliases.TryGetValue(e, &alias) then alias
            else
                let mutable found = Unchecked.defaultof<HTMLElement>
                for existing in knownStyleElements do
                    if isNull (box found) && (existing :> Node).isSameNode(e :> Node) then
                        found <- existing
                if not (isNull (box found)) then
                    styleElementAliases[e] <- found
                    found
                else
                    knownStyleElements.Add e |> ignore
                    e
    let check()=if disposed || not(ownerLive()) then raise(ObjectDisposedException(nameof AutoLayout))
    let text (value:float) = value.ToString("G17",CultureInfo.InvariantCulture)
    let px value = LayoutConst.formatPx value
    let captureStyle (e:HTMLElement) name =
        let style=inlineStyle e
        {Element=e;Style=style;Name=name;Snapshot=LayoutStyleSnapshot(style,name)}
    let restoreOne (entry:LayoutStyleWrite) =
        entry.Snapshot.Restore()
    let restoreAllStyles () =
        if styles.Count > 0 then
            let mutable failure:exn option=None
            let count = styles.Count
            for i = count - 1 downto 0 do
                try restoreOne styles[i]
                with error -> if failure.IsNone then failure <- Some error
            styles.Clear()
            styleMap.Clear()
            knownStyleElements.Clear()
            styleElementAliases.Clear()
            failure |> Option.iter(fun error -> System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw())
    let restoreWhere predicate =
        if styles.Count > 0 then
            let mutable failure:exn option=None
            let entries=styles.ToArray()
            styles.Clear()
            styleMap.Clear()
            knownStyleElements.Clear()
            styleElementAliases.Clear()
            for i = 0 to entries.Length - 1 do
                let entry = entries[i]
                if not(predicate entry) then
                    styles.Add entry
                    let canon = canonicalStyleElement entry.Element
                    styleMap[struct(canon, entry.Name)] <- entry
            for i=entries.Length-1 downto 0 do
                try if predicate entries[i] then restoreOne entries[i]
                with error -> if failure.IsNone then failure<-Some error
            failure |> Option.iter(fun error -> System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw())
    let restore()=restoreAllStyles()
    let getOrCaptureStyle (e:HTMLElement) (name:string) : LayoutStyleWrite =
        let canon = canonicalStyleElement e
        let key = struct(canon, name)
        let mutable existing = Unchecked.defaultof<LayoutStyleWrite>
        if styleMap.TryGetValue(key, &existing) then existing
        else
            let created = captureStyle canon name
            styles.Add created
            styleMap[key] <- created
            created
    let write (e:HTMLElement) name value priority =
        check()
        let expected=generation
        let entry = getOrCaptureStyle e name
        entry.Style.setProperty(name,value,priority)
        if expected<>generation || disposed || not(ownerLive()) then raise LayoutConstructionInterrupted
        entry.Snapshot.MarkWritten()
    let track (e:HTMLElement) name : LayoutStyleWrite =
        getOrCaptureStyle e name
    let updateLast (e:HTMLElement) name =
        let canon = canonicalStyleElement e
        let mutable entry = Unchecked.defaultof<LayoutStyleWrite>
        if styleMap.TryGetValue(struct(canon, name), &entry) then
            entry.Snapshot.MarkWritten()
    let stop() =
        generation<-generation+1L
        clock |> Option.iter(fun c -> c.Cancel())
        clock<-None
        driver |> Option.iter(fun c -> c.Cancel())
        driver<-None
        for transform in transforms do transform.Cancel()
        transforms.Clear()
        for job in deferred do job.Dispose()
        deferred.Clear()
    let props=
        Array.concat [options.Properties;[|"transform"|]
                      options.EnterFrom.Properties |> Array.map Declaration.name
                      options.LeaveTo.Properties |> Array.map Declaration.name
                      options.SwapAt.Properties |> Array.map Declaration.name] |> Array.distinct
    let propIndexMap =
        let d = Dictionary<string, int>(props.Length, StringComparer.Ordinal)
        for i = 0 to props.Length - 1 do d[props[i]] <- i
        d
    let isAdjacentText (e:HTMLElement) =
        let rec significant (node:Node) direction =
            if isNull(box node) then false
            elif node.nodeType=8us || (node.nodeType=3us && String.IsNullOrWhiteSpace node.textContent) then
                significant (direction node) direction
            else node.nodeType=3us
        significant (e:>Node).previousSibling (fun n -> n.previousSibling) ||
        significant (e:>Node).nextSibling (fun n -> n.nextSibling)
    let capture() =
        let est = max 16 measurements.Length
        let result=ResizeArray<LayoutMeasurement>(est)
        let temporary=ResizeArray<LayoutStyleWrite>(est * 2)
        let mute (e:HTMLElement) name value =
            let style=inlineStyle e
            let entry=captureStyle e name
            temporary.Add entry
            style.setProperty(name,value,"important")
            entry.Snapshot.MarkWritten()
        let restoreTemporary() =
            for i=temporary.Count-1 downto 0 do
                restoreOne temporary[i]
        try
            let mutable ancestor=(root:>Node).parentElement
            while not(isNull(box ancestor)) do
                match ancestor with
                | :? HTMLElement as html when (computed.Get(html,None)).getPropertyValue "transform"<>"none" ->
                    mute html "transition" "none"
                    mute html "transform" "none"
                | _ -> ()
                ancestor<-(ancestor:>Node).parentElement
            let rootStyle=computed.Get(root,None)
            check()
            let absolute=let p=rootStyle.getPropertyValue "position" in p="absolute" || p="fixed"
            let isWildcard = options.Children = "*"
            let propLen = props.Length
            let rec walk (e:HTMLElement) (parent:LayoutMeasurement option) =
                let css=computed.Get(e,None)
                check()
                let values = Array.zeroCreate<string> propLen
                for idx = 0 to propLen - 1 do
                    values[idx] <- css.getPropertyValue props[idx]
                let display=css.getPropertyValue "display"
                let hidden=display="none" || css.getPropertyValue "visibility"="hidden" || (parent |> Option.exists(fun p -> p.Hidden))
                mute e "transition" "none"
                if css.getPropertyValue "transform"<>"none" then mute e "transform" "none"
                let rect=e.getBoundingClientRect()
                check()
                let left,top,width,height=if hidden then 0.,0.,0.,0. else rect.left,rect.top,rect.width,rect.height
                let x,y=
                    match parent with
                    | None when same root e -> if absolute then left,top else 0.,0.
                    | None ->
                        let p=result[0]
                        left-p.Left-p.BorderLeft,top-p.Top-p.BorderTop
                    | Some p -> left-p.Left-p.BorderLeft,top-p.Top-p.BorderTop
                let parentOpt, parentIdOpt =
                    match parent with
                    | Some p -> Some p.Element, ValueSome p.Id
                    | None -> None, ValueNone
                let current=
                    { Id=identity e;Element=e;Measure=e;RenderHidden=hidden
                      Parent=parentOpt;ParentId=parentIdOpt;Left=left;Top=top;X=x;Y=y
                      BorderLeft=float e.clientLeft;BorderTop=float e.clientTop;Width=width;Height=height
                      Hidden=hidden;Inline=isAdjacentText e;Target=isWildcard || same root e || e.matches options.Children
                      Display=display;Position=css.getPropertyValue "position";Values=values }
                result.Add current
                let mutable child=(e:>Node).firstChild
                while not(isNull(box child)) do
                    match child with
                    | :? HTMLElement as html -> walk html (Some current)
                    | _ -> ()
                    child<-child.nextSibling
            walk root None
            if options.Targets.IsNone && isWildcard then
                // Wildcard selection with no external targets: every walked in-root node is selected,
                // unless duplicate data-layout-id attributes exist inside the subtree.
                let firstIdxById = Dictionary<string, int>(result.Count, StringComparer.Ordinal)
                let mutable hasDuplicateId = false
                for i = 0 to result.Count - 1 do
                    let id = result[i].Id
                    if not (firstIdxById.TryAdd(id, i)) then hasDuplicateId <- true
                if not hasDuplicateId then
                    let arr = Array.zeroCreate<LayoutMeasurement> result.Count
                    for i = 0 to result.Count - 1 do
                        arr[i] <- { result[i] with Target = true }
                    arr
                else
                    firstIdxById.Clear()
                    let firsts = ResizeArray<LayoutMeasurement>(result.Count)
                    let visibles = ResizeArray<LayoutMeasurement>(result.Count)
                    for i = 0 to result.Count - 1 do
                        let m = result[i]
                        let mutable gIdx = 0
                        if firstIdxById.TryGetValue(m.Id, &gIdx) then
                            if visibles[gIdx].Hidden && not m.Hidden then visibles[gIdx] <- m
                        else
                            firstIdxById[m.Id] <- firsts.Count
                            firsts.Add m
                            visibles.Add m
                    let mergedById = Dictionary<string, LayoutMeasurement>(firsts.Count, StringComparer.Ordinal)
                    let outArr = Array.zeroCreate<LayoutMeasurement> firsts.Count
                    for g = 0 to firsts.Count - 1 do
                        let first = firsts[g]
                        let measured = visibles[g]
                        let chosen =
                            if first.Hidden && not measured.Hidden then
                                let parentMeas =
                                    match first.ParentId with
                                    | ValueSome pid ->
                                        let mutable pm = Unchecked.defaultof<LayoutMeasurement>
                                        if mergedById.TryGetValue(pid, &pm) then Some pm
                                        else
                                            match first.Parent with
                                            | Some pEl -> result |> Seq.tryFind(fun r -> same pEl r.Element)
                                            | None -> None
                                    | ValueNone -> None
                                let x, y =
                                    match parentMeas with
                                    | Some p -> measured.Left-p.Left-p.BorderLeft, measured.Top-p.Top-p.BorderTop
                                    | None -> measured.X, measured.Y
                                { measured with Element=first.Element; Measure=measured.Element; RenderHidden=true; Parent=first.Parent; ParentId=first.ParentId; X=x; Y=y }
                            else first
                        let node = { chosen with Target = true }
                        mergedById[node.Id] <- node
                        outArr[g] <- node
                    outArr
            else
                let selected=
                    match options.Targets with
                    | Some targets -> Array.copy targets
                    | None ->
                        let scope=defaultArg options.SelectionRoot ((root:>Node).ownerDocument:>ParentNode)
                        let matches=scope.querySelectorAll options.Children
                        [| for index=0 to int matches.length-1 do
                               match matches.item(uint index) with :? HTMLElement as html -> yield html | _ -> () |]
                let inRootIds=HashSet<string>(result.Count, StringComparer.Ordinal)
                for i = 0 to result.Count - 1 do inRootIds.Add result[i].Id |> ignore
                for candidate in selected do
                    let candId = candidate.getAttribute "data-layout-id"
                    if not (isNull candId) && inRootIds.Contains candId && not((root:>Node).contains(candidate:>Node)) then
                        walk candidate None
                let selectedIds=HashSet<string>(selected.Length, StringComparer.Ordinal)
                for candidate in selected do
                    let cid = candidate.getAttribute "data-layout-id"
                    if not (isNull cid) then selectedIds.Add cid |> ignore
                let firstIdxById = Dictionary<string, int>(result.Count, StringComparer.Ordinal)
                let firsts = ResizeArray<LayoutMeasurement>(result.Count)
                let visibles = ResizeArray<LayoutMeasurement>(result.Count)
                for i = 0 to result.Count - 1 do
                    let m = result[i]
                    let mutable gIdx = 0
                    if firstIdxById.TryGetValue(m.Id, &gIdx) then
                        if visibles[gIdx].Hidden && not m.Hidden then visibles[gIdx] <- m
                    else
                        firstIdxById[m.Id] <- firsts.Count
                        firsts.Add m
                        visibles.Add m
                let mergedById = Dictionary<string, LayoutMeasurement>(firsts.Count, StringComparer.Ordinal)
                let nodes = Array.zeroCreate<LayoutMeasurement> firsts.Count
                for g = 0 to firsts.Count - 1 do
                    let first = firsts[g]
                    let measured = visibles[g]
                    let chosen =
                        if first.Hidden && not measured.Hidden then
                            let parentMeas =
                                match first.ParentId with
                                | ValueSome pid ->
                                    let mutable pm = Unchecked.defaultof<LayoutMeasurement>
                                    if mergedById.TryGetValue(pid, &pm) then Some pm
                                    else
                                        match first.Parent with
                                        | Some pEl -> result |> Seq.tryFind(fun r -> same pEl r.Element)
                                        | None -> None
                                | ValueNone -> None
                            let x,y =
                                match parentMeas with
                                | Some p -> measured.Left-p.Left-p.BorderLeft,measured.Top-p.Top-p.BorderTop
                                | None -> measured.X,measured.Y
                            {measured with Element=first.Element;Measure=measured.Element;RenderHidden=true;Parent=first.Parent;ParentId=first.ParentId;X=x;Y=y}
                        else first
                    let node={chosen with Target=same root chosen.Element || selectedIds.Contains chosen.Id}
                    mergedById[node.Id] <- node
                    nodes[g] <- node
                // Selected descendants animate their ancestor chain.
                let nodeRefSet = HashSet<HTMLElement>(nodes.Length, HashIdentity.Reference)
                for i = 0 to nodes.Length - 1 do nodeRefSet.Add nodes[i].Element |> ignore
                let targetRefSet = HashSet<HTMLElement>(nodes.Length, HashIdentity.Reference)
                let extraTargets = ResizeArray<HTMLElement>()
                for i = 0 to nodes.Length - 1 do
                    let m = nodes[i]
                    if m.Target then
                        let mutable current:Element=m.Element
                        while not(isNull(box current)) && (root:>Node).contains(current:>Node) do
                            match current with
                            | :? HTMLElement as h ->
                                if targetRefSet.Add h && not (nodeRefSet.Contains h) then
                                    extraTargets.Add h
                            | _ -> ()
                            current<-(current:>Node).parentElement
                for i = 0 to nodes.Length - 1 do
                    let m = nodes[i]
                    let isTgt =
                        targetRefSet.Contains m.Element ||
                        (extraTargets.Count > 0 && extraTargets |> Seq.exists(same m.Element))
                    if m.Target <> isTgt then
                        nodes[i] <- { m with Target = isTgt }
                nodes
        finally restoreTemporary()
    let finish deferTransitions =
        for transform in transforms do transform.Cancel()
        transforms.Clear()
        try restoreWhere(fun entry -> not deferTransitions || entry.Name<>"transition")
        finally root.classList.remove LayoutConst.isAnimatedTokens
        if deferTransitions && styles.Count>0 then
            let expected=generation
            let job=scheduler.Frame(fun _ ->
                if not disposed && ownerLive() && expected=generation then restore())
            deferred.Add job
    do
        if isNull(box root) then nullArg "root"
        if String.IsNullOrWhiteSpace options.Children then invalidArg "options" "A children selector is required."
        measurements<-capture()
        scroll<-viewport |> Option.map(fun v -> v.Position())
        ScopeRegistration.register this.Revert
    /// <summary>Root HTML element measured by this layout.</summary>
    member _.Root=root
    /// <summary>Targets newly shown by the latest mutation.</summary>
    member _.Entering=Array.copy entering
    /// <summary>Targets hidden by the latest mutation.</summary>
    member _.Leaving=Array.copy leaving
    /// <summary>Existing targets participating in the latest mutation.</summary>
    member _.Animating=Array.copy animating
    /// <summary>Unselected descendants that swap geometry at half duration.</summary>
    member _.Swapping=Array.copy swapping
    /// <summary>Latest playback controller, if a transition has been created.</summary>
    member _.Animation=clock
    /// <summary>Captures the current visual state, cancels playback and releases temporary declarations.</summary>
    member _.Record() =
        check()
        let current=capture()
        stop()
        restore()
        root.classList.remove LayoutConst.isAnimatedTokens
        measurements<-current
        scroll<-viewport |> Option.map(fun v -> v.Position())
    /// <summary>Moves the recorded box of <c>element</c> to the viewport position <c>left</c>, <c>top</c>, keeping its recorded size.</summary>
    /// <remarks>Starts an element's next transition from its visual box rather than its laid-out box. Call after <c>Record</c> and before <c>Animate</c>.</remarks>
    member _.StartFrom(element:HTMLElement, left:float, top:float) =
        measurements <-
            measurements |> Array.map(fun m ->
                if same m.Element element then { m with X=m.X+left-m.Left; Y=m.Y+top-m.Top; Left=left; Top=top } else m)
    /// <summary>Recaptures the current layout without starting a transition.</summary>
    member _.Refresh()=this.Record()
    /// <summary>Animates from the recorded state to the current DOM and returns paused-capable playback.</summary>
    /// <remarks>Animation overrides affect timing, effects and playback; selection and captured names stay fixed.
    /// Transform changes use native keyframe effects and require CSS linear easing support.</remarks>
    member _.Animate(?animationOptions:LayoutOptions) =
        check()
        stop()
        restore()
        let activeProps =
            match animationOptions with
            | Some overrideOpts -> overrideOpts.Properties |> Array.filter propIndexMap.ContainsKey
            | None -> props
        let activePropIndices =
            if Object.ReferenceEquals(activeProps, props) then
                Array.init props.Length id
            else
                activeProps |> Array.map (fun p -> propIndexMap[p])
        let options=defaultArg animationOptions options
        let expected=generation
        let mutable constructing = true
        let live()=not disposed && ownerLive() && expected=generation
        let ensureLive()=if not(live()) then raise LayoutConstructionInterrupted
        let fresh=capture()
        let oldById = Dictionary<string, LayoutMeasurement>(measurements.Length, StringComparer.Ordinal)
        for i = 0 to measurements.Length - 1 do
            let m = measurements[i]
            oldById[m.Id] <- m
        let freshById = Dictionary<string, LayoutMeasurement>(fresh.Length, StringComparer.Ordinal)
        for i = 0 to fresh.Length - 1 do
            let m = fresh[i]
            freshById[m.Id] <- m
        let tryFindById (id:string) (map:Dictionary<string, LayoutMeasurement>) =
            if isNull id then ValueNone
            else
                let mutable found = Unchecked.defaultof<LayoutMeasurement>
                if map.TryGetValue(id, &found) then ValueSome found else ValueNone
        let anyChanges =
            let mutable changed = false
            let mutable fi = 0
            while fi < fresh.Length && not changed do
                let m = fresh[fi]
                match tryFindById m.Id oldById with
                | ValueNone -> changed <- true
                | ValueSome previous ->
                    if previous.Width<>m.Width || previous.Height<>m.Height || previous.X<>m.X || previous.Y<>m.Y ||
                       previous.Hidden<>m.Hidden then
                        changed <- true
                    else
                        let mutable pi = 0
                        while pi < activePropIndices.Length && not changed do
                            let idx = activePropIndices[pi]
                            if not (String.Equals(previous.Values[idx], m.Values[idx], StringComparison.Ordinal)) then
                                changed <- true
                            pi <- pi + 1
                fi <- fi + 1
            changed
        let enter=ResizeArray<HTMLElement>()
        let leave=ResizeArray<HTMLElement>()
        let animate=ResizeArray<HTMLElement>()
        let swap=ResizeArray<HTMLElement>()
        let added=HashSet<HTMLElement>(HashIdentity.Reference)
        let removed=HashSet<HTMLElement>(HashIdentity.Reference)
        let mutable duration=0.
        let renders=ResizeArray<float -> unit>()
        let swaps=ResizeArray<float*(unit -> unit)>()
        let defaultModifier = AnimationDefaults.modifier PropertyNumbers.identityFloat
        let defaultPrecision =
            match EngineDefaults.precision() with
            | None -> -1
            | Some v when v <= 308u -> int v
            | _ -> invalidArg "options" "Precision exceeds the finite decimal scale."
        let defaultReversed = (PlaybackDefaults.current()).Reversed
        let timeScale = (EngineFrames.forScheduler scheduler).TimeScale
        let nativeSampler (m:LayoutMeasurement) values length ease =
            track m.Element "transform" |> ignore
            let sb = System.Text.StringBuilder(900)
            sb.Append("linear(") |> ignore
            for index = 0 to 100 do
                if index > 0 then sb.Append(", ") |> ignore
                let value=ease(float index/100.)
                ensureLive()
                if not(Double.IsFinite value) then invalidArg "ease" "Expected a finite easing result."
                sb.Append(text(Math.Floor(value*10000.+0.5)/10000.)) |> ignore
            sb.Append(')') |> ignore
            let transform=new LayoutTransformSampler(m.Element,values,sb.ToString())
            if not(live()) then
                transform.Cancel()
                raise LayoutConstructionInterrupted
            transforms.Add transform
            fun time ->
                if live() then
                    let normalized=if time<0. then 0. elif length=0. then 1. else Math.Clamp(time/length,0.,1.)
                    transform.Sample normalized
        let propertySampler (m:LayoutMeasurement) name first last length ease =
            if first=last then ignore
            elif name="transform" then nativeSampler m [|first;last|] length ease
            else
                let entry = track m.Element name
                let style = entry.Style
                let dFirst = Declaration.create name first
                let dLast = Declaration.create name last
                let iterDuration = PropertyNumbers.round 1e12 (if length = 0. then 1e-11 else length)
                if iterDuration = 0. then invalidArg "duration" "Positive duration rounds to zero at source timing precision."
                ensureLive()
                let origPrio = style.getPropertyPriority name
                ensureLive()
                let origVal = style.getPropertyValue name
                ensureLive()
                let priority =
                    if Declaration.isImportant dFirst || Declaration.isImportant dLast || origPrio = "important" then "important"
                    else ""
                let normFirst, normLast =
                    ValueInterpolation.normalize None (ValueInterpolation.Text origVal)
                        (ValueInterpolation.Text (Declaration.value dFirst))
                        (ValueInterpolation.Text (Declaration.value dLast))
                let isScalar = normFirst.Kind = ValueInterpolation.ValueKind.Number || normFirst.Kind = ValueInterpolation.ValueKind.Unit
                let unitSuffix = if normLast.Kind = ValueInterpolation.ValueKind.Unit then normLast.Unit else ""
                let compBuf = if isScalar then Array.Empty<float>() else Array.zeroCreate<float> normLast.Components.Length
                let applyAt (seekTime: float) =
                    let clamped = Math.Clamp(seekTime, 0., iterDuration)
                    let dirTime = if defaultReversed then iterDuration - clamped else clamped
                    let progress = ease (dirTime / iterDuration)
                    if constructing then ensureLive()
                    if live() then
                        if not (Double.IsFinite progress) then invalidArg "progress" "Expected finite input."
                        if isScalar then
                            let struct(ok, scalar) =
                                ValueInterpolation.trySampleScalarValue(
                                    true, normFirst.Kind, normFirst.Number, normLast.Number, progress, defaultPrecision, defaultModifier, live)
                            if ok && live() then
                                style.setProperty(name, ValueInterpolation.formatNumberWithUnit scalar unitSuffix, priority = priority)
                                if live() then entry.Snapshot.MarkWritten()
                        else
                            if ValueInterpolation.sampleComponentsInto(true, normFirst, normLast, progress, defaultPrecision, defaultModifier, live, compBuf) &&
                               live() then
                                style.setProperty(name, ValueInterpolation.formatComponentsText normLast compBuf false, priority = priority)
                                if live() then entry.Snapshot.MarkWritten()
                applyAt 0.
                if not(live()) then raise LayoutConstructionInterrupted
                fun time ->
                    if live() then
                        let seekTime = if time < 0. then 0. elif length = 0. then iterDuration else Math.Clamp(time, 0., length)
                        applyAt seekTime
        let addProperty m name first last delay length ease =
            if first<>last then
                let render=propertySampler m name first last length ease
                duration<-max duration (delay+length)
                renders.Add(fun time -> render(time-delay))
        try
            let groupIndices = Array.zeroCreate<int> fresh.Length
            for fi = 0 to fresh.Length - 1 do
                let m = fresh[fi]
                let oldOpt = tryFindById m.Id oldById
                let wasHidden = match oldOpt with ValueSome o -> o.Hidden | ValueNone -> true
                let parentAdded = match m.Parent with Some p -> added.Contains p | None -> false
                let parentRemoved = match m.Parent with Some p -> removed.Contains p | None -> false
                let isEntering = not m.Hidden && wasHidden && (not parentAdded || oldOpt.IsSome)
                let isLeaving = m.Hidden && not wasHidden && not parentRemoved
                if isEntering || parentAdded then added.Add m.Element |> ignore
                if isLeaving || parentRemoved then removed.Add m.Element |> ignore
                if m.Target then
                    if isEntering then
                        groupIndices[fi] <- enter.Count
                        enter.Add m.Element
                    elif isLeaving then
                        groupIndices[fi] <- leave.Count
                        leave.Add m.Element
                    elif not(same root m.Element) then
                        groupIndices[fi] <- animate.Count
                        animate.Add m.Element
                    else
                        groupIndices[fi] <- 0
                else
                    groupIndices[fi] <- swap.Count
                    swap.Add m.Element
            added.Clear()
            removed.Clear()
            let plans=ResizeArray<_>(fresh.Length)
            for fi = 0 to fresh.Length - 1 do
                let m = fresh[fi]
                let oldOpt = tryFindById m.Id oldById
                let previous = match oldOpt with ValueSome o -> o | ValueNone -> m
                let parentAdded = match m.Parent with Some p -> added.Contains p | None -> false
                let parentRemoved = match m.Parent with Some p -> removed.Contains p | None -> false
                let wasHidden = match oldOpt with ValueSome o -> o.Hidden | ValueNone -> true
                let isEntering = not m.Hidden && wasHidden && (not parentAdded || oldOpt.IsSome)
                let isLeaving = m.Hidden && not wasHidden && not parentRemoved
                if parentAdded || isEntering then added.Add m.Element |> ignore
                if parentRemoved || isLeaving then removed.Add m.Element |> ignore
                let effect=
                    if not m.Target then Some options.SwapAt
                    elif isEntering then Some options.EnterFrom
                    elif isLeaving then Some options.LeaveTo
                    else None
                let group=if not m.Target then swap elif isEntering then enter elif isLeaving then leave else animate
                let index = groupIndices[fi]
                let context={Element=m.Element;Index=uint index;Count=uint group.Count}
                let delay=effect |> Option.bind(fun e -> e.DelayBy |> Option.map(fun resolve -> resolve context) |> Option.orElse e.Delay)
                          |> Option.defaultWith(fun () -> options.Delay context)
                ensureLive()
                let length=effect |> Option.bind(fun e -> e.DurationBy |> Option.map(fun resolve -> resolve context) |> Option.orElse e.Duration)
                           |> Option.defaultWith(fun () -> options.Duration context)
                ensureLive()
                let ease=effect |> Option.bind(fun e -> e.EaseBy |> Option.map(fun resolve -> resolve context) |> Option.orElse e.Ease)
                         |> Option.defaultWith(fun () -> options.EaseBy |> Option.map(fun resolve -> resolve context) |> Option.defaultValue options.Ease)
                ensureLive()
                let boundaryProperties=effect |> Option.map(fun e ->
                    e.PropertiesBy |> Option.map(fun resolve -> resolve context) |> Option.defaultValue e.Properties) |> Option.defaultValue [||]
                ensureLive()
                for declaration in boundaryProperties do
                    if not(propIndexMap.ContainsKey (Declaration.name declaration)) then
                        invalidArg "animationOptions" "Boundary property providers must use names declared by the layout's captured Properties."
                finite "delay" delay
                finite "duration" length
                plans.Add(m,previous,parentRemoved,wasHidden,isEntering,isLeaving,boundaryProperties,delay * timeScale,length * timeScale,ease)
            let rootPosFixed = fresh[0].Position="absolute" || fresh[0].Position="fixed"
            let childPositionVal = if rootPosFixed then "fixed" else "absolute"
            for m,previous,parentRemoved,wasHidden,isEntering,isLeaving,boundaryProperties,delay,length,ease in plans do
                let mutable nodeSwaps : ResizeArray<unit -> unit> = null
                let inline addNodeSwap action =
                    if isNull nodeSwaps then nodeSwaps <- ResizeArray<unit -> unit>(4)
                    nodeSwaps.Add action
                let before=if wasHidden then m else previous
                let after=if isLeaving || parentRemoved then previous else m
                let sameParent =
                    match before.Parent, m.Parent with
                    | None, None -> true
                    | Some a, Some b -> same a b
                    | _ -> false
                let x,y=
                    if not sameParent && not wasHidden then
                        let pOpt =
                            match m.ParentId with
                            | ValueSome pid ->
                                match tryFindById pid oldById with
                                | ValueSome pm -> ValueSome pm
                                | ValueNone -> tryFindById pid freshById
                            | ValueNone ->
                                match m.Parent with
                                | Some p ->
                                    let pid = p.getAttribute "data-layout-id"
                                    match tryFindById pid oldById with
                                    | ValueSome pm -> ValueSome pm
                                    | ValueNone -> tryFindById pid freshById
                                | None -> ValueNone
                        match pOpt with ValueSome p -> before.Left-p.Left-p.BorderLeft,before.Top-p.Top-p.BorderTop | ValueNone -> before.X,before.Y
                    else before.X,before.Y
                let wFirst = px before.Width
                let wLast = if before.Width = after.Width then wFirst else px after.Width
                let hFirst = px before.Height
                let hLast = if before.Height = after.Height then hFirst else px after.Height
                let changed=abs(before.Width-after.Width)>1. || abs(before.Height-after.Height)>1.
                let shouldFadeSwap=
                    changed &&
                    (let parentFreshOpt =
                        match m.ParentId with
                        | ValueSome pid -> tryFindById pid freshById
                        | ValueNone ->
                            match m.Parent with
                            | Some parent -> tryFindById (parent.getAttribute "data-layout-id") freshById
                            | None -> ValueNone
                     match parentFreshOpt with
                     | ValueSome parent when parent.Target ->
                         match tryFindById parent.Id oldById with
                         | ValueSome old -> abs(parent.Width-old.Width)>1. || abs(parent.Height-old.Height)>1.
                         | ValueNone -> false
                     | _ -> false)
                if anyChanges || not m.Target then
                    write m.Element "transition" "none" "important"
                    if m.RenderHidden && not(same m.Element m.Measure) then
                        write m.Element "display" before.Display ""
                        write m.Element "visibility" "visible" ""
                        write m.Measure "visibility" "hidden" ""
                    if isLeaving then
                        write m.Element "display" before.Display ""
                        write m.Element "visibility" "visible" ""
                    if not m.Inline then
                        if before.Display="grid" || m.Display="grid" then write m.Element "display" "block" "important"
                        if not(same root m.Element) || (m.Position="absolute" || m.Position="fixed") then
                            write m.Element "position" childPositionVal ""
                            for k = 0 to LayoutConst.zeroOffsetProps.Length - 1 do
                                write m.Element LayoutConst.zeroOffsetProps[k] "0px" ""
                        elif m.Position="static" then
                            write m.Element "position" "relative" ""
                            write m.Element "left" "0px" ""
                            write m.Element "top" "0px" ""
                    for k = 0 to LayoutConst.minSizeProps.Length - 1 do
                        write m.Element LayoutConst.minSizeProps[k] "auto" ""
                    for k = 0 to LayoutConst.maxSizeProps.Length - 1 do
                        write m.Element LayoutConst.maxSizeProps[k] "none" ""
                    write m.Element "width" wFirst ""
                    if m.Target then addProperty m "width" wFirst wLast delay length ease
                    else addNodeSwap(fun () -> if live() then write m.Element "width" wLast "")
                    write m.Element "height" hFirst ""
                    if m.Target then addProperty m "height" hFirst hLast delay length ease
                    else addNodeSwap(fun () -> if live() then write m.Element "height" hLast "")
                    let hasActivePropDiff () =
                        let mutable diff = false
                        let mutable pi = 0
                        while pi < activePropIndices.Length && not diff do
                            let idx = activePropIndices[pi]
                            if not (String.Equals(before.Values[idx], after.Values[idx], StringComparison.Ordinal)) then
                                diff <- true
                            pi <- pi + 1
                        diff
                    if not m.Inline && (not(same root m.Element) || rootPosFixed ||
                                       wFirst <> wLast || hFirst <> hLast || hasActivePropDiff()) then
                        let first = LayoutConst.formatTranslatePx x y
                        let last = if x = after.X && y = after.Y then first else LayoutConst.formatTranslatePx after.X after.Y
                        write m.Element "translate" first ""
                        if m.Target then addProperty m "translate" first last delay length ease
                        else addNodeSwap(fun () -> if live() then write m.Element "translate" last "")
                    for pi = 0 to activeProps.Length - 1 do
                        let name = activeProps[pi]
                        let pIdx = activePropIndices[pi]
                        let mutable boundary : Declaration voption = ValueNone
                        for bi = 0 to boundaryProperties.Length - 1 do
                            if boundary.IsNone && Declaration.name boundaryProperties[bi] = name then
                                boundary <- ValueSome boundaryProperties[bi]
                        let first =
                            if isEntering then
                                match boundary with ValueSome d -> Declaration.value d | ValueNone -> before.Values[pIdx]
                            else before.Values[pIdx]
                        let last =
                            if isLeaving then
                                match boundary with ValueSome d -> Declaration.value d | ValueNone -> after.Values[pIdx]
                            else after.Values[pIdx]
                        if m.Target then addProperty m name first last delay length ease
                        else
                            if shouldFadeSwap && boundary.IsSome then
                                duration<-max duration (delay+length)
                                let middle=Declaration.value boundary.Value
                                let swapEase=ease
                                if name="transform" then
                                    let sample=nativeSampler m [|first;middle;last|] length swapEase
                                    renders.Add(fun time -> sample(time-delay))
                                else
                                    let rawHalf=length/2.
                                    let halfLen=if rawHalf>0. && PropertyNumbers.round 1e12 rawHalf=0. then 1e-11 else rawHalf
                                    let firstHalf=propertySampler m name first middle halfLen swapEase
                                    let secondHalf=propertySampler m name middle last halfLen (fun t -> 1.-swapEase(1.-t))
                                    renders.Add(fun time ->
                                        let local=time-delay
                                        if local<halfLen then firstHalf local else secondHalf(local-halfLen))
                            elif name<>"transform" then
                                write m.Element name first ""
                                addNodeSwap(fun () -> if live() then write m.Element name last "")
                if not (isNull nodeSwaps) && nodeSwaps.Count>0 then
                    let swapArr = nodeSwaps.ToArray()
                    duration<-max duration (delay+length/2.)
                    swaps.Add(delay+length/2.,fun () ->
                        for si = 0 to swapArr.Length - 1 do
                            if live() then swapArr[si]())
            entering<-enter.ToArray()
            leaving<-leave.ToArray()
            animating<-animate.ToArray()
            swapping<-swap.ToArray()
            let onPause animation =
                if live() then
                    if options.RetainLayoutOnComplete then
                        options.Callbacks.OnComplete animation
                        if live() then options.Callbacks.OnPause animation
                    elif root.classList.contains "is-animated" then
                        for transform in transforms do
                            transform.Commit()
                            updateLast transform.Element "transform"
                            transform.Cancel()
                        for entry in styles do
                            if entry.Name="display" || entry.Name="visibility" then restoreOne entry
                        root.classList.remove LayoutConst.isAnimatedTokens
                        options.Callbacks.OnComplete animation
                        if live() then options.Callbacks.OnPause animation
            let onComplete animation =
                if live() then
                    if options.RetainLayoutOnComplete then options.Callbacks.OnComplete animation
                    else
                        let active=root.classList.contains "is-animated"
                        finish true
                        if active && live() then options.Callbacks.OnComplete animation
            let animation=ScopeRegistration.run ignore (fun () ->
                new Timeline(scheduler,options=options.Playback,?playbackEase=options.PlaybackEase,
                                               onBegin=options.Callbacks.OnBegin,onBeforeUpdate=options.Callbacks.OnBeforeUpdate,
                                               onRender=options.Callbacks.OnRender,onUpdate=options.Callbacks.OnUpdate,
                                               onLoop=options.Callbacks.OnLoop,onPause=onPause,onComplete=onComplete))
            clock<-Some animation
            let empty=renders.Count=0 && swaps.Count=0
            if not empty then
              let renderArr = renders.ToArray()
              ScopeRegistration.run ignore (fun () ->
                let renderDriver=new NumericAnimation(scheduler,(fun () -> 0.),(fun t ->
                    for ri = 0 to renderArr.Length - 1 do
                        if live() then renderArr[ri] t),
                                                     duration,duration)
                driver<-Some renderDriver
                animation.Sync(renderDriver,position=TimelinePosition.At 0.) |> ignore
                for position,apply in swaps do animation.Call(apply,position=TimelinePosition.At position) |> ignore)
            if not empty then root.classList.add LayoutConst.isAnimatedTokens
            animation.Reset()
            if empty then
                animation.Complete(muteCallbacks=true); finish false
                if options.RetainLayoutOnComplete && live() then
                    options.Callbacks.OnComplete animation
                    if live() then options.Callbacks.OnPause animation
            elif options.Autoplay then animation.Resume()
            match viewport,scroll with
            | Some v,Some position ->
                let job=scheduler.Frame(fun _ -> if live() then v.ScrollTo {Left=Some position.X;Top=Some position.Y;Behavior=ScrollBehavior.Instant})
                deferred.Add job
            | _ -> ()
            constructing <- false
            animation
        with
        | LayoutConstructionInterrupted ->
            constructing <- false
            let abandoned=ScopeRegistration.run ignore (fun () -> new Timeline(scheduler))
            abandoned.Cancel()
            abandoned
        | _ ->
            constructing <- false
            if expected=generation then
                try stop(); finish false with _ -> ()
            reraise()
    /// <summary>Records, invokes the mutation synchronously and animates its resulting layout.</summary>
    member _.Update(mutate:AutoLayout -> unit,?animationOptions:LayoutOptions) =
        let previousMeasurements = measurements
        let previousScroll = scroll
        let previousIdsCount = identities.Count
        this.Record()
        try mutate this; this.Animate(?animationOptions=animationOptions)
        with _ ->
            try
                stop()
                finish false
                for i = identities.Count - 1 downto previousIdsCount do
                    let element, value = identities[i]
                    if element.getAttribute "data-layout-id" = value then element.removeAttribute "data-layout-id"
                    identities.RemoveAt i
                measurements <- previousMeasurements
                scroll <- previousScroll
            with _ -> ()
            reraise()
    /// <summary>Cancels playback and restores owned declarations while retaining DOM mutations.</summary>
    member _.Revert() =
        stop()
        finish false
        for element,value in identities do
            if element.getAttribute "data-layout-id"=value then element.removeAttribute "data-layout-id"
        identities.Clear()
        measurements<-[||]
        entering<-[||]; leaving<-[||]; animating<-[||]; swapping<-[||]
    /// <summary>The most recently constructed layout transition timeline, if active.</summary>
    member _.Timeline = clock
    /// <summary>Maps observable completion to the callback result.</summary>
    member this.ThenResult<'T>(callback: AutoLayout -> 'T) : Threading.Tasks.Task<'T> =
        match clock with
        | Some active -> active.ThenResult(fun _ -> callback this)
        | None -> task { return callback this }

    /// <summary>Awaits the task returned by the completion callback.</summary>
    member this.ThenAsync<'T>(callback: AutoLayout -> Threading.Tasks.Task<'T>) : Threading.Tasks.Task<'T> =
        Threading.Tasks.TaskExtensions.Unwrap(this.ThenResult(callback))

    /// <summary>Awaits a completion callback that produces no result.</summary>
    member this.ThenAsync(callback: AutoLayout -> Threading.Tasks.Task) : Threading.Tasks.Task =
        Threading.Tasks.TaskExtensions.Unwrap(this.ThenResult(callback))

    /// <summary>Runs the optional completion callback and returns this instance.</summary>
    member this.Then(?callback: AutoLayout -> unit) : Threading.Tasks.Task<AutoLayout> =
        this.ThenResult(CompletionTask.withInstance callback)
    /// <summary>Returns a task settling when the active layout transition completes.</summary>
    member this.AsTask() : Threading.Tasks.Task<AutoLayout> = this.Then()
    /// <summary>Attaches this layout's cleanup to the current Ranvier owner.</summary>
    member _.Own() =
        check()
        ScopeRegistration.attach(this:>IDisposable)
        this
    interface IRevertible with
        member this.Revert() = this.Revert()
    interface IDisposable with
        member _.Dispose() =
            if not disposed then
                disposed<-true
                this.Revert()

/// <summary>Factory functions matching Anime.js createLayout.</summary>
[<RequireQualifiedAccess>]
module Layout =
    /// <summary>Creates an AutoLayout observer for the root element and records its initial state.</summary>
    let create (root: HTMLElement) (computed: ComputedStyleService) (scheduler: FrameScheduler)
               (options: LayoutOptions option) (viewport: ViewportScrollService option) =
        new AutoLayout(root, computed, scheduler, ?options=options, ?viewport=viewport)

