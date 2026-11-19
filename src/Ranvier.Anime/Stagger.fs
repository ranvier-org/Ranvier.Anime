namespace Ranvier.Anime

open System
open System.Runtime.CompilerServices
open System.Runtime.InteropServices
open System.Runtime.Intrinsics
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
      /// <summary>An offset added to each step or range value, in the same units as the stagger.</summary>
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

    [<MethodImpl(MethodImplOptions.AggressiveInlining)>]
    let private roundDistance value =
        let scaled = value * 100.
        let nearest = Math.Round(scaled, MidpointRounding.ToEven)
        let rounded = if abs(scaled - nearest) = 0.5 then Math.Ceiling scaled else nearest
        rounded / 100.

    let private automaticDistances (options: StaggerOptions) totalLength originIndex =
        match options.Grid with
        | StaggerGrid.Automatic positions ->
            if isNull positions then nullArg "positions"
            if totalLength = 0 then
                Some [||]
            elif totalLength > positions.Length then
                None
            else
                let mutable missing = false
                let mutable idx = 0
                while idx < totalLength && not missing do
                    match positions[idx] with
                    | None -> missing <- true
                    | Some p when isNull p -> missing <- true
                    | _ -> idx <- idx + 1
                if missing then None
                else
                    let mutable minX = Double.PositiveInfinity
                    let mutable maxX = Double.NegativeInfinity
                    let mutable minY = Double.PositiveInfinity
                    let mutable maxY = Double.NegativeInfinity
                    let mutable minZ = Double.PositiveInfinity
                    let mutable maxZ = Double.NegativeInfinity
                    for i = 0 to totalLength - 1 do
                        let point = positions[i].Value
                        let x, y, z = point.x, point.y, point.z
                        if not (Double.IsFinite x && Double.IsFinite y && Double.IsFinite z) then
                            invalidArg "positions" "Position coordinates must be finite."
                        if x < minX then minX <- x
                        if x > maxX then maxX <- x
                        if y < minY then minY <- y
                        if y > maxY then maxY <- y
                        if z < minZ then minZ <- z
                        if z > maxZ then maxZ <- z
                    let originX, originY, originZ =
                        match options.Origin with
                        | StaggerOrigin.Position2D(x, y) ->
                            minX + x * (maxX - minX), minY + y * (maxY - minY), (minZ + maxZ) * 0.5
                        | StaggerOrigin.Position3D(x, y, z) ->
                            minX + x * (maxX - minX), minY + y * (maxY - minY), minZ + z * (maxZ - minZ)
                        | StaggerOrigin.Center ->
                            (minX + maxX) * 0.5, (minY + maxY) * 0.5, (minZ + maxZ) * 0.5
                        | _ ->
                            if originIndex >= 0. && originIndex < float totalLength then
                                let p = positions[int originIndex].Value
                                p.x, p.y, p.z
                            else
                                Double.NaN, Double.NaN, Double.NaN
                    let distances = Array.zeroCreate<float> totalLength
                    let mutable minimumDistance = Double.PositiveInfinity
                    match options.Axis with
                    | Some axis ->
                        for i = 0 to totalLength - 1 do
                            let p = positions[i].Value
                            let d =
                                match axis with
                                | StaggerAxis.X -> p.x - originX
                                | StaggerAxis.Y -> p.y - originY
                                | StaggerAxis.Z -> p.z - originZ
                            distances[i] <- d
                            let ad = abs d
                            if ad > 0. && ad < minimumDistance then minimumDistance <- ad
                    | None ->
                        let mutable i = 0
                        let rDist = &MemoryMarshal.GetArrayDataReference(distances)
                        if Vector512.IsHardwareAccelerated && totalLength >= 8 then
                            let vOx = Vector512.Create(originX)
                            let vOy = Vector512.Create(originY)
                            let vOz = Vector512.Create(originZ)
                            let vZero = Vector512<float>.Zero
                            let mutable vMin = Vector512.Create(Double.PositiveInfinity)
                            while i <= totalLength - 8 do
                                let p0 = positions[i].Value
                                let p1 = positions[i + 1].Value
                                let p2 = positions[i + 2].Value
                                let p3 = positions[i + 3].Value
                                let p4 = positions[i + 4].Value
                                let p5 = positions[i + 5].Value
                                let p6 = positions[i + 6].Value
                                let p7 = positions[i + 7].Value
                                let vx = Vector512.Create(p0.x, p1.x, p2.x, p3.x, p4.x, p5.x, p6.x, p7.x)
                                let vy = Vector512.Create(p0.y, p1.y, p2.y, p3.y, p4.y, p5.y, p6.y, p7.y)
                                let vz = Vector512.Create(p0.z, p1.z, p2.z, p3.z, p4.z, p5.z, p6.z, p7.z)
                                let dx = vOx - vx
                                let dy = vOy - vy
                                let dz = vOz - vz
                                let vd = Vector512.Sqrt(dx * dx + dy * dy + dz * dz)
                                vd.StoreUnsafe(&rDist, unativeint i)
                                let vPos = Vector512.GreaterThan(vd, vZero)
                                let vCand = Vector512.ConditionalSelect(vPos, vd, vMin)
                                vMin <- Vector512.ConditionalSelect(Vector512.LessThan(vCand, vMin), vCand, vMin)
                                i <- i + 8
                            for lane = 0 to 7 do
                                let lv = vMin.GetElement(lane)
                                if lv < minimumDistance then minimumDistance <- lv
                        elif Vector256.IsHardwareAccelerated && totalLength >= 4 then
                            let vOx = Vector256.Create(originX)
                            let vOy = Vector256.Create(originY)
                            let vOz = Vector256.Create(originZ)
                            let vZero = Vector256<float>.Zero
                            let mutable vMin = Vector256.Create(Double.PositiveInfinity)
                            while i <= totalLength - 4 do
                                let p0 = positions[i].Value
                                let p1 = positions[i + 1].Value
                                let p2 = positions[i + 2].Value
                                let p3 = positions[i + 3].Value
                                let vx = Vector256.Create(p0.x, p1.x, p2.x, p3.x)
                                let vy = Vector256.Create(p0.y, p1.y, p2.y, p3.y)
                                let vz = Vector256.Create(p0.z, p1.z, p2.z, p3.z)
                                let dx = vOx - vx
                                let dy = vOy - vy
                                let dz = vOz - vz
                                let vd = Vector256.Sqrt(dx * dx + dy * dy + dz * dz)
                                vd.StoreUnsafe(&rDist, unativeint i)
                                let vPos = Vector256.GreaterThan(vd, vZero)
                                let vCand = Vector256.ConditionalSelect(vPos, vd, vMin)
                                vMin <- Vector256.ConditionalSelect(Vector256.LessThan(vCand, vMin), vCand, vMin)
                                i <- i + 4
                            for lane = 0 to 3 do
                                let lv = vMin.GetElement(lane)
                                if lv < minimumDistance then minimumDistance <- lv
                        elif Vector128.IsHardwareAccelerated && totalLength >= 2 then
                            let vOx = Vector128.Create(originX)
                            let vOy = Vector128.Create(originY)
                            let vOz = Vector128.Create(originZ)
                            let vZero = Vector128<float>.Zero
                            let mutable vMin = Vector128.Create(Double.PositiveInfinity)
                            while i <= totalLength - 2 do
                                let p0 = positions[i].Value
                                let p1 = positions[i + 1].Value
                                let vx = Vector128.Create(p0.x, p1.x)
                                let vy = Vector128.Create(p0.y, p1.y)
                                let vz = Vector128.Create(p0.z, p1.z)
                                let dx = vOx - vx
                                let dy = vOy - vy
                                let dz = vOz - vz
                                let vd = Vector128.Sqrt(dx * dx + dy * dy + dz * dz)
                                vd.StoreUnsafe(&rDist, unativeint i)
                                let vPos = Vector128.GreaterThan(vd, vZero)
                                let vCand = Vector128.ConditionalSelect(vPos, vd, vMin)
                                vMin <- Vector128.ConditionalSelect(Vector128.LessThan(vCand, vMin), vCand, vMin)
                                i <- i + 2
                            for lane = 0 to 1 do
                                let lv = vMin.GetElement(lane)
                                if lv < minimumDistance then minimumDistance <- lv
                        while i < totalLength do
                            let p = positions[i].Value
                            let dx = originX - p.x
                            let dy = originY - p.y
                            let dz = originZ - p.z
                            let d = Math.Sqrt(dx * dx + dy * dy + dz * dz)
                            distances[i] <- d
                            if d > 0. && d < minimumDistance then minimumDistance <- d
                            i <- i + 1
                    if Double.IsFinite minimumDistance then
                        let mutable i = 0
                        let rDist = &MemoryMarshal.GetArrayDataReference(distances)
                        if Vector512.IsHardwareAccelerated && totalLength >= 8 then
                            let vDiv = Vector512.Create(minimumDistance)
                            while i <= totalLength - 8 do
                                (Vector512.LoadUnsafe(&rDist, unativeint i) / vDiv).StoreUnsafe(&rDist, unativeint i)
                                i <- i + 8
                        elif Vector256.IsHardwareAccelerated && totalLength >= 4 then
                            let vDiv = Vector256.Create(minimumDistance)
                            while i <= totalLength - 4 do
                                (Vector256.LoadUnsafe(&rDist, unativeint i) / vDiv).StoreUnsafe(&rDist, unativeint i)
                                i <- i + 4
                        elif Vector128.IsHardwareAccelerated && totalLength >= 2 then
                            let vDiv = Vector128.Create(minimumDistance)
                            while i <= totalLength - 2 do
                                (Vector128.LoadUnsafe(&rDist, unativeint i) / vDiv).StoreUnsafe(&rDist, unativeint i)
                                i <- i + 2
                        while i < totalLength do
                            distances[i] <- distances[i] / minimumDistance
                            i <- i + 1
                    Some distances
        | _ -> None

    /// <summary>Computes offsets before applying modifiers.</summary>
    /// <remarks>Authored numbers must be finite. Easing receives signed axis values; zero maximum distance uses zero easing input.</remarks>
    let private distributeGeneral value (options: StaggerOptions) (targetCount: uint) =
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
        let maximum =
            if distances.Length = 0 then 0.
            else
                let mutable m = distances[0]
                for i = 1 to distances.Length - 1 do
                    let v = distances[i]
                    if Double.IsNaN v || v > m then m <- v
                m
        if options.Ease.IsSome || options.Reversed then
            for index = 0 to distances.Length - 1 do
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
        let start = first + Option.defaultValue 0. options.Start
        let spacing = if isRange then (finish - first) / maximum else step
        if length = totalLength && options.IndexMap.IsNone && jitter.IsNone && options.Modifier.IsNone then
            // In-place SIMD output pass: reuses `distances` directly with zero extra array allocation!
            let mutable i = 0
            let rDist = &MemoryMarshal.GetArrayDataReference(distances)
            if Double.IsFinite spacing && Vector512.IsHardwareAccelerated && length >= 8 then
                let v100 = Vector512.Create(100.)
                let vHalf = Vector512.Create(0.5)
                let vSpacing = Vector512.Create(spacing)
                let vStart = Vector512.Create(start)
                let vZero = Vector512<float>.Zero
                while i <= length - 8 do
                    let vd = Vector512.LoadUnsafe(&rDist, unativeint i)
                    let vScaled = vd * v100
                    let vNear = Vector512.Round(vScaled, MidpointRounding.ToEven)
                    let vTie = Vector512.Equals(Vector512.Abs(vScaled - vNear), vHalf)
                    let vRound = Vector512.ConditionalSelect(vTie, Vector512.Ceiling(vScaled), vNear) / v100
                    let vTerm = vSpacing * vRound
                    // Preserve +0.0 on NaN or -0.0 term: !(vTerm == vTerm) || vTerm == 0 -> vZero
                    let vFiniteNonZero = Vector512.AndNot(Vector512.Equals(vTerm, vTerm), Vector512.Equals(vTerm, vZero))
                    let vCleanTerm = Vector512.ConditionalSelect(vFiniteNonZero, vTerm, vZero)
                    (vStart + vCleanTerm).StoreUnsafe(&rDist, unativeint i)
                    i <- i + 8
            elif Double.IsFinite spacing && Vector256.IsHardwareAccelerated && length >= 4 then
                let v100 = Vector256.Create(100.)
                let vHalf = Vector256.Create(0.5)
                let vSpacing = Vector256.Create(spacing)
                let vStart = Vector256.Create(start)
                let vZero = Vector256<float>.Zero
                while i <= length - 4 do
                    let vd = Vector256.LoadUnsafe(&rDist, unativeint i)
                    let vScaled = vd * v100
                    let vNear = Vector256.Round(vScaled, MidpointRounding.ToEven)
                    let vTie = Vector256.Equals(Vector256.Abs(vScaled - vNear), vHalf)
                    let vRound = Vector256.ConditionalSelect(vTie, Vector256.Ceiling(vScaled), vNear) / v100
                    let vTerm = vSpacing * vRound
                    let vFiniteNonZero = Vector256.AndNot(Vector256.Equals(vTerm, vTerm), Vector256.Equals(vTerm, vZero))
                    let vCleanTerm = Vector256.ConditionalSelect(vFiniteNonZero, vTerm, vZero)
                    (vStart + vCleanTerm).StoreUnsafe(&rDist, unativeint i)
                    i <- i + 4
            while i < length do
                let distance = distances[i]
                let term = spacing * roundDistance distance
                distances[i] <- start + (if Double.IsNaN term || term = 0. then 0. else term)
                i <- i + 1
            distances
        else
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

    let private flatStep length (origin: float) (step: float) (start: float) =
        // Flat origins are integral or half-integral, so roundDistance leaves every distance exact.
        let offsets = Array.zeroCreate<float> length
        let mutable index = 0
        if length > 0 then
            let rOut = &MemoryMarshal.GetArrayDataReference(offsets)
            if Vector512.IsHardwareAccelerated && length >= 8 then
                let vStride = Vector512.Create(8.)
                let vOrigin = Vector512.Create(origin)
                let vStep = Vector512.Create(step)
                let vStart = Vector512.Create(start)
                let mutable vIndices = Vector512.Create(0., 1., 2., 3., 4., 5., 6., 7.)
                while index <= length - 8 do
                    let vValues = vStart + Vector512.Abs(vOrigin - vIndices) * vStep
                    vValues.StoreUnsafe(&rOut, unativeint index)
                    vIndices <- vIndices + vStride
                    index <- index + 8
            elif Vector256.IsHardwareAccelerated && length >= 4 then
                let vStride = Vector256.Create(4.)
                let vOrigin = Vector256.Create(origin)
                let vStep = Vector256.Create(step)
                let vStart = Vector256.Create(start)
                let mutable vIndices = Vector256.Create(0., 1., 2., 3.)
                while index <= length - 4 do
                    let vValues = vStart + Vector256.Abs(vOrigin - vIndices) * vStep
                    vValues.StoreUnsafe(&rOut, unativeint index)
                    vIndices <- vIndices + vStride
                    index <- index + 4
            elif Vector128.IsHardwareAccelerated && length >= 2 then
                let vStride = Vector128.Create(2.)
                let vOrigin = Vector128.Create(origin)
                let vStep = Vector128.Create(step)
                let vStart = Vector128.Create(start)
                let mutable vIndices = Vector128.Create(0., 1.)
                while index <= length - 2 do
                    let vValues = vStart + Vector128.Abs(vOrigin - vIndices) * vStep
                    vValues.StoreUnsafe(&rOut, unativeint index)
                    vIndices <- vIndices + vStride
                    index <- index + 2
        while index < length do
            offsets[index] <- start + abs (origin - float index) * step
            index <- index + 1
        offsets

    /// <summary>Returns an offset per target, adding Start to the distributed step or range.</summary>
    let distribute value (options: StaggerOptions) targetCount =
        if isNull (box options) then nullArg "options"
        match value, options.Grid, options.Origin with
        | StaggerValue.Step step, StaggerGrid.Flat,
          (StaggerOrigin.First | StaggerOrigin.Last | StaggerOrigin.Center | StaggerOrigin.Index _)
            when options.Ease.IsNone && options.Modifier.IsNone && options.Jitter.IsNone
                 && options.IndexMap.IsNone && options.Seed.IsNone && not options.Reversed
                 && options.Total.IsNone ->
            let length = count "targetCount" targetCount
            finite "value" step
            options.Start |> Option.iter (finite "Start")
            let start = Option.defaultValue 0. options.Start
            let origin =
                match options.Origin with
                | StaggerOrigin.Last -> float length - 1.
                | StaggerOrigin.Center -> (float length - 1.) / 2.
                | StaggerOrigin.Index index -> float index
                | _ -> 0.
            flatStep length origin step (if start = 0. then 0. else start)
        | _ -> distributeGeneral value options targetCount

    /// <summary>Distributes offsets using element bounding-box centers measured on the caller's owning host thread.</summary>
    /// <remarks>Reads each relevant element once and retains no host resources. Missing elements use flat index fallback.</remarks>
    let autoElements value (options: StaggerOptions) (targets: Element array) =
        if isNull targets then nullArg "targets"
        if isNull (box options) then nullArg "options"
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
