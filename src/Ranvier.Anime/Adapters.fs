namespace Ranvier.Anime

open System
open System.Collections.Generic
open System.Globalization

/// <summary>Value read from or supplied to a custom target adapter property.</summary>
[<RequireQualifiedAccess; Diagnostics.DebuggerDisplay("{ToString(),nq}")>]
type AdaptedValue =
    /// <summary>Finite numeric scalar property value.</summary>
    | Scalar of float
    /// <summary>String, unit, hex/rgb/hsl color or compound value.</summary>
    | Text of string
    /// <summary>An invariant diagnostic of the adapted value.</summary>
    override this.ToString() =
        match this with
        | Scalar v -> "Scalar " + v.ToString("R", CultureInfo.InvariantCulture)
        | Text s -> "Text " + (if isNull s then "<null>" else s)

/// <summary>Sampled tween state passed to a custom target adapter setter.</summary>
[<Sealed; Diagnostics.DebuggerDisplay("{ToString(),nq}")>]
type AdaptedTweenInfo internal (target: obj, property: string, number: float, numbers: float array) =
    /// <summary>Target object instance being animated.</summary>
    member _.Target = target
    /// <summary>Canonical property name resolved on the target.</summary>
    member _.Property = property
    /// <summary>Interpolated scalar number (0 for color and multi-component tweens).</summary>
    member _.Number = number
    /// <summary>Interpolated component array (RGBA channels for colors, template numbers for complex values).</summary>
    member _.Numbers = numbers
    /// <summary>An invariant diagnostic identifying the adapted property and scalar state.</summary>
    override _.ToString() =
        "AdaptedTweenInfo(Property=" + property + "; Number=" + number.ToString("R", CultureInfo.InvariantCulture) + ")"

/// <summary>Resolved getter, setter and optional per-target gate for an adapted property.</summary>
[<NoEquality; NoComparison; Diagnostics.DebuggerDisplay("{ToString(),nq}")>]
type TargetAdapterEntry =
    { /// <summary>Reads the current property value from a target.</summary>
      Get: obj -> AdaptedValue
      /// <summary>Writes a sampled scalar (Some v) or color/complex components (None, via tween.Numbers) to a target.</summary>
      Set: obj -> float option -> AdaptedTweenInfo -> unit
      /// <summary>Optional target predicate; when false, resolution falls through.</summary>
      Gate: (obj -> bool) option }
    /// <summary>An invariant diagnostic for a target adapter entry.</summary>
    override this.ToString() =
        "TargetAdapterEntry(Gated=" + this.Gate.IsSome.ToString() + ")"

