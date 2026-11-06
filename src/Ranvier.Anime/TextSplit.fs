namespace Ranvier.Anime

open System
open System.Collections.Generic
open System.Globalization
open System.Text
open System.Text.RegularExpressions
open Ranvier.Dom

/// <summary>Clone offset direction for split text elements.</summary>
[<RequireQualifiedAccess; Diagnostics.DebuggerDisplay("{ToString(),nq}")>]
type SplitClone =
    | None
    | Center
    | Left
    | Right
    | Top
    | Bottom
    /// <summary>Invariant diagnostic of the clone direction.</summary>
    override this.ToString() =
        match this with
        | None -> "None" | Center -> "Center" | Left -> "Left"
        | Right -> "Right" | Top -> "Top" | Bottom -> "Bottom"

/// <summary>Overflow wrapper configuration for split text elements.</summary>
[<RequireQualifiedAccess; Diagnostics.DebuggerDisplay("{ToString(),nq}")>]
type SplitWrap =
    | None
    | Clip
    | Overflow of string
    /// <summary>Invariant diagnostic of the wrap mode.</summary>
    override this.ToString() =
        match this with
        | None -> "None"
        | Clip -> "Clip"
        | Overflow s -> "Overflow " + (if isNull s then "<null>" else s)

/// <summary>Generated span template parameters for one split granularity (line, word or char).</summary>
[<NoEquality; NoComparison; Diagnostics.DebuggerDisplay("{ToString(),nq}")>]
type SplitTemplateOptions =
    { Class: string option
      Wrap: SplitWrap
      Clone: SplitClone }
    /// <summary>Default span template without extra class, wrapper or clone.</summary>
    static member Default = { Class = None; Wrap = SplitWrap.None; Clone = SplitClone.None }
    /// <summary>Invariant diagnostic of template options.</summary>
    override this.ToString() =
        "SplitTemplateOptions(Wrap=" + this.Wrap.ToString() + "; Clone=" + this.Clone.ToString() + ")"

/// <summary>Granularity activation and HTML template configuration for <see cref="T:Ranvier.Anime.TextSplitter"/>.</summary>
[<RequireQualifiedAccess; NoEquality; NoComparison; Diagnostics.DebuggerDisplay("{ToString(),nq}")>]
type SplitMode =
    | Disabled
    | Enabled
    | Options of SplitTemplateOptions
    | Template of string
    | TemplateBy of (Node -> string)
    /// <summary>Invariant diagnostic of the split mode.</summary>
    override this.ToString() =
        match this with
        | Disabled -> "Disabled"
        | Enabled -> "Enabled"
        | Options o -> "Options(" + o.ToString() + ")"
        | Template t -> "Template(" + (if isNull t then "<null>" else t) + ")"
        | TemplateBy _ -> "TemplateBy"

/// <summary>Parameters configuring <see cref="T:Ranvier.Anime.TextSplitter"/>.</summary>
[<NoEquality; NoComparison; Diagnostics.DebuggerDisplay("{ToString(),nq}")>]
type TextSplitterOptions =
    { /// <summary>Word splitting mode (Enabled by default).</summary>
      Words: SplitMode
      /// <summary>Character/grapheme splitting mode (Disabled by default).</summary>
      Chars: SplitMode
      /// <summary>Rendered line splitting mode (Disabled by default).</summary>
      Lines: SplitMode
      /// <summary>Prepends a visually-hidden accessible span and marks split spans aria-hidden (true by default).</summary>
      Accessible: bool
      /// <summary>Appends trailing spaces as non-breaking spaces inside word/char elements (false by default).</summary>
      IncludeSpaces: bool
      /// <summary>Applies dotted debug outlines to line, word and char wrappers.</summary>
      Debug: bool
      /// <summary>Optional native ICU text segmenter (e.g. TextSegmentation.forHost); falls back to Unicode rules when None.</summary>
      Segmenter: TextSegmenter option
      /// <summary>Optional document FontSet used to defer line splitting until fonts settle.</summary>
      Fonts: FontSet option
      /// <summary>Optional ResizeObservationService used to re-split when target offsetWidth changes.</summary>
      Resize: ResizeObservationService option
      /// <summary>Optional FrameScheduler used for 150ms resize debouncing and deferred line initialization.</summary>
      Scheduler: FrameScheduler option }
    /// <summary>Default splitText options (words enabled, accessible true).</summary>
    static member Default =
        { Words = SplitMode.Enabled
          Chars = SplitMode.Disabled
          Lines = SplitMode.Disabled
          Accessible = true
          IncludeSpaces = false
          Debug = false
          Segmenter = None
          Fonts = None
          Resize = None
          Scheduler = None }
    /// <summary>Invariant diagnostic of TextSplitter options.</summary>
    override this.ToString() =
        "TextSplitterOptions(Words=" + this.Words.ToString() + "; Chars=" + this.Chars.ToString() +
        "; Lines=" + this.Lines.ToString() + ")"

