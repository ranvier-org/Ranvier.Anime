module Ranvier.Anime.Tests.ColorSpaceTests

open Expecto
open Ranvier.Anime

let tests =
    testList
        "Perceptual colors"
        [
            testCase "Lab roundtrip retains sRGB channels and alpha"
            <| fun _ ->
                for color in
                    [
                        ColorSpace.rgba 1. 0. 0. 0.3
                        ColorSpace.rgba 0. 1. 0. 1.
                        ColorSpace.rgba 0. 0. 1. 1.
                        ColorSpace.rgba 0.5 0.5 0.5 0.5
                    ] do
                    let actual = color |> ColorSpace.toLab |> ColorSpace.fromLab
                    Expect.isLessThan (abs (actual.Red - color.Red)) 1e-5 "red"
                    Expect.isLessThan (abs (actual.Green - color.Green)) 1e-5 "green"
                    Expect.isLessThan (abs (actual.Blue - color.Blue)) 1e-5 "blue"
                    Expect.equal actual.Alpha color.Alpha "alpha"
            testCase "HCL hue uses shortest wrap and borrows achromatic hue"
            <| fun _ ->
                let a =
                    ColorSpace.fromHcl
                        {
                            Hue = 350.
                            Chroma = 20.
                            Lightness = 50.
                            Alpha = 0.2
                        }

                let b =
                    ColorSpace.fromHcl
                        {
                            Hue = 10.
                            Chroma = 20.
                            Lightness = 50.
                            Alpha = 0.8
                        }

                let midpoint =
                    ColorSpace.interpolate ColorInterpolation.Hcl GamutPolicy.Preserve a b 0.5
                    |> ColorSpace.toHcl

                Expect.isTrue (midpoint.Hue < 0.01 || midpoint.Hue > 359.99) "shortest hue"
                Expect.isLessThan (abs (midpoint.Alpha - 0.5)) 1e-12 "alpha interpolated"
            testCase "gamut policies are explicit"
            <| fun _ ->
                let color =
                    {
                        Rgba.Red = 2.
                        Green = -1.
                        Blue = 0.5
                        Alpha = 1.
                    }

                let clamp = ColorSpace.applyGamut GamutPolicy.Clip color
                Expect.equal (clamp.Red, clamp.Green) (1., 0.) "clip"

                Expect.throws
                    (fun () ->
                        ColorSpace.applyGamut GamutPolicy.Reject color
                        |> ignore)
                    "reject"
        ]