/// <summary>Per-class or predicate-matched property table scoped to an <see cref="T:Ranvier.Anime.Adapter"/>.</summary>
[<Sealed; Diagnostics.DebuggerDisplay("{ToString(),nq}")>]
type TargetAdapter internal (detect: obj -> bool) =
    let props = Dictionary<string, TargetAdapterEntry>(StringComparer.Ordinal)

    member internal _.Detect = detect
    member internal _.TryGetProperty(name: string) =
        match props.TryGetValue name with
        | true, entry -> Some entry
        | _ -> None

    /// <summary>Registers a property handler with optional target gate.</summary>
    member _.RegisterProperty(name: string, getter: obj -> AdaptedValue,
                              setter: obj -> float option -> AdaptedTweenInfo -> unit, ?gate: obj -> bool) =
        if String.IsNullOrWhiteSpace name then invalidArg "name" "A property name is required."
        if isNull (box getter) then nullArg "getter"
        if isNull (box setter) then nullArg "setter"
        props[name] <- { Get = getter; Set = setter; Gate = gate }

    /// <summary>Registers a strongly typed scalar numeric property handler.</summary>
    member this.RegisterScalar<'Target>(name: string, getter: 'Target -> float,
                                        setter: 'Target -> float -> AdaptedTweenInfo -> unit,
                                        ?gate: 'Target -> bool) =
        if isNull (box getter) then nullArg "getter"
        if isNull (box setter) then nullArg "setter"
        let untypedGate = gate |> Option.map (fun g -> fun (t: obj) -> match t with :? 'Target as typed -> g typed | _ -> false)
        this.RegisterProperty(
            name,
            (fun t -> AdaptedValue.Scalar (getter (unbox<'Target> t))),
            (fun t scalarOpt info ->
                let v = defaultArg scalarOpt info.Number
                setter (unbox<'Target> t) v info),
            ?gate = untypedGate)

    /// <summary>Registers a strongly typed RGBA color property handler receiving 0-255 RGB and 0-1 alpha channels.</summary>
    member this.RegisterColor<'Target>(name: string, getter: 'Target -> string,
                                       setter: 'Target -> float * float * float * float -> AdaptedTweenInfo -> unit,
                                       ?gate: 'Target -> bool) =
        if isNull (box getter) then nullArg "getter"
        if isNull (box setter) then nullArg "setter"
        let untypedGate = gate |> Option.map (fun g -> fun (t: obj) -> match t with :? 'Target as typed -> g typed | _ -> false)
        this.RegisterProperty(
            name,
            (fun t -> AdaptedValue.Text (getter (unbox<'Target> t))),
            (fun t _ info ->
                let c = info.Numbers
                let r = if c.Length > 0 then c[0] else 0.
                let g = if c.Length > 1 then c[1] else 0.
                let b = if c.Length > 2 then c[2] else 0.
                let a = if c.Length > 3 then c[3] else 1.
                setter (unbox<'Target> t) (r, g, b, a) info),
            ?gate = untypedGate)

    /// <summary>An invariant diagnostic reporting registered property count.</summary>
    override _.ToString() = "TargetAdapter(Properties=" + props.Count.ToString(CultureInfo.InvariantCulture) + ")"

/// <summary>Extensible target and property-resolver group matching Anime.js registerAdapter().</summary>
[<Sealed; Diagnostics.DebuggerDisplay("{ToString(),nq}")>]
type Adapter internal (detect: (obj -> bool) option) =
    let targetAdapters = ResizeArray<TargetAdapter>()
    let propertyResolvers = ResizeArray<obj -> string -> TargetAdapterEntry option>()

    member internal _.Detect = detect
    member internal _.TargetAdapters = targetAdapters
    member internal _.PropertyResolvers = propertyResolvers

    /// <summary>Creates and registers a predicate-matched <see cref="T:Ranvier.Anime.TargetAdapter"/>.</summary>
    member _.RegisterTargetAdapter(targetDetect: obj -> bool) : TargetAdapter =
        if isNull (box targetDetect) then nullArg "targetDetect"
        let ta = TargetAdapter targetDetect
        targetAdapters.Add ta
        ta

    /// <summary>Creates and registers a type-tested <see cref="T:Ranvier.Anime.TargetAdapter"/>.</summary>
    member this.RegisterTypeAdapter<'Target>(?predicate: 'Target -> bool) : TargetAdapter =
        this.RegisterTargetAdapter(fun candidate ->
            match candidate with
            | :? 'Target as typed -> predicate |> Option.forall (fun p -> p typed)
            | _ -> false)

    /// <summary>Registers a fallback runtime property resolver invoked when no target adapter claims the name.</summary>
    member _.RegisterPropertyResolver(resolver: obj -> string -> TargetAdapterEntry option) =
        if isNull (box resolver) then nullArg "resolver"
        if not (propertyResolvers |> Seq.exists (fun existing -> Object.ReferenceEquals(existing, resolver))) then
            propertyResolvers.Add resolver

    /// <summary>An invariant diagnostic of the adapter group.</summary>
    override _.ToString() =
        "Adapter(TargetAdapters=" + targetAdapters.Count.ToString(CultureInfo.InvariantCulture) +
        "; Resolvers=" + propertyResolvers.Count.ToString(CultureInfo.InvariantCulture) + ")"

/// <summary>Isolated or shared registry of Anime.js target adapters and property resolvers.</summary>
[<Sealed; Diagnostics.DebuggerDisplay("{ToString(),nq}")>]
type AdapterRegistry() =
    let adapters = ResizeArray<Adapter>()

    /// <summary>Registers a new <see cref="T:Ranvier.Anime.Adapter"/> with an optional outer short-circuit predicate.</summary>
    member _.Register(?detect: obj -> bool) : Adapter =
        let adapter = Adapter detect
        adapters.Add adapter
        adapter

    /// <summary>Resolves an adapted property using upstream two-pass precedence (first matching target adapter, then resolvers).</summary>
    member _.Resolve(target: obj, name: string) : TargetAdapterEntry option =
        if isNull target || String.IsNullOrEmpty name then None
        else
            let mutable claimed : TargetAdapterEntry option = None
            let mutable stopTargetScan = false
            let mutable i = 0
            while i < adapters.Count && not stopTargetScan do
                let a = adapters[i]
                let outerAllows = match a.Detect with Some d -> d target | None -> true
                if outerAllows then
                    let mutable j = 0
                    while j < a.TargetAdapters.Count && not stopTargetScan do
                        let ta = a.TargetAdapters[j]
                        if ta.Detect target then
                            stopTargetScan <- true
                            match ta.TryGetProperty name with
                            | Some entry when (match entry.Gate with Some g -> g target | None -> true) ->
                                claimed <- Some entry
                            | _ -> ()
                        j <- j + 1
                i <- i + 1
            if claimed.IsSome then claimed
            else
                let mutable resolved : TargetAdapterEntry option = None
                let mutable k = 0
                while k < adapters.Count && resolved.IsNone do
                    let a = adapters[k]
                    let outerAllows = match a.Detect with Some d -> d target | None -> true
                    if outerAllows then
                        let mutable m = 0
                        while m < a.PropertyResolvers.Count && resolved.IsNone do
                            resolved <- a.PropertyResolvers[m] target name
                            m <- m + 1
                    k <- k + 1
                resolved

    /// <summary>Removes all registered adapters in this registry.</summary>
    member _.Clear() = adapters.Clear()

    /// <summary>An invariant diagnostic reporting registered adapter count.</summary>
    override _.ToString() = "AdapterRegistry(Count=" + adapters.Count.ToString(CultureInfo.InvariantCulture) + ")"

/// <summary>Global Anime.js adapter registration, resolution and color-space helpers.</summary>
[<AbstractClass; Sealed>]
type Adapters private () =
    static let defaultRegistry = AdapterRegistry()

    /// <summary>Default process-wide adapter registry matching Anime.js registerAdapter().</summary>
    static member DefaultRegistry = defaultRegistry

    /// <summary>Creates and registers an <see cref="T:Ranvier.Anime.Adapter"/> in the default registry.</summary>
    static member register(?detect: obj -> bool) : Adapter =
        match detect with
        | Some d -> defaultRegistry.Register d
        | None -> defaultRegistry.Register()

    /// <summary>Resolves a property entry against the default registry.</summary>
    static member resolve (target: obj) (name: string) : TargetAdapterEntry option =
        defaultRegistry.Resolve(target, name)

    /// <summary>Converts a normalized [0,1] sRGB channel to linear-sRGB working space.</summary>
    static member srgbToLinear (channel: float) : float =
        if channel < 0.04045 then channel / 12.92
        else Math.Pow((channel + 0.055) / 1.055, 2.4)

    /// <summary>Converts a linear-sRGB channel to normalized [0,1] sRGB.</summary>
    static member linearToSrgb (channel: float) : float =
        if channel <= 0.0031308 then channel * 12.92
        else 1.055 * Math.Pow(channel, 1.0 / 2.4) - 0.055

/// <summary>
/// Deferred stub surface for the upstream JavaScript <c>animejs/adapters/three</c> scene-graph integration.
/// </summary>
/// <remarks>
/// JavaScript Three.js WebGL/WebGPU classes (<c>Object3D</c>, <c>InstancedMesh</c>, <c>BatchedMesh</c>,
/// <c>ShaderMaterial</c>, TSL <c>UniformNode</c>) require a JavaScript Three.js runtime outside the scope
/// of the native .NET WebCore port (see <c>parity-inventory.json</c>). Native .NET scene graphs and custom
/// widgets are supported through <see cref="M:Ranvier.Anime.Adapters.register(Microsoft.FSharp.Core.FSharpOption{Microsoft.FSharp.Core.FSharpFunc{System.Object,System.Boolean}})"/>.
/// </remarks>
[<RequireQualifiedAccess>]
module ThreeJsAdapter =
    let private deferred memberName =
        raise (NotSupportedException(
            "ThreeJsAdapter." + memberName + " is deferred: JavaScript three.js runtime scene-graph classes are outside the scope of the native .NET WebCore port. Use Adapters.register for typed .NET objects."))

    /// <summary>Deferred stub for registering the built-in JavaScript Three.js scene-graph adapter.</summary>
    let register () : Adapter = deferred "register"

    /// <summary>Deferred stub for Three.js InstancedMesh / BatchedMesh proxy extraction (getInstances).</summary>
    let getInstances (_mesh: obj) : obj array = deferred "getInstances"

    /// <summary>Deferred stub for flushing Three.js InstancedMesh / BatchedMesh matrix and color buffers (commitChanges).</summary>
    let commitChanges (_target: obj) : unit = deferred "commitChanges"