module internal TextSplitHelpers =
    let valueRgx = Regex(@"\{value\}", RegexOptions.CultureInvariant)
    let indexRgx = Regex(@"\{i\}", RegexOptions.CultureInvariant)
    let whiteSpaceGroupRgx = Regex(@"(\s+)", RegexOptions.CultureInvariant)
    let whiteSpaceOnlyRgx = Regex(@"^\s+$", RegexOptions.CultureInvariant)

    let generateTemplate (kind: string) (opts: SplitTemplateOptions) : string =
        let sb = StringBuilder()
        let classStr =
            match opts.Class with
            | Some c when not (String.IsNullOrEmpty c) -> " class=\"" + c + "\""
            | _ -> ""
        let hasClone = opts.Clone <> SplitClone.None
        let hasWrap = opts.Wrap <> SplitWrap.None
        let overflowOpt =
            match opts.Wrap with
            | SplitWrap.Clip -> Some "clip"
            | SplitWrap.Overflow v -> if String.IsNullOrEmpty v then None else Some v
            | SplitWrap.None -> if hasClone then Some "clip" else None
        if hasWrap then
            match overflowOpt with
            | Some ov -> sb.Append("<span style=\"overflow:").Append(ov).Append(";\">") |> ignore
            | None -> sb.Append("<span>") |> ignore
        sb.Append("<span").Append(classStr) |> ignore
        if hasClone then sb.Append(" style=\"position:relative;\"") |> ignore
        sb.Append(" data-").Append(kind).Append("=\"{i}\">") |> ignore
        if hasClone then
            let left =
                match opts.Clone with
                | SplitClone.Left -> "-100%"
                | SplitClone.Right -> "100%"
                | _ -> "0"
            let top =
                match opts.Clone with
                | SplitClone.Top -> "-100%"
                | SplitClone.Bottom -> "100%"
                | _ -> "0"
            sb.Append("<span>{value}</span>") |> ignore
            sb.Append("<span inert style=\"position:absolute;top:").Append(top)
              .Append(";left:").Append(left).Append(";white-space:nowrap;\">{value}</span>") |> ignore
        else
            sb.Append("{value}") |> ignore
        sb.Append("</span>") |> ignore
        if hasWrap then sb.Append("</span>") |> ignore
        sb.ToString()

    let resolveTemplate (kind: string) (mode: SplitMode) : (Node -> string) option =
        match mode with
        | SplitMode.Disabled -> None
        | SplitMode.Enabled ->
            let s = generateTemplate kind SplitTemplateOptions.Default
            Some (fun _ -> s)
        | SplitMode.Options opts ->
            let s = generateTemplate kind opts
            Some (fun _ -> s)
        | SplitMode.Template s -> Some (fun _ -> s)
        | SplitMode.TemplateBy fn -> Some fn

    let jsIsNumPlus (s: string) =
        if String.IsNullOrWhiteSpace s then true
        else
            let trimmed = s.Trim()
            let mutable parsed = 0.
            Double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, &parsed) && not (Double.IsNaN parsed)

    let isSegmentWordLike (seg: TextSegment) =
        seg.IsWordLike || seg.Segment = " " || jsIsNumPlus seg.Segment

    let segmentWords (segmenterOpt: TextSegmenter option) (text: string) : TextSegment array =
        match segmenterOpt with
        | Some seg ->
            match seg.Segment(text, SegmentGranularity.Word, None) with
            | SegmentationResult.Segmented res -> res.Segments
            | SegmentationResult.NotSupported _ -> [||]
        | None ->
            let parts = whiteSpaceGroupRgx.Split text
            let out = ResizeArray<TextSegment>()
            let mutable offset = 0
            for part in parts do
                if part.Length > 0 then
                    out.Add { Index = offset; Segment = part; IsWordLike = not (whiteSpaceOnlyRgx.IsMatch part) }
                    offset <- offset + part.Length
            out.ToArray()

    let segmentGraphemes (segmenterOpt: TextSegmenter option) (text: string) : string array =
        match segmenterOpt with
        | Some seg ->
            match seg.Segment(text, SegmentGranularity.Grapheme, None) with
            | SegmentationResult.Segmented res -> res.Segments |> Array.map _.Segment
            | SegmentationResult.NotSupported _ ->
                let e = StringInfo.GetTextElementEnumerator text
                let list = ResizeArray<string>()
                while e.MoveNext() do list.Add(e.GetTextElement())
                list.ToArray()
        | None ->
            let e = StringInfo.GetTextElementEnumerator text
            let list = ResizeArray<string>()
            while e.MoveNext() do list.Add(e.GetTextElement())
            list.ToArray()

    let getAllTopLevelElements (root: Element) (kind: string) : HTMLElement array =
        let list = (root :> ParentNode).querySelectorAll("[data-" + kind + "]:not([data-" + kind + "] [data-" + kind + "])")
        Array.init (int list.length) (fun i -> list.item (uint i) :?> HTMLElement)

    let rec filterEmptyElements (el: Element) =
        let txt = (el :> Node).textContent
        let trimmed = if isNull txt then "" else txt.Trim()
        if (el :> ParentNode).childElementCount = 0u && trimmed.Length = 0 then
            let parent = (el :> Node).parentElement
            let pNode = (el :> Node).parentNode
            if not (isNull (box pNode)) then pNode.removeChild(el :> Node) |> ignore
            if not (isNull (box parent)) then filterEmptyElements parent

    let rec filterLineElements (el: Element) (lineIndex: int) (bin: HashSet<Node>) =
        let attr = el.getAttribute "data-line"
        let mutable parsedLine = -1
        let hasLineMismatch =
            not (isNull attr) &&
            (not (Int32.TryParse(attr, NumberStyles.Integer, CultureInfo.InvariantCulture, &parsedLine)) || parsedLine <> lineIndex)
        if hasLineMismatch || String.Equals(el.tagName, "BR", StringComparison.OrdinalIgnoreCase) then
            bin.Add(el :> Node) |> ignore
            let prev = (el :> Node).previousSibling
            let next = (el :> Node).nextSibling
            if not (isNull (box prev)) && prev.nodeType = 3us then
                let pv = prev.textContent
                if not (isNull pv) && whiteSpaceOnlyRgx.IsMatch pv then bin.Add prev |> ignore
            if not (isNull (box next)) && next.nodeType = 3us then
                let nv = next.textContent
                if not (isNull nv) && whiteSpaceOnlyRgx.IsMatch nv then bin.Add next |> ignore
        let children = (el :> ParentNode).children
        let mutable i = int children.length - 1
        while i >= 0 do
            filterLineElements (children.item (uint i)) lineIndex bin
            i <- i - 1

    let debugColor = function
        | "line" -> "#00D672"
        | "word" -> "#FF4B4B"
        | _ -> "#5A87FF"

    let processHtmlTemplate (doc: Document) (templateEl: HTMLTemplateElement) (templateFn: Node -> string)
                            (store: ResizeArray<HTMLElement>) (node: Node) (parentFragment: DocumentFragment)
                            (kind: string) (debug: bool) (lineIndex: int) (wordIndex: int) (charIndex: int) : HTMLElement option =
        let isLine = kind = "line"
        let isChar = kind = "char"
        let className = "_" + kind + "_"
        let rawTemplate = templateFn node
        let idxValue = if isChar then charIndex elif isLine then lineIndex else wordIndex
        let html =
            indexRgx.Replace(
                valueRgx.Replace(rawTemplate, "<i class=\"" + className + "\"></i>"),
                idxValue.ToString(CultureInfo.InvariantCulture))
        (templateEl :> HTMLElement).innerHTML <- html
        let content = templateEl.content
        let firstChild = (content :> ParentNode).firstElementChild
        if isNull (box firstChild) then None
        else
            let highestParent = firstChild :?> HTMLElement
            let splitCandidate = (content :> ParentNode).querySelector("[data-" + kind + "]")
            let splitEl =
                if isNull (box splitCandidate) then highestParent
                else splitCandidate :?> HTMLElement
            let replacables = (content :> ParentNode).querySelectorAll("i." + className)
            let repLen = int replacables.length
            if repLen > 0 then
                let displayStyle = if isLine then "block" else "inline-block"
                ((highestParent :> ElementCSSInlineStyle).style).setProperty("display", displayStyle)
                ((splitEl :> ElementCSSInlineStyle).style).setProperty("display", displayStyle)
                splitEl.setAttribute("data-line", lineIndex.ToString(CultureInfo.InvariantCulture))
                if not isLine then
                    splitEl.setAttribute("data-word", wordIndex.ToString(CultureInfo.InvariantCulture))
                    if isChar then splitEl.setAttribute("data-char", charIndex.ToString(CultureInfo.InvariantCulture))
                let mutable i = repLen - 1
                while i >= 0 do
                    let replaceNode = replacables.item (uint i)
                    let closestParent = replaceNode.parentElement
                    if not (isNull (box closestParent)) then
                        DomTargetHelpers.tryStyle closestParent |> Option.iter (fun st -> st.setProperty("display", displayStyle))
                        if isLine then
                            match box node with
                            | :? HTMLElement as htmlNode -> closestParent.innerHTML <- htmlNode.innerHTML
                            | :? Element as elNode -> closestParent.innerHTML <- elNode.innerHTML
                            | _ -> closestParent.innerHTML <- (if isNull node.textContent then "" else node.textContent)
                        else
                            (closestParent :> Node).replaceChild(node.cloneNode true, replaceNode) |> ignore
                    i <- i - 1
                store.Add splitEl
                (parentFragment :> Node).appendChild(content :> Node) |> ignore
            if debug then
                ((highestParent :> ElementCSSInlineStyle).style).setProperty("outline", "1px dotted " + debugColor kind)
            Some highestParent

