namespace Ranvier.Anime

open System
open Ranvier.Dom

/// <summary>The origin of a stagger distribution. Position coordinates are relative to grid dimensions.</summary>
[<RequireQualifiedAccess>]
type StaggerOrigin =
    | First
    | Last
    | Center
    | Random
    | Index of uint
    | Position2D of x: float * y: float
    | Position3D of x: float * y: float * z: float

/// <summary>Explicit grid dimensions or measured positions; missing automatic positions cause flat index fallback.</summary>
[<RequireQualifiedAccess>]
type StaggerGrid =
    | Flat
    | Grid2D of columns: uint * rows: uint
    | Grid3D of columns: uint * rows: uint * layers: uint
    | Automatic of positions: DOMPointInit option array

/// <summary>A signed coordinate axis used instead of radial grid distance.</summary>
[<RequireQualifiedAccess>]
type StaggerAxis = X | Y | Z

/// <summary>A per-distance step or a range distributed over the maximum origin distance.</summary>
[<RequireQualifiedAccess>]
type StaggerValue = Step of float | Range of first: float * last: float

/// <summary>A constant jitter magnitude or a magnitude interpolated over stagger progress; values may be signed.</summary>
[<RequireQualifiedAccess>]
type StaggerJitter = Magnitude of float | Range of first: float * last: float

/// <summary>Stagger transformations evaluated once for an explicit target count.</summary>
[<NoEquality; NoComparison>]
type StaggerOptions =
    { Origin: StaggerOrigin
      Grid: StaggerGrid
      Axis: StaggerAxis option
      Reversed: bool
      Start: float option
      Ease: (float -> float) option
      Modifier: (float -> float) option
      Total: uint option
      /// <summary>A finite seed for reproducible random origins and jitter; None uses the shared random source.</summary>
      Seed: float option
      Jitter: StaggerJitter option
      /// <summary>Maps natural index and effective total to an index; out-of-range results fall back to the natural index.</summary>
      IndexMap: (uint -> uint -> int) option }
    /// <summary>A flat distribution from the first target with default offsets and transformations.</summary>
    static member Default =
        { Origin = StaggerOrigin.First; Grid = StaggerGrid.Flat; Axis = None; Reversed = false
          Start = None; Ease = None; Modifier = None; Total = None; Seed = None; Jitter = None; IndexMap = None }

