module Ranvier.Anime.Tests.TextTests

open System
open System.IO
open System.Text.Json
open Expecto
open Ranvier.Dom
open Ranvier.Anime

let private fixture () =
    let root = Environment.GetEnvironmentVariable "RANVIER_ANIME_REFERENCE_ROOT"
    let dir = if String.IsNullOrWhiteSpace root then Path.Combine(AppContext.BaseDirectory, "reference") else root
    JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "text-reference.json")))

let private buildOptions (paramsEl: JsonElement) (onChange: string -> float -> unit) : ScrambleTextOptions =
    let mutable prop = Unchecked.defaultof<JsonElement>
    let chars =
        if paramsEl.TryGetProperty("chars", &prop) then Some (prop.GetString()) else None
    let text =
        if paramsEl.TryGetProperty("text", &prop) then Some (prop.GetString()) else None
    let ease =
        if paramsEl.TryGetProperty("ease", &prop) then Some (Ease.parse (prop.GetString())) else None
    let fromMode =
        if paramsEl.TryGetProperty("from", &prop) then
            if prop.ValueKind = JsonValueKind.Number then ScrambleFrom.Index (prop.GetDouble())
            else
                match prop.GetString() with
                | "left" -> ScrambleFrom.Left
                | "right" -> ScrambleFrom.Right
                | "center" -> ScrambleFrom.Center
                | "random" -> ScrambleFrom.Random
                | _ -> ScrambleFrom.Auto
        else ScrambleFrom.Auto
    let reversed = paramsEl.TryGetProperty("reversed", &prop) && prop.GetBoolean()
    let perturbation = if paramsEl.TryGetProperty("perturbation", &prop) then prop.GetDouble() else 0.
    let cursor =
        if paramsEl.TryGetProperty("cursor", &prop) then
            match prop.ValueKind with
            | JsonValueKind.True -> ScrambleCursor.Default
            | JsonValueKind.Number -> ScrambleCursor.CharCode (prop.GetInt32())
            | JsonValueKind.String -> ScrambleCursor.Text (prop.GetString())
            | _ -> ScrambleCursor.None
        else ScrambleCursor.None
    let seed = if paramsEl.TryGetProperty("seed", &prop) then prop.GetDouble() else 0.
    let overrideMode =
        if paramsEl.TryGetProperty("override", &prop) then
            match prop.ValueKind with
            | JsonValueKind.True -> ScrambleOverride.MatchChars
            | JsonValueKind.False -> ScrambleOverride.Preserve
            | JsonValueKind.String ->
                let s = prop.GetString()
                if s = "" then ScrambleOverride.Blank
                elif s = " " then ScrambleOverride.Space
                else ScrambleOverride.Custom s
            | _ -> ScrambleOverride.MatchChars
        else ScrambleOverride.MatchChars
    let revealRate = if paramsEl.TryGetProperty("revealRate", &prop) then prop.GetDouble() else 60.
    let settleDuration = if paramsEl.TryGetProperty("settleDuration", &prop) then Some (prop.GetDouble()) else None
    let settleRate = if paramsEl.TryGetProperty("settleRate", &prop) then prop.GetDouble() else 30.
    let duration = if paramsEl.TryGetProperty("duration", &prop) then Some (prop.GetDouble()) else None
    let revealDelay = if paramsEl.TryGetProperty("revealDelay", &prop) then prop.GetDouble() else 0.
    let delay = if paramsEl.TryGetProperty("delay", &prop) then prop.GetDouble() else 0.
    { ScrambleTextOptions.Default with
        Chars = chars
        Text = text
        Ease = ease
        From = fromMode
        Reversed = reversed
        Perturbation = perturbation
        Cursor = cursor
        Seed = seed
        Override = overrideMode
        RevealRate = revealRate
        SettleDuration = settleDuration
        SettleRate = settleRate
        Duration = duration
        RevealDelay = revealDelay
        Delay = delay
        OnChange = onChange }