/// <summary>Splits DOM element text into words, characters and rendered lines while preserving nested HTML markup.</summary>
[<Sealed; Diagnostics.DebuggerDisplay("{ToString(),nq}")>]
type TextSplitter(target: HTMLElement, ?options: TextSplitterOptions) as this =
    do if isNull (box target) then nullArg "target"
    let opts = defaultArg options TextSplitterOptions.Default
    let doc = (target :> Node).ownerDocument
    let templateEl = doc.createElement "template" :?> HTMLTemplateElement
    let originalHtml =
        let h = target.innerHTML
        if isNull h then "" else h
    let isEnabled mode = match mode with SplitMode.Disabled -> false | _ -> true
    let linesOnly =
        isEnabled opts.Lines &&
        not (isEnabled opts.Words) &&
        not (isEnabled opts.Chars)
    let lineTemplate = TextSplitHelpers.resolveTemplate "line" opts.Lines
    let wordTemplate =
        if linesOnly then TextSplitHelpers.resolveTemplate "word" SplitMode.Enabled
        else TextSplitHelpers.resolveTemplate "word" opts.Words
    let charTemplate = TextSplitHelpers.resolveTemplate "char" opts.Chars
    let hasAnySplit = lineTemplate.IsSome || wordTemplate.IsSome || charTemplate.IsSome

    let linesStore = ResizeArray<HTMLElement>()
    let wordsStore = ResizeArray<HTMLElement>()
    let charsStore = ResizeArray<HTMLElement>()
    // Each effect entry returns an optional cleanup function (between splits) and revert function (on Revert).
    let effects = ResizeArray<TextSplitter -> ((TextSplitter -> unit) option * (unit -> unit) option)>()
    let effectCleanups = ResizeArray<(TextSplitter -> unit) option>()
    let effectReverts = ResizeArray<(unit -> unit) option>()

    let mutable htmlCache : string option = None
    let mutable ready = false
    let mutable lastWidth = 0
    let mutable reverted = false
    let mutable resizeTimeout : IDisposable option = None
    let mutable fontReadyOp : IDisposable option = None
    let mutable resizeObserver : ResizeObserver option = None

    let clearResizeTimeout () =
        match resizeTimeout with
        | Some d ->
            resizeTimeout <- None
            try d.Dispose() with _ -> ()
        | None -> ()

    let rec splitNode (node: Node) =
        if node.nodeType = 3us then
            let nodeText = if isNull node.nodeValue then "" else node.nodeValue
            if nodeText.Trim().Length > 0 then
                let wordSegments = TextSplitHelpers.segmentWords opts.Segmenter nodeText
                let tempWords = ResizeArray<string>()
                let mutable prevSeg : TextSegment option = None
                for wordSeg in wordSegments do
                    let segText = wordSeg.Segment
                    let wordLike = TextSplitHelpers.isSegmentWordLike wordSeg
                    let startNew =
                        match prevSeg with
                        | None -> true
                        | Some prev -> wordLike && TextSplitHelpers.isSegmentWordLike prev
                    if startNew then
                        tempWords.Add segText
                    else
                        let lastIdx = tempWords.Count - 1
                        let lastWord = tempWords[lastIdx]
                        if not (TextSplitHelpers.whiteSpaceGroupRgx.IsMatch lastWord) &&
                           not (TextSplitHelpers.whiteSpaceGroupRgx.IsMatch segText) then
                            tempWords[lastIdx] <- lastWord + segText
                        else
                            tempWords.Add segText
                    prevSeg <- Some wordSeg

                let wordsFragment = doc.createDocumentFragment()
                let mutable i = 0
                let l = tempWords.Count
                while i < l do
                    let word = tempWords[i]
                    if word.Trim().Length = 0 then
                        if not (i > 0 && opts.IncludeSpaces) then
                            (wordsFragment :> Node).appendChild(doc.createTextNode word :> Node) |> ignore
                    else
                        let hasFollowingSpace =
                            opts.IncludeSpaces && i + 1 < l && tempWords[i + 1].Trim().Length = 0
                        match charTemplate with
                        | Some cTpl ->
                            let charSegments = TextSplitHelpers.segmentGraphemes opts.Segmenter word
                            let charsFragment = doc.createDocumentFragment()
                            for j in 0 .. charSegments.Length - 1 do
                                let isLast = j = charSegments.Length - 1
                                let charText = if isLast && hasFollowingSpace then charSegments[j] + "\u00A0" else charSegments[j]
                                let charNode = doc.createTextNode charText :> Node
                                TextSplitHelpers.processHtmlTemplate
                                    doc templateEl cTpl charsStore charNode charsFragment
                                    "char" opts.Debug -1 wordsStore.Count charsStore.Count |> ignore
                            match wordTemplate with
                            | Some wTpl ->
                                TextSplitHelpers.processHtmlTemplate
                                    doc templateEl wTpl wordsStore (charsFragment :> Node) wordsFragment
                                    "word" opts.Debug -1 wordsStore.Count charsStore.Count |> ignore
                            | None ->
                                (wordsFragment :> Node).appendChild(charsFragment :> Node) |> ignore
                        | None ->
                            let textNode = doc.createTextNode(if hasFollowingSpace then word + "\u00A0" else word) :> Node
                            match wordTemplate with
                            | Some wTpl ->
                                TextSplitHelpers.processHtmlTemplate
                                    doc templateEl wTpl wordsStore textNode wordsFragment
                                    "word" opts.Debug -1 wordsStore.Count charsStore.Count |> ignore
                            | None ->
                                (wordsFragment :> Node).appendChild textNode |> ignore
                        if hasFollowingSpace then i <- i + 1
                    i <- i + 1
                let parent = node.parentNode
                if not (isNull (box parent)) then
                    parent.replaceChild(wordsFragment :> Node, node) |> ignore
        elif node.nodeType = 1us then
            let childList = node.childNodes
            let snapshot = Array.init (int childList.length) (fun idx -> childList.item (uint idx))
            for child in snapshot do splitNode child

    let runSplit (clearCache: bool) (fontsSettled: bool) =
        if not reverted then
            let isCached = htmlCache.IsSome && not clearCache
            let fontsReady =
                fontsSettled ||
                match opts.Fonts with
                | Some f -> f.Status <> FontSetStatus.Loading
                | None -> true
            let canSplitLines = lineTemplate.IsSome && fontsReady
            ready <- lineTemplate.IsNone || fontsReady
            if canSplitLines || clearCache then
                for i in 0 .. effectCleanups.Count - 1 do
                    match effectCleanups[i] with
                    | Some cleanup ->
                        effectCleanups[i] <- None
                        cleanup this
                    | None -> ()
            if not isCached then
                if clearCache then
                    target.innerHTML <- originalHtml
                    wordsStore.Clear()
                    charsStore.Clear()
                splitNode (target :> Node)
                htmlCache <- Some target.innerHTML
            if canSplitLines then
                if isCached then target.innerHTML <- htmlCache.Value
                linesStore.Clear()
                if wordTemplate.IsSome then
                    wordsStore.Clear()
                    wordsStore.AddRange(TextSplitHelpers.getAllTopLevelElements (target :> Element) "word")
            if charTemplate.IsSome && (canSplitLines || wordTemplate.IsSome) then
                charsStore.Clear()
                charsStore.AddRange(TextSplitHelpers.getAllTopLevelElements (target :> Element) "char")

            let measureElements = if wordsStore.Count > 0 then wordsStore else charsStore
            let mutable prevY : float option = None
            let mutable linesCount = 0
            for i in 0 .. measureElements.Count - 1 do
                let el = measureElements[i]
                let rect = el.getBoundingClientRect()
                match prevY with
                | Some y when rect.top - y > rect.height * 0.5 -> linesCount <- linesCount + 1
                | _ -> ()
                let lineText = linesCount.ToString(CultureInfo.InvariantCulture)
                el.setAttribute("data-line", lineText)
                let nested = (el :> ParentNode).querySelectorAll "[data-line]"
                for c in 0 .. int nested.length - 1 do
                    (nested.item (uint c) :?> Element).setAttribute("data-line", lineText)
                prevY <- Some rect.top

            if canSplitLines then
                let linesFragment = doc.createDocumentFragment()
                let parents = HashSet<Element>(HashIdentity.Reference)
                let clones = ResizeArray<HTMLElement>()
                for lineIdx in 0 .. linesCount do
                    let clone = (target :> Node).cloneNode true :?> HTMLElement
                    let bin = HashSet<Node>(HashIdentity.Reference)
                    TextSplitHelpers.filterLineElements (clone :> Element) lineIdx bin
                    for doomed in bin do
                        let p = doomed.parentNode
                        if not (isNull (box p)) then
                            if doomed.nodeType = 1us && p :? Element then
                                parents.Add(p :?> Element) |> ignore
                            p.removeChild doomed |> ignore
                    clones.Add clone
                for p in parents do TextSplitHelpers.filterEmptyElements p
                for cloneIdx in 0 .. clones.Count - 1 do
                    TextSplitHelpers.processHtmlTemplate
                        doc templateEl lineTemplate.Value linesStore (clones[cloneIdx] :> Node) linesFragment
                        "line" opts.Debug cloneIdx 0 0 |> ignore
                target.innerHTML <- ""
                (target :> Node).appendChild(linesFragment :> Node) |> ignore
                if wordTemplate.IsSome then
                    wordsStore.Clear()
                    wordsStore.AddRange(TextSplitHelpers.getAllTopLevelElements (target :> Element) "word")
                if charTemplate.IsSome then
                    charsStore.Clear()
                    charsStore.AddRange(TextSplitHelpers.getAllTopLevelElements (target :> Element) "char")

            if linesOnly then
                for w = wordsStore.Count - 1 downto 0 do
                    let wordEl = wordsStore[w]
                    let parent = (wordEl :> Node).parentNode
                    if not (isNull (box parent)) then
                        let txt = doc.createTextNode(if isNull (wordEl :> Node).textContent then "" else (wordEl :> Node).textContent)
                        parent.replaceChild(txt :> Node, wordEl :> Node) |> ignore
                wordsStore.Clear()

            if opts.Accessible && (canSplitLines || not isCached) then
                let acc = doc.createElement "span" :?> HTMLElement
                ((acc :> ElementCSSInlineStyle).style).cssText <-
                    "position:absolute;overflow:hidden;clip:rect(0 0 0 0);clip-path:inset(50%);width:1px;height:1px;white-space:nowrap;"
                acc.innerHTML <- originalHtml
                (target :> Node).insertBefore(acc :> Node, (target :> Node).firstChild) |> ignore
                for l in linesStore do l.setAttribute("aria-hidden", "true")
                for w in wordsStore do w.setAttribute("aria-hidden", "true")
                for c in charsStore do c.setAttribute("aria-hidden", "true")

            lastWidth <- target.offsetWidth
            if canSplitLines || clearCache then
                for i in 0 .. effects.Count - 1 do
                    let cleanupOpt, revertOpt = effects[i] this
                    effectCleanups[i] <- cleanupOpt
                    effectReverts[i] <- revertOpt

    let handleSplit fontsSettled =
        if originalHtml.Length > 0 && hasAnySplit && not reverted then
            runSplit false fontsSettled

    do
        ScopeRegistration.register (fun () -> this.Revert() |> ignore)
        match opts.Resize with
        | Some resizeSvc ->
            let obs =
                resizeSvc.Create(fun _ ->
                    if not reverted then
                        clearResizeTimeout()
                        let checkWidth () =
                            if not reverted then
                                let currentWidth = target.offsetWidth
                                if currentWidth <> lastWidth then
                                    lastWidth <- currentWidth
                                    handleSplit true
                        match opts.Scheduler with
                        | Some sched ->
                            resizeTimeout <- Some (sched.Timeout(150, checkWidth))
                        | None -> checkWidth())
            obs.Observe(target :> Element)
            resizeObserver <- Some obs
        | None -> ()

        if lineTemplate.IsSome && not ready then
            match opts.Fonts with
            | Some fontSet ->
                let op = fontSet.Ready(fun _ -> handleSplit true)
                fontReadyOp <- Some (op :> IDisposable)
            | None ->
                match opts.Scheduler with
                | Some sched ->
                    fontReadyOp <- Some (sched.Microtask(fun () -> handleSplit true))
                | None ->
                    handleSplit true
        else
            handleSplit true

    /// <summary>Target HTML element managed by this splitter.</summary>
    member _.Target = target
    /// <summary>Captured original innerHTML before splitting.</summary>
    member _.Html = originalHtml
    /// <summary>Current split line elements ([data-line]).</summary>
    member _.Lines = linesStore.ToArray()
    /// <summary>Current split word elements ([data-word]).</summary>
    member _.Words = wordsStore.ToArray()
    /// <summary>Current split character elements ([data-char]).</summary>
    member _.Chars = charsStore.ToArray()
    /// <summary>Whether initial splitting has completed (including deferred font readiness for lines).</summary>
    member _.Ready = ready
    /// <summary>Most recently measured offsetWidth of the target element.</summary>
    member _.Width = lastWidth

    member private this.RegisterEffectEntry(runner: TextSplitter -> ((TextSplitter -> unit) option * (unit -> unit) option)) =
        effects.Add runner
        effectCleanups.Add None
        effectReverts.Add None
        let idx = effects.Count - 1
        if ready && not reverted then
            let cleanupOpt, revertOpt = runner this
            effectCleanups[idx] <- cleanupOpt
            effectReverts[idx] <- revertOpt
        this

    /// <summary>Registers a side-effect callback invoked once split is ready and after each line/resize refresh.</summary>
    member this.AddEffect(effect: TextSplitter -> unit) =
        if isNull (box effect) then nullArg "effect"
        this.RegisterEffectEntry(fun s -> effect s; None, None)

    /// <summary>Registers an effect returning a cleanup callback run between refreshes and on Revert.</summary>
    member this.AddEffectCleanup(effect: TextSplitter -> (TextSplitter -> unit)) =
        if isNull (box effect) then nullArg "effect"
        this.RegisterEffectEntry(fun s ->
            let cleanup = effect s
            Some cleanup, None)

    /// <summary>Registers a ComposedAnimation effect tracked via keepTime across resize/refresh re-splits.</summary>
    member this.AddEffectAnimation(effect: TextSplitter -> ComposedAnimation) =
        if isNull (box effect) then nullArg "effect"
        let timekeeper = Utils.keepTime effect
        let mutable latest : ComposedAnimation option = None
        this.RegisterEffectEntry(fun s ->
            let anim = timekeeper s
            latest <- Some anim
            None, Some (fun () -> latest |> Option.iter (fun a -> a.Revert() |> ignore)))

    /// <summary>Registers a Timeline effect tracked via keepTime across resize/refresh re-splits.</summary>
    member this.AddEffectTimeline(effect: TextSplitter -> Timeline) =
        if isNull (box effect) then nullArg "effect"
        let mutable tracked : Timeline option = None
        this.RegisterEffectEntry(fun s ->
            let snap = tracked |> Option.map (fun tl -> tl.CaptureScopeTime())
            tracked |> Option.iter (fun tl -> tl.Revert())
            let next = effect s
            tracked <- Some next
            snap |> Option.iter (fun st -> next.RestoreScopeTime st)
            None, Some (fun () -> tracked |> Option.iter (fun tl -> tl.Revert())))

    /// <summary>Executes a split pass, optionally clearing cached word/char markup.</summary>
    member this.Split(?clearCache: bool) =
        runSplit (defaultArg clearCache false) true
        this

    /// <summary>Clears cached markup, runs cleanups, re-splits the target and re-applies effects.</summary>
    member this.Refresh() =
        runSplit true true
        this

    /// <summary>Disconnects resize observation, runs effect cleanups/reverts and restores original innerHTML.</summary>
    member this.Revert() =
        if not reverted then
            reverted <- true
            clearResizeTimeout()
            match fontReadyOp with
            | Some op ->
                fontReadyOp <- None
                try op.Dispose() with _ -> ()
            | None -> ()
            match resizeObserver with
            | Some obs ->
                resizeObserver <- None
                try obs.Disconnect() with _ -> ()
                try obs.Dispose() with _ -> ()
            | None -> ()
            linesStore.Clear()
            wordsStore.Clear()
            charsStore.Clear()
            for i in 0 .. effectCleanups.Count - 1 do
                match effectCleanups[i] with
                | Some cleanup ->
                    effectCleanups[i] <- None
                    cleanup this
                | None -> ()
            for i in 0 .. effectReverts.Count - 1 do
                match effectReverts[i] with
                | Some rev ->
                    effectReverts[i] <- None
                    rev()
                | None -> ()
            target.innerHTML <- originalHtml
        this

    /// <summary>Invariant diagnostic of the TextSplitter state.</summary>
    override _.ToString() =
        "TextSplitter(Lines=" + linesStore.Count.ToString(CultureInfo.InvariantCulture) +
        "; Words=" + wordsStore.Count.ToString(CultureInfo.InvariantCulture) +
        "; Chars=" + charsStore.Count.ToString(CultureInfo.InvariantCulture) + ")"

    interface IDisposable with
        member this.Dispose() = this.Revert() |> ignore