/// <summary>Numeric Anime.js stagger distributions over explicit target indices and grids.</summary>
[<RequireQualifiedAccess>]
module Stagger =
    let private createRandom seed =
        let next =
            match seed with
            | None -> fun () -> System.Random.Shared.NextDouble()
            | Some seed ->
                let mutable state = seed
                let toUint32 (value: float) =
                    let remainder = System.Math.Truncate(value) % 4294967296.
                    uint32 (if remainder < 0. then remainder + 4294967296. else remainder)
                fun () ->
                    let bits = toUint32 (state + 1831565813.)
                    let mixed = (bits ^^^ (bits >>> 15)) * (bits ||| 1u)
                    let mixed = mixed ^^^ (mixed + ((mixed ^^^ (mixed >>> 7)) * (mixed ||| 61u)))
                    state <- float mixed
                    float (mixed ^^^ (mixed >>> 14)) / 4294967296.
        fun minimum maximum decimals ->
            let factor = System.Math.Pow(10., float decimals)
            System.Math.Floor((next() * (maximum - minimum + 1. / factor) + minimum) * factor) / factor

    let private finite name value =
        if not (Double.IsFinite value) then invalidArg name "Expected a finite value."

    let private count name (value: uint) =
        if value > uint Int32.MaxValue then invalidArg name "Count exceeds the supported array length."
        int value

    let private roundDistance value =
        let scaled = value * 100.
        let nearest = Math.Round(scaled, MidpointRounding.ToEven)
        let rounded = if abs(scaled - nearest) = 0.5 then Math.Ceiling scaled else nearest
        rounded / 100.

    let private automaticDistances (options: StaggerOptions) totalLength originIndex =
        match options.Grid with
        | StaggerGrid.Automatic positions ->
            if isNull positions then nullArg "positions"
            let points = Array.init totalLength (fun index -> if index < positions.Length then positions[index] else None)
            if points |> Array.exists (function None -> true | Some point -> isNull point) then None
            elif totalLength = 0 then Some [||]
            else
                let coordinates = points |> Array.map(fun point ->
                    let point = Option.get point
                    let x, y, z = point.x, point.y, point.z
                    if not(System.Double.IsFinite x && System.Double.IsFinite y && System.Double.IsFinite z) then
                        invalidArg "positions" "Position coordinates must be finite."
                    x, y, z)
                let minimum selector = coordinates |> Array.map selector |> Array.min
                let maximum selector = coordinates |> Array.map selector |> Array.max
                let minX, maxX = minimum (fun(x, _, _) -> x), maximum (fun(x, _, _) -> x)
                let minY, maxY = minimum (fun(_, y, _) -> y), maximum (fun(_, y, _) -> y)
                let minZ, maxZ = minimum (fun(_, _, z) -> z), maximum (fun(_, _, z) -> z)
                let originX, originY, originZ =
                    match options.Origin with
                    | StaggerOrigin.Position2D(x, y) ->
                        minX + x * (maxX - minX), minY + y * (maxY - minY), (minZ + maxZ) / 2.
                    | StaggerOrigin.Position3D(x, y, z) ->
                        minX + x * (maxX - minX), minY + y * (maxY - minY), minZ + z * (maxZ - minZ)
                    | StaggerOrigin.Center -> (minX + maxX) / 2., (minY + maxY) / 2., (minZ + maxZ) / 2.
                    | _ ->
                        if originIndex >= 0. && originIndex < float totalLength then coordinates[int originIndex]
                        else System.Double.NaN, System.Double.NaN, System.Double.NaN
                let distances = coordinates |> Array.map(fun(x, y, z) ->
                    let dx, dy, dz = originX - x, originY - y, originZ - z
                    match options.Axis with
                    | Some StaggerAxis.X -> -dx
                    | Some StaggerAxis.Y -> -dy
                    | Some StaggerAxis.Z -> -dz
                    | None -> System.Math.Sqrt(dx * dx + dy * dy + dz * dz))
                let minimumDistance = distances |> Array.fold(fun smallest distance ->
                    let distance = abs distance
                    if distance > 0. && distance < smallest then distance else smallest) System.Double.PositiveInfinity
                if System.Double.IsFinite minimumDistance then
                    for index in 0 .. distances.Length - 1 do distances[index] <- distances[index] / minimumDistance
                Some distances
        | _ -> None

    /// <summary>Returns one staggered value per target, preserving source range, origin and grid rounding behavior.</summary>
    /// <remarks>Authored numbers must be finite. Easing receives signed axis values; zero maximum distance uses zero easing input.</remarks>
    let distribute value (options: StaggerOptions) (targetCount: uint) =
        let length = count "targetCount" targetCount
        let total = options.Total |> Option.defaultValue targetCount
        let totalLength = count "Total" total
        let step, first, finish, isRange =
            match value with
            | StaggerValue.Step value ->
                finite "value" value
                value, 0., 0., false
            | StaggerValue.Range(first, finish) ->
                finite "first" first
                finite "finish" finish
                0., first, finish, true
        options.Start |> Option.iter (finite "Start")
        options.Seed |> Option.iter (finite "Seed")
        let jitter =
            options.Jitter |> Option.map(fun value ->
                let first, last =
                    match value with
                    | StaggerJitter.Magnitude amount -> amount, amount
                    | StaggerJitter.Range(first, last) -> first, last
                finite "jitter first" first
                finite "jitter last" last
                first, last)
        let random = createRandom options.Seed
        let dimensions =
            let positive name value = if value = 0u then invalidArg name "Grid dimensions must be positive."
            match options.Grid with
            | StaggerGrid.Flat | StaggerGrid.Automatic _ -> None
            | StaggerGrid.Grid2D(columns, rows) ->
                positive "columns" columns
                positive "rows" rows
                Some(float columns, float rows, 1., false)
            | StaggerGrid.Grid3D(columns, rows, layers) ->
                positive "columns" columns
                positive "rows" rows
                positive "layers" layers
                Some(float columns, float rows, float layers, true)
        let originIndex =
            match options.Origin with
            | StaggerOrigin.First | StaggerOrigin.Random -> 0.
            | StaggerOrigin.Last -> float total - 1.
            | StaggerOrigin.Center -> (float total - 1.) / 2.
            | StaggerOrigin.Index index -> float index
            | StaggerOrigin.Position2D(x, y) ->
                finite "x" x
                finite "y" y
                0.
            | StaggerOrigin.Position3D(x, y, z) ->
                finite "x" x
                finite "y" y
                finite "z" z
                0.
        let originPoint columns rows layers hasDepth =
            match options.Origin with
            | StaggerOrigin.Position2D(x, y) ->
                x * (columns - 1.), y * (rows - 1.), (if hasDepth then 0.5 * (layers - 1.) else 0.)
            | StaggerOrigin.Position3D(x, y, z) ->
                x * (columns - 1.), y * (rows - 1.), (if hasDepth then z * (layers - 1.) else 0.)
            | StaggerOrigin.Center ->
                (columns - 1.) / 2., (rows - 1.) / 2., (if hasDepth then (layers - 1.) / 2. else 0.)
            | _ ->
                originIndex % columns, Math.Floor(originIndex / columns) % rows,
                (if hasDepth then Math.Floor(originIndex / (columns * rows)) else 0.)
        let indexedDistances () =
            Array.init totalLength (fun index ->
                match dimensions with
                | None -> abs(originIndex - float index)
                | Some(columns, rows, layers, hasDepth) ->
                    let originX, originY, originZ = originPoint columns rows layers hasDepth
                    let x = float index % columns
                    let y = Math.Floor(float index / columns) % rows
                    let z = if hasDepth then Math.Floor(float index / (columns * rows)) else 0.
                    let dx, dy, dz = originX - x, originY - y, originZ - z
                    match options.Axis with
                    | Some StaggerAxis.X -> -dx
                    | Some StaggerAxis.Y -> -dy
                    | Some StaggerAxis.Z -> -dz
                    | None -> Math.Sqrt(dx * dx + dy * dy + (if hasDepth then dz * dz else 0.)))
        let distances =
            match automaticDistances options totalLength originIndex with
            | Some distances -> distances
            | None -> indexedDistances()
        let maximum = if distances.Length = 0 then 0. else Array.max distances
        for index in 0 .. distances.Length - 1 do
            let mutable distance = distances[index]
            match options.Ease with
            | Some ease ->
                let normalized = if maximum = 0. then 0. else distance / maximum
                distance <- ease normalized * maximum
            | None -> ()
            if options.Reversed then
                distance <- if options.Axis.IsSome then -distance else abs(maximum - distance)
            distances[index] <- distance
        let jitterSamples = jitter |> Option.map(fun _ -> Array.init totalLength (fun _ -> random -1. 1. 4))
        if options.Origin = StaggerOrigin.Random then
            for last in totalLength - 1 .. -1 .. 0 do
                let index = int (random 0. (float last) 0)
                let previous = distances[last]
                distances[last] <- distances[index]
                distances[index] <- previous
        let start =
            match options.Start with
            | Some value when value <> 0. -> value
            | _ -> first
        let spacing = if isRange then (finish - first) / maximum else step
        Array.init length (fun index ->
            let mapped = options.IndexMap |> Option.map(fun mapping -> mapping (uint index) total) |> Option.defaultValue index
            let mapped = if mapped >= 0 && mapped < totalLength then mapped else index
            let distance = if mapped < totalLength then distances[mapped] else Double.NaN
            let term = spacing * roundDistance distance
            let output = start + (if Double.IsNaN term || term = 0. then 0. else term)
            let output =
                match jitter, jitterSamples with
                | Some(first, last), Some samples ->
                    let progress = if maximum = 0. then 0. else distance / maximum
                    let magnitude = first + (last - first) * progress
                    let sample = if mapped < samples.Length then samples[mapped] else Double.NaN
                    output + sample * magnitude
                | _ -> output
            match options.Modifier with Some modify -> modify output | None -> output)

    /// <summary>Distributes offsets using element bounding-box centers measured on the caller's owning host thread.</summary>
    /// <remarks>Reads each relevant element once and retains no host resources. Missing elements use flat index fallback.</remarks>
    let autoElements value (options: StaggerOptions) (targets: Element array) =
        if isNull targets then nullArg "targets"
        let total = options.Total |> Option.defaultValue (uint targets.Length) |> count "Total"
        let positions = Array.create (min targets.Length total) None
        let mutable index = 0
        let mutable measuring = true
        while index < positions.Length && measuring do
            let target = targets[index]
            if isNull (box target) then measuring <- false
            else
                let rect = target.getBoundingClientRect()
                positions[index] <- Some(DOMPointInit(x = rect.left + rect.width / 2., y = rect.top + rect.height / 2.))
                index <- index + 1
        distribute value { options with Grid = StaggerGrid.Automatic positions } (uint targets.Length)
