namespace Ranvier.Anime

open System
open System.Collections.Generic
open System.Globalization
open System.Runtime.CompilerServices
open System.Text
open Ranvier.Dom

/// <summary>Reveal wave origin for <see cref="M:Ranvier.Anime.Text.scramble(Microsoft.FSharp.Core.FSharpOption{Ranvier.Anime.ScrambleTextOptions})"/>.</summary>
[<RequireQualifiedAccess; Diagnostics.DebuggerDisplay("{ToString(),nq}")>]
type ScrambleFrom =
    /// <summary>Automatically reveals from left when growing/equal and from right when shrinking.</summary>
    | Auto
    /// <summary>Reveals sequentially from index 0 to the end.</summary>
    | Left
    /// <summary>Reveals from the right boundary toward index 0.</summary>
    | Right
    /// <summary>Reveals outward from the center character.</summary>
    | Center
    /// <summary>Reveals in a seeded or pseudorandom permutation order.</summary>
    | Random
    /// <summary>Reveals outward from an explicit character index.</summary>
    | Index of float
    /// <summary>Invariant diagnostic of the reveal origin.</summary>
    override this.ToString() =
        match this with
        | Auto -> "Auto"
        | Left -> "Left"
        | Right -> "Right"
        | Center -> "Center"
        | Random -> "Random"
        | Index idx -> "Index " + idx.ToString("R", CultureInfo.InvariantCulture)

/// <summary>Leading cursor configuration for active scrambling characters.</summary>
[<RequireQualifiedAccess; Diagnostics.DebuggerDisplay("{ToString(),nq}")>]
type ScrambleCursor =
    /// <summary>No leading cursor characters.</summary>
    | None
    /// <summary>Default underscore '_' cursor (cursor: true).</summary>
    | Default
    /// <summary>Single UTF-16 code unit cursor (cursor: number).</summary>
    | CharCode of int
    /// <summary>Explicit cursor string (cursor: string).</summary>
    | Text of string
    /// <summary>Invariant diagnostic of the cursor mode.</summary>
    override this.ToString() =
        match this with
        | None -> "None"
        | Default -> "Default '_'"
        | CharCode c -> "CharCode " + c.ToString(CultureInfo.InvariantCulture)
        | Text s -> "Text " + (if isNull s then "<null>" else s)

/// <summary>Pre-transition starting text display mode for <see cref="M:Ranvier.Anime.Text.scramble(Microsoft.FSharp.Core.FSharpOption{Ranvier.Anime.ScrambleTextOptions})"/>.</summary>
[<RequireQualifiedAccess; Diagnostics.DebuggerDisplay("{ToString(),nq}")>]
type ScrambleOverride =
    /// <summary>Scrambles starting characters using the active character set (override: true, default).</summary>
    | MatchChars
    /// <summary>Preserves the original starting text until each character enters the active zone (override: false).</summary>
    | Preserve
    /// <summary>Starts blank with empty string and animates only target text length (override: '').</summary>
    | Blank
    /// <summary>Replaces non-space starting characters with non-breaking spaces (override: ' ').</summary>
    | Space
    /// <summary>Scrambles starting characters using a named or custom range-expanded character set.</summary>
    | Custom of string
    /// <summary>Invariant diagnostic of the override mode.</summary>
    override this.ToString() =
        match this with
        | MatchChars -> "MatchChars"
        | Preserve -> "Preserve"
        | Blank -> "Blank"
        | Space -> "Space"
        | Custom s -> "Custom " + (if isNull s then "<null>" else s)