/// <summary>Anime.js text family entry points (splitText and scrambleText).</summary>
[<RequireQualifiedAccess>]
module Text =
    /// <summary>Creates a <see cref="T:Ranvier.Anime.TextSplitter"/> on a DOM element.</summary>
    let splitText (target: HTMLElement) (options: TextSplitterOptions option) : TextSplitter =
        new TextSplitter(target, ?options = options)

    /// <summary>Selects the first matching element in the active Scope or document and creates a <see cref="T:Ranvier.Anime.TextSplitter"/>.</summary>
    let splitSelector (document: Document) (selector: string) (options: TextSplitterOptions option) : TextSplitter option =
        let found = Utils.select document selector
        if found.Length = 0 then None
        else
            match box found[0] with
            | :? HTMLElement as htmlEl -> Some (new TextSplitter(htmlEl, ?options = options))
            | _ -> None

    /// <summary>Creates a function-based scrambleText tween factory matching Anime.js scrambleText(params).</summary>
    let scramble (options: ScrambleTextOptions option) : (obj -> int -> int -> string option -> ScrambleTextTween) =
        ScrambleImpl.createFactory (defaultArg options ScrambleTextOptions.Default)

    /// <summary>Evaluates scrambleText directly for a single target without a preceding tween.</summary>
    let scrambleTarget (options: ScrambleTextOptions option) (target: obj) : ScrambleTextTween =
        let factory = scramble options
        factory target 0 1 None

    /// <summary>Animates one or more sequential scrambleText steps on a DOM element or AnimatedObject using the shared Timer.</summary>
    let animateScrambleSteps (scheduler: FrameScheduler) (target: obj) (steps: ScrambleTextOptions array)
                             (durationOverride: float option) (autoplay: bool option)
                             (onComplete: (Timer -> unit) option) : Timer =
        if isNull (box scheduler) then nullArg "scheduler"
        if isNull target then nullArg "target"
        if isNull steps || steps.Length = 0 then invalidArg "steps" "Expected at least one scramble step."
        let original = ScrambleImpl.readTargetText target
        let compiled = ResizeArray<float * float * ScrambleTextTween>()
        let mutable cursor = 0.
        let mutable prevSettled : string option = None
        for stepOpts in steps do
            let effectiveOpts =
                match durationOverride with
                | Some d when steps.Length = 1 && stepOpts.Duration.IsNone && stepOpts.DurationBy.IsNone ->
                    { stepOpts with Duration = Some d }
                | _ -> stepOpts
            let tween = (ScrambleImpl.createFactory effectiveOpts) target 0 1 prevSettled
            let start = cursor + tween.Delay
            compiled.Add(start, tween.Duration, tween)
            cursor <- start + tween.Duration
            prevSettled <- Some tween.SettledText
        let totalDur = max 1e-11 cursor
        let timer = new Timer(scheduler, totalDur, ?onComplete = onComplete)
        timer.ConfigureRenderer(fun sample _ _ ->
            let t = sample.IterationTime
            let mutable idx = compiled.Count - 1
            let mutable found = false
            for i in 0 .. compiled.Count - 1 do
                if not found then
                    let st, dur, _ = compiled[i]
                    if t <= st + dur || i = compiled.Count - 1 then
                        idx <- i
                        found <- true
            let st, dur, tw = compiled[idx]
            let localProgress = if dur <= 1e-11 then 1. else Math.Clamp((t - st) / dur, 0., 1.)
            ScrambleImpl.writeTargetHtml target (tw.Modifier localProgress))
        ScopeRegistration.register (fun () ->
            timer.Revert()
            ScrambleImpl.writeTargetHtml target original)
        timer.Reset()
        if defaultArg autoplay (AnimationDefaults.autoplay true) then timer.Resume()
        timer

    /// <summary>Animates scrambleText on a target with optional duration override and completion callback.</summary>
    let animateScramble (scheduler: FrameScheduler) (target: obj) (options: ScrambleTextOptions option)
                        (durationOverride: float option) (autoplay: bool option)
                        (onComplete: (Timer -> unit) option) : Timer =
        animateScrambleSteps scheduler target [| defaultArg options ScrambleTextOptions.Default |] durationOverride autoplay onComplete

