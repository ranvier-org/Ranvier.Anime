namespace Ranvier.Anime

open System
open Ranvier
open Ranvier.Dom
open Ranvier.Css

type internal NativeDragCapture =
    { Restore:(unit -> bool) -> unit
      Cancel:unit -> unit }

/// <summary>One native draggable lifetime with ordered input/controller restoration and owned CSS snapshots.</summary>
/// <remarks>Cancel retains rendered values and is terminal. Revert restores while the captured owner remains live, then cancels.</remarks>
type NativeDraggable internal(controller:Draggable,input:DraggableDom option,scroll:DraggableScroll option,
                              captures:NativeDragCapture array) as this =
    let captures=Array.copy captures
    let ownerAllowsWrite=ScopeRegistration.captureWriteGuard()
    let mutable state=0
    let live ()=if state<>0 || controller.Cancelled || not(ownerAllowsWrite()) then raise(ObjectDisposedException(nameof NativeDraggable))
    let cleanup actions =
        let mutable first:exn option=None
        for action in actions do
            try action() with error -> if first.IsNone then first<-Some error
        first |> Option.iter(fun error -> Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw())
    /// <summary>Borrowed physics and existing Animatable playback controller.</summary>
    member _.Controller=controller
    /// <summary>Current horizontal source coordinate in its captured scalar unit.</summary>
    member _.X with get()=controller.X and set value=live();controller.X<-value
    /// <summary>Current vertical source coordinate in its captured scalar unit.</summary>
    member _.Y with get()=controller.Y and set value=live();controller.Y<-value
    /// <summary>Horizontal container progress.</summary>
    member _.ProgressX with get()=controller.ProgressX and set value=live();controller.ProgressX<-value
    /// <summary>Vertical container progress.</summary>
    member _.ProgressY with get()=controller.ProgressY and set value=live();controller.ProgressY<-value
    /// <summary>Last horizontal release destination.</summary>
    member _.DestX=controller.DestX
    /// <summary>Last vertical release destination.</summary>
    member _.DestY=controller.DestY
    /// <summary>Last horizontal rendered movement.</summary>
    member _.DeltaX=controller.DeltaX
    /// <summary>Last vertical rendered movement.</summary>
    member _.DeltaY=controller.DeltaY
    /// <summary>Current sampled source velocity.</summary>
    member _.Velocity=controller.Velocity
    /// <summary>Current pointer or release angle in radians.</summary>
    member _.Angle=controller.Angle
    /// <summary>Existing retargetable animation group, with its source-compatible independent callback clock.</summary>
    member _.Animate=controller.Animate
    /// <summary>Whether input currently owns a gesture.</summary>
    member _.Grabbed=controller.Grabbed
    /// <summary>Whether input crossed its configured threshold.</summary>
    member _.Dragged=controller.Dragged
    /// <summary>The controller's release state flag.</summary>
    member _.Released=controller.Released
    /// <summary>Whether fresh input gestures are enabled.</summary>
    member _.Enabled=controller.Enabled
    /// <summary>Whether cancellation or captured owner disposal ended writes.</summary>
    member _.Cancelled=state=2 || controller.Cancelled || not(ownerAllowsWrite())
    /// <summary>Refreshes function-based settings, native containment and pointer geometry.</summary>
    member _.Refresh()=live();match input with Some binding -> binding.Refresh() | None -> controller.Refresh()
    /// <summary>Stops controller and native scrolling motion without releasing captures.</summary>
    member _.Stop()=live();match input with Some binding -> binding.Stop() | None -> controller.Stop()
    /// <summary>Resets coordinates and pending input/resize motion.</summary>
    member _.Reset()=live();match input with Some binding -> binding.Reset() | None -> controller.Reset()
    /// <summary>Installs input listeners and touch-action after Disable.</summary>
    member _.Enable()=live();match input with Some binding -> binding.Enable() | None -> controller.Enable()
    /// <summary>Removes input listeners while retaining property captures.</summary>
    member _.Disable()=live();match input with Some binding -> binding.Disable() | None -> controller.Disable()
    /// <summary>Scrolls the native release destination into view using unsigned whole milliseconds.</summary>
    member _.ScrollInView(?duration:uint,?gap:float,?ease:float -> float)=live();scroll |> Option.iter(fun value -> value.ScrollInView(?duration=duration,?gap=gap,?ease=ease))
    /// <summary>Scrolls the native release destination into view using precise milliseconds.</summary>
    member _.ScrollInViewPrecise(duration,?gap:float,?ease:float -> float)=live();scroll |> Option.iter(fun value -> value.ScrollInViewPrecise(duration,?gap=gap,?ease=ease))
    /// <summary>Moves enabled axes into the native scroll viewport using unsigned whole milliseconds.</summary>
    member _.AnimateInView(?duration:uint,?gap:float,?ease:float -> float)=live();scroll |> Option.iter(fun value -> value.AnimateInView(?duration=duration,?gap=gap,?ease=ease))
    /// <summary>Moves enabled axes into the native scroll viewport using precise milliseconds.</summary>
    member _.AnimateInViewPrecise(duration,?gap:float,?ease:float -> float)=live();scroll |> Option.iter(fun value -> value.AnimateInViewPrecise(duration,?gap=gap,?ease=ease))
    /// <summary>Restores DOM/controller state before CSS captures, then cancels every child even after cleanup failures.</summary>
    member _.Revert() =
        if state=0 then
            state<-1
            let current ()=state=1 && ownerAllowsWrite()
            cleanup (seq {
                yield fun () -> if current() then match input with Some binding -> binding.Revert() | None -> controller.Revert()
                for capture in captures do yield fun () -> if current() then capture.Restore current
                yield this.Cancel })
    /// <summary>Stops input and playback before ending CSS captures; repeated cancellation is harmless.</summary>
    member _.Cancel() =
        if state<>2 then
            state<-2
            cleanup (seq {
                yield fun () -> match input with Some binding -> binding.Cancel() | None -> controller.Cancel()
                yield fun () -> scroll |> Option.iter _.Cancel()
                for capture in captures do yield capture.Cancel })
    interface IDisposable with member _.Dispose()=this.Cancel()

