namespace Ranvier.Anime

open System
open Ranvier.Dom
open Ranvier.Anime.ValueInterpolation

/// <summary>Retargetable property tracks with a separate source-compatible callback animation and shared root ordering.</summary>
type internal AnimatableGroup(scheduler:FrameScheduler,?onBegin:NumericAnimation -> unit,?onBeforeUpdate:NumericAnimation -> unit,
                              ?onRender:NumericAnimation -> unit,?onUpdate:NumericAnimation -> unit,?onLoop:NumericAnimation -> unit,
                              ?onPause:NumericAnimation -> unit,?onComplete:NumericAnimation -> unit) as this =
    let tracks=ResizeArray<AnimatableTrack>()
    let roots=ResizeArray<FrameScheduler>()
    let mutable disposed=false
    let callbackRoot=AnimatableFrames.createRoot scheduler
    do roots.Add callbackRoot
    let mutable value=0.
    let hasHandlers=
        onBegin.IsSome || onBeforeUpdate.IsSome || onRender.IsSome || onUpdate.IsSome || onLoop.IsSome || onPause.IsSome || onComplete.IsSome
    let clockDefaults=ScopePlaybackContext.current()
    let callbackDuration=AnimationDefaults.duration None
    let callbackEase=(AnimationDefaults.current()).Ease
    let mutable callbacks:NumericAnimation=Unchecked.defaultof<_>
    let created () = not(isNull(box callbacks))
    let live () = if disposed then raise(ObjectDisposedException(nameof AnimatableGroup))
    let createCallbacks () =
        let animation=ScopeRegistration.run ignore (fun () ->
            ScopePlaybackContext.run clockDefaults (fun () ->
                new NumericAnimation(callbackRoot,(fun () -> value),(fun next -> value<-next),1.,callbackDuration,ease=callbackEase,
                                     ?onBegin=onBegin,?onBeforeUpdate=onBeforeUpdate,?onRender=onRender,?onUpdate=onUpdate,
                                     ?onLoop=onLoop,?onPause=onPause,?onComplete=onComplete)))
        callbacks<-animation
        animation.PlaybackClock.OnFault <- fun () -> this.Cancel()
        animation
    let anyRunning () =
        let mutable running=false
        for track in tracks do if not track.Clock.Paused then running<-true
        running
    let beginProperty (_:Timer) =
        if not disposed && created() then
            if callbacks.Completed then callbacks.ReviveReset()
            // Play on a running forward animation changes nothing; a retarget every frame skips it.
            if not disposed && (not callbacks.Playing || callbacks.Reversed) then callbacks.Play()
    let pauseProperty (_:Timer) =
        if not disposed && created() && not callbacks.Completed && not(anyRunning()) then callbacks.Complete()
    do if hasHandlers then createCallbacks() |> ignore
    /// <summary>The callback animation if it exists; without handlers it is created only when read.</summary>
    member internal _.CreatedCallbacks=if created() then ValueSome callbacks else ValueNone
    /// <summary>The callback animation, whose default 1000 ms duration is independent of property durations.</summary>
    /// <remarks>Without handlers it is created on first read, playing when a property is running.</remarks>
    member _.Callbacks=
        if created() then callbacks
        else
            let animation=createCallbacks()
            if disposed then animation.Cancel()
            elif anyRunning() then animation.Play()
            animation
    /// <summary>Number of attached property tracks.</summary>
    member _.Count=tracks.Count
    /// <summary>Whether cancellation has ended the group's lifetime.</summary>
    member _.Cancelled=disposed
    /// <summary>Captures, initializes and attaches a property track to the group's callback lifecycle.</summary>
    member _.Add(kernels:AnimatableValue array,write:int -> Output -> unit,duration:float,options:PlaybackOptions,
                 ease:float -> float,modifier:float -> float,precision:int) =
        live()
        let root=AnimatableFrames.createRoot scheduler
        let mutable created:AnimatableTrack option=None
        try
            let track=new AnimatableTrack(root,kernels,write,duration,options,ease,modifier,precision,
                                          onBegin=beginProperty,onPause=pauseProperty,onComplete=pauseProperty)
            created<-Some track
            roots.Add root
            tracks.Add track
            track.Clock.OnFault <- fun () -> this.Cancel()
            track.Initialize()
            live()
            track
        with _ ->
            created |> Option.iter(fun track ->
                tracks.Remove track |> ignore
                try track.Cancel() with _ -> ())
            roots.Remove root |> ignore
            try root.Dispose() with _ -> ()
            reraise()
    /// <summary>Cancels every property, callback animation and root lease, preserving the first cleanup error.</summary>
    member _.Cancel() =
        if not disposed then
            disposed<-true
            let mutable failure:exn option=None
            let cleanup action = try action() with error -> if failure.IsNone then failure<-Some error
            for track in tracks do cleanup track.Cancel
            if created() then cleanup callbacks.Cancel
            for root in roots do cleanup root.Dispose
            tracks.Clear()
            roots.Clear()
            failure |> Option.iter(fun error -> System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw())
    interface IDisposable with member _.Dispose()=this.Cancel()
