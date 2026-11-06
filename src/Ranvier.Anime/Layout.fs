namespace Ranvier.Anime

open System
open System.Collections.Generic
open System.Collections.Frozen
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

type private LayoutMeasurement =
    { Id: string
      Element: HTMLElement
      Measure: HTMLElement
      RenderHidden: bool
      Parent: HTMLElement option
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
      Values: FrozenDictionary<string,string> }

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
    let samplers=ResizeArray<Timer>()
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
            let value="ranvier-layout-"+identityPrefix+"-"+string nextIdentity
            nextIdentity<-nextIdentity+1u
            e.setAttribute("data-layout-id",value)
            identities.Add(e,value)
            value
    let same (a:HTMLElement) (b:HTMLElement)=Object.ReferenceEquals(a,b) || (a:>Node).isSameNode(b:>Node)
    let check()=if disposed || not(ownerLive()) then raise(ObjectDisposedException(nameof AutoLayout))
    let text (value:float) = value.ToString("G17",CultureInfo.InvariantCulture)
    let px value = text value+"px"
    let captureStyle (e:HTMLElement) name =
        let style=inlineStyle e
        {Element=e;Style=style;Name=name;Snapshot=LayoutStyleSnapshot(style,name)}
    let restoreOne (entry:LayoutStyleWrite) =
        entry.Snapshot.Restore()
    let restoreWhere predicate =
        let mutable failure:exn option=None
        let entries=styles.ToArray()
        styles.Clear()
        for entry in entries do if not(predicate entry) then styles.Add entry
        for i=entries.Length-1 downto 0 do
            try if predicate entries[i] then restoreOne entries[i]
            with error -> if failure.IsNone then failure<-Some error
        failure |> Option.iter(fun error -> System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw())
    let restore()=restoreWhere(fun _ -> true)
    let write (e:HTMLElement) name value priority =
        check()
        let expected=generation
        let style=inlineStyle e
        let entry=
            styles |> Seq.tryFind(fun x -> same x.Element e && x.Name=name)
            |> Option.defaultWith(fun () ->
                let x=captureStyle e name
                styles.Add x
                x)
        style.setProperty(name,value,priority)
        if expected<>generation || disposed || not(ownerLive()) then raise LayoutConstructionInterrupted
        entry.Snapshot.MarkWritten()
    let track (e:HTMLElement) name =
        if not(styles |> Seq.exists(fun x -> same x.Element e && x.Name=name)) then
            styles.Add(captureStyle e name)
    let updateLast (e:HTMLElement) name =
        let style=inlineStyle e
        for entry in styles do
            if same entry.Element e && entry.Name=name then entry.Snapshot.MarkWritten()
    let stop() =
        generation<-generation+1L
        clock |> Option.iter(fun c -> c.Cancel())
        clock<-None
        driver |> Option.iter(fun c -> c.Cancel())
        driver<-None
        for sampler in samplers do sampler.Cancel()
        samplers.Clear()
        for transform in transforms do transform.Cancel()
        transforms.Clear()
        for job in deferred do job.Dispose()
        deferred.Clear()
    let props=
        Array.concat [options.Properties;[|"transform"|]
                      options.EnterFrom.Properties |> Array.map Declaration.name
                      options.LeaveTo.Properties |> Array.map Declaration.name
                      options.SwapAt.Properties |> Array.map Declaration.name] |> Array.distinct
    let isAdjacentText (e:HTMLElement) =
        let rec significant (node:Node) direction =
            if isNull(box node) then false
            elif node.nodeType=8us || (node.nodeType=3us && String.IsNullOrWhiteSpace node.textContent) then
                significant (direction node) direction
            else node.nodeType=3us
        significant (e:>Node).previousSibling (fun n -> n.previousSibling) ||
        significant (e:>Node).nextSibling (fun n -> n.nextSibling)
    let capture() =
        let result=ResizeArray<LayoutMeasurement>()
        let temporary=ResizeArray<LayoutStyleWrite>()
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
            let rec walk (e:HTMLElement) (parent:LayoutMeasurement option) =
                let css=computed.Get(e,None)
                check()
                let values=props |> Array.map(fun name -> KeyValuePair(name,css.getPropertyValue name)) |> FrozenDictionary.ToFrozenDictionary
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
                let current=
                    { Id=identity e;Element=e;Measure=e;RenderHidden=hidden
                      Parent=parent |> Option.map(fun p -> p.Element);Left=left;Top=top;X=x;Y=y
                      BorderLeft=float e.clientLeft;BorderTop=float e.clientTop;Width=width;Height=height
                      Hidden=hidden;Inline=isAdjacentText e;Target=same root e || e.matches options.Children
                      Display=display;Position=css.getPropertyValue "position";Values=values }
                result.Add current
                let mutable child=(e:>Node).firstChild
                while not(isNull(box child)) do
                    match child with
                    | :? HTMLElement as html -> walk html (Some current)
                    | _ -> ()
                    child<-child.nextSibling
            walk root None
            let selected=
                match options.Targets with
                | Some targets -> Array.copy targets
                | None when options.Children="*" -> result |> Seq.map(fun m -> m.Element) |> Seq.toArray
                | None ->
                    let scope=defaultArg options.SelectionRoot ((root:>Node).ownerDocument:>ParentNode)
                    let matches=scope.querySelectorAll options.Children
                    [| for index=0 to int matches.length-1 do
                           match matches.item(uint index) with :? HTMLElement as html -> yield html | _ -> () |]
            let inRootIds=HashSet<string>(result |> Seq.map(fun m -> m.Id),StringComparer.Ordinal)
            for candidate in selected do
                if not((root:>Node).contains(candidate:>Node)) && inRootIds.Contains(candidate.getAttribute "data-layout-id") then
                    walk candidate None
            let selectedIds=HashSet<string>(selected |> Seq.map(fun e -> e.getAttribute "data-layout-id"),StringComparer.Ordinal)
            let merged=ResizeArray<LayoutMeasurement>()
            let nodes=
                result.ToArray() |> Array.groupBy(fun m -> m.Id) |> Array.map(fun (_,group) ->
                    let first=group[0]
                    let measured=group |> Array.tryFind(fun m -> not m.Hidden) |> Option.defaultValue first
                    let chosen=
                        if first.Hidden && not measured.Hidden then
                            let parent=first.Parent |> Option.bind(fun parent ->
                                let id=parent.getAttribute "data-layout-id"
                                merged |> Seq.tryFind(fun m -> m.Id=id)
                                |> Option.orElseWith(fun () -> result |> Seq.tryFind(fun m -> same parent m.Element)))
                            let x,y=
                                match parent with
                                | Some p -> measured.Left-p.Left-p.BorderLeft,measured.Top-p.Top-p.BorderTop
                                | None -> measured.X,measured.Y
                            {measured with Element=first.Element;Measure=measured.Element;RenderHidden=true;Parent=first.Parent;X=x;Y=y}
                        else first
                    let node={chosen with Target=same root chosen.Element || selectedIds.Contains chosen.Id}
                    merged.Add node
                    node)
            // Selected descendants animate their ancestor chain.
            let targetElements=ResizeArray<HTMLElement>()
            for m in nodes do
                if m.Target then
                    let mutable current:Element=m.Element
                    while not(isNull(box current)) && (root:>Node).contains(current:>Node) do
                        match current with :? HTMLElement as h -> targetElements.Add h | _ -> ()
                        current<-(current:>Node).parentElement
            nodes |> Array.map(fun m -> {m with Target=targetElements |> Seq.exists(same m.Element)})
        finally restoreTemporary()
    let finish deferTransitions =
        for sampler in samplers do sampler.Cancel()
        samplers.Clear()
        for transform in transforms do transform.Cancel()
        transforms.Clear()
        try restoreWhere(fun entry -> not deferTransitions || entry.Name<>"transition")
        finally root.classList.remove [|"is-animated"|]
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
        root.classList.remove [|"is-animated"|]
        measurements<-current
        scroll<-viewport |> Option.map(fun v -> v.Position())
    /// <summary>Recaptures the current layout without starting a transition.</summary>
    member _.Refresh()=this.Record()
    /// <summary>Animates from the recorded state to the current DOM and returns paused-capable playback.</summary>
    /// <remarks>Animation overrides affect timing, effects and playback; selection and captured names stay fixed.
    /// Transform changes use native keyframe effects and require CSS linear easing support.</remarks>
    member _.Animate(?animationOptions:LayoutOptions) =
        check()
        stop()
        restore()
        let options=defaultArg animationOptions options
        let expected=generation
        let live()=not disposed && ownerLive() && expected=generation
        let ensureLive()=if not(live()) then raise LayoutConstructionInterrupted
        let fresh=capture()
        let find (element:HTMLElement) (nodes:LayoutMeasurement array)=
            let id=element.getAttribute "data-layout-id"
            nodes |> Array.tryFind(fun n -> n.Id=id)
        let anyChanges=
            fresh |> Array.exists(fun m ->
                match find m.Element measurements with
                | None -> true
                | Some previous ->
                    previous.Width<>m.Width || previous.Height<>m.Height || previous.X<>m.X || previous.Y<>m.Y ||
                    previous.Hidden<>m.Hidden || props |> Array.exists(fun p -> previous.Values[p]<>m.Values[p]))
        let enter=ResizeArray<HTMLElement>()
        let leave=ResizeArray<HTMLElement>()
        let animate=ResizeArray<HTMLElement>()
        let swap=ResizeArray<HTMLElement>()
        let added=HashSet<HTMLElement>(HashIdentity.Reference)
        let removed=HashSet<HTMLElement>(HashIdentity.Reference)
        let mutable duration=0.
        let renders=ResizeArray<float -> unit>()
        let swaps=ResizeArray<float*(unit -> unit)>()
        let nativeSampler (m:LayoutMeasurement) values length ease =
            track m.Element "transform"
            let points=Array.init 101(fun index ->
                let value=ease(float index/100.)
                ensureLive()
                if not(Double.IsFinite value) then invalidArg "ease" "Expected a finite easing result."
                text(Math.Floor(value*10000.+0.5)/10000.))
            let transform=new LayoutTransformSampler(m.Element,values,"linear("+String.concat ", " points+")")
            if not(live()) then
                transform.Cancel()
                raise LayoutConstructionInterrupted
            transforms.Add transform
            fun time ->
                if live() then
                    let normalized=if length=0. then 1. else Math.Clamp(time/length,0.,1.)
                    transform.Sample normalized
        let propertySampler (m:LayoutMeasurement) name first last length ease =
            if first=last then ignore
            elif name="transform" then nativeSampler m [|first;last|] length ease
            else
                track m.Element name
                let guardedEase time =
                    let value=ease time
                    ensureLive()
                    value
                let options={DeclarationAnimationOptions.Default with Ease=guardedEase}
                let timer=ScopeRegistration.run ignore (fun () ->
                    CssAnimation.declarationPreciseWith options scheduler (inlineStyle m.Element)
                        (Declaration.create name first) (Declaration.create name last) length)
                if not(live()) then
                    timer.Cancel()
                    raise LayoutConstructionInterrupted
                timer.Pause()
                samplers.Add timer
                updateLast m.Element name
                fun time ->
                    if live() then
                        timer.Seek(Math.Clamp(time,0.,length),muteCallbacks=true)
                        updateLast m.Element name
        let addProperty m name first last delay length ease =
            if first<>last then
                let render=propertySampler m name first last length ease
                duration<-max duration (delay+length)
                renders.Add(fun time -> render(time-delay))
        try
            let classify (m:LayoutMeasurement) =
                let old=find m.Element measurements
                let wasHidden=old |> Option.map(fun o -> o.Hidden) |> Option.defaultValue true
                let parentAdded=m.Parent |> Option.exists added.Contains
                let parentRemoved=m.Parent |> Option.exists removed.Contains
                let entering=not m.Hidden && wasHidden && (not parentAdded || old.IsSome)
                let leaving=m.Hidden && not wasHidden && not parentRemoved
                if entering || parentAdded then added.Add m.Element |> ignore
                if leaving || parentRemoved then removed.Add m.Element |> ignore
                if m.Target then
                    if entering then enter.Add m.Element
                    elif leaving then leave.Add m.Element
                    elif not(same root m.Element) then animate.Add m.Element
                else swap.Add m.Element
            for m in fresh do classify m
            added.Clear()
            removed.Clear()
            let plans=ResizeArray<_>()
            for m in fresh do
                let old=find m.Element measurements
                let previous=defaultArg old m
                let parentAdded=m.Parent |> Option.exists added.Contains
                let parentRemoved=m.Parent |> Option.exists removed.Contains
                let wasHidden=old |> Option.map(fun o -> o.Hidden) |> Option.defaultValue true
                let isEntering=not m.Hidden && wasHidden && (not parentAdded || old.IsSome)
                let isLeaving=m.Hidden && not wasHidden && not parentRemoved
                if parentAdded || isEntering then added.Add m.Element |> ignore
                if parentRemoved || isLeaving then removed.Add m.Element |> ignore
                let effect=
                    if isEntering then Some options.EnterFrom
                    elif isLeaving then Some options.LeaveTo
                    elif not m.Target then Some options.SwapAt
                    else None
                let group=if isEntering then enter elif isLeaving then leave else animate
                let index=group |> Seq.tryFindIndex(same m.Element) |> Option.defaultValue 0
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
                    if not(Array.contains (Declaration.name declaration) props) then
                        invalidArg "animationOptions" "Boundary property providers must use names declared by the layout's captured Properties."
                finite "delay" delay
                finite "duration" length
                plans.Add(m,previous,parentRemoved,wasHidden,isEntering,isLeaving,boundaryProperties,delay,length,ease)
            for m,previous,parentRemoved,wasHidden,isEntering,isLeaving,boundaryProperties,delay,length,ease in plans do
                let nodeSwaps=ResizeArray<unit -> unit>()
                let before=if wasHidden then m else previous
                let after=if isLeaving || parentRemoved then previous else m
                let x,y=
                    if before.Parent<>m.Parent && not wasHidden then
                        let p=m.Parent |> Option.bind(fun p -> find p measurements |> Option.orElseWith(fun () -> find p fresh))
                        match p with Some p -> before.Left-p.Left-p.BorderLeft,before.Top-p.Top-p.BorderTop | None -> before.X,before.Y
                    else before.X,before.Y
                let geometry=[|"width",px before.Width,px after.Width;"height",px before.Height,px after.Height|]
                let changed=abs(before.Width-after.Width)>1. || abs(before.Height-after.Height)>1.
                let shouldFadeSwap=
                    changed && (m.Parent |> Option.bind(fun parent -> find parent fresh) |> Option.exists(fun parent ->
                        parent.Target && (find parent.Element measurements |> Option.exists(fun old ->
                            abs(parent.Width-old.Width)>1. || abs(parent.Height-old.Height)>1.))))
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
                            write m.Element "position" (if fresh[0].Position="absolute" || fresh[0].Position="fixed" then "fixed" else "absolute") ""
                            for name in [|"left";"top";"margin-left";"margin-top"|] do write m.Element name "0px" ""
                        elif m.Position="static" then
                            write m.Element "position" "relative" ""
                            write m.Element "left" "0px" ""
                            write m.Element "top" "0px" ""
                    for name in [|"min-width";"min-height"|] do write m.Element name "auto" ""
                    for name in [|"max-width";"max-height"|] do write m.Element name "none" ""
                    for name,first,last in geometry do
                        write m.Element name first ""
                        if m.Target then addProperty m name first last delay length ease
                        else nodeSwaps.Add(fun () -> if live() then write m.Element name last "")
                    if not m.Inline && (not(same root m.Element) || fresh[0].Position="absolute" || fresh[0].Position="fixed" ||
                                       geometry |> Array.exists(fun (_,first,last) -> first<>last) ||
                                       props |> Array.exists(fun property -> before.Values[property]<>after.Values[property])) then
                        let first=px x+" "+px y
                        let last=px after.X+" "+px after.Y
                        write m.Element "translate" first ""
                        if m.Target then addProperty m "translate" first last delay length ease
                        else nodeSwaps.Add(fun () -> if live() then write m.Element "translate" last "")
                    for name in props do
                        let boundary=boundaryProperties |> Array.tryFind(fun d -> Declaration.name d=name)
                        let first=if isEntering then boundary |> Option.map Declaration.value |> Option.defaultValue before.Values[name] else before.Values[name]
                        let last=if isLeaving then boundary |> Option.map Declaration.value |> Option.defaultValue after.Values[name] else after.Values[name]
                        if m.Target then addProperty m name first last delay length ease
                        else
                            let midpoint=boundaryProperties |> Array.tryFind(fun d -> Declaration.name d=name)
                            if shouldFadeSwap && midpoint.IsSome then
                                duration<-max duration (delay+length)
                                let middle=Declaration.value midpoint.Value
                                let swapEase=ease
                                if name="transform" then
                                    let sample=nativeSampler m [|first;middle;last|] length swapEase
                                    renders.Add(fun time -> sample(time-delay))
                                else
                                    let firstHalf=propertySampler m name first middle (length/2.) swapEase
                                    let secondHalf=propertySampler m name middle last (length/2.) (fun t -> 1.-swapEase(1.-t))
                                    renders.Add(fun time ->
                                        let local=time-delay
                                        if local<length/2. then firstHalf local else secondHalf(local-length/2.))
                            elif name<>"transform" then
                                write m.Element name first ""
                                nodeSwaps.Add(fun () -> if live() then write m.Element name last "")
                if nodeSwaps.Count>0 then
                    duration<-max duration (delay+length/2.)
                    swaps.Add(delay+length/2.,fun () -> for apply in nodeSwaps do if live() then apply())
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
                        root.classList.remove [|"is-animated"|]
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
              ScopeRegistration.run ignore (fun () ->
                let renderDriver=new NumericAnimation(scheduler,(fun () -> 0.),(fun t -> for render in renders do if live() then render t),
                                                     duration,duration)
                driver<-Some renderDriver
                animation.Sync(renderDriver,position=TimelinePosition.At 0.) |> ignore
                for position,apply in swaps do animation.Call(apply,position=TimelinePosition.At position) |> ignore)
            if not empty then root.classList.add [|"is-animated"|]
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
            animation
        with
        | LayoutConstructionInterrupted ->
            let abandoned=ScopeRegistration.run ignore (fun () -> new Timeline(scheduler))
            abandoned.Cancel()
            abandoned
        | _ ->
            if expected=generation then
                try stop(); finish false with _ -> ()
            reraise()
    /// <summary>Records, invokes the mutation synchronously and animates its resulting layout.</summary>
    member _.Update(mutate:AutoLayout -> unit,?animationOptions:LayoutOptions) =
        this.Record()
        try mutate this; this.Animate(?animationOptions=animationOptions)
        with _ ->
            try this.Revert() with _ -> ()
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
    /// <summary>Registers an optional completion callback on the active layout transition and returns a settling task.</summary>
    member this.Then(?callback: AutoLayout -> unit) : Threading.Tasks.Task<AutoLayout> =
        let invoke () = callback |> Option.iter (fun action -> action this)
        match clock with
        | None ->
            invoke()
            Threading.Tasks.Task.FromResult this
        | Some active when active.Completed ->
            invoke()
            Threading.Tasks.Task.FromResult this
        | Some active when active.Cancelled ->
            Threading.Tasks.Task.FromCanceled<AutoLayout>(Threading.CancellationToken(true))
        | Some active ->
            let source = Threading.Tasks.TaskCompletionSource<AutoLayout>()
            active.Then(fun _ -> invoke()).ContinueWith((fun (task: Threading.Tasks.Task<Timeline>) ->
                if task.IsCanceled then source.TrySetCanceled() |> ignore
                elif task.IsFaulted then source.TrySetException(task.Exception.InnerExceptions) |> ignore
                else source.TrySetResult this |> ignore
            ), Threading.Tasks.TaskContinuationOptions.ExecuteSynchronously) |> ignore
            source.Task
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
        let instance = new AutoLayout(root, computed, scheduler, ?options=options, ?viewport=viewport)
        instance.Record()
        instance