/// <summary>Native axis mapping to source translations, scalar transform functions, numeric identities or typed CSS declarations.</summary>
/// <remarks>Transform accepts the source's fifteen scalar transform names and preserves authored scalar units. Complex inline values are rejected explicitly.</remarks>
[<RequireQualifiedAccess;NoEquality;NoComparison>]
type DraggableAxisMapping =
    | Translation
    | Transform of name:string
    | Numeric of NumericProperty
    | Declaration of initial:float * author:(float -> Declaration)

/// <summary>Actual native host services borrowed for the lifetime of a draggable.</summary>
[<NoEquality;NoComparison>]
type DraggableNativeServices =
    { Scheduler:FrameScheduler; Document:Document; Computed:ComputedStyleService
      Geometry:GeometryService; Resize:ResizeObservationService; Media:MediaQueryService
      Viewport:ViewportService; WindowScroll:ViewportScrollService }

/// <summary>Controller and native resize callbacks, all running on the host owner thread.</summary>
[<NoEquality;NoComparison>]
type DraggableCallbacks =
    { OnGrab:NativeDraggable -> unit; OnDrag:NativeDraggable -> unit; OnRelease:NativeDraggable -> unit
      OnUpdate:NativeDraggable -> unit; OnSettle:NativeDraggable -> unit; OnSnap:NativeDraggable -> unit
      OnResize:NativeDraggable -> unit; OnAfterResize:NativeDraggable -> unit }
    /// <summary>No-op callbacks.</summary>
    static member Default={OnGrab=ignore;OnDrag=ignore;OnRelease=ignore;OnUpdate=ignore;OnSettle=ignore;OnSnap=ignore;OnResize=ignore;OnAfterResize=ignore}

