module Ranvier.Anime.Tests.CssTests

open System
open System.Collections.Generic
open System.Globalization
open Expecto
open Ranvier.Dom
open Ranvier.Css
open Ranvier.Anime

type private RecordingStyle() =
    let values = Dictionary<string, string * string>()
    let writes = ResizeArray<string * string * string>()
    member _.Writes = writes
    interface CSSStyleDeclaration with
        member _.getPropertyValue name = match values.TryGetValue name with true, (value, _) -> value | _ -> ""
        member _.getPropertyPriority name = match values.TryGetValue name with true, (_, priority) -> priority | _ -> ""
        member _.setProperty(name, value, priority) =
            let priority = defaultArg priority ""
            values[name] <- value, priority
            writes.Add(name, value, priority)
        member _.removeProperty name =
            let old = match values.TryGetValue name with true, (value, _) -> value | _ -> ""
            values.Remove name |> ignore
            old
        member _.item index = values.Keys |> Seq.tryItem (int index) |> Option.defaultValue ""
        member _.length = uint32 values.Count
        member _.cssText with get() = failwith "Snapshot access is outside this fixture" and set _ = failwith "Whole style replacement"
        member _.parentRule = Unchecked.defaultof<CSSRule>
        member _.cssRules = Unchecked.defaultof<CSSRuleList>

let tests = testList "typed CSS animation" [
    testCase "existing typed endpoints animate the named property and preserve importance" <| fun _ ->
        let clock = new ManualFrameScheduler()
        let recorder = RecordingStyle()
        let style = recorder :> CSSStyleDeclaration
        style.setProperty("--gap", "7px", priority = "important")
        style.setProperty("color", "red")
        use tween = CssAnimation.customProperty clock style (RuntimeApi.createCustomProperty "--gap") (Css.px 0.) (Css.px 100.) 100.
        Expect.equal (style.getPropertyValue "--gap") "0px" "explicit start"
        clock.Advance 25.
        clock.RunFrame()
        Expect.equal (style.getPropertyValue "--gap") "25px" "numeric frame"
        Expect.equal (style.getPropertyPriority "--gap") "important" "preserved priority"
        Expect.equal (style.getPropertyValue "color") "red" "unrelated property"

    testCase "different units are rejected before writes" <| fun _ ->
        let clock = new ManualFrameScheduler()
        let recorder = RecordingStyle()
        Expect.throwsT<ArgumentException>
            (fun () -> CssAnimation.customProperty clock recorder (RuntimeApi.createCustomProperty "--gap") (Css.px 0.) (Css.rem 2.) 100. |> ignore)
            "unit conversion needs a DOM context"
        Expect.equal recorder.Writes.Count 0 "no partial update"

    testCase "CSS numeric output is independent of the current culture" <| fun _ ->
        let previous = CultureInfo.CurrentCulture
        try
            CultureInfo.CurrentCulture <- CultureInfo.GetCultureInfo "fr-FR"
            let clock = new ManualFrameScheduler()
            let recorder = RecordingStyle()
            let style = recorder :> CSSStyleDeclaration
            use tween = CssAnimation.customProperty clock style (RuntimeApi.createCustomProperty "--gap") (Css.px 0.) (Css.px 1.) 100.
            clock.Advance 25.
            clock.RunFrame()
            Expect.equal (style.getPropertyValue "--gap") "0.25px" "CSS decimal point"
        finally CultureInfo.CurrentCulture <- previous
]