let tests =
    testList "text scrambling and WAAPI convertEase conformity" [
        testCase "all 24 upstream scrambleText schedules match character-for-character across all progress samples and onChange callbacks" <| fun _ ->
            use doc = fixture()
            let mutable totalSamples = 0
            for item in (doc.RootElement.GetProperty "scrambleFixtures").EnumerateArray() do
                let caseName = item.GetProperty("name").GetString()
                let initial = item.GetProperty("initial").GetString()
                let changes = ResizeArray<string * float>()
                let opts = buildOptions (item.GetProperty "params") (fun s t -> changes.Add(s, t))
                let bag = AnimatedObject [ "textContent", AdaptedValue.Text initial ]
                let tw = Text.scrambleTarget (Some opts) (box bag)
                Expect.equal tw.From (item.GetProperty("from").GetDouble()) (caseName + " from")
                Expect.equal tw.To (item.GetProperty("to").GetDouble()) (caseName + " to")
                Expect.floatClose Accuracy.veryHigh tw.Duration (item.GetProperty("duration").GetDouble()) (caseName + " duration")
                Expect.floatClose Accuracy.veryHigh tw.Delay (item.GetProperty("delay").GetDouble()) (caseName + " delay")
                Expect.equal tw.Ease (item.GetProperty("ease").GetString()) (caseName + " ease")
                for s in (item.GetProperty "samples").EnumerateArray() do
                    let p = s.GetProperty("p").GetDouble()
                    let expectedText = s.GetProperty("text").GetString()
                    let actualText = tw.Modifier p
                    Expect.equal actualText expectedText (sprintf "%s at progress %g" caseName p)
                    totalSamples <- totalSamples + 1
                let cached = tw.Modifier 0.25 = tw.Modifier 0.25
                Expect.equal cached (item.GetProperty("cachedRepeat").GetBoolean()) (caseName + " repeat cache")
                let expectedChanges = (item.GetProperty "changes").EnumerateArray() |> Seq.toArray
                Expect.equal changes.Count expectedChanges.Length (caseName + " onChange count")
                for i in 0 .. changes.Count - 1 do
                    let actStr, actT = changes[i]
                    Expect.equal actStr (expectedChanges[i].GetProperty("str").GetString()) (caseName + " onChange str")
                    Expect.floatClose Accuracy.veryHigh actT (expectedChanges[i].GetProperty("t").GetDouble()) (caseName + " onChange t")
            Expect.equal totalSamples (24 * 21) "verified 504 scrambleText progress samples"

        testCase "scrambleText keyframe and Timeline chaining inherit previous settled text" <| fun _ ->
            use doc = fixture()
            let chained = doc.RootElement.GetProperty "chainedScramble"
            let bag = AnimatedObject [ "textContent", AdaptedValue.Text "" ]
            let f1 = Text.scramble (Some { ScrambleTextOptions.Default with Seed = 31.; Text = Some "First Step"; Duration = Some 200. })
            let f2 = Text.scramble (Some { ScrambleTextOptions.Default with Seed = 32.; Text = Some "Second"; Duration = Some 200. })
            let tw1 = f1 (box bag) 0 1 None
            for s in (chained.GetProperty "firstSamples").EnumerateArray() do
                Expect.equal (tw1.Modifier (s.GetProperty("p").GetDouble())) (s.GetProperty("text").GetString()) "first step sample"
            let tw2 = f2 (box bag) 0 1 (Some tw1.SettledText)
            for s in (chained.GetProperty "secondSamples").EnumerateArray() do
                Expect.equal (tw2.Modifier (s.GetProperty("p").GetDouble())) (s.GetProperty("text").GetString()) "second chained step sample"

            // Also verify animateScrambleSteps and Timeline.AddScramble
            use host = new ManualFrameScheduler()
            let domBag = AnimatedObject [ "textContent", AdaptedValue.Text "" ]
            use seqTimer =
                Text.animateScrambleSteps
                    host
                    (box domBag)
                    [| { ScrambleTextOptions.Default with Text = Some "First"; Duration = Some 100. }
                       { ScrambleTextOptions.Default with Text = Some "Second"; Duration = Some 100. } |]
                    None (Some false) None
            seqTimer.Seek 100.
            Expect.equal (domBag.GetText "innerHTML") "First" "end of first scramble keyframe"
            seqTimer.Seek seqTimer.Duration
            Expect.equal (domBag.GetText "innerHTML") "Second" "end of second scramble keyframe"

            let tlBag = AnimatedObject [ "textContent", AdaptedValue.Text "" ]
            use tl = new Timeline(host)
            tl.AddScramble(host, box tlBag, options = { ScrambleTextOptions.Default with Text = Some "Hello"; Duration = Some 100. })
              .AddScramble(host, box tlBag, options = { ScrambleTextOptions.Default with Override = ScrambleOverride.Preserve; Duration = Some 100. }) |> ignore
            tl.Seek tl.Duration
            Expect.equal (tlBag.GetText "innerHTML") "Hello" "Timeline.AddScramble chains settled text"

        testCase "Waapi.convertEase matches upstream CSS linear() formatting across curves and springs" <| fun _ ->
            use doc = fixture()
            let sp = Spring(bounce = 0.35, duration = 450.)
            for item in (doc.RootElement.GetProperty "waapiEases").EnumerateArray() do
                let samples = item.GetProperty("samples").GetInt32()
                let fn =
                    match item.GetProperty("kind").GetString() with
                    | "linear" -> id
                    | "outQuad" -> Ease.named "outQuad"
                    | "inOutBack" -> Ease.named "inOutBack"
                    | _ -> sp.Ease
                let actual = Waapi.convertEase fn (Some samples)
                Expect.equal actual (item.GetProperty("css").GetString()) (item.GetProperty("name").GetString())
    ]