/// <summary>Native draggable authoring, including dynamic container/physics settings and default source translations.</summary>
[<NoEquality;NoComparison>]
type NativeDraggableOptions =
    { X:DraggableAxisMapping; Y:DraggableAxisMapping
      Container:unit -> HTMLElement option; Trigger:HTMLElement option
      Physics:unit -> DraggableSettings; Scroll:unit -> DraggableScrollSettings
      Modifier:float -> float; ModifierX:(float -> float) option; ModifierY:(float -> float) option
      ReleaseSpring:Spring option; ReleaseEase:(float -> float) option
      ReleaseMass:float; ReleaseStiffness:float; ReleaseDamping:float
      Cursor:bool; CursorHover:string; CursorGrab:string; Callbacks:DraggableCallbacks }
    /// <summary>Source translateX/translateY mappings, unconstrained body interaction and native source defaults.</summary>
    static member Default =
        { X=DraggableAxisMapping.Translation;Y=DraggableAxisMapping.Translation;Container=(fun()->None);Trigger=None
          Physics=(fun()->DraggableSettings.Default);Scroll=(fun()->DraggableScrollSettings.Default)
          Modifier=id;ModifierX=None;ModifierY=None;ReleaseSpring=None;ReleaseEase=None
          ReleaseMass=1.;ReleaseStiffness=80.;ReleaseDamping=20.;Cursor=true;CursorHover="grab";CursorGrab="grabbing"
          Callbacks=DraggableCallbacks.Default }