/// <summary>Timeline extensions for chaining scrambleText animations with previous-value inheritance.</summary>
[<AutoOpen>]
module TextTimelineExtensions =
    type private ScrambleChainTable() =
        static let table = System.Runtime.CompilerServices.ConditionalWeakTable<Timeline, Dictionary<obj, string>>()
        static member Get(tl: Timeline) =
            table.GetValue(tl, fun _ -> Dictionary<obj, string>(HashIdentity.Reference))

    type Timeline with
        /// <summary>Adds a scrambleText step to this timeline, chaining settled text from any prior AddScramble on the same target.</summary>
        member this.AddScramble(scheduler: FrameScheduler, target: obj, ?options: ScrambleTextOptions, ?duration: float, ?position: TimelinePosition) =
            if isNull target then nullArg "target"
            let chainMap = ScrambleChainTable.Get this
            let prevVal =
                match chainMap.TryGetValue target with
                | true, s -> Some s
                | _ -> None
            let baseOpts = defaultArg options ScrambleTextOptions.Default
            let effectiveOpts =
                match duration with
                | Some d when baseOpts.Duration.IsNone && baseOpts.DurationBy.IsNone -> { baseOpts with Duration = Some d }
                | _ -> baseOpts
            let tw = (ScrambleImpl.createFactory effectiveOpts) target 0 1 prevVal
            chainMap[target] <- tw.SettledText
            let child = ScopeRegistration.run ignore (fun () -> new Timer(scheduler, tw.Duration, options = { PlaybackOptions.Default with Delay = tw.Delay }))
            child.ConfigureRenderer(fun sample _ _ ->
                let progress = if tw.Duration <= 1e-11 then 1. else Math.Clamp(sample.IterationTime / tw.Duration, 0., 1.)
                ScrambleImpl.writeTargetHtml target (tw.Modifier progress))
            child.Reset()
            this.Sync(child, ?position = position)
