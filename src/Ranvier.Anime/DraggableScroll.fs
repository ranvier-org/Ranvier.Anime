namespace Ranvier.Anime

open System
open Ranvier
open Ranvier.Dom

/// <summary>Container padding in CSS pixels, ordered top, right, bottom, left.</summary>
[<Struct>]
type DragPadding =
    {Top:float;Right:float;Bottom:float;Left:float}
    /// <summary>Zero padding on every edge.</summary>
    static member Zero={Top=0.;Right=0.;Bottom=0.;Left=0.}

/// <summary>Refreshable native scroll and containment settings.</summary>
[<NoEquality;NoComparison>]
type DraggableScrollSettings =
    { Padding:DragPadding; Speed:float; Threshold:float; UseWindow:bool
      Contained:bool; ExplicitBounds:DragBounds option }
    /// <summary>Element containment with source autoscroll speed and threshold.</summary>
    static member Default={Padding=DragPadding.Zero;Speed=1.5;Threshold=20.;UseWindow=false;Contained=true;ExplicitBounds=None}

/// <summary>Native container geometry, autoscrolling and in-view movement using existing frame/animation services.</summary>
type DraggableScroll(controller:Draggable,scheduler:FrameScheduler,target:HTMLElement,container:HTMLElement,
                     computed:ComputedStyleService,?settings:unit -> DraggableScrollSettings,
                     ?viewport:ViewportService,?windowScroll:ViewportScrollService,?containerProvider:unit -> HTMLElement) as this =
    let mutable container=container
    let settings=defaultArg settings (fun()->DraggableScrollSettings.Default)
    let mutable config=settings()
    let mutable disposed=false
    let mutable active=false
    let mutable animation:NumericAnimation option=None
    let mutable targetBounds=DragBounds.Unbounded
    let mutable areaWidth=0.
    let mutable areaHeight=0.
    let mutable viewWidth=0.
    let mutable viewHeight=0.
    let mutable canScroll=false
    let mutable animating=false
    let round value=Math.Floor(value+0.5)
    let position () =
        if config.UseWindow then
            match windowScroll with
            | Some service -> let position=service.Position() in struct(position.X,position.Y)
            | None -> invalidOp "Window scrolling requires the host ViewportScrollService."
        else struct(float container.scrollLeft,float container.scrollTop)
    let scrollTo x y =
        if not disposed then
            if config.UseWindow then
                match windowScroll with
                | Some service -> service.ScrollTo({Left=Some x;Top=Some y;Behavior=ScrollBehavior.Instant})
                | None -> invalidOp "Window scrolling requires the host ViewportScrollService."
            else container.scrollTo(x,y)
    let scrollBounds () : DragBounds =
        let struct(x,y)=position()
        let padding=config.Padding
        {Top=y-targetBounds.Top+padding.Top-config.Threshold
         Right=x-targetBounds.Right-padding.Right+config.Threshold
         Bottom=y-targetBounds.Bottom-padding.Bottom+config.Threshold
         Left=x-targetBounds.Left+padding.Left-config.Threshold}
    let pause ()=active<-false
    let update () =
        if not disposed && not controller.Cancelled && active then
            if canScroll && not animating && controller.Dragged then
                let b=scrollBounds()
                let padding=config.Padding
                let struct(oldX,oldY)=position()
                let newWidth=float container.scrollWidth
                let newHeight=float container.scrollHeight
                let lostX=max 0. (viewWidth-newWidth)
                let lostY=max 0. (viewHeight-newHeight)
                if lostX>0. || lostY>0. then
                    controller.ScrollBy(-lostX,-lostY)
                    if lostX>0. then viewWidth<-newWidth
                    if lostY>0. then viewHeight<-newHeight
                let dx=if controller.Settings.XEnabled then DragPhysics.edgeScroll b.Left b.Right padding.Left padding.Right config.Threshold config.Speed controller.RawX else 0.
                let dy=if controller.Settings.YEnabled then DragPhysics.edgeScroll b.Top b.Bottom padding.Top padding.Bottom config.Threshold config.Speed controller.RawY else 0.
                let newX=round(Math.Clamp(oldX+dx,0.,max 0. (viewWidth-areaWidth)))
                let newY=round(Math.Clamp(oldY+dy,0.,max 0. (viewHeight-areaHeight)))
                if newX<>oldX || newY<>oldY then
                    controller.ScrollBy(newX-oldX,newY-oldY)
                    scrollTo newX newY

    do controller.SetBeforeUpdate update
    let live ()=if disposed then raise(ObjectDisposedException(nameof DraggableScroll))
    /// <summary>Last refresh snapshot, including threshold and padding.</summary>
    member _.Settings=config
    /// <summary>Whether native content dimensions and overflow permit autoscrolling.</summary>
    member _.CanScroll=canScroll
    /// <summary>Source scroll-view translation limits with threshold and padding.</summary>
    member _.ScrollBounds=scrollBounds()
    /// <summary>Re-measures at zero translation with ancestor transforms temporarily removed and restored individually.</summary>
    member _.Refresh() =
        live()
        config<-settings()
        containerProvider |> Option.iter(fun provider -> container<-provider())
        let p=config.Padding
        let mutable measured:DragBounds option=None
        controller.WithOrigin(fun () ->
            let restores=ResizeArray<unit -> unit>()
            try
                let mutable parent=target.parentElement
                while not(isNull(box parent)) do
                    match parent with
                    | :? HTMLElement as element ->
                        let style=(element:>ElementCSSInlineStyle).style
                        let original=style.getPropertyValue "transform"
                        let priority=style.getPropertyPriority "transform"
                        restores.Add(fun () -> if original="" then style.removeProperty "transform" |> ignore else style.setProperty("transform",original,priority))
                        style.setProperty("transform","none","")
                    | _ -> ()
                    parent<-parent.parentElement
                let rect=container.getBoundingClientRect()
                let metrics=if config.UseWindow then Some((viewport |> Option.defaultWith(fun()->invalidOp "Window bounds require the host ViewportService.")).Current()) else None
                let width=metrics |> Option.map(fun m->float m.InnerWidth) |> Option.defaultValue rect.width
                let height=metrics |> Option.map(fun m->float m.InnerHeight) |> Option.defaultValue rect.height
                areaWidth<-round width;areaHeight<-round height
                viewWidth<-max width (float container.scrollWidth)
                viewHeight<-max height (float container.scrollHeight)
                let fixedTarget=computed.Get(target,None).getPropertyValue "position"="fixed"
                let overflow=computed.Get(container,None).getPropertyValue "overflow"
                canScroll<-not fixedTarget && config.Contained && config.ExplicitBounds.IsNone &&
                           ((config.UseWindow && overflow="visible")||(overflow<>"hidden" && overflow<>"visible")) &&
                           (float container.scrollWidth>areaWidth+p.Left-p.Right || float container.scrollHeight>areaHeight+p.Top-p.Bottom)
                if config.Contained then
                    let targetRect=target.getBoundingClientRect()
                    let struct(sx,sy)=position()
                    targetBounds<-{Top=round(targetRect.top+sy-(if config.UseWindow then 0. else rect.top))
                                   Right=round(targetRect.right+sx-(if config.UseWindow then width else rect.right))
                                   Bottom=round(targetRect.bottom+sy-(if config.UseWindow then height else rect.bottom))
                                   Left=round(targetRect.left+sx-(if config.UseWindow then 0. else rect.left))}
                    let hiddenLeft=if canScroll && not config.UseWindow then float container.scrollLeft else 0.
                    let hiddenTop=if canScroll && not config.UseWindow then float container.scrollTop else 0.
                    let hiddenRight=if canScroll then viewWidth-hiddenLeft-rect.width else 0.
                    let hiddenBottom=if canScroll then viewHeight-hiddenTop-rect.height else 0.
                    let fixedEdge maximum value=if fixedTarget then Math.Clamp(value,0.,maximum) else value
                    let bounds:DragBounds=
                        match config.ExplicitBounds with
                        | Some b -> {Top=b.Top+p.Top;Right=b.Right-p.Right;Bottom=b.Bottom-p.Bottom;Left=b.Left+p.Left}
                        | None ->
                            {Top= -round(targetRect.top-fixedEdge height rect.top+hiddenTop-p.Top)
                             Right= -round(targetRect.right-fixedEdge width rect.right-hiddenRight+p.Right)
                             Bottom= -round(targetRect.bottom-fixedEdge height rect.bottom-hiddenBottom+p.Bottom)
                             Left= -round(targetRect.left-fixedEdge width rect.left+hiddenLeft-p.Left)}
                    measured<-Some bounds
            finally
                for restore in restores do restore())
        match measured with
        | Some bounds -> controller.SetBounds(bounds,true)
        | None -> controller.SetBounds(DragBounds.Unbounded,false)
    /// <summary>Starts source-style edge autoscrolling while the controller's gesture remains grabbed.</summary>
    member _.Start()=live();active<-true
    /// <summary>Stops autoscroll frames while retaining the measured geometry.</summary>
    member _.Stop()=pause()
    /// <summary>Clears source scroll eligibility on Disable while retaining existing release scrolling motion.</summary>
    member internal _.Disable()=pause();canScroll<-false
    /// <summary>Animates native scrolling to the release destination with precise nonnegative milliseconds.</summary>
    member _.ScrollInViewPrecise(duration:float,?gap:float,?ease:float -> float) =
        live()
        let gap=defaultArg gap 0.
        let b=scrollBounds()
        if config.ExplicitBounds.IsNone && DragPhysics.outOfBounds config.Contained (not controller.Settings.XEnabled) (not controller.Settings.YEnabled) b controller.DestX controller.DestY<>0 then
            let struct(x,y)=position()
            let left=min 0. (round(controller.DestX-b.Left))
            let right=max 0. (round(controller.DestX-b.Right))
            let top=min 0. (round(controller.DestY-b.Top))
            let bottom=max 0. (round(controller.DestY-b.Bottom))
            let targetX=round(x+(if left<>0. then left-gap elif right<>0. then right+gap else 0.))
            let targetY=round(y+(if top<>0. then top-gap elif bottom<>0. then bottom+gap else 0.))
            animation |> Option.iter _.Cancel()
            animating<-true
            let driver=new NumericAnimation(scheduler,(fun()->0.),(fun progress->scrollTo (x+(targetX-x)*progress) (y+(targetY-y)*progress)),1.,duration,
                                            ease=defaultArg ease (Ease.named "inOutQuad"),onComplete=(fun _ -> animating<-false))
            animation<-Some driver
            driver.Reset();driver.Resume()
    /// <summary>Animates native scrolling with unsigned whole milliseconds, defaulting to 350.</summary>
    member _.ScrollInView(?duration:uint,?gap:float,?ease:float -> float)=this.ScrollInViewPrecise(float(defaultArg duration 350u),?gap=gap,?ease=ease)
    /// <summary>Moves out-of-view enabled axes into the measured scroll viewport using precise milliseconds.</summary>
    member _.AnimateInViewPrecise(duration:float,?gap:float,?ease:float -> float) =
        live();controller.Stop();this.Refresh()
        let gap=defaultArg gap 0.
        let struct(sx,sy)=position()
        let p=config.Padding
        let b:DragBounds={Top=sy-targetBounds.Top+p.Top+gap;Right=sx-targetBounds.Right-p.Right-gap;
               Bottom=sy-targetBounds.Bottom-p.Bottom-gap;Left=sx-targetBounds.Left+p.Left+gap}
        let outside=DragPhysics.outOfBounds config.Contained (not controller.Settings.XEnabled) (not controller.Settings.YEnabled) b controller.X controller.Y
        let ease=defaultArg ease (Ease.named "inOutQuad")
        if outside&&&1<>0 then controller.AnimateXPrecise(DragPhysics.clampAxis b.Left b.Right (DragPhysics.snap controller.Settings.SnapX controller.X),duration,ease)
        if outside&&&2<>0 then controller.AnimateYPrecise(DragPhysics.clampAxis b.Top b.Bottom (DragPhysics.snap controller.Settings.SnapY controller.Y),duration,ease)
    /// <summary>Moves enabled axes into view with unsigned whole milliseconds, defaulting to 350.</summary>
    member _.AnimateInView(?duration:uint,?gap:float,?ease:float -> float)=this.AnimateInViewPrecise(float(defaultArg duration 350u),?gap=gap,?ease=ease)
    /// <summary>Stops edge scrolling and any in-view scroll animation while retaining measured geometry.</summary>
    member _.StopMotion() =
        pause()
        animation |> Option.iter _.Cancel();animation<-None
        animating<-false
    /// <summary>Stops native scroll motion and clears scroll eligibility until the next geometry refresh.</summary>
    member _.Reset()=this.StopMotion();canScroll<-false
    /// <summary>Cancels owned native scroll animation and edge frames.</summary>
    member _.Cancel() =
        if not disposed then
            disposed<-true;pause()
            animation |> Option.iter _.Cancel();animation<-None
    interface IDisposable with member _.Dispose()=this.Cancel()
