// In the repository root, build Ranvier.Anime.Tests in Release, then run:
// $env:DOTNET_PROCESSOR_COUNT='1'; dotnet fsi --optimize+ tools/stagger-benchmark.fsx
// DOTNET_EnableHWIntrinsic=0 exercises the public API's scalar fallback.
#I "../tests/Ranvier.Anime.Tests/bin/Release/net10.0"
#r "../src/Ranvier.Anime/bin/Release/net10.0/Ranvier.Anime.dll"

open System
open System.Diagnostics
open System.Numerics
open System.Reflection
open System.Runtime.InteropServices
open Ranvier.Anime

// Bind once outside timing. Both modes allocate and fill the result in the same
// compiled Release method, with the same runtime, allocator and input values.
let stagger = typeof<StaggerOptions>.Assembly.GetType("Ranvier.Anime.Stagger", true)
let flags = BindingFlags.Static ||| BindingFlags.NonPublic

let kernel =
    stagger.GetMethod("flatStep", flags).CreateDelegate<Func<bool, int, float, float, float, float array>>()

let general =
    stagger.GetMethod("distributeGeneral", flags).CreateDelegate<Func<StaggerValue, StaggerOptions, uint, float array>>()

let options =
    { StaggerOptions.Default with
        Origin = StaggerOrigin.Center
        Start = Some 3.
    }

let step = StaggerValue.Step -1.25

let run iterations (distribute: unit -> float array) =
    let mutable checksum = 0.
    let watch = Stopwatch.StartNew ()

    for i in 0 .. iterations - 1 do
        let offsets = distribute ()
        checksum <- checksum + offsets[i % offsets.Length]

    watch.Stop ()
    GC.KeepAlive checksum
    watch.Elapsed.TotalNanoseconds / float iterations

let compare iterations nameA a nameB b =
    run iterations a |> ignore
    run iterations b |> ignore

    let pairs =
        Array.init 21 (fun i ->
            if i % 2 = 0 then
                let first = run iterations a
                first, run iterations b
            else
                let second = run iterations b
                run iterations a, second)

    let timesA = Array.map fst pairs |> Array.sort
    let timesB = Array.map snd pairs |> Array.sort

    let ratios =
        pairs
        |> Array.map (fun (a, b) -> a / b)
        |> Array.sort

    printfn "  %s %.1f ns, %s %.1f ns; paired ratio %.2fx (p25 %.2f, p75 %.2f)" nameA timesA[10] nameB timesB[10] ratios[10] ratios[5] ratios[15]

printfn
    "%s %A | hardware SIMD %b | double lanes %d | server GC %b"
    RuntimeInformation.FrameworkDescription
    RuntimeInformation.ProcessArchitecture
    Vector.IsHardwareAccelerated
    Vector<float>.Count
    System.Runtime.GCSettings.IsServerGC

for count in [ 7; 16; 31; 32; 65; 257; 1024; 4097 ] do
    let origin = float (count - 1) / 2.

    let fill useSimd () =
        kernel.Invoke (useSimd, count, origin, -1.25, 3.)

    let api () =
        Stagger.distribute step options (uint count)

    let previous () =
        general.Invoke (step, options, uint count)

    let expected = previous ()

    if fill false () <> expected || api () <> expected then
        failwith "Scalar or public output mismatch"

    printfn "%d targets, one allocated distribution:" count

    if Vector.IsHardwareAccelerated then
        if fill true () <> expected then
            failwith "SIMD output mismatch"

        compare (max 1000 (1_500_000 / count)) "scalar" (fill false) "SIMD" (fill true)

    compare (max 200 (200_000 / count)) "general" previous "public" api
