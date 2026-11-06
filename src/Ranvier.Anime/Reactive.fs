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
        let parent = ScopeRegistration.resourceOwner()
        if parent.IsDisposed then raise (ObjectDisposedException "Owner")
        let child = Api.runWithOwner parent (fun () -> Api.createRoot(fun child ->
            try
                let initial = Api.untrack read
                let mutable rendered = initial
                let mutable desired = initial
                let apply value =
                    graph.AssertOnGraphThread "Reactive.drive frame"
                    if not child.IsDisposed then
                        rendered <- value
                        graph.Untrack(fun () -> write value)
                let tween = ScopeRegistration.run ignore (fun () ->
                    new NumericAnimation(scheduler, (fun () -> rendered), apply, initial, duration))
                child.Attach tween
                let mutable restoring=false
                let revert()=
                    if not child.IsDisposed && not restoring then
                        restoring<-true
                        let errorOffset=child.ErrorCount
                        let mutable failure:exn option=None
                        try tween.Revert() with error -> failure<-Some error
                        try child.Dispose() with error -> if failure.IsNone then failure<-Some error
                        for error in child.ErrorsFrom errorOffset do
                            if failure.IsNone then failure<-Some error
                        restoring<-false
                        failure |> Option.iter(fun error -> Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw())
                ScopeRegistration.register revert
                apply initial
                if not child.IsDisposed then
                    Api.createEffectOn read (fun target ->
                        if not child.IsDisposed && not restoring && target <> desired then
                            tween.Retarget target
                            desired <- target)
                child
            with _ ->
                child.Dispose()
                reraise()))
        child :> IDisposable

    /// <summary>Whether animations run in full or reduced to their end states.</summary>
    type MotionPolicy =
        | Full
        | Reduced

    let private policies = Runtime.CompilerServices.ConditionalWeakTable<Graph, Signal<MotionPolicy>>()

    /// <summary>The motion policy of the current graph: one writable signal per graph, seeded <c>Full</c>.</summary>
    /// <remarks>Native hosts have no reduced-motion media query yet; write <c>Reduced</c> to turn enter, exit and FLIP off.</remarks>
    let policy () : Signal<MotionPolicy> =
        let graph = Graph.Current
        match policies.TryGetValue graph with
        | true, signal -> signal
        | _ ->
            let signal = Api.createSignal Full
            policies.Add(graph, signal)
            signal