/// <summary>Parameters configuring Anime.js scrambleText behavior.</summary>
[<NoEquality; NoComparison; Diagnostics.DebuggerDisplay("{ToString(),nq}")>]
type ScrambleTextOptions =
    { /// <summary>Named charset ('lowercase', 'uppercase', 'numbers', 'symbols', 'braille', 'blocks', 'shades') or range string.</summary>
      Chars: string option
      /// <summary>Target-dependent character set provider (target, index, total).</summary>
      CharsBy: (obj -> int -> int -> string) option
      /// <summary>Easing curve remapping linear reveal progress (defaults to linear).</summary>
      Ease: (float -> float) option
      /// <summary>Explicit destination text; when None, uses previous tween value or cached original target text.</summary>
      Text: string option
      /// <summary>Target-dependent destination text provider.</summary>
      TextBy: (obj -> int -> int -> string) option
      /// <summary>Reveal wave origin (defaults to Auto).</summary>
      From: ScrambleFrom
      /// <summary>Reverses the computed character reveal order.</summary>
      Reversed: bool
      /// <summary>Per-character start/end timing jitter factor (defaults to 0).</summary>
      Perturbation: float
      /// <summary>Cursor characters shown at the leading edge of each character's active window.</summary>
      Cursor: ScrambleCursor
      /// <summary>Deterministic Mulberry32 seed; 0 uses auto-incrementing seed like upstream.</summary>
      Seed: float
      /// <summary>Pre-transition starting text display mode (defaults to MatchChars / true).</summary>
      Override: ScrambleOverride
      /// <summary>Character reveal rate per second when Duration is omitted (defaults to 60).</summary>
      RevealRate: float
      /// <summary>Active scrambling window duration in engine time units (defaults to 300 * timeScale).</summary>
      SettleDuration: float option
      /// <summary>Scramble character refresh rate per second (defaults to 30).</summary>
      SettleRate: float
      /// <summary>Explicit animation duration override.</summary>
      Duration: float option
      /// <summary>Target-dependent animation duration provider.</summary>
      DurationBy: (obj -> int -> int -> float) option
      /// <summary>Delay before character reveal begins within the tween duration.</summary>
      RevealDelay: float
      /// <summary>Target-dependent reveal delay provider.</summary>
      RevealDelayBy: (obj -> int -> int -> float) option
      /// <summary>Outer tween start delay.</summary>
      Delay: float
      /// <summary>Target-dependent outer tween delay provider.</summary>
      DelayBy: (obj -> int -> int -> float) option
      /// <summary>Callback invoked whenever scramble characters refresh (scrambledText, easedProgress).</summary>
      OnChange: string -> float -> unit }
    /// <summary>Default scrambleText options matching upstream Anime.js defaults.</summary>
    static member Default =
        { Chars = None
          CharsBy = None
          Ease = None
          Text = None
          TextBy = None
          From = ScrambleFrom.Auto
          Reversed = false
          Perturbation = 0.
          Cursor = ScrambleCursor.None
          Seed = 0.
          Override = ScrambleOverride.MatchChars
          RevealRate = 60.
          SettleDuration = None
          SettleRate = 30.
          Duration = None
          DurationBy = None
          RevealDelay = 0.
          RevealDelayBy = None
          Delay = 0.
          DelayBy = None
          OnChange = fun _ _ -> () }
    /// <summary>Invariant diagnostic of scrambleText options.</summary>
    override this.ToString() =
        "ScrambleTextOptions(From=" + this.From.ToString() + "; Override=" + this.Override.ToString() + ")"

/// <summary>Evaluated scrambleText tween descriptor matching Anime.js ScrambleTextTween.</summary>
[<NoEquality; NoComparison; Diagnostics.DebuggerDisplay("{ToString(),nq}")>]
type ScrambleTextTween =
    { /// <summary>Start progress (always 0).</summary>
      From: float
      /// <summary>End progress (always 1).</summary>
      To: float
      /// <summary>Computed total duration in the engine's current time unit.</summary>
      Duration: float
      /// <summary>Outer start delay in the engine's current time unit.</summary>
      Delay: float
      /// <summary>Easing name ("linear").</summary>
      Ease: string
      /// <summary>Settled destination text at progress 1.</summary>
      SettledText: string
      /// <summary>Maps normalized progress in [0, 1] to the current scrambled/revealed string.</summary>
      Modifier: float -> string }
    /// <summary>Invariant diagnostic of the evaluated scramble tween.</summary>
    override this.ToString() =
        "ScrambleTextTween(Duration=" + this.Duration.ToString("R", CultureInfo.InvariantCulture) + ")"

