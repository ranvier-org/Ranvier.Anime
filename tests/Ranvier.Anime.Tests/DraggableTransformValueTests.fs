namespace Ranvier.Anime.Tests

open Expecto
open Ranvier.Anime
module DraggableTransformValueTests =
    let private scalar inlineText name =
        let struct(cache,full)=TransformCache.parse inlineText
        DraggableTransformValue.scalar cache full name
    let scalarTransformTests=testList "draggable scalar transform mapping" [
        testCase "all15 source scalar defaults retain their property units" <| fun _ ->
            for name,expected in ["perspective",struct(0.,"px");"translateX",struct(0.,"px");"translateY",struct(0.,"px");"translateZ",struct(0.,"px");"rotate",struct(0.,"deg");"rotateX",struct(0.,"deg");"rotateY",struct(0.,"deg");"rotateZ",struct(0.,"deg");"scale",struct(1.,"");"scaleX",struct(1.,"");"scaleY",struct(1.,"");"scaleZ",struct(1.,"");"skew",struct(0.,"deg");"skewX",struct(0.,"deg");"skewY",struct(0.,"deg")] do
                Expect.equal (scalar "" name) expected name
        testCase "explicit scalar units and unitless scale survive mapping" <| fun _ ->
            let text="translate(3em,4%) rotate(0.5turn) scale(2)"
            Expect.equal (scalar text "translateX") (struct(3.,"em")) "length"
            Expect.equal (scalar text "translateY") (struct(4.,"%")) "percentage"
            Expect.equal (scalar text "rotate") (struct(0.5,"turn")) "angle"
            Expect.equal (scalar text "scale") (struct(2.,"")) "unitless"
        testCase "complex inline inputs are rejected explicitly" <| fun _ ->
            Expect.throwsT<System.NotSupportedException> (fun()->scalar "translateX(calc(1px + 2%))" "translateX" |> ignore) "complex scalar"
            Expect.throwsT<System.ArgumentException> (fun()->scalar "" "rotate3d" |> ignore) "non-animatable"
    ]

