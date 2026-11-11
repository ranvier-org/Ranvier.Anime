module Ranvier.Anime.NativeSmoke.FullFamiliesNativeProbe

open System
open Ranvier.Anime
open Ranvier.Dom
open Ranvier.View.WebCore

let private require name condition =
    if not condition then failwith ("FULL_FAMILIES_NATIVE_FAIL: " + name)
    Console.WriteLine("PASS full-families native " + name)

/// <summary>Verifies ElementTransforms, Composed Blend/Replace, Utils DOM helpers and Engine precision on native WebCore.</summary>
let run (host: WebCoreHost) (scheduler: FrameScheduler) =
    let computed = ComputedStyles.forHost host
    let doc = host.Document
    let el = doc.createElement "div" :?> HTMLElement
    el.setAttribute("id", "full-family-probe")
    let st = (el :> ElementCSSInlineStyle).style
    st.setProperty("width", "32px")
    st.setProperty("height", "20px")
    st.setProperty("font-size", "16px")
    doc.body.appendChild(el :> Node) |> ignore

    try
        // 1. Utils.select, Utils.get (unit conversion & unit stripping) and Utils.set
        let selected = Utils.select doc "#full-family-probe"
        require "Utils.select finds native DOM element" (selected.Length = 1 && (selected[0] :> Node).isSameNode(el :> Node))
        let inEm = Utils.get (box el) "width" (Choice3Of3 "em") (Some computed)
        let stripped = Utils.get (box el) "width" (Choice2Of3 false) (Some computed)
        require "Utils.get converts CSS units and strips units"
            (inEm = AdaptedValue.Text "2em" && stripped = AdaptedValue.Scalar 32.)
        Utils.set scheduler [| box el |] [| PropertySpec.Scalar("width", 48.); PropertySpec.Scalar("data-probe", 7.) |] |> ignore
        require "Utils.set writes CSS width and data-* attribute"
            (st.getPropertyValue "width" = "48px" && el.getAttribute "data-probe" = "7")

        // 2. Multi-animation individual CSS transform aggregation & partial Revert
        st.removeProperty "width" |> ignore
        st.removeProperty "height" |> ignore
        st.removeProperty "font-size" |> ignore
        el.removeAttribute "style"
        use animX =
            Composed.animate scheduler (box el) [| PropertySpec.Scalar("x", 100.) |]
                (Some { ComposedAnimationOptions.Default with Duration = 50.; Ease = id; Autoplay = false })
        use animY =
            Composed.animate scheduler (box el) [| PropertySpec.Scalar("y", 200.) |]
                (Some { ComposedAnimationOptions.Default with Duration = 50.; Ease = id; Autoplay = false })
        animX.Seek 50. |> ignore
        animY.Seek 50. |> ignore
        let transformText = st.getPropertyValue "transform"
        require "multiple independent animations aggregate x and y into translate(100px, 200px)"
            (transformText = "translate(100px, 200px)" || transformText = "translate(100px,200px)")
        animY.Revert() |> ignore
        require "partial transform Revert cleans only y and preserves x"
            (st.getPropertyValue "transform" = "translateX(100px)")
        animX.Revert() |> ignore
        require "reverting final transform removes empty style attribute"
            (isNull (el.getAttribute "style"))

        // 3. Engine.Precision controls intermediate transform rounding while preserving exact endpoint
        let eng = Engine.forScheduler scheduler
        try
            eng.Precision <- Some 1u
            st.setProperty("transform", "translateX(0.12345px) scale(0.12345)")
            use precAnim =
                Composed.animate scheduler (box el)
                    [| PropertySpec.Scalar("x", 2.12345); PropertySpec.Scalar("scale", 2.12345) |]
                    (Some { ComposedAnimationOptions.Default with Duration = 500.; Ease = id; Autoplay = false })
            precAnim.Seek 250. |> ignore
            require "Engine.Precision 1 rounds midpoint transform values"
                (st.getPropertyValue "transform" = "translateX(1.1px) scale(1.1)")
            precAnim.Seek 500. |> ignore
            require "Engine.Precision preserves exact unrounded endpoint"
                (st.getPropertyValue "transform" = "translateX(2.12345px) scale(2.12345)")
            precAnim.Revert() |> ignore
        finally
            eng.Precision <- Some 4u
            st.removeProperty "transform" |> ignore

        // 4. Additive Blend composition driving real native DOM geometry
        st.setProperty("width", "10px")
        let baseLeft = el.getBoundingClientRect().left
        use blend1 =
            Composed.animate scheduler (box el) [| PropertySpec.Scalar("x", 100.) |]
                (Some { ComposedAnimationOptions.Default with
                          Duration = 200.; Ease = id; Composition = KeyframeComposition.Blend; Autoplay = false })
        use blend2 =
            Composed.animate scheduler (box el) [| PropertySpec.Scalar("x", 200.) |]
                (Some { ComposedAnimationOptions.Default with
                          Duration = 100.; Ease = id; Composition = KeyframeComposition.Blend; Autoplay = false })
        blend1.Seek 50. |> ignore
        blend2.Seek 50. |> ignore
        let midLeft = el.getBoundingClientRect().left - baseLeft
        blend1.Seek 100. |> ignore
        blend2.Seek 100. |> ignore
        let at100Left = el.getBoundingClientRect().left - baseLeft
        require "additive Blend composition updates real native DOM layout geometry"
            (abs (midLeft - 75.) < 0.05 && abs (at100Left - 150.) < 0.05)
        Utils.cleanInlineStyles blend1 |> ignore
        Utils.cleanInlineStyles blend2 |> ignore

        // 5. Engine.AttachVisibility listener lifecycle on native Document
        use visSub = eng.AttachVisibility doc
        require "Engine.AttachVisibility attaches and disposes cleanly on native Document" (not (isNull (box visSub)))
    finally
        doc.body.removeChild(el :> Node) |> ignore