module internal ScrambleImpl =
    let originalTexts = ConditionalWeakTable<obj, string>()

    let namedCharSets =
        dict [
            "lowercase", "a-z"
            "uppercase", "A-Z"
            "numbers", "0-9"
            "symbols", "!%#_|*+="
            "braille", "\u2800-\u28FF"
            "blocks", "\u2580-\u259F"
            "shades", "\u2591-\u2593"
        ]

    let expandCharRanges (str: string) : string =
        if String.IsNullOrEmpty str then ""
        else
            let sb = StringBuilder()
            let mutable i = 0
            let l = str.Length
            while i < l do
                if i + 2 < l && str[i + 1] = '-' && int str[i] < int str[i + 2] then
                    let startCode = int str[i]
                    let endCode = int str[i + 2]
                    for c in startCode .. endCode do
                        sb.Append(char c) |> ignore
                    i <- i + 3
                else
                    sb.Append(str[i]) |> ignore
                    i <- i + 1
            sb.ToString()

    let resolveCharset (raw: string) : string =
        match namedCharSets.TryGetValue raw with
        | true, mapped -> expandCharRanges mapped
        | _ -> expandCharRanges raw

    let readTargetText (target: obj) : string =
        match target with
        | :? Element as el ->
            let s = (el :> Node).textContent
            if isNull s then "" else s
        | :? AnimatedObject as bag -> bag.GetText "textContent"
        | :? string as s -> s
        | _ -> ""

    let writeTargetHtml (target: obj) (value: string) =
        match target with
        | :? HTMLElement as htmlEl -> htmlEl.innerHTML <- value
        | :? Element as el -> (el :> Node).textContent <- value
        | :? AnimatedObject as bag -> bag.SetText("innerHTML", value)
        | _ -> ()

    let createFactory (options: ScrambleTextOptions) : (obj -> int -> int -> string option -> ScrambleTextTween) =
        let easeFn = defaultArg options.Ease id
        let cursorChars =
            match options.Cursor with
            | ScrambleCursor.None -> ""
            | ScrambleCursor.Default -> "_"
            | ScrambleCursor.CharCode code -> string (char code)
            | ScrambleCursor.Text s -> if isNull s then "" else s
        let cursorLen = cursorChars.Length
        let revealRate = if options.RevealRate > 0. then options.RevealRate else 60.
        let settleRate = if options.SettleRate > 0. then options.SettleRate else 30.

        fun (target: obj) (index: int) (totalTargets: int) (prevTweenValue: string option) ->
            let timeScale = EngineDefaults.timeScale()
            let interval = 1000. * timeScale / revealRate
            let settleDuration = defaultArg options.SettleDuration (300. * timeScale)
            let rawChars =
                match options.CharsBy with
                | Some fn -> fn target index totalTargets
                | None -> defaultArg options.Chars "a-zA-Z0-9!%#_"
            let rawChars = if String.IsNullOrEmpty rawChars then "a-zA-Z0-9!%#_" else rawChars
            let characters = resolveCharset rawChars
            let totalChars = max 0 (characters.Length - 1)
            let explicitDuration =
                match options.DurationBy with
                | Some fn -> Some (fn target index totalTargets)
                | None -> options.Duration
            let revealDelay =
                match options.RevealDelayBy with
                | Some fn -> fn target index totalTargets
                | None -> options.RevealDelay
            let delay =
                match options.DelayBy with
                | Some fn -> fn target index totalTargets
                | None -> options.Delay
            let rng =
                if options.Seed <> 0. then Utils.createSeededRandom (Some options.Seed) None None None
                else Utils.createSeededRandom None None None None
            let rngInt lo hi = int (rng (Some (float lo)) (Some (float hi)) (Some 0))

            let currentText = readTargetText target
            let cachedOriginal =
                if isNull target then currentText
                else
                    match originalTexts.TryGetValue target with
                    | true, existing -> existing
                    | _ ->
                        originalTexts.Add(target, currentText)
                        currentText

            let startingText = defaultArg prevTweenValue currentText
            let targetText =
                match options.TextBy with
                | Some fn -> fn target index totalTargets
                | None ->
                    match options.Text with
                    | Some t -> t
                    | None -> defaultArg prevTweenValue cachedOriginal
            let settledText =
                if targetText = " " || targetText = "&nbsp;" then "\u00A0"
                elif isNull targetText then ""
                else targetText

            let startLength = if startingText = "\u00A0" then 0 else startingText.Length
            let endLength = settledText.Length

            let overrideCharsOpt, overrideCharOpt, hasOverride =
                match options.Override with
                | ScrambleOverride.MatchChars -> Some characters, None, true
                | ScrambleOverride.Preserve -> None, None, true
                | ScrambleOverride.Blank -> None, None, false
                | ScrambleOverride.Space -> Some " ", Some "\u00A0", true
                | ScrambleOverride.Custom "" -> None, None, false
                | ScrambleOverride.Custom " " -> Some " ", Some "\u00A0", true
                | ScrambleOverride.Custom custom -> Some (resolveCharset custom), None, true

            let totalOverrideChars =
                match overrideCharsOpt with
                | Some oc when oc.Length > 0 -> oc.Length - 1
                | _ -> 0

            let animLength = if not hasOverride then endLength else max startLength endLength
            let animDuration =
                match explicitDuration with
                | Some d when d > 0. -> d
                | _ -> float (animLength - 1) * interval + settleDuration
            let computedDuration = Utils.round ((animDuration + revealDelay) / timeScale) 0 * timeScale
            let revealDelayRatio = if revealDelay > 0. && computedDuration > 0. then Utils.round (revealDelay / computedDuration) 12 else 0.

            let resolvedFrom =
                match options.From with
                | ScrambleFrom.Auto -> if endLength < startLength then ScrambleFrom.Right else ScrambleFrom.Left
                | other -> other

            let charOrder = Array.zeroCreate<int> animLength
            if animLength > 0 then
                match resolvedFrom with
                | ScrambleFrom.Random ->
                    for i in 0 .. animLength - 1 do charOrder[i] <- i
                    for i = animLength - 1 downto 1 do
                        let j = rngInt 0 i
                        let tmp = charOrder[i]
                        charOrder[i] <- charOrder[j]
                        charOrder[j] <- tmp
                | _ ->
                    let refPos =
                        match resolvedFrom with
                        | ScrambleFrom.Right -> float ((if not hasOverride || startLength = 0 then animLength else startLength) - 1)
                        | ScrambleFrom.Center -> float ((if not hasOverride || startLength = 0 then animLength else startLength) - 1) / 2.
                        | ScrambleFrom.Index idx -> idx
                        | _ -> 0.
                    let indices = Array.init animLength id
                    let sorted = indices |> Array.sortBy (fun a -> abs (float a - refPos), a)
                    for i in 0 .. animLength - 1 do charOrder[sorted[i]] <- i

                if options.Reversed then
                    let last = animLength - 1
                    for i in 0 .. animLength - 1 do charOrder[i] <- last - charOrder[i]

            let settleRatio = if animDuration > 0. then Utils.round (settleDuration / animDuration) 12 else 1.
            let settleSpacing = if animLength > 0 then Utils.round ((1. - settleRatio) / float animLength) 12 else 0.
            let cursorZone = float cursorLen * settleSpacing
            let stepRatio = if computedDuration > 0. then Utils.round (1000. * timeScale / (settleRate * computedDuration)) 12 else 1.

            let charStarts = Array.zeroCreate<float32> animLength
            let charEnds = Array.zeroCreate<float32> animLength
            let scale = if options.Perturbation > 0. then options.Perturbation * settleRatio else 0.
            for c in 0 .. animLength - 1 do
                let so = if scale > 0. then float (rngInt 0 2000 - 1000) / 1000. * scale else 0.
                let eo = if scale > 0. then float (rngInt 0 2000 - 1000) / 1000. * scale else 0.
                let cs = float32 (float charOrder[c] * settleSpacing + so)
                charStarts[c] <- cs
                let ce =
                    if stepRatio > 0. then Math.Ceiling((float cs + settleRatio + eo) / stepRatio) * stepRatio
                    else float cs + settleRatio + eo
                charEnds[c] <- float32 ce

            let isSequentialOrRandom =
                match resolvedFrom with
                | ScrambleFrom.Left | ScrambleFrom.Right | ScrambleFrom.Random -> true
                | _ -> false

            if endLength < animLength && endLength > 0 && not isSequentialOrRandom then
                let mutable maxExtraEnd = 0.
                for c in endLength .. animLength - 1 do
                    if float charEnds[c] > maxExtraEnd then maxExtraEnd <- float charEnds[c]
                let targetIndices = Array.init endLength id |> Array.sortBy (fun a -> charOrder[a], a)
                let targetSpacing = (1. - maxExtraEnd) / float endLength
                for i in 0 .. endLength - 1 do
                    let revealTime = maxExtraEnd + float i * targetSpacing
                    if revealTime > float charEnds[targetIndices[i]] then
                        charEnds[targetIndices[i]] <- float32 revealTime

            let charCache =
                Array.init animLength (fun _ ->
                    if characters.Length > 0 then string characters[rngInt 0 totalChars] else "")

            let overrideCache =
                match overrideCharsOpt with
                | None -> None
                | Some oc when StringComparer.Ordinal.Equals(oc, characters) && overrideCharOpt.IsNone ->
                    Some charCache
                | Some oc ->
                    Some (Array.init animLength (fun _ ->
                        match overrideCharOpt with
                        | Some ch -> ch
                        | None -> if oc.Length > 0 then string oc[rngInt 0 (oc.Length - 1)] else ""))

            let fillStartText =
                if prevTweenValue.IsSome then startingText
                elif not hasOverride then ""
                else
                    match overrideCache with
                    | Some oc ->
                        let sb = StringBuilder(startLength)
                        for c in 0 .. startLength - 1 do
                            if startingText[c] = ' ' then sb.Append ' ' |> ignore
                            else sb.Append oc[c] |> ignore
                        sb.ToString()
                    | None -> startingText

            let mutable lastValue = -1.
            let mutable lastStep = -1
            let mutable scrambled = ""
            let hasOverrideChars = overrideCharsOpt.IsSome
            let hasCursor = cursorLen > 0

            let modifier (v: float) =
                if v = lastValue then scrambled
                else
                    lastValue <- v
                    if delay > 0. && v <= 0. then
                        scrambled <- startingText
                        startingText
                    elif v <= 0. then
                        scrambled <- fillStartText
                        fillStartText
                    elif v >= 1. then
                        scrambled <- settledText
                        settledText
                    else
                        let currentStep = if stepRatio > 0. then int (Math.Truncate (v / stepRatio)) else 0
                        let refreshChars = currentStep <> lastStep
                        if refreshChars then lastStep <- currentStep
                        let linear = if revealDelayRatio > 0. then (v - revealDelayRatio) / (1. - revealDelayRatio) else v
                        let t = if linear > 0. then easeFn linear else 0.
                        let sb = StringBuilder(animLength)
                        for c in 0 .. animLength - 1 do
                            let charStart = float charStarts[c]
                            let charEnd = float charEnds[c]
                            if t >= charEnd then
                                if c < endLength then sb.Append settledText[c] |> ignore
                            elif t <= 0. || t < charStart then
                                if hasOverride && c < startLength then
                                    if hasOverrideChars then
                                        if startingText[c] = ' ' then sb.Append ' ' |> ignore
                                        else
                                            let ocArr = overrideCache.Value
                                            if refreshChars then
                                                ocArr[c] <-
                                                    match overrideCharOpt with
                                                    | Some ch -> ch
                                                    | None ->
                                                        let ocStr = overrideCharsOpt.Value
                                                        if ocStr.Length > 0 then string ocStr[rngInt 0 totalOverrideChars] else ""
                                            sb.Append ocArr[c] |> ignore
                                    else
                                        sb.Append startingText[c] |> ignore
                            else
                                let isSpace =
                                    (c < endLength && settledText[c] = ' ') ||
                                    (c < startLength && startingText[c] = ' ')
                                if isSpace then
                                    sb.Append ' ' |> ignore
                                elif hasCursor && t - charStart < cursorZone then
                                    let cursorIdx =
                                        if settleSpacing > 0. then
                                            cursorLen - 1 - int (Math.Truncate ((t - charStart) / settleSpacing))
                                        else cursorLen - 1
                                    let clampedIdx = Math.Clamp(cursorIdx, 0, cursorLen - 1)
                                    sb.Append cursorChars[clampedIdx] |> ignore
                                else
                                    if refreshChars && characters.Length > 0 then
                                        charCache[c] <- string characters[rngInt 0 totalChars]
                                    sb.Append charCache[c] |> ignore
                        scrambled <- sb.ToString()
                        if refreshChars then options.OnChange scrambled t
                        scrambled

            { From = 0.
              To = 1.
              Duration = max 1e-11 computedDuration
              Delay = delay
              Ease = "linear"
              SettledText = settledText
              Modifier = modifier }
