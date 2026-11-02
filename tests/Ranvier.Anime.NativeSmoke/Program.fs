module Ranvier.Anime.NativeSmoke.Program

open System
open System.Diagnostics
open System.Globalization
open Ranvier
open Ranvier.Anime
open Ranvier.Css
open Ranvier.Dom
open Ranvier.View.WebCore

let private require name condition =
    if not condition then failwith name
    Console.WriteLine("PASS " + name)

let private pumpUntil (host: WebCoreHost) condition =
    let watch = Stopwatch.StartNew()
    while not(condition()) && watch.ElapsedMilliseconds < 3000L do
        host.Cycle(milliseconds = 1)
        host.Frame()
    require "native frame loop reached its target" (condition())

[<EntryPoint>]
let main args =
    try
        if Array.contains "--require-aot" args then
            require "consumer is NativeAOT" (not System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported)
#if EXPECT_TRACED
        let expectedTrace = true
#else
        let expectedTrace = false
#endif
        for assembly in [typeof<Graph>.Assembly; typeof<Document>.Assembly; typeof<WebCoreHost>.Assembly] do
            let traced =
                System.Reflection.CustomAttributeExtensions.GetCustomAttributes<System.Reflection.AssemblyMetadataAttribute>(assembly)
                |> Seq.exists(fun attribute -> attribute.Key = "RanvierTrace" && attribute.Value = "true")
            require (assembly.GetName().Name + " trace cohort") (traced = expectedTrace)
        use host = WebCoreHost.Create(320, 200)
        use scheduler = Frames.forHost host
        let element = host.Document.createElement "div" :?> HTMLElement
        host.Document.body.appendChild element |> ignore
        let style = (element :> ElementCSSInlineStyle).style
        style.setProperty("--anime-gap", "0px", priority = "important")
        style.setProperty("width", "var(--anime-gap)")
        style.setProperty("height", "20px")
        style.setProperty("background-color", "red")
        use tween = CssAnimation.customProperty scheduler style (RuntimeApi.createCustomProperty "--anime-gap") (Ranvier.Css.Css.px 0.) (Ranvier.Css.Css.px 100.) 100.
        tween.Pause()
        tween.Seek 25.
        require "typed CSS seek writes native style" (style.getPropertyValue "--anime-gap" = "25px")
        require "typed CSS retains priority" (style.getPropertyPriority "--anime-gap" = "important")
        tween.Play()
        pumpUntil host (fun () -> style.getPropertyValue "--anime-gap" = "100px")
        require "native layout consumes animated custom property" (abs(element.getBoundingClientRect().width - 100.) < 0.01)
        use graph = new Graph()
        use activation = graph.Activate()
        let mutable writes = 0
        let owner, signal = Api.createRoot(fun owner ->
            let signal = Api.createSignal 0.
            Reactive.drive scheduler 50. (fun () -> signal.Value) (fun value ->
                writes <- writes + 1
                style.setProperty("opacity", value.ToString("G17", CultureInfo.InvariantCulture))) |> ignore
            owner, signal)
        graph.Flush()
        signal.Value <- 1.
        graph.Flush()
        pumpUntil host (fun () -> style.getPropertyValue "opacity" = "1")
        owner.Dispose()
        let priorWrites = writes
        signal.Value <- 0.
        graph.Flush()
        host.Cycle()
        host.Frame()
        require "Ranvier owner disposal stops native writes" (writes = priorWrites)
        require "native host has no listener errors" (host.Errors.Count = 0)
        Console.WriteLine "NATIVE_ANIME_SMOKE_PASS"
        0
    with error ->
        Console.Error.WriteLine("NATIVE_ANIME_SMOKE_FAIL " + error.GetType().FullName + ": " + error.Message)
        1
