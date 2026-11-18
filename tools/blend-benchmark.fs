module Ranvier.Anime.BlendBenchmark

// From the repository root, build with dotnet build src/Ranvier.Anime -c Release -p:BlendBenchmark=true
// Set DOTNET_PROCESSOR_COUNT=1 and DOTNET_TieredCompilation=0, then run
// dotnet src/Ranvier.Anime/bin/BlendBenchmark/Release/net10.0/Ranvier.Anime.dll
// Set DOTNET_EnableHWIntrinsic=0 to verify the candidate's scalar fallback.

open System
open System.Diagnostics
open System.Numerics
open System.Runtime.InteropServices
open Ranvier.Anime
open Ranvier.Dom

let private checkKernel () =
    let samples = [| -0.; 0.; 0.125; -1.25; Double.MaxValue; Double.Epsilon |]

    for n in 0 .. 3 * Vector<float>.Count + 1 do
        for m in 0 .. 3 * Vector<float>.Count + 1 do
            let values = Array.init n (fun i -> samples[i % samples.Length])
            let delta = Array.init m (fun i -> samples[(2 * i) % samples.Length])
            let expected = Array.copy values

            for c in 0 .. min n m - 1 do
                expected[c] <- expected[c] + delta[c]

            BlendKernel.add values delta

            if
                Array.map BitConverter.DoubleToInt64Bits values
                <> Array.map BitConverter.DoubleToInt64Bits expected
            then
                failwithf "Channel mismatch for lengths %d and %d" n m

let private compare label frames first middle last =
    use host = new ManualFrameScheduler ()
    let target = AnimatedObject [ "value", AdaptedValue.Text first ]

    let options =
        Some
            { ComposedAnimationOptions.Default with
                Autoplay = false
                Duration = 1000.
                Ease = id
                Composition = KeyframeComposition.Blend
            }

    let animate fromValue toValue =
        Composed.animate host target [| PropertySpec.Text ("value", toValue, fromValue = fromValue) |] options

    use lower = animate first middle
    use upper = animate middle last
    lower.Seek (373.) |> ignore

    let seek t =
        upper.Seek (t) |> ignore

    let trace mode =
        BlendKernel.useVector <- mode

        [|
            for t in [ 1.; 499.; 987.; 5.; 501.; 1. ] do
                seek t
                yield target.GetText "value"
        |]

    let scalarTrace = trace false

    if
        scalarTrace <> trace true
        || scalarTrace[0] <> Array.last scalarTrace
        || scalarTrace[0] = scalarTrace[1]
    then
        failwithf "Serialized target mismatch or inactive blend for %s" label

    let timed mode =
        BlendKernel.useVector <- mode
        let start = Stopwatch.GetTimestamp ()

        for i in 0 .. frames - 1 do
            seek (float (1 + (37 * i) % 997))

        Stopwatch.GetElapsedTime(start).TotalMicroseconds
        / float frames

    timed false |> ignore
    timed true |> ignore

    let pairs =
        Array.init 16 (fun i ->
            if i % 2 = 0 then
                let scalar = timed false
                scalar, timed true
            else
                let vector = timed true
                timed false, vector)

    let median f =
        let ordered = pairs |> Array.map f |> Array.sort
        let midpoint = ordered.Length / 2
        (ordered[midpoint - 1] + ordered[midpoint]) * 0.5

    let wins =
        pairs
        |> Array.filter (fun (scalar, candidate) -> scalar > candidate)
        |> Array.length

    printfn
        "Measured here, %s: %.3f scalar, %.3f candidate us/seek, median paired scalar/candidate %.3fx (%d/%d wins)"
        label
        (median fst)
        (median snd)
        (median (fun (scalar, candidate) -> scalar / candidate))
        wins
        pairs.Length

[<EntryPoint>]
let main _ =
    printfn
        "Measured here, %s %O, hardware SIMD %b, %d double lanes"
        RuntimeInformation.FrameworkDescription
        RuntimeInformation.ProcessArchitecture
        Vector.IsHardwareAccelerated
        Vector<float>.Count

    checkKernel ()
    compare "RGBA" 3000 "rgba(3,24,47,0.2)" "rgba(201,40,17,0.7)" "rgba(125,135,220,0.9)"

    for n in [ 17; 65; 257; 1025 ] do
        let text offset =
            Array.init n (fun i -> string ((i % 43 - 19) * offset))
            |> String.concat " "

        compare (sprintf "%d channels" n) (max 60 (30000 / n)) (text 1) (text 3) (text -2)

    0
