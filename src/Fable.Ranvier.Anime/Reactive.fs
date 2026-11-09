/// <summary>Ownership and signals for anime objects: reverting with an owner, playback as memos, signal-driven seeking.</summary>
module Ranvier.Anime.Reactive

open Fable.Core
open global.Ranvier

/// <summary>Reverts <c>revertible</c> when the current owner is disposed, and returns it.</summary>
/// <remarks>Outside every root the owner is the graph's own scope, which reverts it when the graph is disposed.</remarks>
let own (revertible: 'r) : 'r :> IRevertible =
    Api.onCleanup (fun () -> revertible.revert ())
    revertible

/// <summary>A tickable's playback state as memos, updated from its callbacks.</summary>
type Playback =
    {
        /// <summary>The overall progress in 0..1.</summary>
        Progress: Memo<float>
        /// <summary>Whether the tickable is playing and not completed.</summary>
        Running: Memo<bool>
        Completed: Memo<bool>
    }

[<Emit("(() => { const previous = $0[$1]; const chained = self => { if (previous) previous(self); $2(); }; $0[$1] = chained; return () => { if ($0[$1] === chained) $0[$1] = previous; }; })()")>]
let private chain (tickable: ITickable) (callback: string) (after: unit -> unit) : unit -> unit = jsNative

/// <summary>
/// The playback state of <c>tickable</c>, written on its begin, update, loop, pause and completion. The callbacks it
/// already had still run, first.
/// </summary>
/// <remarks>
/// When the current owner is disposed, each callback slot still holding the chain gets back the callback it had; a slot
/// assigned since keeps its new callback.
/// </remarks>
let track (tickable: #ITickable) : Playback =
    let tickable = tickable :> ITickable
    let graph = Graph.Current
    let progress = Api.createSignal tickable.progress
    let running = Api.createSignal (not tickable.paused && not tickable.completed)
    let completed = Api.createSignal tickable.completed

    let write () =
        graph.Batch (fun () ->
            progress.Value <- tickable.progress
            running.Value <- not tickable.paused && not tickable.completed
            completed.Value <- tickable.completed)

    let restores =
        [| "onBegin"; "onUpdate"; "onLoop"; "onPause"; "onComplete" |]
        |> Array.map (fun callback -> chain tickable callback write)

    Api.onCleanup (fun () ->
        for restore in restores do
            restore ())

    {
        Progress = Api.createMemo (fun _ -> progress.Value)
        Running = Api.createMemo (fun _ -> running.Value)
        Completed = Api.createMemo (fun _ -> completed.Value)
    }

/// <summary>An effect that seeks <c>tickable</c> to the progress returned by <c>read</c>, in 0..1, whenever it changes.</summary>
/// <remarks>A tickable created with <c>autoplay = false</c> moves only when <c>read</c> changes.</remarks>
let scrub (read: unit -> float) (tickable: #ITickable) : unit =
    Api.createEffectOn read (fun progress -> tickable.progress <- progress)

/// <summary>An effect that animates <c>property</c> of <c>animatable</c> to <c>read ()</c> whenever it changes.</summary>
let drive (read: unit -> float) (animatable: Animatable) (property: string) : unit =
    Api.createEffectOn read (fun value -> animatable.set (property, value))

/// <summary>Seeds a typed numeric custom property from a reader and animates subsequent changes from its current value.</summary>
/// <param name="read">A reactive reader returning numeric tokens with a constant grammar marker and unit suffix.</param>
/// <param name="element">The element owning the animated custom property.</param>
/// <param name="property">The typed custom property consumed by CSS expressions.</param>
/// <param name="parameters">Animation timing; the reader's token supplies the unit.</param>
/// <remarks>
/// Disposal cancels the active animation and restores the original inline value and priority. Each animation frame bypasses signals.
/// </remarks>
/// <exception cref="T:System.ArgumentException">A reader result lacks numeric token metadata or changes its unit suffix.</exception>
let driveCss
    (read: unit -> Ranvier.Css.Value<'syntax>)
    (element: TS.Dom.Element)
    (property: Ranvier.Css.CustomProperty<'syntax>)
    (parameters: AnimatableProp)
    : unit =
    let restore =
        CssAnimationRuntime.capture element (Ranvier.Css.RuntimeApi.customPropertyName property)

    let mutable active: Animatable = null
    let mutable seed: JSAnimation = null
    let mutable suffix: string option = None

    Api.onCleanup (fun () ->
        if not (isNull active) then
            active.revert ()

        if not (isNull seed) then
            seed.revert ()

        restore ())

    Api.createEffectOn read (fun value ->
        let nextSuffix = CssAnimationRuntime.unitOf value

        match suffix with
        | Some expected -> CssAnimationRuntime.requireUnit expected value
        | None -> ()

        if isNull active then
            seed <- Utils.set (element, Anim().With(property, value))
            let settings = CssAnimationRuntime.withUnit parameters nextSuffix
            active <- Anime.animatable (element, AnimatableParams().With(Ranvier.Css.RuntimeApi.customPropertyName property, settings))
            suffix <- Some nextSuffix
        else
            let number, _ =
                Ranvier.Css.RuntimeApi.tryNumericValue value
                |> Option.get

            active.set (Ranvier.Css.RuntimeApi.customPropertyName property, number))

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
