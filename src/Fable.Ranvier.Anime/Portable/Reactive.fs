namespace Ranvier.Anime

open Fable.Core
open global.Ranvier

/// <summary>The motion policy of a graph, and ownership of animation objects.</summary>
[<RequireQualifiedAccess>]
module Reactive =
    /// <summary>Whether animations run in full or reduced to their end states.</summary>
    type MotionPolicy =
        | Full
        | Reduced

    [<AllowNullLiteral>]
    type private MediaQuery =
        abstract matches: bool
        abstract addEventListener: string * (MediaQuery -> unit) -> unit
        abstract removeEventListener: string * (MediaQuery -> unit) -> unit

    [<Emit("(typeof window !== 'undefined' && typeof window.matchMedia === 'function' ? window.matchMedia($0) : null)")>]
    let private matchMedia (query: string) : MediaQuery = jsNative

    let private policyOf (matches: bool) =
        if matches then Reduced else Full

#if FABLE_COMPILER
    [<AllowNullLiteral>]
    type private PolicyCache =
        abstract get: Graph -> Signal<MotionPolicy>
        abstract set: Graph * Signal<MotionPolicy> -> unit

    [<Emit("new WeakMap()")>]
    let private policyCache () : PolicyCache = jsNative

    let private policies = policyCache ()
#else
    let private policies =
        System.Runtime.CompilerServices.ConditionalWeakTable<Graph, Signal<MotionPolicy>>()
#endif

    /// <summary>
    /// The motion policy of the current graph: a signal seeded from <c>(prefers-reduced-motion: reduce)</c> on first read and
    /// written on each change of the media query.
    /// </summary>
    /// <remarks>
    /// Every call on a graph returns the same signal. A write overrides the media query until its next change; disposing the
    /// graph detaches the media query. Without <c>matchMedia</c>, as on .NET or in jsdom, the seed is <c>Full</c>. The first
    /// call on a graph runs outside pure derivations, as <c>onCleanup</c> does.
    /// </remarks>
    let policy () : Signal<MotionPolicy> =
        let graph = Graph.Current
#if FABLE_COMPILER
        let cached = policies.get graph

        if not (isNull (box cached)) then
            cached
        else
            let query = matchMedia "(prefers-reduced-motion: reduce)"

            let signal =
                Api.createSignal (if isNull query then Full else policyOf query.matches)

            if not (isNull query) then
                let follow (change: MediaQuery) =
                    signal.Value <- policyOf change.matches

                query.addEventListener ("change", follow)
                Api.runWithOwner graph.Root (fun () -> Api.onCleanup (fun () -> query.removeEventListener ("change", follow)))

            policies.set (graph, signal)
            signal
#else
        match policies.TryGetValue graph with
        | true, signal -> signal
        | _ ->
            let signal = Api.createSignal Full
            policies.Add (graph, signal)
            signal
#endif

    /// <summary>Reverts the object when the current owner is disposed.</summary>
    let own (revertible: #IRevertible) : unit =
        Api.onCleanup (fun () -> (revertible :> IRevertible).Revert())
