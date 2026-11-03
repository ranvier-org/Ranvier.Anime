namespace Ranvier.Anime

// Derived from Anime.js v4.5.0 scope.js, Copyright (c) 2025 Julian Garnier, MIT.
// The upstream MIT notice is retained in THIRD-PARTY-NOTICES.md.

open System
open System.Collections.Generic
open System.Collections.Frozen
open Ranvier
open Ranvier.Dom

/// <summary>A refreshable group of animation resources attached to a Ranvier owner.</summary>
/// <remarks>All operations require its graph thread. Revert ends the lifetime; repeated calls are harmless.</remarks>
type Scope(?root: ParentNode, ?defaults: PlaybackOptions, ?clockDefaults: PlaybackDefaults, ?animationDefaults: AnimationDefaults, ?media: MediaQueryProvider, ?mediaQueries: seq<string * string>) as this =
    static let current = new Threading.ThreadLocal<Scope option>(fun () -> None)
    let parentScope = current.Value
    let inheritedDefaults = ScopePlaybackContext.current()
    let clockDefaults =
        match defaults,clockDefaults,animationDefaults with
        | None,None,None -> inheritedDefaults
        | supplied,overrides,animationOverrides ->
            let fallback = defaultArg supplied inheritedDefaults.Options
            let playback = overrides |> Option.map(fun value -> value.Apply fallback) |> Option.defaultValue fallback
            let animation = animationOverrides |> Option.map(fun value -> value.Apply inheritedDefaults.Animation) |> Option.defaultValue inheritedDefaults.Animation
            ScopeClockDefaults(playback,animation=animation)
    let graph = Graph.Current
    do if not(ScopeRegistration.canConstruct()) then raise(ObjectDisposedException "Scope")
    let parent = ScopeRegistration.resourceOwner()
    let lifetime = Api.runWithOwner parent (fun () -> Api.createRoot id)
    let persistent = Api.runWithOwner lifetime (fun () -> Api.createRoot id)
    let regularLifetime = Api.runWithOwner lifetime (fun () -> Api.createRoot id)
    let mutable epoch = Api.runWithOwner regularLifetime (fun () -> Api.createRoot id)
    let constructors = ResizeArray<Scope -> (unit -> unit) option>()
    let cleanups = ResizeArray<unit -> unit>()
    let resources = ResizeArray<unit -> unit>()
    let onceCleanups = ResizeArray<unit -> unit>()
    let onceResources = ResizeArray<unit -> unit>()
    let onceSlots = ResizeArray<obj>()
    let methods = Dictionary<string,obj>(StringComparer.Ordinal)
    let data = Dictionary<string,obj>(StringComparer.Ordinal)
    let faults = ResizeArray<exn>()
    let mutable once = false
    let mutable onceIndex = 0
    let mutable disposed = false
    let mutable generation = 0
    let mutable matches = FrozenDictionary<string,bool>.Empty
    let queries = ResizeArray<string * ViewportMediaQuery>()
    let listener =
        { new EventListenerObject with
            member _.handleEvent _ =
                graph.AssertOnGraphThread "Scope media change"
                if not disposed then
                    use active = graph.Activate()
                    this.Refresh() |> ignore }
    let assertThread () =
        graph.AssertOnGraphThread "Scope"
        if not (Object.ReferenceEquals(Graph.Current,graph)) then invalidOp "Scope operations require their creating graph."
    let live () =
        assertThread()
        if disposed || parent.IsDisposed || lifetime.IsDisposed then raise(ObjectDisposedException "Scope")
    let reverse (items: ResizeArray<unit -> unit>) (errors: ResizeArray<exn>) =
        let snapshot = items.ToArray()
        items.Clear()
        for index = snapshot.Length - 1 downto 0 do
            try snapshot[index]() with error -> errors.Add error
    let fail (errors: ResizeArray<exn>) =
        if errors.Count > 0 then
            faults.AddRange errors
            raise(AggregateException(errors))
    let disposeOwner (owner: Owner) (errors: ResizeArray<exn>) =
        let offset = owner.ErrorCount
        try owner.Dispose() with error -> errors.Add error
        errors.AddRange(owner.ErrorsFrom offset)
    let cleanupRegular errors =
        let previousEpoch = epoch
        reverse resources errors
        reverse cleanups errors
        disposeOwner previousEpoch errors
    do
        assertThread()
        if parent.IsDisposed then
            lifetime.Dispose()
            raise(ObjectDisposedException "Owner")
        ScopeRegistration.register this.Revert
        lifetime.OnCleanup(Action(fun () -> this.Revert()))
        try
            for name, query in defaultArg mediaQueries Seq.empty do
                if queries |> Seq.exists(fun (key,_) -> key = name) then invalidArg "mediaQueries" "Media names must be unique."
                let provider = media |> Option.defaultWith(fun () -> invalidArg "media" "Media queries require the host media provider.")
                let matched = provider.MatchMedia query
                let mutable adopted = false
                let mutable target : EventTarget option = None
                let ensureConstruction () =
                    if disposed || parent.IsDisposed || lifetime.IsDisposed || not(ScopeRegistration.canConstruct()) then
                        raise(ObjectDisposedException "Scope")
                try
                    ensureConstruction()
                    let acquired = matched.Target
                    target <- Some acquired
                    ensureConstruction()
                    queries.Add(name,matched)
                    adopted <- true
                    acquired.addEventListener("change", listener)
                    ensureConstruction()
                with _ ->
                    if not adopted then
                        try matched.Dispose() with error -> faults.Add error
                    elif disposed || parent.IsDisposed || lifetime.IsDisposed then
                        target |> Option.iter(fun target ->
                            try target.removeEventListener("change", listener) with error -> faults.Add error)
                    reraise()
        with _ ->
            try this.Revert() with error -> faults.Add error
            reraise()
    /// <summary>The optional native selector root inherited by scope-aware target factories.</summary>
    member _.Root = root
    /// <summary>Resolved clock values retained by this scope; ClockDefaults updates their shared mutable cell.</summary>
    member _.Defaults = Some clockDefaults.Options
    /// <summary>The retained mutable clock-default cell; scopes without overrides share their parent's cell.</summary>
    member _.ClockDefaults = clockDefaults
    /// <summary>The executing scope captured when this scope was constructed.</summary>
    member internal _.ParentScope = parentScope
    /// <summary>Media matches sampled before the most recent scoped execution.</summary>
    member _.Matches : IReadOnlyDictionary<string,bool> = matches
    /// <summary>User data retained through refresh and cleared by revert.</summary>
    member _.Data : IDictionary<string,obj> = data
    /// <summary>Teardown failures retained after explicit revert or parent-owner disposal.</summary>
    member _.Errors : IReadOnlyList<exn> = faults.ToArray()
    /// <summary>Whether revert or parent disposal ended this scope.</summary>
    member _.IsDisposed = disposed || lifetime.IsDisposed
    /// <summary>Changes when refresh or revert supersedes a controlled restoration operation.</summary>
    member internal _.RestorationGeneration = generation
    /// <summary>Runs with this scope's active Ranvier owner and restores the previous scope after exceptions.</summary>
    member _.Execute(callback: Scope -> 'T) : 'T =
        live()
        let expected = generation
        let register revert =
            if disposed || generation <> expected then revert()
            else this.Register revert
        let previous = current.Value
        current.Value <- Some this
        try ScopePlaybackContext.run clockDefaults (fun () ->
            matches <- (queries |> Seq.map(fun (name,query) -> KeyValuePair(name,query.Matches))).ToFrozenDictionary(StringComparer.Ordinal)
            ScopeRegistration.runOwned register (fun () -> if once then persistent else epoch)
                (fun () -> not disposed && generation = expected && not parent.IsDisposed) (fun () ->
                Api.runWithOwner (if once then persistent else epoch) (fun () -> Api.untrack(fun () -> callback this))))
        finally current.Value <- previous
    /// <summary>Registers restoration in the regular or once group selected by the latest Add or AddOnce.</summary>
    member _.Register(revert: unit -> unit) =
        live()
        (if once then onceResources else resources).Add revert
    /// <summary>Executes immediately and reruns after each refresh; optional cleanup runs after registered resources.</summary>
    member _.Add(constructor: Scope -> (unit -> unit) option) =
        live()
        once <- false
        constructors.Add constructor
        let expected = generation
        this.Execute(fun scope ->
            constructor scope |> Option.iter(fun cleanup ->
                if disposed || generation <> expected then cleanup() else cleanups.Add cleanup))
        this
    /// <summary>Executes a positional once slot; the same slot survives subsequent refreshes.</summary>
    member _.AddOnce(constructor: Scope -> (unit -> unit) option) =
        live()
        once <- true
        let index = onceIndex
        onceIndex <- onceIndex + 1
        if index >= onceSlots.Count then
            onceSlots.Add(box constructor)
            this.Execute(fun scope ->
                constructor scope |> Option.iter(fun cleanup ->
                    if disposed then cleanup() else onceCleanups.Add cleanup))
        this
    /// <summary>Registers a typed named method; calling it executes within this scope.</summary>
    member _.AddMethod<'Argument,'Result>(name: string, callback: 'Argument -> 'Result) =
        live()
        once <- false
        methods[name] <- box (fun argument -> this.Execute(fun _ -> callback argument))
        this
    /// <summary>Invokes a registered method with its declared argument and result types.</summary>
    /// <exception cref="T:System.InvalidOperationException">The supplied generic types differ from the registered method.</exception>
    member _.Invoke<'Argument,'Result>(name: string, argument: 'Argument) : 'Result =
        live()
        match methods[name] with
        | :? ('Argument -> 'Result) as callback -> callback argument
        | _ -> invalidOp "Scope method argument or result type differs from its registration."
    /// <summary>Recreates a positional resource while transferring explicitly typed playback state.</summary>
    /// <remarks>The caller supplies native state capture, restoration and revert operations for its playback type.</remarks>
    member _.KeepTime<'Playback,'State>(constructor: Scope -> 'Playback, capture: 'Playback -> 'State,
                                        restore: 'Playback -> 'State -> unit, revert: 'Playback -> unit) : 'Playback =
        live()
        once <- true
        let index = onceIndex
        onceIndex <- onceIndex + 1
        if index < onceSlots.Count then
            match onceSlots[index] with
            | :? (Scope -> 'Playback) as tracked -> tracked this
            | _ -> invalidOp "A scope keepTime slot changed its playback type or replaced an addOnce slot."
        else
            let mutable tracked : 'Playback option = None
            let recreate scope =
                let state = tracked |> Option.map capture
                tracked |> Option.iter revert
                let playback = constructor scope
                tracked <- Some playback
                state |> Option.iter(restore playback)
                playback
            onceSlots.Add(box recreate)
            this.Execute recreate
    /// <summary>Reverts regular resources and cleanup in reverse order, then reconstructs them using current media matches.</summary>
    member _.Refresh() =
        live()
        generation <- generation + 1
        let expected = generation
        onceIndex <- 0
        this.Execute(fun _ ->
            let errors = ResizeArray<exn>()
            cleanupRegular errors
            if not disposed && generation = expected then
                epoch <- Api.runWithOwner regularLifetime (fun () -> Api.createRoot id)
            fail errors
            for constructor in constructors.ToArray() do
                if not disposed && generation = expected then
                    Api.runWithOwner epoch (fun () ->
                        constructor this |> Option.iter(fun cleanup ->
                            if disposed || generation <> expected then cleanup() else cleanups.Add cleanup)))
        this
    /// <summary>Restores both resource groups, removes media listeners and disposes every child owner.</summary>
    member _.Revert() =
        if not disposed then
            assertThread()
            disposed <- true
            generation <- generation + 1
            let errors = ResizeArray<exn>()
            reverse resources errors
            reverse cleanups errors
            reverse onceResources errors
            reverse onceCleanups errors
            for _,query in queries do
                try query.Target.removeEventListener("change", listener) with error -> errors.Add error
                try query.Dispose() with error -> errors.Add error
            queries.Clear()
            constructors.Clear()
            onceSlots.Clear()
            methods.Clear()
            data.Clear()
            onceIndex <- 0
            matches <- FrozenDictionary<string,bool>.Empty
            disposeOwner epoch errors
            disposeOwner regularLifetime errors
            disposeOwner persistent errors
            disposeOwner lifetime errors
            fail errors
    /// <summary>Resolves a selector against the supplied native document, falling back to the document when no target matches.</summary>
    static member ResolveRoot(document: Document, selector: string option) : ParentNode =
        match selector with
        | Some selector ->
            let found = (document :> ParentNode).querySelector selector
            if isNull(box found) then document :> ParentNode else found :> ParentNode
        | None -> document :> ParentNode
    /// <summary>The scope currently executing on this thread.</summary>
    static member Current = current.Value
    /// <summary>The shared clock-default cell used outside scoped execution and by top-level scopes without overrides.</summary>
    static member GlobalClockDefaults = ScopePlaybackContext.globalDefaults
    /// <summary>Registers a factory's restoration when constructed inside a scoped callback.</summary>
    static member RegisterCurrent(revert: unit -> unit) =
        ScopeRegistration.register revert
    interface IDisposable with
        member _.Dispose() = this.Revert()
