namespace Ranvier.Anime

open System
open Ranvier
open Ranvier.Dom
open Ranvier.Css
open Ranvier.Css.Dom

module private DragDomStyles =
    let mutable private zIndex=0.
    let nextZ current = zIndex<-max current zIndex+1.;zIndex
    let snapshot (element:HTMLElement) name =
        let style=(element:>ElementCSSInlineStyle).style
        let original=style.getPropertyValue name
        let priority=style.getPropertyPriority name
        fun () ->
            if original="" then style.removeProperty name |> ignore
            else style.setProperty(name,original,priority)
    let apply (element:HTMLElement) name value =
        StyleDeclaration.apply (element:>ElementCSSInlineStyle).style (Declaration.create name value)
    let matrixInit (matrix:DOMMatrixReadOnly) =
        DOMMatrixInit(a=Some matrix.a,b=Some matrix.b,c=Some matrix.c,d=Some matrix.d,e=Some matrix.e,f=Some matrix.f,
                      m13=matrix.m13,m14=matrix.m14,m23=matrix.m23,m24=matrix.m24,
                      m31=matrix.m31,m32=matrix.m32,m33=matrix.m33,m34=matrix.m34,
                      m43=matrix.m43,m44=matrix.m44,is2D=Some matrix.is2D)

/// <summary>Native mouse/touch input, ancestor coordinate normalization, resize observation and owned interaction styles.</summary>
/// <remarks>The controller retains typed accessor mappings. Dispose removes all listeners and cancels the controller.</remarks>
type DraggableDom(controller:Draggable,scheduler:FrameScheduler,target:HTMLElement,trigger:HTMLElement,
                  document:Document,computed:ComputedStyleService,geometry:GeometryService,resize:ResizeObservationService,media:MediaQueryService,
                  ?container:HTMLElement,?scroll:DraggableScroll,?constructionGuard:unit -> bool,?cursor:bool,?cursorHover:string,?cursorGrab:string,
                  ?onResize:Draggable -> unit,?onAfterResize:Draggable -> unit) as this =
    let ownerAllowsWrite=ScopeRegistration.captureWriteGuard()
    let constructionAllowed=defaultArg constructionGuard (fun()->true)
    let mutable disposed=false
    let mutable enabled=false
    let mutable listening=false
    let mutable timeout:IDisposable option=None
    let mutable observer:ResizeObserver option=None
    let mutable initialResize=true
    let mutable pointerOriginY=0.
    let mutable finePointer=true
    let cursorEnabled=defaultArg cursor true
    // The inverse ancestor transform's terms for a point at z=0, w=1, which is all transformPoint reads of it.
    let mutable inverseA=1.
    let mutable inverseB=0.
    let mutable inverseC=0.
    let mutable inverseD=1.
    let mutable inverseE=0.
    let mutable inverseF=0.
    let mutable touchRestore: (unit -> unit) option=None
    let mutable hoverRestore: (unit -> unit) option=None
    let mutable grabRestore: (unit -> unit) option=None
    let mutable pointerRestore: (unit -> unit) option=None
    let mutable zRestore: (unit -> unit) option=None
    let mutable cleanupDocument:unit -> unit=ignore
    let cleanup actions =
        let mutable failure:exn option=None
        for action in actions do
            try action() with error -> if failure.IsNone then failure<-Some error
        failure |> Option.iter(fun error -> Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw())
    let restore cell=cell |> Option.iter(fun action -> if ownerAllowsWrite() && constructionAllowed() then action())
    let parents () =
        seq {
            let mutable parent=target.parentElement
            while not(isNull(box parent)) do
                yield parent
                parent<-parent.parentElement
        }
    let updatePointerType () =
        use query=media.Match "(pointer:fine)"
        finePointer<-query.Matches
    let updateMatrix () =
        let matrix=geometry.CreateMatrix()
        for parent in parents() do
            let transform=computed.Get(parent,None).getPropertyValue "transform"
            if transform<>"" && transform<>"none" then
                let parentMatrix=geometry.CreateMatrix transform
                matrix.preMultiplySelf(DragDomStyles.matrixInit parentMatrix) |> ignore
        let inverse=matrix.inverse()
        inverseA<-inverse.a
        inverseB<-inverse.b
        inverseC<-inverse.c
        inverseD<-inverse.d
        inverseE<-inverse.e
        inverseF<-inverse.f
    // transformPoint of (x, y, 0, 1), without a DOM point per pointer move.
    let normalize x y = struct(inverseA*x+inverseC*y+inverseE,inverseB*x+inverseD*y+inverseF)
    let point (event:Event) =
        match event with
        | :? TouchEvent as touch when touch.changedTouches.length>0u ->
            let point=touch.changedTouches.item 0u
            let struct(x,y)=normalize point.clientX point.clientY
            ValueSome(struct(x,y,true))
        | :? MouseEvent as mouse ->
            let struct(x,y)=normalize mouse.clientX mouse.clientY
            ValueSome(struct(x,y,false))
        | _ -> ValueNone
    // Each listener is one function value: a class let-function becomes a method, and every use of a method as a value is a
    // new closure, which removeEventListener cannot match and addEventListener cannot deduplicate.
    let prevent:EventListener=Closure.keep <| fun event -> if event.cancelable then event.preventDefault()
    let nestedScroller (event:Event) movedY =
        match event with
        | :? TouchEvent ->
            let mutable element=match event.target with :? HTMLElement as element -> Some element | _ -> None
            let mutable consumes=false
            let mutable found=false
            while element.IsSome && not found && not(Object.ReferenceEquals(element.Value,trigger)) do
                let current=element.Value
                let overflow=computed.Get(current,None).getPropertyValue "overflow-y"
                if overflow<>"hidden" && overflow<>"visible" && current.scrollHeight>current.clientHeight then
                    found<-true
                    let atTop=current.scrollTop<=3
                    let atBottom=current.scrollTop>=current.scrollHeight-current.clientHeight-3
                    consumes<-(not atTop && not atBottom)||(atTop && movedY<0.)||(atBottom && movedY>0.)
                element<-match current.parentElement with :? HTMLElement as parent -> Some parent | _ -> None
            consumes
        | _ -> false
    let releaseStyles () =
        let pointer,grab=pointerRestore,grabRestore
        pointerRestore<-None;grabRestore<-None
        cleanup [(fun () -> restore pointer);(fun () -> restore grab)
                 (fun () -> (trigger:>EventTarget).removeEventListener("touchstart",prevent))
                 (fun () -> (trigger:>EventTarget).removeEventListener("touchmove",prevent))
                 (fun () -> (trigger:>EventTarget).removeEventListener("touchend",prevent))]
    let move:EventListener=Closure.keep <| fun event ->
        if not disposed && controller.Grabbed then
            match point event with
            | ValueSome(struct(x,y,_)) when nestedScroller event (y-pointerOriginY) ->
                controller.RebasePointer(x,y)
                pointerOriginY<-y
            | ValueSome(struct(x,y,_)) ->
                prevent event
                if pointerRestore.IsNone then
                    pointerRestore<-Some(DragDomStyles.snapshot trigger "pointer-events")
                    DragDomStyles.apply trigger "pointer-events" "none"
                    (trigger:>EventTarget).addEventListener("touchstart",prevent,AddEventListenerOptions(passive=Some false))
                    (trigger:>EventTarget).addEventListener("touchmove",prevent,AddEventListenerOptions(passive=Some false))
                    (trigger:>EventTarget).addEventListener("touchend",prevent)
                controller.Move(x,y)
            | ValueNone -> ()
    let up:EventListener=Closure.keep <| fun _ ->
        if not disposed then
            releaseStyles()
            cleanupDocument()
            scroll |> Option.iter _.Stop()
            if not controller.Cancelled then
                controller.Release()
                if not controller.Cancelled then scroll |> Option.iter(fun scroll -> scroll.ScrollInViewPrecise(controller.ReleaseDuration,gap=scroll.Settings.Threshold,ease=controller.ReleaseEase))
    let select:EventListener=prevent
    let attachDocument () =
        if not listening then
            listening<-true
            for name in ["mousemove";"touchmove"] do (document:>EventTarget).addEventListener(name,move)
            for name in ["mouseup";"touchend";"touchcancel"] do (document:>EventTarget).addEventListener(name,up)
            (document:>EventTarget).addEventListener("selectstart",select)
    do cleanupDocument<-fun () ->
        if listening then
            listening<-false
            cleanup (seq {
                for name in ["mousemove";"touchmove"] do yield fun () -> (document:>EventTarget).removeEventListener(name,move)
                for name in ["mouseup";"touchend";"touchcancel"] do yield fun () -> (document:>EventTarget).removeEventListener(name,up)
                yield fun () -> (document:>EventTarget).removeEventListener("selectstart",select) })
    let down:EventListener=Closure.keep <| fun event ->
        if not disposed && enabled && not controller.Cancelled && not controller.Grabbed then
            let range=match event.target with :? HTMLInputElement as input -> input.getAttribute "type"="range" | _ -> false
            if not range then
                event.stopPropagation()
                updateMatrix()
                match point event with
                | ValueSome(struct(x,y,_)) ->
                    let measure ()=match scroll with Some value -> value.Refresh() | None -> controller.WithOrigin(ignore)
                    let prepare () =
                        pointerOriginY<-y
                        restore hoverRestore;hoverRestore<-None
                        restore zRestore
                        zRestore<-Some(DragDomStyles.snapshot target "z-index")
                        let current=computed.Get(target,None).getPropertyValue "z-index"
                        let mutable index=0.
                        Double.TryParse(current,Globalization.NumberStyles.Float,Globalization.CultureInfo.InvariantCulture,&index) |> ignore
                        DragDomStyles.apply target "z-index" ((DragDomStyles.nextZ index).ToString(Globalization.CultureInfo.InvariantCulture))
                        if finePointer && cursorEnabled then
                            let body=document.body
                            grabRestore<-Some(DragDomStyles.snapshot body "cursor")
                            DragDomStyles.apply body "cursor" (defaultArg cursorGrab "grabbing")
                        scroll |> Option.iter(fun value -> value.ScrollInView(100u,ease=Ease.named "outCubic"))
                    controller.Grab(x,y,coarse=not finePointer,measure=measure,beforeNotify=prepare)
                    if not disposed && controller.Grabbed then
                        attachDocument()
                        scroll |> Option.iter _.Start()
                | ValueNone -> ()
    let hover:EventListener=Closure.keep <| fun _ ->
        if not disposed && enabled && not controller.Cancelled && finePointer && cursorEnabled && hoverRestore.IsNone then
            hoverRestore<-Some(DragDomStyles.snapshot trigger "cursor")
            DragDomStyles.apply trigger "cursor" (defaultArg cursorHover "grab")
    let live ()=if disposed || not(ownerAllowsWrite()) || not(constructionAllowed()) then raise(ObjectDisposedException(nameof DraggableDom))
    do
        try
            updatePointerType()
            this.Enable()
            this.Refresh()
            let resized (_:ResizeObserverEntry array) =
                if initialResize then initialResize<-false
                elif not disposed then
                    timeout |> Option.iter _.Dispose()
                    timeout<-Some(scheduler.Timeout(150.,fun () ->
                        timeout<-None
                        if not disposed then
                            onResize |> Option.iter(fun callback -> callback controller)
                            if not disposed && not controller.Cancelled then
                                this.Refresh()
                                if not disposed then onAfterResize |> Option.iter(fun callback -> callback controller)))
            let observed=resize.Create(resized)
            observer<-Some observed
            observed.Observe target
            container |> Option.iter(fun container -> observed.Observe container)
        with _ ->
            try this.Cancel() with _ -> ()
            reraise()
    do ScopeRegistration.register this.Revert
    /// <summary>Underlying physics and animatable controller.</summary>
    member _.Controller=controller
    /// <summary>Refreshes dynamic controller settings and the native inverse ancestor transform.</summary>
    member _.Refresh() =
        live()
        controller.RefreshWithMeasure(scroll |> Option.map(fun value -> value.Refresh))
        updatePointerType();updateMatrix()
    /// <summary>Installs trigger listeners and axis-appropriate touch-action.</summary>
    member _.Enable() =
        live()
        if not enabled then
            enabled<-true
            controller.Enable()
            target.classList.remove [|"is-disabled"|]
            touchRestore<-Some(DragDomStyles.snapshot trigger "touch-action")
            let settings=controller.Settings
            DragDomStyles.apply trigger "touch-action" (if not settings.XEnabled then "pan-x" elif not settings.YEnabled then "pan-y" else "none")
            (trigger:>EventTarget).addEventListener("mousedown",down,AddEventListenerOptions(passive=Some true))
            (trigger:>EventTarget).addEventListener("touchstart",down,AddEventListenerOptions(passive=Some true))
            (trigger:>EventTarget).addEventListener("mouseenter",hover)
    /// <summary>Removes gesture listeners and restores only owned interaction properties.</summary>
    member _.Disable() =
        if enabled then
            enabled<-false
            cleanup [
                controller.Disable
                (fun () -> target.classList.add [|"is-disabled"|])
                (fun () -> scroll |> Option.iter _.Disable())
                cleanupDocument
                (fun () -> (trigger:>EventTarget).removeEventListener("mousedown",down))
                (fun () -> (trigger:>EventTarget).removeEventListener("touchstart",down))
                (fun () -> (trigger:>EventTarget).removeEventListener("mouseenter",hover))
                releaseStyles
                (fun () -> let restoreOriginal=hoverRestore in hoverRestore<-None;restore restoreOriginal)
                (fun () -> let restoreOriginal=touchRestore in touchRestore<-None;restore restoreOriginal)
                (fun () -> let restoreOriginal=zRestore in zRestore<-None;restore restoreOriginal) ]
    /// <summary>Stops controller and native scrolling motion while retaining styles and input subscriptions.</summary>
    member _.Stop()=live();controller.Stop();scroll |> Option.iter _.StopMotion()
    /// <summary>Clears the gesture, pending resize debounce and native scrolling motion, then silently resets coordinates to zero.</summary>
    member _.Reset() =
        live()
        timeout |> Option.iter _.Dispose();timeout<-None
        scroll |> Option.iter _.Reset()
        controller.Reset()
    /// <summary>Detaches observation, pending debounce and input, then restores captured axis accessors.</summary>
    member _.Revert() =
        if not disposed then
            disposed<-true
            cleanup [
                this.Disable
                (fun () -> timeout |> Option.iter _.Dispose();timeout<-None)
                (fun () -> observer |> Option.iter _.Disconnect();observer<-None)
                (fun () -> scroll |> Option.iter _.Cancel())
                controller.Revert
                (fun () -> target.classList.remove [|"is-disabled"|]) ]
    /// <summary>Detaches observation, pending debounce and input, retaining final axis values.</summary>
    member _.Cancel() =
        if not disposed then
            disposed<-true
            cleanup [
                this.Disable
                (fun () -> timeout |> Option.iter _.Dispose();timeout<-None)
                (fun () -> observer |> Option.iter _.Disconnect();observer<-None)
                (fun () -> scroll |> Option.iter _.Cancel())
                controller.Cancel ]
    interface IDisposable with member _.Dispose()=this.Cancel()

/// <summary>Native gesture ownership in the current Ranvier owner.</summary>
[<RequireQualifiedAccess>]
module DraggableDom =
    /// <summary>Owner disposal removes listeners, observation and every controller frame.</summary>
    let own (binding:DraggableDom)=ScopeRegistration.attach(binding:>IDisposable);binding
