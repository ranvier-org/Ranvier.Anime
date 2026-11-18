module internal Ranvier.Anime.BlendKernel

open System.Numerics

let mutable useVector = false

let add (result: float array) (channels: float array) =
    let count = min result.Length channels.Length
    let mutable i = 0

    if Vector.IsHardwareAccelerated then
        while i <= count - Vector<float>.Count do
            (Vector<float>(result, i)
             + Vector<float>(channels, i))
                .CopyTo(result, i)

            i <- i + Vector<float>.Count

    while i < count do
        result[i] <- result[i] + channels[i]
        i <- i + 1
