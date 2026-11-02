namespace Ranvier.Anime

open System
open Ranvier
open Ranvier.Dom

/// <summary>Owned reactive targets whose frames write directly to their destination.</summary>
[<RequireQualifiedAccess>]
module Reactive =
    /// <summary>Seeds the current value immediately and animates later changes until disposed or its owner ends.</summary>
    /// <remarks>Requires a live Ranvier owner and the graph's thread. Duration is finite, nonnegative milliseconds.</remarks>
    let drive (scheduler: FrameScheduler) (duration: float) (read: unit -> float) (write: float -> unit) : IDisposable =
        let graph = Graph.Current
        graph.AssertOnGraphThread "Reactive.drive"
        let parent = Api.getOwner()
        if parent.IsDisposed then raise (ObjectDisposedException "Owner")
        let child = Api.createRoot(fun child ->
            try
                let initial = Api.untrack read
                let mutable rendered = initial
                let mutable desired = initial
                let apply value =
                    graph.AssertOnGraphThread "Reactive.drive frame"
                    if not child.IsDisposed then
                        rendered <- value
                        graph.Untrack(fun () -> write value)
                let tween = new NumericAnimation(scheduler, (fun () -> rendered), apply, initial, duration)
                child.Attach tween
                apply initial
                if not child.IsDisposed then
                    Api.createEffectOn read (fun target ->
                        if not child.IsDisposed && target <> desired then
                            tween.Retarget target
                            desired <- target)
                child
            with _ ->
                child.Dispose()
                reraise())
        child :> IDisposable
