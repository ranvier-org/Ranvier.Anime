namespace Ranvier.Anime

open System

/// <summary>Normalized sRGB channels and straight alpha. Conversion may preserve out-of-gamut channels.</summary>
type Rgba =
    {
        Red: float
        Green: float
        Blue: float
        Alpha: float
    }

/// <summary>CIE Lab using the D50 reference white.</summary>
type LabColor =
    {
        Lightness: float
        A: float
        B: float
        Alpha: float
    }

/// <summary>Polar D50 Lab. Hue is degrees; achromatic colors use zero hue and zero chroma.</summary>
type HclColor =
    {
        Hue: float
        Chroma: float
        Lightness: float
        Alpha: float
    }

[<RequireQualifiedAccess>]
type ColorInterpolation =
    | Srgb
    | Lab
    | Hcl

[<RequireQualifiedAccess>]
type GamutPolicy =
    | Preserve
    | Clip
    | Reject

/// <summary>Pure color conversion and interpolation without CSS parsing or a scheduler. D50 matrices follow d3-color; see THIRD-PARTY-NOTICES.md.</summary>
[<RequireQualifiedAccess>]
module ColorSpace =
    [<Struct>]
    type private LabVal =
        val Lightness: float
        val A: float
        val B: float
        new(l, a, b) = { Lightness = l; A = a; B = b }

    [<Struct>]
    type private HclVal =
        val Hue: float
        val Chroma: float
        val Lightness: float
        new(h, c, l) = { Hue = h; Chroma = c; Lightness = l }

    let inline private finite3 a b c =
        if not (Double.IsFinite a && Double.IsFinite b && Double.IsFinite c) then
            invalidArg "color" "Color components must be finite."

    let inline private checkAlpha value =
        if not (Double.IsFinite value) || value < 0. || value > 1. then
            invalidArg "alpha" "Alpha must be in [0,1]."

    let rgba red green blue opacity =
        finite3 red green blue
        checkAlpha opacity

        if red < 0. || red > 1. || green < 0. || green > 1. || blue < 0. || blue > 1. then
            invalidArg "channels" "Expected normalized sRGB in [0,1]."

        {
            Red = red
            Green = green
            Blue = blue
            Alpha = opacity
        }

    let inline private clipUnit x = Math.Clamp(x, 0., 1.)

    let applyGamut policy (color: Rgba) =
        finite3 color.Red color.Green color.Blue
        checkAlpha color.Alpha

        match policy with
        | GamutPolicy.Preserve -> color
        | GamutPolicy.Clip ->
            { color with
                Red = clipUnit color.Red
                Green = clipUnit color.Green
                Blue = clipUnit color.Blue
            }
        | GamutPolicy.Reject -> rgba color.Red color.Green color.Blue color.Alpha

    let inline private srgbLinear x =
        if x <= 0.04045 then x / 12.92
        else Math.Pow((x + 0.055) / 1.055, 2.4)

    let inline private labForward x =
        if x > 216. / 24389. then Math.Cbrt x
        else x / (108. / 841.) + 4. / 29.

    let inline private labInverse t =
        if t > 6. / 29. then t * t * t
        else (108. / 841.) * (t - 4. / 29.)

    let inline private srgbGamma x =
        if x <= 0.0031308 then 12.92 * x
        else 1.055 * Math.Pow(x, 1. / 2.4) - 0.055

    let inline private snapLabUnit v =
        if v < 0. && v >= -1e-5 then 0.
        elif v > 1. && v <= 1. + 1e-5 then 1.
        else v

    let private toLabCore (color: Rgba) : LabVal =
        let r = srgbLinear color.Red
        let g = srgbLinear color.Green
        let b = srgbLinear color.Blue
        let y = labForward (0.2225045 * r + 0.7168786 * g + 0.0606169 * b)
        if r = g && g = b then
            LabVal(116. * y - 16., 0., 0.)
        else
            let x = labForward ((0.4360747 * r + 0.3850649 * g + 0.1430804 * b) / 0.96422)
            let z = labForward ((0.0139322 * r + 0.0971045 * g + 0.7141733 * b) / 0.82521)
            LabVal(116. * y - 16., 500. * (x - y), 200. * (y - z))

    let private fromLabCore (l: float) (a: float) (b: float) : struct(float * float * float) =
        let fy = (l + 16.) / 116.
        let y = labInverse fy
        if a = 0. && b = 0. then
            let grey = snapLabUnit (srgbGamma y)
            struct(grey, grey, grey)
        else
            let x = 0.96422 * labInverse (fy + a / 500.)
            let z = 0.82521 * labInverse (fy - b / 200.)
            let r = snapLabUnit (srgbGamma (3.1338561 * x - 1.6168667 * y - 0.4906146 * z))
            let g = snapLabUnit (srgbGamma (-0.9787684 * x + 1.9161415 * y + 0.0334540 * z))
            let bl = snapLabUnit (srgbGamma (0.0719453 * x - 0.2289914 * y + 1.4052427 * z))
            struct(r, g, bl)

    let private toHclCore (color: Rgba) : HclVal =
        let lab = toLabCore color
        let chroma = sqrt (lab.A * lab.A + lab.B * lab.B)
        if chroma < 1e-7 then
            HclVal(0., 0., lab.Lightness)
        else
            let hue = (atan2 lab.B lab.A * (180. / Math.PI) + 360.) % 360.
            HclVal(hue, chroma, lab.Lightness)

    let toLab (color: Rgba) =
        finite3 color.Red color.Green color.Blue
        checkAlpha color.Alpha
        let lab = toLabCore color
        {
            Lightness = lab.Lightness
            A = lab.A
            B = lab.B
            Alpha = color.Alpha
        }

    let fromLab (color: LabColor) =
        finite3 color.Lightness color.A color.B
        checkAlpha color.Alpha
        let struct(r, g, b) = fromLabCore color.Lightness color.A color.B
        finite3 r g b
        {
            Red = r
            Green = g
            Blue = b
            Alpha = color.Alpha
        }

    let toHcl color =
        finite3 color.Red color.Green color.Blue
        checkAlpha color.Alpha
        let hcl = toHclCore color
        {
            Hue = hcl.Hue
            Chroma = hcl.Chroma
            Lightness = hcl.Lightness
            Alpha = color.Alpha
        }

    let fromHcl (color: HclColor) =
        finite3 color.Hue color.Chroma color.Lightness
        checkAlpha color.Alpha

        if color.Chroma < 0. then
            invalidArg "color" "Negative chroma."

        let angle = (color.Hue % 360.) * (Math.PI / 180.)
        let struct(sinA, cosA) = Math.SinCos angle
        let struct(r, g, b) = fromLabCore color.Lightness (cosA * color.Chroma) (sinA * color.Chroma)
        finite3 r g b
        {
            Red = r
            Green = g
            Blue = b
            Alpha = color.Alpha
        }

    /// <summary>Interpolates straight alpha and color at progress in [0,1]. HCL takes the shortest hue path and borrows hue from a chromatic endpoint.</summary>
    let interpolate space gamut (first: Rgba) (last: Rgba) progress =
        if not (Double.IsFinite progress) || progress < 0. || progress > 1. then
            invalidArg "progress" "Expected progress in [0,1]."

        finite3 first.Red first.Green first.Blue
        checkAlpha first.Alpha
        finite3 last.Red last.Green last.Blue
        checkAlpha last.Alpha

        let inline lerp a b =
            let result = PropertyNumbers.interpolate a b progress
            if Double.IsFinite result then result else a * (1. - progress) + b * progress

        let opacity = lerp first.Alpha last.Alpha

        let struct(r, g, b) =
            match space with
            | ColorInterpolation.Srgb ->
                struct(lerp first.Red last.Red, lerp first.Green last.Green, lerp first.Blue last.Blue)
            | ColorInterpolation.Lab ->
                let a = toLabCore first
                let bl = toLabCore last
                fromLabCore (lerp a.Lightness bl.Lightness) (lerp a.A bl.A) (lerp a.B bl.B)
            | ColorInterpolation.Hcl ->
                let a = toHclCore first
                let bl = toHclCore last
                let h0 = if a.Chroma = 0. then bl.Hue else a.Hue
                let h1 = if bl.Chroma = 0. then h0 else bl.Hue
                let delta = (h1 - h0 + 540.) % 360. - 180.
                let hue = h0 + progress * delta
                let chroma = lerp a.Chroma bl.Chroma
                let lightness = lerp a.Lightness bl.Lightness
                let angle = (hue % 360.) * (Math.PI / 180.)
                let struct(sinA, cosA) = Math.SinCos angle
                fromLabCore lightness (cosA * chroma) (sinA * chroma)

        finite3 r g b

        if progress = 0. then applyGamut gamut first
        elif progress = 1. then applyGamut gamut last
        else
            match gamut with
            | GamutPolicy.Preserve -> { Red = r; Green = g; Blue = b; Alpha = opacity }
            | GamutPolicy.Clip -> { Red = clipUnit r; Green = clipUnit g; Blue = clipUnit b; Alpha = opacity }
            | GamutPolicy.Reject -> rgba r g b opacity
