module Ranvier.Anime.CssAotProbe.Program

[<EntryPoint>]
let main _ =
    match Ranvier.Css.RuntimeApi.tryNumericValue (Ranvier.Css.Css.px 12.5) with
    | Some(value, unitName) when value = 12.5 && unitName = "px" ->
        System.Console.WriteLine "CSS_AOT_PROBE_PASS"
        0
    | _ ->
        System.Console.Error.WriteLine "Typed CSS literal changed"
        1
