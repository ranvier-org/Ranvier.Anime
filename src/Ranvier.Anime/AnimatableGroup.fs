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
    let callbacks=new NumericAnimation(callbackRoot,(fun () -> value),(fun next -> value<-next),1.,1000u,ease=Ease.named "outQuad",
                                       ?onBegin=onBegin,?onBeforeUpdate=onBeforeUpdate,?onRender=onRender,?onUpdate=onUpdate,
                                       ?onLoop=onLoop,?onPause=onPause,?onComplete=onComplete)
    let live () = if disposed then raise(ObjectDisposedException(nameof AnimatableGroup))
    let beginProperty (_:Timer) =
        if not disposed then
            if callbacks.Completed then callbacks.ReviveReset()
            if not disposed then callbacks.Play()
    let pauseProperty (_:Timer) =
        if not disposed && not callbacks.Completed && (tracks |> Seq.forall(fun track -> track.Clock.Paused)) then callbacks.Complete()
    do callbacks.PlaybackClock.OnFault <- fun () -> this.Cancel()
    /// <summary>The callback animation, whose default 1000 ms duration is independent of property durations.</summary>
    member _.Callbacks=callbacks
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
            cleanup callbacks.Cancel
            for root in roots do cleanup root.Dispose
            tracks.Clear()
            roots.Clear()
            failure |> Option.iter(fun error -> System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw())
    interface IDisposable with member _.Dispose()=this.Cancel()
