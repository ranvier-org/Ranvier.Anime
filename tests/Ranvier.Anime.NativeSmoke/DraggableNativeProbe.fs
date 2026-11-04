namespace Ranvier.Anime.Tests

open System
open Ranvier
open Ranvier.Dom
open Ranvier.Css
open Ranvier.Css.Dom
open Ranvier.Anime

/// <summary>Real-host dragging probes using native hit-testing and frame advancement.</summary>
module DraggableNativeProbe =
    /// <summary>Checks native mouse input, CSS rendering, thresholds, cancellation and property-scoped restoration.</summary>
    /// <remarks>Call inside a real Ranvier owner. Input must enter through native host hit-testing and event dispatch.</remarks>
    let private runGesture threshold (scheduler:FrameScheduler) (document:Document) (computed:ComputedStyleService)
            (geometry:GeometryService) (resize:ResizeObservationService) (media:MediaQueryService)
            (dispatchInput:string -> float -> float -> unit) (advance:uint -> unit) =
        let container=document.createElement("div") :?> HTMLElement
        let target=document.createElement("div") :?> HTMLElement
        let containerStyle=(container:>ElementCSSInlineStyle).style
        let style=(target:>ElementCSSInlineStyle).style
        StyleDeclaration.applyMany containerStyle [
            Declaration.create "position" "relative";Declaration.create "width" "100px"
            Declaration.create "height" "100px";Declaration.create "overflow" "hidden" ]
        StyleDeclaration.applyMany style [
            Declaration.create "position" "absolute";Declaration.create "width" "20px";Declaration.create "height" "20px" ]
        container.appendChild target |> ignore
        document.body.appendChild container |> ignore
        let px (value:float)=value.ToString("G17",Globalization.CultureInfo.InvariantCulture)+"px"
        let horizontal=new DraggableCssAxis(target,0.,fun value -> Declaration.create "left" (px value))
        let vertical=new DraggableCssAxis(target,0.,fun value -> Declaration.create "top" (px value))
        let callbacks=ResizeArray<string>()
        let settings ()={DraggableSettings.Default with Contained=true;Bounds={Top=0.;Right=80.;Bottom=80.;Left=0.}}
        let controller=new Draggable(scheduler,horizontal.Property,vertical.Property,settings=settings,
                                     onGrab=(fun _->callbacks.Add "grab"),onDrag=(fun _->callbacks.Add "drag"),
                                     onUpdate=(fun _->callbacks.Add "update"),onRelease=(fun _->callbacks.Add "release"))
        let scroll=new DraggableScroll(controller,scheduler,target,container,computed,
                        settings=(fun()->{DraggableScrollSettings.Default with ExplicitBounds=Some(settings().Bounds)}))
        let binding=new DraggableDom(controller,scheduler,target,target,document,computed,geometry,resize,media,container=container,scroll=scroll)
        DraggableDom.own binding |> ignore
        let require passed message=if not passed then failwith message
        try
            advance 17u
            let rect=target.getBoundingClientRect()
            let startX,startY=rect.left+1.,rect.top+1.
            dispatchInput "mousedown" startX startY
            dispatchInput "mousemove" (startX+threshold) startY
            require (not controller.Dragged) "Equal threshold must not drag."
            dispatchInput "mousemove" (startX+20.) (startY+10.)
            advance 51u
            require (abs(controller.X-20.)<0.001 && abs(controller.Y-10.)<0.001) "Native axes did not render pointer movement."
            require (callbacks.Contains "update") "Native movement did not emit onUpdate."
            let nativeLeft=computed.Get(target,None).getPropertyValue "left"
            require (nativeLeft="20px") ("Native computed left: "+nativeLeft)
            dispatchInput "mouseup" (startX+20.) (startY+10.)
            require (callbacks.Contains "release") "Native release callback did not run."
            StyleDeclaration.apply style (Declaration.create "opacity" "0.6")
            binding.Revert()
            horizontal.Revert();vertical.Revert()
            require (style.getPropertyValue "left"="" && style.getPropertyValue "top"="") "Original absent axis declarations were not restored."
            require (style.getPropertyValue "opacity"="0.6") "Revert overwrote an unrelated concurrent declaration."
            let count=callbacks.Count
            dispatchInput "mousedown" startX startY
            dispatchInput "mousemove" (startX+50.) (startY+50.)
            advance 51u
            require (callbacks.Count=count) "Reverted native listeners remained active."
            callbacks.ToArray()
        finally
            binding.Cancel();horizontal.Cancel();vertical.Cancel()
            container.remove()

    /// <summary>Checks default native transform mapping, composite restoration and actual owner cleanup.</summary>
    let runDefault (services:DraggableNativeServices) dispatchInput (advance:uint -> unit) =
        let target=services.Document.createElement("div") :?> HTMLElement
        let style=(target:>ElementCSSInlineStyle).style
        let original="rotate(5deg) translate(3px, 4px)"
        StyleDeclaration.applyMany style [
            Declaration.create "position" "absolute";Declaration.create "left" "20px";Declaration.create "top" "20px"
            Declaration.create "width" "20px";Declaration.create "height" "20px"
            Declaration.important(Declaration.create "transform" original)]
        services.Document.body.appendChild target |> ignore
        let callbacks=ResizeArray<string>()
        let options={NativeDraggableOptions.Default with
                        Callbacks={DraggableCallbacks.Default with OnGrab=(fun _->callbacks.Add "grab");OnUpdate=(fun _->callbacks.Add "update");OnRelease=(fun _->callbacks.Add "release")}}
        let facade=NativeDraggable.createOwnedWith options services target
        let require result message=if not result then failwith message
        try
            advance 17u
            let before=target.getBoundingClientRect()
            let x,y=before.left+before.width/2.,before.top+before.height/2.
            dispatchInput "mousedown" x y
            dispatchInput "mousemove" (x+20.) (y+10.)
            advance 51u
            require (abs(facade.X-23.)<0.001 && abs(facade.Y-14.)<0.001) "Default native translation did not retain initial scalar coordinates."
            let rendered=style.getPropertyValue "transform"
            require (rendered.StartsWith("translate(23px, 14px) rotate(5deg)",StringComparison.Ordinal)) ("Default source transform order: "+rendered)
            let after=target.getBoundingClientRect()
            require (abs(after.left-before.left-20.)<0.01 && abs(after.top-before.top-10.)<0.01) "Native geometry did not reflect transformed pointer movement."
            require (callbacks.Contains "update") "Default native movement lacked callback."
            dispatchInput "mouseup" (x+20.) (y+10.)
            require (callbacks.Contains "release") "Default native release lacked callback."
            StyleDeclaration.apply style (Declaration.create "opacity" "0.6")
            facade.Revert()
            require (style.getPropertyValue "transform"=original) "Composite revert did not restore exact original transform."
            require (style.getPropertyPriority "transform"="important") "Composite revert lost original transform priority."
            require (style.getPropertyValue "opacity"="0.6") "Composite revert changed concurrent unrelated CSS."
            let count=callbacks.Count
            dispatchInput "mousedown" x y
            dispatchInput "mousemove" (x+50.) (y+50.)
            advance 51u
            require (callbacks.Count=count) "Composite revert retained native input listeners."
            callbacks.ToArray()
        finally
            facade.Cancel()
            target.remove()

    /// <summary>Checks scalar transform mapTo authoring and shared same-property mapping identities on a real target.</summary>
    let mappedTransforms (services:DraggableNativeServices) =
        let target=services.Document.createElement("div") :?> HTMLElement
        let style=(target:>ElementCSSInlineStyle).style
        let original="translate(3px, 4px) rotate(5deg) scale(2)"
        StyleDeclaration.apply style (Declaration.create "transform" original)
        services.Document.body.appendChild target |> ignore
        let options={NativeDraggableOptions.Default with X=DraggableAxisMapping.Transform "rotate";Y=DraggableAxisMapping.Transform "scale"}
        let require result message=if not result then failwith message
        try
            use facade=NativeDraggable.createWith options services target
            require (facade.X=5. && facade.Y=2.) "Mapped scalar initial values."
            facade.X<-15.
            facade.Y<-3.
            require (style.getPropertyValue "transform"="translate(3px, 4px) rotate(15deg) scale(3)") "Mapped transform units/order."
            facade.Revert()
            require (style.getPropertyValue "transform"=original) "Mapped transform snapshot restoration."
            use alias=NativeDraggable.createWith {options with X=DraggableAxisMapping.Transform "translateX";Y=DraggableAxisMapping.Translation} services target
            alias.Revert()
            use shared=NativeDraggable.createWith {options with X=DraggableAxisMapping.Transform "translateX";Y=DraggableAxisMapping.Transform "translateX"} services target
            shared.X<-12.
            require (shared.Y=12.) "Same mapTo must share property identity."
            shared.Revert()
        finally target.remove()

    /// <summary>Checks full-factory rollback after native listener/style installation and ResizeObserver failure.</summary>
    let compositeConstructorFailure (services:DraggableNativeServices) dispatchInput (advance:uint -> unit) =
        use failingResize =
            { new ResizeObservationService with
                member _.Supports _=true
                member _.Create(_,?signal)=raise(InvalidOperationException("Probe ResizeObserver failure"))
              interface IDisposable with member _.Dispose()=() }
        let target=services.Document.createElement("div") :?> HTMLElement
        let style=(target:>ElementCSSInlineStyle).style
        StyleDeclaration.applyMany style [
            Declaration.create "position" "absolute";Declaration.create "width" "20px";Declaration.create "height" "20px"
            Declaration.create "transform" "rotate(4deg)";Declaration.create "touch-action" "pan-y"]
        services.Document.body.appendChild target |> ignore
        let mutable callbacks=0
        let options={NativeDraggableOptions.Default with Callbacks={DraggableCallbacks.Default with OnGrab=(fun _->callbacks<-callbacks+1)}}
        try
            let mutable rejected=false
            try
                use facade=NativeDraggable.createWith options {services with Resize=failingResize} target
                ()
            with :? InvalidOperationException -> rejected<-true
            if not rejected || style.getPropertyValue "transform"<>"rotate(4deg)" || style.getPropertyValue "touch-action"<>"pan-y" then
                failwith "Composite constructor retained modified captured CSS."
            advance 17u
            let rect=target.getBoundingClientRect()
            dispatchInput "mousedown" (rect.left+rect.width/2.) (rect.top+rect.height/2.)
            dispatchInput "mousemove" (rect.left+30.) (rect.top+20.)
            dispatchInput "mouseup" (rect.left+30.) (rect.top+20.)
            advance 51u
            if callbacks<>0 then failwith "Failed composite constructor retained native listeners."
        finally target.remove()

    /// <summary>Checks complex scalar rejection before owned scheduling, observation or input installation, preserving exact CSS.</summary>
    let complexInlineRejection (services:DraggableNativeServices) dispatchInput (advance:uint -> unit) =
        for original,xMapping in [
            "translateX(calc(10px + 2%))",DraggableAxisMapping.Translation
            "translateX(var(--offset, 2px))",DraggableAxisMapping.Translation
            "skew(4deg,5deg)",DraggableAxisMapping.Transform "skew" ] do
            let target=services.Document.createElement("div") :?> HTMLElement
            let style=(target:>ElementCSSInlineStyle).style
            StyleDeclaration.applyMany style [
                Declaration.create "position" "absolute";Declaration.create "width" "20px";Declaration.create "height" "20px"
                Declaration.important(Declaration.create "transform" original);Declaration.create "touch-action" "pan-y"]
            services.Document.body.appendChild target |> ignore
            let capturedTransform=style.getPropertyValue "transform"
            let capturedPriority=style.getPropertyPriority "transform"
            let mutable scheduled=0
            let mutable observed=0
            let mutable callbacks=0
            use scheduler =
                { new FrameScheduler with
                    member _.Now()=services.Scheduler.Now()
                    member _.Frame callback=scheduled<-scheduled+1;services.Scheduler.Frame callback
                    member _.Timeout(delay,callback)=scheduled<-scheduled+1;services.Scheduler.Timeout(delay,callback)
                    member _.Microtask callback=scheduled<-scheduled+1;services.Scheduler.Microtask callback
                  interface IDisposable with member _.Dispose()=() }
            use resize =
                { new ResizeObservationService with
                    member _.Supports capability=services.Resize.Supports capability
                    member _.Create(callback,?signal)=observed<-observed+1;services.Resize.Create(callback,?signal=signal)
                  interface IDisposable with member _.Dispose()=() }
            let options={NativeDraggableOptions.Default with X=xMapping;Callbacks={DraggableCallbacks.Default with OnGrab=(fun _->callbacks<-callbacks+1)}}
            try
                let mutable rejected=false
                try
                    use facade=NativeDraggable.createWith options {services with Scheduler=scheduler;Resize=resize} target
                    ()
                with :? NotSupportedException -> rejected<-true
                if not rejected then failwith ("Complex native transform was not rejected: "+capturedTransform)
                if scheduled<>0 || observed<>0 then failwith "Rejected complex mapping started owned scheduling or observation."
                if style.getPropertyValue "transform"<>capturedTransform || style.getPropertyPriority "transform"<>capturedPriority || style.getPropertyValue "touch-action"<>"pan-y" then
                    failwith "Rejected complex mapping changed original CSS."
                advance 17u
                let rect=target.getBoundingClientRect()
                dispatchInput "mousedown" (rect.left+rect.width/2.) (rect.top+rect.height/2.)
                dispatchInput "mouseup" (rect.left+rect.width/2.) (rect.top+rect.height/2.)
                if callbacks<>0 then failwith "Rejected complex mapping installed native gesture listeners."
            finally target.remove()

    /// <summary>Checks native mouse input with the host's current primary-pointer threshold.</summary>
    let run scheduler document computed geometry resize (media:MediaQueryService) dispatchMouse advance =
        use query=media.Match "(pointer:fine)"
        runGesture (if query.Matches then 3. else 7.) scheduler document computed geometry resize media dispatchMouse advance

    /// <summary>Checks native touch input with the host's current primary-pointer threshold.</summary>
    let runTouch scheduler document computed geometry resize (media:MediaQueryService) dispatchTouch advance =
        use query=media.Match "(pointer:fine)"
        runGesture (if query.Matches then 3. else 7.) scheduler document computed geometry resize media dispatchTouch advance

    /// <summary>Checks rollback when native ResizeObserver creation fails after trigger listeners were installed.</summary>
    let constructorFailure (scheduler:FrameScheduler) (document:Document) (computed:ComputedStyleService)
                           (geometry:GeometryService) (media:MediaQueryService) =
        use failingResize =
            { new ResizeObservationService with
                member _.Supports _=true
                member _.Create(_,?signal)=raise(InvalidOperationException("Probe ResizeObserver failure"))
              interface IDisposable with member _.Dispose()=() }
        let target=document.createElement("div") :?> HTMLElement
        document.body.appendChild target |> ignore
        let style=(target:>ElementCSSInlineStyle).style
        let original=style.getPropertyValue "touch-action"
        let mutable x=0.
        let mutable y=0.
        let controller=new Draggable(scheduler,NumericProperty((fun()->x),(fun value->x<-value)),NumericProperty((fun()->y),(fun value->y<-value)))
        let mutable rejected=false
        try
            try
                use binding=new DraggableDom(controller,scheduler,target,target,document,computed,geometry,failingResize,media)
                ()
            with :? InvalidOperationException -> rejected<-true
            if not rejected || not controller.Cancelled || style.getPropertyValue "touch-action"<>original then
                failwith "Failed constructor retained controller or interaction styles."
        finally
            controller.Cancel()
            target.remove()
