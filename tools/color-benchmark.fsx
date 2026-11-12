#r "../src/Ranvier.Anime/bin/Release/net10.0/Ranvier.Anime.dll"

open System
open System.Diagnostics
open Ranvier.Anime

let first = ColorSpace.rgba 0.2 0.4 0.8 0.3
let last = ColorSpace.rgba 0.9 0.2 0.1 0.9

let run () =
    for i in 0..9999 do
        ColorSpace.interpolate ColorInterpolation.Hcl GamutPolicy.Clip first last (float (i % 100) / 99.)
        |> ignore

for _ in 1..3 do
    run ()

let samples =
    Array.init 60 (fun _ ->
        let allocated = GC.GetAllocatedBytesForCurrentThread ()
        let watch = Stopwatch.StartNew ()
        run ()
        watch.Stop ()

        watch.Elapsed.TotalMilliseconds,
        GC.GetAllocatedBytesForCurrentThread ()
        - allocated)

let times = samples |> Array.map fst |> Array.sort
let allocations = samples |> Array.map snd |> Array.sort
printfn "HCL-10000 median %.3f ms; p95 %.3f ms; allocation %d bytes" times[30] times[56] allocations[30]

if times[56] > 100. || allocations[30] > 16000000L then
    failwith "Color performance gate exceeded."
