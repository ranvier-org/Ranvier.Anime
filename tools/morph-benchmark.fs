module Ranvier.Anime.NativeSmoke.MorphBenchmark

// Run in Release with -p:MorphBenchmark=true on the NativeSmoke project.

open System
open System.Diagnostics
open System.Numerics
open System.Reflection
open System.Runtime.InteropServices
open Ranvier
open Ranvier.Anime
open Ranvier.Dom
open Ranvier.View.WebCore

let private vectorCoordinates (first:float[]) (last:float[]) progress =
    let values = Array.zeroCreate first.Length
    if progress = 0. || progress = 1. then
        Array.Copy((if progress = 0. then first else last), values, values.Length)
    else
        let mutable index = 0
        if Vector.IsHardwareAccelerated then
            let factor, t = Vector<float>(10000.), Vector<float>(progress)
            while index <= values.Length - Vector<float>.Count do
                let a, b = Vector<float>(first,index), Vector<float>(last,index)
                let scaled = (a + (b-a)*t)*factor
                let nearest = Vector.Round scaled
                let ties = Vector.Equals(Vector.Abs(scaled-nearest), Vector<float>(0.5))
                let rounded = Vector.ConditionalSelect(ties,Vector.Ceiling scaled,nearest)
                (rounded/factor).CopyTo(values,index)
                index <- index + Vector<float>.Count
        for i = index to values.Length-1 do
            values[i] <- PropertyNumbers.round 10000. (first[i]+(last[i]-first[i])*progress)
    values

// SvgMorph always passes the identity modifier. Both candidates use the same
// capture, lifetime guards and production formatter. Only the coordinate loop differs.
let private install vector (morph:SvgMorph) firstText lastText =
    let text = ValueInterpolation.Text
    let sample =
        if vector then
            let first,last = ValueInterpolation.normalize None (text firstText) (text firstText) (text lastText)
            fun progress current ->
                if not(Double.IsFinite progress) then invalidArg "progress" "Expected finite input."
                if not(current()) then None
                else
                    let values = vectorCoordinates first.Components last.Components progress
                    if current() then Some(ValueInterpolation.compose(last,0.,values)) else None
        else
            let generic = ValueInterpolation.prepare (text firstText) (text firstText) (text lastText)
            fun progress current -> generic progress 4 id current
    // Substitute the sampler once, outside timing, without a runtime dispatch hook.
    let field = typeof<SvgMorph>.GetField("interpolate",BindingFlags.NonPublic ||| BindingFlags.Instance)
    field.SetValue(morph,Some sample)

let private timed frames action =
    let start = Stopwatch.GetTimestamp()
    for i in 0..frames-1 do action (float(1 + (i*37)%997))
    Stopwatch.GetElapsedTime(start).TotalMicroseconds/float frames

let private paired frames name scalar vector =
    let measure = timed frames
    measure scalar |> ignore
    measure vector |> ignore
    let pairs = Array.init 16 (fun i ->
        if i%2=0 then
            let a=measure scalar
            a,measure vector
        else
            let b=measure vector
            measure scalar,b)
    let median (values:float[]) =
        let sorted = Array.sort values
        (sorted[7]+sorted[8])/2.
    let ratios = pairs |> Array.map(fun (a,b) -> a/b) |> Array.sort
    printfn "  %s: %.3f / %.3f us, median paired ratio %.4fx (quartiles %.4f, %.4f)"
        name (pairs |> Array.map fst |> median) (pairs |> Array.map snd |> median)
        (median ratios) ratios[3] ratios[12]

let private compare (host:WebCoreHost) (scheduler:ManualFrameScheduler) count =
    let document = host.Document
    let ns = "http://www.w3.org/2000/svg"
    let svg = document.createElementNS(ns,"svg")
    document.body.appendChild svg |> ignore
    let path data =
        let element = document.createElementNS(ns,"path") :?> SVGGeometryElement
        element.setAttribute("d",data)
        svg.appendChild element |> ignore
        element
    let data offset =
        Array.init count (fun i ->
            let command = if i=0 then "M" else "L"
            let x = float (i%211-105) * (0.25+offset)
            let y = float (i%113-56) * (0.125-offset)
            command + ValueInterpolation.number x + " " + ValueInterpolation.number y)
        |> String.concat " "
    let first,last = data 0.,data 0.375
    let destination = path last
    let create mode =
        let source = path first
        let morph = SvgProperties.morph source destination 0. None
        mode |> Option.iter(fun simd -> install simd morph first last)
        let options = { KeyframeAnimationOptions.Default with Ease=id; Autoplay=false }
        let animation = Keyframes.Numeric(scheduler,morph.Progress,[|Keyframe.To 1.|],duration=1000u,options=options)
        source,morph,animation
    try
        let reference,refMorph,refAnimation = create (Some false)
        let scalar,scalarMorph,scalarAnimation = create None
        let vector,vectorMorph,vectorAnimation = create (Some true)
        use _r = refAnimation
        use _s = scalarAnimation
        use _v = vectorAnimation
        use _rm = refMorph
        use _sm = scalarMorph
        use _vm = vectorMorph
        for t in [0.;1.;123.45;500.;997.;1000.;375.;0.] do
            for animation in [refAnimation;scalarAnimation;vectorAnimation] do animation.Playback.Seek t
            let expected = reference.getAttribute "d"
            if (t=500. && (expected=first || expected=last)) || (t=1000. && expected<>last) ||
               scalar.getAttribute "d" <> expected || vector.getAttribute "d" <> expected then
                failwithf "Native path mismatch at %g ms (%d points)" t count
        let components text = (ValueInterpolation.decompose(ValueInterpolation.Text text)).Components
        let fromCoords,toCoords = components first,components last
        for t in [-0.5;0.;0.0001;0.125;0.5;0.99999;1.;1.25] do
            if ValueInterpolation.morphCoordinates fromCoords toCoords t <> vectorCoordinates fromCoords toCoords t then
                failwithf "Coordinate mismatch at %g" t
        let kernel fill t = GC.KeepAlive(fill fromCoords toCoords (t/1000.))
        printfn "%d points" count
        paired (max 1000 (1000000/count)) "coordinate array scalar / SIMD" (kernel ValueInterpolation.morphCoordinates) (kernel vectorCoordinates)
        let frames = max 90 (100000/count)
        let vectorSeek t = vectorAnimation.Playback.Seek t
        paired frames "full seek generic / specialized scalar" (fun t -> refAnimation.Playback.Seek t) (fun t -> scalarAnimation.Playback.Seek t)
        paired frames "full seek specialized scalar / SIMD" (fun t -> scalarAnimation.Playback.Seek t) vectorSeek
        paired frames "full seek generic / SIMD" (fun t -> refAnimation.Playback.Seek t) vectorSeek
    finally document.body.removeChild svg |> ignore

[<EntryPoint>]
let main _ =
    use host = WebCoreHost.Create(320,200)
    use graph = new Graph()
    use active = graph.Activate()
    use scheduler = new ManualFrameScheduler()
    printfn "%s %O, SIMD %b, %d double lanes; public Seek through mounted path.setAttribute"
        RuntimeInformation.FrameworkDescription RuntimeInformation.ProcessArchitecture Vector.IsHardwareAccelerated Vector<float>.Count
    let owner = Api.createRoot id
    use lifetime = owner
    Api.runWithOwner owner (fun () ->
        for points in [7;33;129;513;2049] do compare host scheduler points)
    0
