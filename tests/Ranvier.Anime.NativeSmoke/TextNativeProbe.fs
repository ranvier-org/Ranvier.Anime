module Ranvier.Anime.NativeSmoke.TextNativeProbe

open System
open System.Diagnostics
open Ranvier.Anime
open Ranvier.Dom
open Ranvier.View.WebCore

let private require name condition =
    if not condition then failwith ("TEXT_NATIVE_FAIL: " + name)
    Console.WriteLine("PASS text native " + name)

let private pumpCycles (host: WebCoreHost) (count: int) (ms: int) =
    for _ in 1 .. count do
        host.Cycle(milliseconds = ms)
        host.Frame()

let private pumpUntil (host: WebCoreHost) condition =
    let watch = Stopwatch.StartNew()
    while not (condition()) && watch.ElapsedMilliseconds < 3000L do
        host.Cycle(milliseconds = 5)
        host.Frame()
    require "native text host loop reached condition" (condition())

/// <summary>Verifies splitText and scrambleText against live native WebCore DOM, ICU TextSegmenter, FontSet and ResizeObserver.</summary>
let run (host: WebCoreHost) (scheduler: FrameScheduler) =
    use segmenter = TextSegmentation.forHost host
    use fonts = Fonts.forHost host
    use resize = ResizeObservation.forHost host
    let doc = host.Document

    let container = doc.createElement "div" :?> HTMLElement
    ((container :> ElementCSSInlineStyle).style).setProperty("width", "180px")
    ((container :> ElementCSSInlineStyle).style).setProperty("font-size", "16px")
    ((container :> ElementCSSInlineStyle).style).setProperty("line-height", "20px")
    doc.body.appendChild(container :> Node) |> ignore

    try
        let p = doc.createElement "p" :?> HTMLElement
        let sampleHtml = "Split <strong>native words</strong> and <em>graphemes</em> across multiple wrapped lines!"
        p.innerHTML <- sampleHtml
        container.appendChild(p :> Node) |> ignore

        let baseOpts =
            { TextSplitterOptions.Default with
                Segmenter = Some segmenter
                Fonts = Some fonts
                Resize = Some resize
                Scheduler = Some scheduler }

        // 1. Default split (words only)
        do
            use split = Text.splitText p (Some baseOpts)
            require "default words-only split populates words and 0 lines/chars"
                (split.Lines.Length = 0 && split.Words.Length = 9 && split.Chars.Length = 0)
            split.Revert() |> ignore
            require "words-only revert restores exact innerHTML" (p.innerHTML = sampleHtml)

        // 2. Chars only
        do
            use split =
                Text.splitText p (Some { baseOpts with Words = SplitMode.Disabled; Chars = SplitMode.Enabled })
            require "chars-only split populates grapheme chars and 0 words/lines"
                (split.Lines.Length = 0 && split.Words.Length = 0 && split.Chars.Length > 40)
            split.Revert() |> ignore
            require "chars-only revert restores exact innerHTML" (p.innerHTML = sampleHtml)

        // 3. Words and chars together
        do
            use split =
                Text.splitText p (Some { baseOpts with Words = SplitMode.Enabled; Chars = SplitMode.Enabled })
            require "words and chars split populates both arrays"
                (split.Words.Length = 9 && split.Chars.Length > 40)
            split.Revert() |> ignore

        // 4. Lines only (deferred via FontSet.Ready, initially 0 synchronously!)
        do
            let mutable effectFired = false
            use split =
                Text.splitText p (Some { baseOpts with Lines = SplitMode.Enabled; Words = SplitMode.Disabled })
            split.AddEffect(fun s ->
                if s.Lines.Length > 0 && s.Words.Length = 0 && s.Chars.Length = 0 then
                    effectFired <- true) |> ignore
            require "lines-only split starts deferred before font-ready turn"
                (split.Lines.Length = 0 && split.Words.Length = 0 && split.Chars.Length = 0)
            pumpUntil host (fun () -> effectFired)
            let directLineSpans = (p :> ParentNode).querySelectorAll "p > span[data-line]"
            require "lines-only split wraps top-level lines and unwraps temporary words"
                (split.Lines.Length >= 2 && split.Lines.Length = int directLineSpans.length && split.Words.Length = 0)
            split.Revert() |> ignore
            require "lines-only revert restores exact innerHTML" (p.innerHTML = sampleHtml)

        // 5. Custom classes, wrap: clip, clone: true, custom templates
        do
            let mutable wrappedReady = false
            use split =
                Text.splitText p
                    (Some { baseOpts with
                              Lines = SplitMode.Options { SplitTemplateOptions.Default with Class = Some "sp-line"; Wrap = SplitWrap.Clip; Clone = SplitClone.Center }
                              Words = SplitMode.Options { SplitTemplateOptions.Default with Class = Some "sp-word"; Clone = SplitClone.Center }
                              Chars = SplitMode.Template "<span class=\"sp-char sp-char-{i}\" data-char=\"{i}\">{value}</span>" })
            split.AddEffect(fun _ -> wrappedReady <- true) |> ignore
            pumpUntil host (fun () -> wrappedReady)
            let lineWrapOverflow =
                let parent = (split.Lines[0] :> Node).parentElement
                ((parent :?> ElementCSSInlineStyle).style).getPropertyValue "overflow"
            require "split wrap clip, clone doubling and custom template indices apply in native DOM"
                (lineWrapOverflow = "clip" &&
                 split.Words.Length = 18 &&
                 split.Chars[0].classList.contains "sp-char-0")
            split.Revert() |> ignore

        // 6. ResizeObserver + AddEffectCleanup + AddEffectAnimation (keepTime)
        do
            let mutable effectCalls = 0
            let mutable cleanupCalls = 0
            let mutable latestAnim : ComposedAnimation option = None
            use split =
                Text.splitText p (Some { baseOpts with Lines = SplitMode.Enabled })
            split.AddEffectCleanup(fun _ ->
                effectCalls <- effectCalls + 1
                fun _ -> cleanupCalls <- cleanupCalls + 1) |> ignore
            split.AddEffectAnimation(fun s ->
                let targets = s.Lines |> Array.map box
                let anim =
                    Composed.animateTargets scheduler targets [| PropertySpec.Scalar("opacity", 0.) |]
                        (Some { ComposedAnimationOptions.Default with Duration = 1000.; Ease = id })
                latestAnim <- Some anim
                anim) |> ignore
            pumpUntil host (fun () -> effectCalls = 1)
            let initialLineCount = split.Lines.Length
            require "initial line split ran effect once with zero cleanups"
                (effectCalls = 1 && cleanupCalls = 0 && initialLineCount >= 2)
            pumpCycles host 6 10
            let timeBeforeResize = latestAnim.Value.CurrentTime
            ((p :> ElementCSSInlineStyle).style).setProperty("width", "60px")
            pumpUntil host (fun () -> effectCalls >= 2)
            require "native ResizeObserver width change debounces re-split, runs cleanup once and preserves keepTime progress"
                (cleanupCalls = 1 && split.Lines.Length > initialLineCount && latestAnim.Value.CurrentTime >= timeBeforeResize)
            split.Revert() |> ignore
            ((p :> ElementCSSInlineStyle).style).removeProperty "width" |> ignore
            require "revert runs final cleanup and pauses keepTime animation"
                (cleanupCalls = 2 && latestAnim.Value.Paused && p.innerHTML = sampleHtml)

        // 7. Native scrambleText animation on DOM element
        do
            let scEl = doc.createElement "div" :?> HTMLElement
            scEl.textContent <- "Native Scramble!"
            container.appendChild(scEl :> Node) |> ignore
            let mutable completed = false
            use timer =
                Text.animateScramble scheduler (box scEl)
                    (Some { ScrambleTextOptions.Default with Seed = 7. })
                    (Some 40.) (Some true)
                    (Some (fun _ -> completed <- true))
            pumpUntil host (fun () -> completed)
            require "native scrambleText settles to exact original DOM text"
                (scEl.innerHTML = "Native Scramble!" && timer.Completed)
            container.removeChild(scEl :> Node) |> ignore
    finally
        doc.body.removeChild(container :> Node) |> ignore