/// <summary>Default native target construction and one outer scope/owner lifetime.</summary>
[<RequireQualifiedAccess>]
module NativeDraggable =
    /// <summary>Attaches only the outer facade to the actual selected Ranvier owner.</summary>
    let own (draggable:NativeDraggable)=ScopeRegistration.attach(draggable:>IDisposable);draggable

    /// <summary>Constructs a source-compatible native draggable; the caller owns it unless it is scoped or explicitly owned.</summary>
    let createWith (options:NativeDraggableOptions) (services:DraggableNativeServices) (target:HTMLElement) =
        if isNull(box target) then nullArg "target"
        if not(ScopeRegistration.canConstruct()) then raise(ObjectDisposedException(nameof NativeDraggable))
        let build () =
            let captures=ResizeArray<NativeDragCapture>()
            let mutable building=true
            let constructionCurrent ()=not building || ScopeRegistration.canConstruct()
            let mutable controller:Draggable option=None
            let mutable scroll:DraggableScroll option=None
            let mutable input:DraggableDom option=None
            let mutable facade:NativeDraggable option=None
            let callback select (_:Draggable)=facade |> Option.iter(select options.Callbacks)
            let cleanup () =
                let attempt action=try action() with _ -> ()
                input |> Option.iter(fun value->attempt value.Revert)
                controller |> Option.iter(fun value->attempt value.Revert)
                scroll |> Option.iter(fun value->attempt value.Cancel)
                for capture in captures do attempt(fun()->capture.Restore constructionCurrent);attempt capture.Cancel
            try
                let mutable container=defaultArg (options.Container()) services.Document.body
                let mutable scrollSettings=options.Scroll()
                let mutable physics=options.Physics()
                let mutable contained=false
                let refreshSettings () =
                    let requested=options.Container()
                    container<-defaultArg requested services.Document.body
                    scrollSettings<-options.Scroll()
                    physics<-options.Physics()
                    contained<-requested.IsSome || scrollSettings.ExplicitBounds.IsSome || physics.Contained
                    {physics with Contained=contained}
                let getScroll () =
                    {scrollSettings with UseWindow=Object.ReferenceEquals(container,services.Document.body);Contained=contained}
                let isTransform=function DraggableAxisMapping.Translation | DraggableAxisMapping.Transform _ -> true | _ -> false
                let needsTranslation=isTransform options.X || isTransform options.Y
                let translation=
                    if needsTranslation then
                        let value=DraggableTransformTarget target
                        captures.Add {Restore=(fun current->value.Restore(fun()->current() && constructionCurrent()));Cancel=value.Cancel}
                        Some value
                    else None
                let bind axis mapping =
                    match mapping with
                    | DraggableAxisMapping.Translation -> (if axis=0 then translation.Value.X else translation.Value.Y),None
                    | DraggableAxisMapping.Transform name -> translation.Value.Property(name),None
                    | DraggableAxisMapping.Numeric property -> property,None
                    | DraggableAxisMapping.Declaration(initial,author) ->
                        let value=new DraggableCssAxis(target,initial,fun magnitude ->
                            let declaration=author magnitude
                            if not(constructionCurrent()) then raise(ObjectDisposedException(nameof NativeDraggable))
                            declaration)
                        let capture={Restore=(fun current->value.Restore(fun()->current() && constructionCurrent()));Cancel=value.Cancel}
                        value.Property,Some(value.Name,capture)
                let x,xCapture=bind 0 options.X
                let y,yCapture=bind 1 options.Y
                let same=match xCapture,yCapture with Some(x,_),Some(y,_) -> x=y | _ -> false
                let x=
                    if same then
                        xCapture |> Option.iter(fun (_,capture)->capture.Cancel())
                        y
                    else x
                if not same then xCapture |> Option.iter(fun (_,capture)->captures.Add capture)
                yCapture |> Option.iter(fun (_,capture)->captures.Add capture)
                let modifier callback value =
                    let result=callback value
                    if not(constructionCurrent()) then raise(ObjectDisposedException(nameof NativeDraggable))
                    result
                let value=new Draggable(services.Scheduler,x,y,settings=refreshSettings,
                    modifierX=modifier(defaultArg options.ModifierX options.Modifier),modifierY=modifier(defaultArg options.ModifierY options.Modifier),
                    ?releaseSpring=options.ReleaseSpring,?releaseEase=options.ReleaseEase,
                    releaseMass=options.ReleaseMass,releaseStiffness=options.ReleaseStiffness,releaseDamping=options.ReleaseDamping,
                    onGrab=callback(fun callbacks -> callbacks.OnGrab),onDrag=callback(fun callbacks -> callbacks.OnDrag),onRelease=callback(fun callbacks -> callbacks.OnRelease),onUpdate=callback(fun callbacks -> callbacks.OnUpdate),onSettle=callback(fun callbacks -> callbacks.OnSettle),onSnap=callback(fun callbacks -> callbacks.OnSnap))
                controller<-Some value
                let nativeScroll=new DraggableScroll(value,services.Scheduler,target,container,services.Computed,settings=getScroll,
                                                     containerProvider=(fun()->container),viewport=services.Viewport,windowScroll=services.WindowScroll)
                scroll<-Some nativeScroll
                let trigger=defaultArg options.Trigger target
                let binding=new DraggableDom(value,services.Scheduler,target,trigger,services.Document,services.Computed,
                    services.Geometry,services.Resize,services.Media,container=container,scroll=nativeScroll,constructionGuard=constructionCurrent,cursor=options.Cursor,
                    cursorHover=options.CursorHover,cursorGrab=options.CursorGrab,onResize=callback(fun callbacks -> callbacks.OnResize),onAfterResize=callback(fun callbacks -> callbacks.OnAfterResize))
                input<-Some binding
                if not(ScopeRegistration.canConstruct()) then raise(ObjectDisposedException(nameof NativeDraggable))
                building<-false
                let completed=new NativeDraggable(value,input,scroll,captures.ToArray())
                facade<-Some completed
                completed
            with _ -> cleanup();reraise()
        let value=ScopeRegistration.run ignore build
        ScopeRegistration.register value.Revert
        value

    /// <summary>Constructs default translateX/translateY behavior using the real native services.</summary>
    let create services target=createWith NativeDraggableOptions.Default services target
    /// <summary>Constructs and attaches the complete native facade to the actual Ranvier owner.</summary>
    let createOwnedWith options services target=createWith options services target |> own
    /// <summary>Resolves one native selector target through the owning document and constructs its draggable.</summary>
    let selectorWith options (services:DraggableNativeServices) selector =
        match services.Document.querySelector selector with
        | :? HTMLElement as target -> createWith options services target
        | _ -> invalidArg "selector" "Expected a matching native HTML element."
