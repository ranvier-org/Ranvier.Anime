namespace Ranvier.Anime

open System
open Ranvier
open Ranvier.Dom

/// <summary>Refreshable drag physics; distances are CSS pixels and velocities are pixels per millisecond.</summary>
[<NoEquality;NoComparison>]
type DraggableSettings =
    { Bounds:DragBounds; Contained:bool; XEnabled:bool; YEnabled:bool
      SnapX:DragSnap; SnapY:DragSnap; DragSpeed:float
      ContainerFriction:float; ReleaseContainerFriction:float
      MouseThreshold:float; TouchThreshold:float
      MinVelocity:float; MaxVelocity:float; VelocityMultiplier:float }
    /// <summary>Unconstrained two-axis dragging with the source's pointer thresholds and friction.</summary>
    static member Default =
        { Bounds=DragBounds.Unbounded; Contained=false; XEnabled=true; YEnabled=true
          SnapX=DragSnap.None; SnapY=DragSnap.None; DragSpeed=1.
          ContainerFriction=0.8; ReleaseContainerFriction=0.8
          MouseThreshold=3.; TouchThreshold=7.; MinVelocity=0.;MaxVelocity=50.;VelocityMultiplier=1. }

/// <summary>Owned draggable input state using existing numeric animatables and the shared frame scheduler.</summary>
/// <remarks>DOM input, coordinate normalization and scrolling attach through DraggableDom. Accessors supply native mapTo equivalents.</remarks>
type Draggable(scheduler:FrameScheduler,x:NumericProperty,y:NumericProperty,
               ?settings:unit -> DraggableSettings,?modifierX:float -> float,?modifierY:float -> float,
               ?releaseSpring:Spring,?releaseEase:float -> float,?releaseMass:float,?releaseStiffness:float,?releaseDamping:float,
               ?onGrab:Draggable -> unit,?onDrag:Draggable -> unit,?onRelease:Draggable -> unit,
               ?onUpdate:Draggable -> unit,?onSettle:Draggable -> unit,?onSnap:Draggable -> unit) as this =
    let ownerAllowsWrite=ScopeRegistration.captureWriteGuard()
    let settings=defaultArg settings (fun () -> DraggableSettings.Default)
    let mutable config=settings()
    let springX=defaultArg releaseSpring (Spring(mass=defaultArg releaseMass 1.,stiffness=defaultArg releaseStiffness 80.,damping=defaultArg releaseDamping 20.))
    let springY=defaultArg releaseSpring (Spring(mass=defaultArg releaseMass 1.,stiffness=defaultArg releaseStiffness 80.,damping=defaultArg releaseDamping 20.))
    let mutable disposed=false
    let mutable enabled=true
    let mutable grabbed=false
    let mutable dragged=false
    let mutable released=false
    let mutable updated=false
    let mutable manual=true
    let mutable updateTicker:Timer option=None
    let mutable beforeUpdate:unit -> unit=ignore
    let mutable releaseDriver:Timer option=None
    let pointer=Array.zeroCreate<float> 8
    let coordinates=[|x.Value;y.Value;x.Value;y.Value|]
    let snapped=[|0.;0.|]
    let destination=[|0.;0.|]
    let mutable deltaX=0.
    let mutable deltaY=0.
    let mutable angle=0.
    let mutable releaseDuration=0.
    let mutable threshold=3.
    let velocity=DragVelocity(scheduler.Now())
    let mutable axisX:AnimatableNumber option=None
    let mutable axisY:AnimatableNumber option=None
    let notify callback=if not disposed && ownerAllowsWrite() then callback |> Option.iter(fun callback -> callback this)
    let value (axis:AnimatableNumber option)=
        let value=axis |> Option.map _.Value |> Option.defaultValue 0.
        Math.Floor(value*1e4+0.5)/1e4
    let render () =
        if not disposed then
            let currentX=value axisX
            let currentY=value axisY
            deltaX<-currentX-coordinates[2]
            deltaY<-currentY-coordinates[3]
            coordinates[2]<-currentX
            coordinates[3]<-currentY
            let hasMoved=grabbed && updated
            let hasReleased=not hasMoved && released
            if updated && (deltaX<>0. || deltaY<>0.) then notify onUpdate
            if not hasReleased then updated<-false
            elif not disposed then
                velocity.Sample(scheduler.Now(),deltaX,deltaY,config.VelocityMultiplier,config.MinVelocity,config.MaxVelocity) |> ignore
                angle<-Math.Atan2(deltaY,deltaX)
    let settle () =
        if not disposed then
            if not grabbed && released then released<-false
            if not manual then
                deltaX<-0.
                deltaY<-0.
                velocity.Reset()
                notify onSettle
    let group=ScopeRegistration.run ignore (fun () -> new Animatable(scheduler,onRender=(fun _ -> render()),onComplete=(fun _ -> settle())))
    do
        try
            if not(ScopeRegistration.canConstruct()) then raise(ObjectDisposedException(nameof Draggable))
            let options modifier={AnimatablePropertyOptions.Default with Modifier=modifier}
            let same=Object.ReferenceEquals(x,y)
            axisX<-Some(ScopeRegistration.run ignore (fun () -> group.Numeric(x,duration=0u,options=options(if same then defaultArg modifierY id else defaultArg modifierX id))))
            axisY<-if same then axisX else Some(ScopeRegistration.run ignore (fun () -> group.Numeric(y,duration=0u,options=options(defaultArg modifierY id))))
            manual<-false
        with _ ->
            try group.Cancel() with _ -> ()
            reraise()
    let pauseFrame ()=updateTicker |> Option.iter _.Pause()
    let pauseRelease ()=releaseDriver |> Option.iter(fun driver -> driver.Cancel());releaseDriver<-None
    let setAxis index enabled (axis:AnimatableNumber option) snapRule target muted =
        if not disposed && ownerAllowsWrite() && enabled then
            pauseRelease()
            let target=DragPhysics.coordinate target
            manual<-true
            updated<-not muted
            destination[index]<-target
            snapped[index]<-DragPhysics.snap snapRule target
            axis |> Option.iter(fun axis -> axis.To(target,duration=0u))
            manual<-false
    let sampleVelocity dx dy=velocity.Sample(scheduler.Now(),dx,dy,config.VelocityMultiplier,config.MinVelocity,config.MaxVelocity)
    let update () =
        if not disposed && ownerAllowsWrite() then
            beforeUpdate()
            let b=config.Bounds
            coordinates[0]<-coordinates[0]+(pointer[0]-pointer[4])*config.DragSpeed
            coordinates[1]<-coordinates[1]+(pointer[1]-pointer[5])*config.DragSpeed
            let oldSnapX,oldSnapY=snapped[0],snapped[1]
            let dx,dy=pointer[0]-pointer[4],pointer[1]-pointer[5]
            pointer[4]<-pointer[0]
            pointer[5]<-pointer[1]
            setAxis 0 config.XEnabled axisX config.SnapX (DragPhysics.resist b.Left b.Right config.ContainerFriction config.DragSpeed coordinates[0]) false
            setAxis 1 config.YEnabled axisY config.SnapY (DragPhysics.resist b.Top b.Bottom config.ContainerFriction config.DragSpeed coordinates[1]) false
            sampleVelocity dx dy |> ignore
            angle<-Math.Atan2(pointer[1]-pointer[3],pointer[0]-pointer[2])
            if oldSnapX<>snapped[0] && config.SnapX<>DragSnap.None || oldSnapY<>snapped[1] && config.SnapY<>DragSnap.None then notify onSnap
    let ticker=ScopeRegistration.run ignore (fun () -> new Timer(scheduler,1e12,onUpdate=(fun _ -> update())))
    do updateTicker<-Some ticker;ticker.Reset()
    let live ()=if disposed || not(ownerAllowsWrite()) then raise(ObjectDisposedException(nameof Draggable))
    do ScopeRegistration.register this.Revert
    /// <summary>Runs the native scroll calculation inside the existing source update ticker before pointer accumulation.</summary>
    member internal _.SetBeforeUpdate(callback:unit -> unit)=beforeUpdate<-callback
    /// <summary>Sets measured native bounds without re-evaluating other refreshable settings.</summary>
    member internal _.SetBounds(bounds:DragBounds,contained:bool)=config<-{config with Bounds=bounds;Contained=contained}
    /// <summary>Sets native translation bounds and silently clamps the current coordinates.</summary>
    member _.Constrain(bounds:DragBounds,contained:bool) =
        live()
        this.SetBounds(bounds,contained)
        setAxis 0 config.XEnabled axisX config.SnapX (DragPhysics.clampAxis bounds.Left bounds.Right this.X) true
        setAxis 1 config.YEnabled axisY config.SnapY (DragPhysics.clampAxis bounds.Top bounds.Bottom this.Y) true
    /// <summary>Measures native geometry at zero axis translation, restoring coordinates even when measurement throws.</summary>
    member _.WithOrigin(action:unit -> 'T) =
        live()
        let savedX,savedY=this.X,this.Y
        let previousX,previousY=coordinates[2],coordinates[3]
        coordinates[2]<-0.;coordinates[3]<-0.
        setAxis 0 config.XEnabled axisX config.SnapX 0. true
        setAxis 1 config.YEnabled axisY config.SnapY 0. true
        try action()
        finally
            coordinates[2]<-previousX;coordinates[3]<-previousY
            setAxis 0 config.XEnabled axisX config.SnapX savedX true
            setAxis 1 config.YEnabled axisY config.SnapY savedY true
    /// <summary>Retargets enabled axes with precise milliseconds and supplied easing, preserving existing animatable controls.</summary>
    member _.AnimateToPrecise(x,y,duration,ease) =
        live()
        if config.XEnabled then axisX.Value.ToPrecise(x,duration=duration,ease=ease)
        if config.YEnabled then axisY.Value.ToPrecise(y,duration=duration,ease=ease)
    /// <summary>Retargets enabled axes using unsigned whole milliseconds.</summary>
    member _.AnimateTo(x,y,duration:uint,ease)=this.AnimateToPrecise(x,y,float duration,ease)
    /// <summary>Retargets the enabled horizontal axis with precise milliseconds.</summary>
    member _.AnimateXPrecise(value,duration,ease)=live();if config.XEnabled then axisX.Value.ToPrecise(value,duration=duration,ease=ease)
    /// <summary>Retargets the enabled vertical axis with precise milliseconds.</summary>
    member _.AnimateYPrecise(value,duration,ease)=live();if config.YEnabled then axisY.Value.ToPrecise(value,duration=duration,ease=ease)
    /// <summary>Unresisted horizontal pointer coordinate used by native edge scrolling.</summary>
    member _.RawX=coordinates[0]
    /// <summary>Unresisted vertical pointer coordinate used by native edge scrolling.</summary>
    member _.RawY=coordinates[1]
    /// <summary>Current cached x-axis value, including the configured getter modifier.</summary>
    member _.X with get()=value axisX and set target=live();pauseRelease();setAxis 0 config.XEnabled axisX config.SnapX target false
    /// <summary>Current cached y-axis value, including the configured getter modifier.</summary>
    member _.Y with get()=value axisY and set target=live();pauseRelease();setAxis 1 config.YEnabled axisY config.SnapY target false
    /// <summary>Normalized horizontal container progress; unconstrained defaults use the source's finite sentinel bounds.</summary>
    member _.ProgressX with get()=(this.X-config.Bounds.Left)/(config.Bounds.Right-config.Bounds.Left)
                           and set value=this.X<-config.Bounds.Left+value*(config.Bounds.Right-config.Bounds.Left)
    /// <summary>Normalized vertical container progress.</summary>
    member _.ProgressY with get()=(this.Y-config.Bounds.Top)/(config.Bounds.Bottom-config.Bounds.Top)
                           and set value=this.Y<-config.Bounds.Top+value*(config.Bounds.Bottom-config.Bounds.Top)
    /// <summary>Mutable x release spring, shared with y only when supplied as a custom release spring.</summary>
    member _.ReleaseXSpring=springX
    /// <summary>Mutable y release spring.</summary>
    member _.ReleaseYSpring=springY
    /// <summary>Longest precise axis release duration in milliseconds.</summary>
    member _.ReleaseDuration=releaseDuration
    /// <summary>Current release easing used for native scroll synchronization.</summary>
    member _.ReleaseEase=match releaseSpring with Some spring -> spring.Ease | None -> defaultArg releaseEase (Ease.named "outQuint")
    /// <summary>Last release or programmatic x destination.</summary>
    member _.DestX=destination[0]
    /// <summary>Last release or programmatic y destination.</summary>
    member _.DestY=destination[1]
    /// <summary>Last rendered horizontal displacement.</summary>
    member _.DeltaX=deltaX
    /// <summary>Last rendered vertical displacement.</summary>
    member _.DeltaY=deltaY
    /// <summary>Three-sample maximum velocity.</summary>
    member _.Velocity=velocity.Value
    /// <summary>Last pointer or release direction in radians.</summary>
    member _.Angle=angle
    /// <summary>Whether a pointer owns the input gesture.</summary>
    member _.Grabbed=grabbed
    /// <summary>Whether the current gesture crossed its strict threshold.</summary>
    member _.Dragged=dragged
    /// <summary>Whether release motion remains unsettled.</summary>
    member _.Released=released
    /// <summary>Whether fresh gestures are accepted.</summary>
    member _.Enabled=enabled
    /// <summary>Terminal cancellation state.</summary>
    member _.Cancelled=disposed || not(ownerAllowsWrite())
    /// <summary>Current refresh snapshot for native scrolling and geometry adapters.</summary>
    member _.Settings=config
    /// <summary>The reusable existing animatable group used by both axes.</summary>
    member _.Animate=group
    /// <summary>Pauses pointer frames and release motion while retaining coordinates.</summary>
    member _.Stop() =
        pauseFrame()
        pauseRelease()
        axisX |> Option.iter(fun axis -> axis.Playback.Pause())
        axisY |> Option.iter(fun axis -> axis.Playback.Pause())
    /// <summary>Refreshes dynamic settings and silently clamps current coordinates to the new bounds.</summary>
    member internal _.RefreshWithMeasure(measure:(unit -> unit) option) =
        live()
        let capturedX,capturedY=this.X,this.Y
        config<-settings()
        let b=config.Bounds
        for number in [b.Left;b.Right;b.Top;b.Bottom;config.DragSpeed;config.MouseThreshold;config.TouchThreshold;config.MinVelocity;config.MaxVelocity;config.VelocityMultiplier] do
            if not(Double.IsFinite number) then invalidArg "settings" "Physics settings must be finite."
        match measure with Some action -> action() | None -> this.WithOrigin(ignore)
        let b=config.Bounds
        setAxis 0 config.XEnabled axisX config.SnapX (DragPhysics.clampAxis b.Left b.Right capturedX) true
        setAxis 1 config.YEnabled axisY config.SnapY (DragPhysics.clampAxis b.Top b.Bottom capturedY) true
    /// <summary>Refreshes settings, measures the scalar origin and restores clamped source coordinates.</summary>
    member _.Refresh()=this.RefreshWithMeasure None
    /// <summary>Begins a normalized pointer gesture, selecting coarse or fine threshold behavior.</summary>
    member _.Grab(x,y,?coarse:bool,?measure:unit -> unit,?beforeNotify:unit -> unit) =
        live()
        if enabled && not grabbed then
            grabbed<-true
            released<-false
            this.Stop()
            match measure with Some action -> action() | None -> this.WithOrigin(ignore)
            dragged<-false
            threshold<-if defaultArg coarse false then config.TouchThreshold else config.MouseThreshold
            let b=config.Bounds
            let factor=(1.-Math.Clamp(config.ContainerFriction,0.,1.))*config.DragSpeed
            let inverse low high value=if factor=0. then value elif value>high then high+(value-high)/factor elif value<low then low+(value-low)/factor else value
            coordinates[0]<-inverse b.Left b.Right this.X
            coordinates[1]<-inverse b.Top b.Bottom this.Y
            coordinates[2]<-this.X
            coordinates[3]<-this.Y
            for i in [0;2;4;6] do pointer[i]<-x;pointer[i+1]<-y
            deltaX<-0.;deltaY<-0.;angle<-0.;velocity.Reset()
            beforeNotify |> Option.iter(fun action -> if not disposed && ownerAllowsWrite() then action())
            notify onGrab
    /// <summary>Updates normalized pointer coordinates; drag callbacks run on every eligible input event.</summary>
    member _.Move(x,y) =
        live()
        if grabbed && (dragged || DragPhysics.threshold (not config.XEnabled) (not config.YEnabled) threshold (x-pointer[6]) (y-pointer[7])) then
            pointer[2]<-pointer[0];pointer[3]<-pointer[1]
            pointer[0]<-x;pointer[1]<-y
            dragged<-true
            released<-false
            ticker.Resume()
            notify onDrag
    /// <summary>Rebases the pointer origin while a nested touch scroller consumes the gesture.</summary>
    member _.RebasePointer(x,y) =
        if grabbed then for i in [0;2;4;6] do pointer[i]<-x;pointer[i+1]<-y
    /// <summary>Adds externally observed scroll deltas to raw drag coordinates before the next pointer frame.</summary>
    member _.ScrollBy(x,y) =
        if not disposed && grabbed then coordinates[0]<-coordinates[0]+x;coordinates[1]<-coordinates[1]+y
    /// <summary>Starts source inertia and independent spring release; the release callback may stop or cancel it.</summary>
    member _.Release() =
        live()
        if grabbed then
            pauseFrame()
            let b=config.Bounds
            let currentX,currentY=this.X,this.Y
            let pv=sampleVelocity (pointer[0]-pointer[4]) (pointer[1]-pointer[5])
            angle<-Math.Atan2(pointer[1]-pointer[3],pointer[0]-pointer[2])
            let displacement=pv*150.
            let struct(dx,bx)=DragPhysics.releaseAxis b.Left b.Right config.ReleaseContainerFriction config.DragSpeed config.SnapX currentX (Math.Cos angle*displacement)
            let struct(dy,by)=DragPhysics.releaseAxis b.Top b.Bottom config.ReleaseContainerFriction config.DragSpeed config.SnapY currentY (Math.Sin angle*displacement)
            destination[0]<-dx;destination[1]<-dy
            let duration (spring:Spring) disabledOther current target high low =
                let direction=if target=high then (if current>high then -1. else 1.) else (if current<low then -1. else 1.)
                let distance=Math.Floor(current-target+0.5)
                spring.Velocity<-if disabledOther && releaseSpring.IsSome then (if distance=0. then 0. else displacement*direction/abs distance) else pv
                if current=target then 0. elif releaseSpring.IsSome then spring.SettlingDuration else spring.SettlingDuration-200.
            let durationX=if config.XEnabled then duration springX (not config.YEnabled) currentX dx b.Right b.Left else 0.
            let easeX=if releaseSpring.IsSome then springX.Ease else defaultArg releaseEase (Ease.named "outQuint")
            let durationY=if config.YEnabled then duration springY (not config.XEnabled) currentY dy b.Bottom b.Top else 0.
            let easeY=if releaseSpring.IsSome then springY.Ease else defaultArg releaseEase (Ease.named "outQuint")
            let outOfBounds=DragPhysics.outOfBounds config.Contained (not config.XEnabled) (not config.YEnabled) b (currentX+Math.Cos angle*displacement) (currentY+Math.Sin angle*displacement)
            let friction=(1.-Math.Clamp(config.ReleaseContainerFriction,0.,1.))*config.DragSpeed
            let longest=max durationX durationY
            releaseDuration<-longest
            if releaseSpring.IsNone && outOfBounds<>0 && friction<>0. && longest>0. then
                manual<-true
                let ease=defaultArg releaseEase (Ease.named "outQuint")
                let overshoot current outer target duration time =
                    let progress duration=if duration=0. then 1. else Math.Clamp(time/duration,0.,1.)
                    target+(current-outer)*(1.-ease(progress(duration*0.65)))+(outer-target)*(1.-ease(progress duration))
                // Source reads overshootCoords before the engine's post-root additive flush.
                let mutable overshootX,overshootY=currentX,currentY
                let mutable renderedX,renderedY=currentX,currentY
                let driver=ScopeRegistration.run ignore (fun () -> new Timer(scheduler,longest,
                    onUpdate=(fun timer ->
                        if not disposed && ownerAllowsWrite() then
                            updated<-true
                            manual<-true
                            let time=timer.IterationCurrentTime
                            renderedX<-overshootX;renderedY<-overshootY
                            overshootX<-overshoot currentX bx dx durationX time
                            overshootY<-overshoot currentY by dy durationY time
                            if config.XEnabled then axisX.Value.To(renderedX,duration=1u)
                            if not disposed && ownerAllowsWrite() && config.YEnabled then axisY.Value.To(renderedY,duration=1u)),
                    onComplete=(fun _ ->
                        if not disposed && ownerAllowsWrite() then
                            manual<-false
                            if config.XEnabled then axisX.Value.To(renderedX,duration=0u)
                            if not disposed && ownerAllowsWrite() && config.YEnabled then axisY.Value.To(renderedY,duration=0u))))
                releaseDriver<-Some driver
                driver.Reset();driver.Resume()
            else
                if config.XEnabled then axisX.Value.ToPrecise(dx,duration=durationX,ease=easeX)
                if config.YEnabled then axisY.Value.ToPrecise(dy,duration=durationY,ease=easeY)
            let hasSnapped=(dx<>snapped[0] && config.SnapX<>DragSnap.None)||(dy<>snapped[1] && config.SnapY<>DragSnap.None)
            snapped[0]<-dx;snapped[1]<-dy
            if hasSnapped then notify onSnap
            if not disposed then
                grabbed<-false;dragged<-false;updated<-true;released<-true
                notify onRelease
    /// <summary>Rewinds to zero and clears gesture/velocity state while retaining subscriptions.</summary>
    member _.Reset() =
        live()
        this.Stop()
        grabbed<-false;dragged<-false;released<-false;updated<-false
        Array.Clear pointer
        Array.Clear coordinates
        angle<-0.;velocity.Reset()
        setAxis 0 config.XEnabled axisX config.SnapX 0. true
        setAxis 1 config.YEnabled axisY config.SnapY 0. true
    /// <summary>Accepts new gestures after Disable.</summary>
    member _.Enable()=live();enabled<-true
    /// <summary>Ignores new gestures and clears active gesture flags.</summary>
    member _.Disable()=enabled<-false;grabbed<-false;dragged<-false;released<-false;updated<-false
    /// <summary>Stops all motion and future accessor writes.</summary>
    member _.Cancel() =
        if not disposed then
            disposed<-true;enabled<-false;grabbed<-false
            pauseFrame();pauseRelease()
            updateTicker |> Option.iter _.Cancel()
            beforeUpdate<-ignore
            group.Cancel()
    /// <summary>Restores captured axis accessor values and ends the lifetime.</summary>
    member _.Revert() =
        if not disposed then
            this.Stop()
            try group.Revert() finally this.Cancel()
    interface IDisposable with member _.Dispose()=this.Cancel()

/// <summary>Ranvier owner registration for draggable physics and playback.</summary>
[<RequireQualifiedAccess>]
module Draggable =
    /// <summary>Owner disposal cancels pointer scheduling and every axis animation.</summary>
    let own (draggable:Draggable)=ScopeRegistration.attach(draggable:>IDisposable);draggable
