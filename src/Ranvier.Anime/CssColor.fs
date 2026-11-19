namespace Ranvier.Anime

open System
open System.Drawing
open System.Globalization
open System.Text.RegularExpressions

module internal CssColor =
    let private invalid () = invalidArg "value" "Expected a supported absolute CSS colour."
    let private clamp (v:float) = Math.Clamp(v, 0., 1.)
    let private numeric = Regex(@"^[+-]?(?:[0-9]+|[0-9]*\.[0-9]+)(?:e[+-]?[0-9]+)?$", RegexOptions.NonBacktracking)

    let private number (token:string) =
        if numeric.IsMatch token then
            match Double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture) with
            | true, n when Double.IsFinite n -> n
            | _ -> invalid ()
        else invalid ()

    let private scaled percentScale (token:string) =
        if token = "none" then 0.
        elif token.EndsWith('%') then number token[..token.Length-2] / 100. * percentScale
        else number token

    let private hue (token:string) =
        let degrees =
            if token = "none" then 0.
            elif token.EndsWith("turn", StringComparison.Ordinal) then (number token[..token.Length-5] % 1.) * 360.
            elif token.EndsWith("grad", StringComparison.Ordinal) then number token[..token.Length-5] * 0.9
            elif token.EndsWith("rad", StringComparison.Ordinal) then (number token[..token.Length-4] % (2.*Math.PI)) * (180. / Math.PI)
            elif token.EndsWith("deg", StringComparison.Ordinal) then number token[..token.Length-4]
            else number token
        degrees % 360.

    let private hsl h s l =
        if s = 0. then l,l,l
        else
            let q = if l < 0.5 then l * (1.+s) else l+s-l*s
            let p = 2.*l-q
            let channel offset =
                let t = (h/360.+offset) % 1.
                let t = if t < 0. then t+1. else t
                if t < 1./6. then p+(q-p)*6.*t
                elif t < 0.5 then q
                elif t < 2./3. then p+(q-p)*(2./3.-t)*6.
                else p
            channel (1./3.), channel 0., channel (-1./3.)

    let private map3 f (x,y,z) = f x, f y, f z

    // CSS Color 4 §19, colour-space conversion matrices and transfer curves.
    // SIMD candidate: multiple endpoints in one space share these matrix rows.
    // Batch across independent colours after tokenization; alpha bypasses the transform.
    let private transform (a,b,c) (d,e,f) (g,h,i) (x,y,z) =
        a*x+b*y+c*z, d*x+e*y+f*z, g*x+h*y+i*z

    let private xyzToLinear =
        transform (3.2409699419,-1.5373831776,-0.4986107603)
                  (-0.9692436363,1.8759675015,0.0415550574)
                  (0.0556300797,-0.2039769589,1.0569715142)

    let private d50ToD65 =
        transform (0.9554734215,-0.0230984549,0.0632592432)
                  (-0.0283697093,1.0099953981,0.0210414412)
                  (0.0123140149,-0.0205076493,1.3303659262)

    let private signedPower exponent x = Math.CopySign(abs x ** exponent, x)
    let private decode x =
        if abs x <= 0.04045 then x / 12.92
        else Math.CopySign(((abs x + 0.055) / 1.055) ** 2.4, x)
    let private encode x =
        if x <= 0.0031308 then 12.92*x
        else 1.055 * x ** (1./2.4) - 0.055

    let private fromProfile profile rgb =
        match profile with
        | "srgb" -> rgb
        | "srgb-linear" -> map3 encode rgb
        | _ ->
            let xyz =
                match profile with
                | "xyz" | "xyz-d65" -> rgb
                | "xyz-d50" -> d50ToD65 rgb
                | "display-p3" | "display-p3-linear" ->
                    (if profile = "display-p3" then map3 decode rgb else rgb)
                    |> transform (0.4865709486,0.2656676932,0.1982172852)
                                 (0.2289745641,0.6917385218,0.0792869141)
                                 (0.,0.0451133819,1.0439443689)
                | "a98-rgb" ->
                    map3 (signedPower (563./256.)) rgb
                    |> transform (0.5766690429,0.1855582379,0.1882286462)
                                 (0.2973449753,0.6273635663,0.0752914585)
                                 (0.0270313614,0.0706888525,0.9913375368)
                | "rec2020" ->
                    map3 (signedPower 2.4) rgb
                    |> transform (0.6369580483,0.1446169036,0.1688809752)
                                 (0.2627002120,0.6779980715,0.0593017165)
                                 (0.,0.0280726930,1.0609850577)
                | "prophoto-rgb" ->
                    map3 (fun x -> if abs x <= 1./32. then x/16. else signedPower 1.8 x) rgb
                    |> transform (0.7977666449,0.1351812974,0.0313477341)
                                 (0.2880748288,0.7118352342,0.0000899369)
                                 (0.,0.,0.8251046025)
                    |> d50ToD65
                | _ -> invalid ()
            xyz |> xyzToLinear |> map3 encode

    let private oklab l a b =
        (l,a,b)
        |> transform (1.,0.3963377774,0.2158037573)
                     (1.,-0.1055613458,-0.0638541728)
                     (1.,-0.0894841775,-1.2914855480)
        |> map3 (fun x -> x*x*x)
        |> transform (4.0767416621,-3.3077115913,0.2309699292)
                     (-1.2684380046,2.6097574011,-0.3413193965)
                     (-0.0041960863,-0.7034186147,1.7076147010)
        |> map3 encode

    let private named name =
        if name = "transparent" then Some (0.,0.,0.,0.)
        elif name = "rebeccapurple" then Some (102.,51.,153.,1.)
        else
            let name = (name:string).Replace("grey", "gray")
            let color = Color.FromName name
            if color.IsKnownColor && not color.IsSystemColor && color.A = 255uy then
                Some (float color.R, float color.G, float color.B, 1.)
            else None

    let private channels (red,green,blue) opacity =
        let color = ColorSpace.applyGamut GamutPolicy.Clip {Red=red; Green=green; Blue=blue; Alpha=clamp opacity}
        [| color.Red * 255.; color.Green * 255.; color.Blue * 255.; color.Alpha |]

    let private hex (text:string) =
        let digitCount = text.Length - 1
        let short = digitCount = 3 || digitCount = 4
        if not short && digitCount <> 6 && digitCount <> 8 then invalid ()
        let channel i =
            let digits = if short then String(text[1+i],2) else text.Substring(1+i*2,2)
            match Byte.TryParse(digits, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture) with
            | true, n -> float n
            | _ -> invalid ()
        [| channel 0; channel 1; channel 2; if digitCount = 4 || digitCount = 8 then channel 3 / 255. else 1. |]

    let private splitChannels functionName (body:string) =
        let legacy = body.Contains(',')
        if legacy then
            if functionName <> "rgb" && functionName <> "rgba" && functionName <> "hsl" && functionName <> "hsla" then invalid ()
            let parts = body.Split(',',StringSplitOptions.TrimEntries)
            if (parts.Length <> 3 && parts.Length <> 4) || parts |> Array.contains "none" then invalid ()
            parts[..2], (if parts.Length = 4 then scaled 1. parts[3] else 1.), true
        else
            let parts = body.Split('/')
            if parts.Length > 2 then invalid ()
            parts[0].Split([|' ';'\t';'\n';'\r';'\u000C'|], StringSplitOptions.RemoveEmptyEntries),
            (if parts.Length = 2 then scaled 1. (parts[1].Trim()) else 1.), false

    let private functional forInterpolation name body =
        let parts, opacity, legacy = splitChannels name body
        let offset = if name = "color" then 1 else 0
        if parts.Length <> offset + 3 then invalid ()
        let x,y,z = parts[offset], parts[offset+1], parts[offset+2]
        let magnitude token = scaled 100. token / 100. |> clamp
        let rgb =
            match name with
            | "rgb" | "rgba" ->
                if legacy && (x.EndsWith('%') <> y.EndsWith('%') || y.EndsWith('%') <> z.EndsWith('%')) then invalid ()
                map3 (fun t -> scaled 255. t / 255.) (x,y,z)
            | "hsl" | "hsla" ->
                if legacy && not (y.EndsWith('%') && z.EndsWith('%')) then invalid ()
                hsl (hue x) (magnitude y) (magnitude z)
            | "hwb" ->
                let h = hue x
                let w = max 0. (scaled 100. y / 100.)
                let b = max 0. (scaled 100. z / 100.)
                if w+b >= 1. then let grey = w/(w+b) in grey,grey,grey
                else hsl h 1. 0.5 |> map3 (fun c -> c*(1.-w-b)+w)
            | "oklab" | "oklch" | "lab" | "lch" ->
                let ok = name.StartsWith("ok", StringComparison.Ordinal)
                let lightnessScale = if ok then 1. else 100.
                let l = Math.Clamp(scaled lightnessScale x, 0., lightnessScale)
                let a,b =
                    if name.EndsWith("lch", StringComparison.Ordinal) then
                        let c = max 0. (scaled (if ok then 0.4 else 150.) y)
                        let angle = hue z * (Math.PI / 180.)
                        let struct(sinA, cosA) = Math.SinCos angle
                        c * cosA, c * sinA
                    else
                        let scale = if ok then 0.4 else 125.
                        scaled scale y, scaled scale z
                if a = 0. && b = 0. && (l = 0. || l = lightnessScale) then
                    let endpoint = if l = 0. then 0. else 1.
                    endpoint,endpoint,endpoint
                elif ok then oklab l a b
                else
                    let c = ColorSpace.fromLab {Lightness=l; A=a; B=b; Alpha=1.}
                    c.Red,c.Green,c.Blue
            | "color" -> fromProfile parts[0] (map3 (scaled 1.) (x,y,z))
            | _ -> invalid ()
        let result = channels rgb opacity
        // The v4 interpolation oracle quantizes chromatic comma-form HSL at capture.
        if forInterpolation && legacy && (name = "hsl" || name = "hsla") && magnitude y <> 0. then
            for i in 0..2 do result[i] <- PropertyNumbers.round 1. result[i]
        result

    let private tryMatchFunctionName (prefix: ReadOnlySpan<char>) : string voption =
        match prefix.Length with
        | 3 ->
            if prefix.Equals("rgb", StringComparison.OrdinalIgnoreCase) then ValueSome "rgb"
            elif prefix.Equals("hsl", StringComparison.OrdinalIgnoreCase) then ValueSome "hsl"
            elif prefix.Equals("hwb", StringComparison.OrdinalIgnoreCase) then ValueSome "hwb"
            elif prefix.Equals("lab", StringComparison.OrdinalIgnoreCase) then ValueSome "lab"
            elif prefix.Equals("lch", StringComparison.OrdinalIgnoreCase) then ValueSome "lch"
            else ValueNone
        | 4 ->
            if prefix.Equals("rgba", StringComparison.OrdinalIgnoreCase) then ValueSome "rgba"
            elif prefix.Equals("hsla", StringComparison.OrdinalIgnoreCase) then ValueSome "hsla"
            else ValueNone
        | 5 ->
            if prefix.Equals("oklab", StringComparison.OrdinalIgnoreCase) then ValueSome "oklab"
            elif prefix.Equals("oklch", StringComparison.OrdinalIgnoreCase) then ValueSome "oklch"
            elif prefix.Equals("color", StringComparison.OrdinalIgnoreCase) then ValueSome "color"
            else ValueNone
        | _ -> ValueNone

    let private tryParse forInterpolation (value:string) =
        if isNull value then None
        else
            let span = value.AsSpan().Trim()
            if span.IsEmpty then None
            elif span[0] = '#' then
                if span.Length = 4 || span.Length = 5 || span.Length = 7 || span.Length = 9 then
                    let mutable allHex = true
                    for i = 1 to span.Length - 1 do
                        if not (Char.IsAsciiHexDigit span[i]) then allHex <- false
                    if not allHex then None
                    else
                        let result = hex (span.ToString())
                        if forInterpolation then result[3] <- Math.Round(result[3],3,MidpointRounding.AwayFromZero)
                        Some result
                else None
            else
                // Fast zero-alloc scan: CSS color functions have a 3..5 letter prefix before '(';
                // CSS named colors consist solely of ASCII letters (<= 24 chars).
                let mutable i = 0
                let limit = min span.Length 6
                while i < limit && Char.IsAsciiLetter span[i] do i <- i + 1
                if i < span.Length && span[i] = '(' then
                    match tryMatchFunctionName (span.Slice(0, i)) with
                    | ValueNone -> None
                    | ValueSome fnName ->
                        let mutable depth = 1
                        let mutable next = i + 1
                        while depth > 0 && next < span.Length do
                            match span[next] with
                            | '(' -> depth <- depth + 1
                            | ')' -> depth <- depth - 1
                            | _ -> ()
                            next <- next + 1
                        if depth <> 0 then invalid ()
                        elif next < span.Length then None
                        else
                            let body = span.Slice(i + 1, next - i - 2).ToString().ToLowerInvariant()
                            Some(functional forInterpolation fnName body)
                elif span.Length <= 24 then
                    let mutable allLetters = i > 0
                    let mutable k = i
                    while allLetters && k < span.Length do
                        if not (Char.IsAsciiLetter span[k]) then allLetters <- false
                        k <- k + 1
                    if not allLetters then None
                    else
                        let lower = span.ToString().ToLowerInvariant()
                        named lower |> Option.map (fun (r,g,b,a) -> [|r;g;b;a|])
                else None

    let tryParseEndpoint value = tryParse true value

    let parse value =
        match tryParse false value with Some color -> color | None -> invalid ()
