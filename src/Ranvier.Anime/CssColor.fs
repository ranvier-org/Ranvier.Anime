namespace Ranvier.Anime

open System
open System.Collections.Frozen
open System.Collections.Generic
open System.Drawing
open System.Globalization

module internal CssColor =
    let private invalid () = invalidArg "value" "Expected a supported absolute CSS colour."
    let inline private clamp (v: float) = Math.Clamp(v, 0., 1.)

    let private hexLut =
        let t = Array.create 128 -1y
        for c = int '0' to int '9' do t[c] <- sbyte (c - int '0')
        for c = int 'a' to int 'f' do t[c] <- sbyte (c - int 'a' + 10)
        for c = int 'A' to int 'F' do t[c] <- sbyte (c - int 'A' + 10)
        t

    let inline private hexNib (ch: char) : int =
        let u = uint32 ch
        if u < 128u then int hexLut[int u] else -1

    let private namedColors =
        let d = Dictionary<string, struct(float * float * float * float)>(200, StringComparer.OrdinalIgnoreCase)
        d["transparent"] <- struct(0., 0., 0., 0.)
        d["rebeccapurple"] <- struct(102., 51., 153., 1.)
        for code = 1 to 180 do
            let kc : KnownColor = LanguagePrimitives.EnumOfValue code
            let c = Color.FromKnownColor kc
            if c.IsKnownColor && not c.IsSystemColor && c.A = 255uy && not (String.IsNullOrEmpty c.Name) then
                let entry = struct(float c.R, float c.G, float c.B, 1.)
                d[c.Name] <- entry
                if c.Name.IndexOf("Gray", StringComparison.OrdinalIgnoreCase) >= 0 then
                    d[c.Name.Replace("Gray", "Grey").Replace("gray", "grey")] <- entry
        d.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase)

    // Matches `^[+-]?(?:[0-9]+|[0-9]*\.[0-9]+)(?:[eE][+-]?[0-9]+)?$` on ReadOnlySpan<char> without regex or allocation.
    let private isValidCssNumberSpan (span: ReadOnlySpan<char>) : bool =
        let len = span.Length
        if len = 0 then false
        else
            let mutable p = 0
            if span[0] = '+' || span[0] = '-' then p <- 1
            let d1Start = p
            while p < len && span[p] >= '0' && span[p] <= '9' do p <- p + 1
            let d1 = p - d1Start
            let mutable ok = false
            if p < len && span[p] = '.' then
                p <- p + 1
                let d2Start = p
                while p < len && span[p] >= '0' && span[p] <= '9' do p <- p + 1
                ok <- (p - d2Start) >= 1
            else
                ok <- d1 >= 1
            if ok && p < len && (span[p] = 'e' || span[p] = 'E') then
                p <- p + 1
                if p < len && (span[p] = '+' || span[p] = '-') then p <- p + 1
                let d3Start = p
                while p < len && span[p] >= '0' && span[p] <= '9' do p <- p + 1
                ok <- (p - d3Start) >= 1
            ok && p = len

    let private numberSpan (token: ReadOnlySpan<char>) : float =
        if isValidCssNumberSpan token then
            let mutable n = 0.
            if Double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, &n) && Double.IsFinite n then n
            else invalid ()
        else invalid ()

    let private scaledSpan (percentScale: float, token: ReadOnlySpan<char>) : float =
        if token.Equals("none".AsSpan(), StringComparison.OrdinalIgnoreCase) then 0.
        elif token.Length > 0 && token[token.Length - 1] = '%' then
            numberSpan (token.Slice(0, token.Length - 1)) * 0.01 * percentScale
        else numberSpan token

    let private hueSpan (token: ReadOnlySpan<char>) : float =
        let degrees =
            if token.Equals("none".AsSpan(), StringComparison.OrdinalIgnoreCase) then 0.
            elif token.EndsWith("turn".AsSpan(), StringComparison.OrdinalIgnoreCase) then
                (numberSpan (token.Slice(0, token.Length - 4)) % 1.) * 360.
            elif token.EndsWith("grad".AsSpan(), StringComparison.OrdinalIgnoreCase) then
                numberSpan (token.Slice(0, token.Length - 4)) * 0.9
            elif token.EndsWith("rad".AsSpan(), StringComparison.OrdinalIgnoreCase) then
                (numberSpan (token.Slice(0, token.Length - 3)) % (2. * Math.PI)) * (180. / Math.PI)
            elif token.EndsWith("deg".AsSpan(), StringComparison.OrdinalIgnoreCase) then
                numberSpan (token.Slice(0, token.Length - 3))
            else numberSpan token
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

    let private fromProfileSpan (profile: ReadOnlySpan<char>, rgb: float * float * float) =
        if profile.Equals("srgb".AsSpan(), StringComparison.OrdinalIgnoreCase) then rgb
        elif profile.Equals("srgb-linear".AsSpan(), StringComparison.OrdinalIgnoreCase) then map3 encode rgb
        else
            let xyz =
                if profile.Equals("xyz".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
                   profile.Equals("xyz-d65".AsSpan(), StringComparison.OrdinalIgnoreCase) then rgb
                elif profile.Equals("xyz-d50".AsSpan(), StringComparison.OrdinalIgnoreCase) then d50ToD65 rgb
                elif profile.Equals("display-p3".AsSpan(), StringComparison.OrdinalIgnoreCase) then
                    map3 decode rgb
                    |> transform (0.4865709486,0.2656676932,0.1982172852)
                                 (0.2289745641,0.6917385218,0.0792869141)
                                 (0.,0.0451133819,1.0439443689)
                elif profile.Equals("display-p3-linear".AsSpan(), StringComparison.OrdinalIgnoreCase) then
                    rgb
                    |> transform (0.4865709486,0.2656676932,0.1982172852)
                                 (0.2289745641,0.6917385218,0.0792869141)
                                 (0.,0.0451133819,1.0439443689)
                elif profile.Equals("a98-rgb".AsSpan(), StringComparison.OrdinalIgnoreCase) then
                    map3 (signedPower (563./256.)) rgb
                    |> transform (0.5766690429,0.1855582379,0.1882286462)
                                 (0.2973449753,0.6273635663,0.0752914585)
                                 (0.0270313614,0.0706888525,0.9913375368)
                elif profile.Equals("rec2020".AsSpan(), StringComparison.OrdinalIgnoreCase) then
                    map3 (signedPower 2.4) rgb
                    |> transform (0.6369580483,0.1446169036,0.1688809752)
                                 (0.2627002120,0.6779980715,0.0593017165)
                                 (0.,0.0280726930,1.0609850577)
                elif profile.Equals("prophoto-rgb".AsSpan(), StringComparison.OrdinalIgnoreCase) then
                    map3 (fun x -> if abs x <= 1./32. then x/16. else signedPower 1.8 x) rgb
                    |> transform (0.7977666449,0.1351812974,0.0313477341)
                                 (0.2880748288,0.7118352342,0.0000899369)
                                 (0.,0.,0.8251046025)
                    |> d50ToD65
                else invalid ()
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

    let inline private channels (red: float, green: float, blue: float) (opacity: float) =
        [| clamp red * 255.; clamp green * 255.; clamp blue * 255.; clamp opacity |]

    let private tryHexSpan (span: ReadOnlySpan<char>, forInterpolation: bool) : float array option =
        let digitCount = span.Length - 1
        if digitCount = 3 || digitCount = 4 then
            let n0 = hexNib span[1]
            let n1 = hexNib span[2]
            let n2 = hexNib span[3]
            if (n0 ||| n1 ||| n2) < 0 then None
            elif digitCount = 3 then
                Some [| float (n0 * 17); float (n1 * 17); float (n2 * 17); 1. |]
            else
                let n3 = hexNib span[4]
                if n3 < 0 then None
                else
                    let a = float (n3 * 17) / 255.
                    Some [| float (n0 * 17); float (n1 * 17); float (n2 * 17); if forInterpolation then Math.Round(a, 3, MidpointRounding.AwayFromZero) else a |]
        elif digitCount = 6 || digitCount = 8 then
            let rHi, rLo = hexNib span[1], hexNib span[2]
            let gHi, gLo = hexNib span[3], hexNib span[4]
            let bHi, bLo = hexNib span[5], hexNib span[6]
            if (rHi ||| rLo ||| gHi ||| gLo ||| bHi ||| bLo) < 0 then None
            elif digitCount = 6 then
                Some [| float (rHi * 16 + rLo); float (gHi * 16 + gLo); float (bHi * 16 + bLo); 1. |]
            else
                let aHi, aLo = hexNib span[7], hexNib span[8]
                if (aHi ||| aLo) < 0 then None
                else
                    let a = float (aHi * 16 + aLo) / 255.
                    Some [| float (rHi * 16 + rLo); float (gHi * 16 + gLo); float (bHi * 16 + bLo); if forInterpolation then Math.Round(a, 3, MidpointRounding.AwayFromZero) else a |]
        else None

    let inline private isCssWs (c: char) =
        c = ' ' || c = '\t' || c = '\n' || c = '\r' || c = '\u000C'

    let private trimCssWs (span: ReadOnlySpan<char>) : ReadOnlySpan<char> =
        let mutable s = 0
        let mutable e = span.Length
        while s < e && isCssWs span[s] do s <- s + 1
        while e > s && isCssWs span[e - 1] do e <- e - 1
        span.Slice(s, e - s)

    let private functionalSpan (forInterpolation: bool, name: string, body: ReadOnlySpan<char>) : float array =
        let isColorFn = name = "color"
        let neededTokens = if isColorFn then 4 else 3
        let commaIdx = body.IndexOf ','
        let mutable p0 = ReadOnlySpan<char>.Empty
        let mutable p1 = ReadOnlySpan<char>.Empty
        let mutable p2 = ReadOnlySpan<char>.Empty
        let mutable p3 = ReadOnlySpan<char>.Empty
        let mutable opacity = 1.
        let legacy = commaIdx >= 0
        if legacy then
            if name <> "rgb" && name <> "rgba" && name <> "hsl" && name <> "hsla" then invalid ()
            let mutable count = 0
            let mutable start = 0
            let len = body.Length
            while start <= len do
                let rem = body.Slice(start)
                let cPos = rem.IndexOf ','
                let rawTok = if cPos < 0 then rem else rem.Slice(0, cPos)
                let tok = trimCssWs rawTok
                if tok.Equals("none".AsSpan(), StringComparison.OrdinalIgnoreCase) then invalid ()
                match count with
                | 0 -> p0 <- tok
                | 1 -> p1 <- tok
                | 2 -> p2 <- tok
                | 3 -> p3 <- tok
                | _ -> invalid ()
                count <- count + 1
                start <- if cPos < 0 then len + 1 else start + cPos + 1
            if count <> 3 && count <> 4 then invalid ()
            if count = 4 then opacity <- scaledSpan(1., p3)
        else
            let slashIdx = body.IndexOf '/'
            let mainSpan =
                if slashIdx < 0 then body
                else
                    let afterSlash = body.Slice(slashIdx + 1)
                    if afterSlash.IndexOf '/' >= 0 then invalid ()
                    opacity <- scaledSpan(1., trimCssWs afterSlash)
                    body.Slice(0, slashIdx)
            let mutable count = 0
            let mutable idx = 0
            let mLen = mainSpan.Length
            while idx < mLen do
                while idx < mLen && isCssWs mainSpan[idx] do idx <- idx + 1
                if idx < mLen then
                    let tStart = idx
                    while idx < mLen && not (isCssWs mainSpan[idx]) do idx <- idx + 1
                    let tok = mainSpan.Slice(tStart, idx - tStart)
                    match count with
                    | 0 -> p0 <- tok
                    | 1 -> p1 <- tok
                    | 2 -> p2 <- tok
                    | 3 -> p3 <- tok
                    | _ -> invalid ()
                    count <- count + 1
            if count <> neededTokens then invalid ()
        let x = if isColorFn then p1 else p0
        let y = if isColorFn then p2 else p1
        let z = if isColorFn then p3 else p2
        let xPct = x.Length > 0 && x[x.Length - 1] = '%'
        let yPct = y.Length > 0 && y[y.Length - 1] = '%'
        let zPct = z.Length > 0 && z[z.Length - 1] = '%'
        let rgb =
            match name with
            | "rgb" | "rgba" ->
                if legacy && (xPct <> yPct || yPct <> zPct) then invalid ()
                scaledSpan(255., x) / 255., scaledSpan(255., y) / 255., scaledSpan(255., z) / 255.
            | "hsl" | "hsla" ->
                if legacy && not (yPct && zPct) then invalid ()
                let sy = clamp (scaledSpan(100., y) * 0.01)
                let lz = clamp (scaledSpan(100., z) * 0.01)
                hsl (hueSpan x) sy lz
            | "hwb" ->
                let h = hueSpan x
                let w = max 0. (scaledSpan(100., y) * 0.01)
                let b = max 0. (scaledSpan(100., z) * 0.01)
                if w + b >= 1. then let grey = w / (w + b) in grey, grey, grey
                else hsl h 1. 0.5 |> map3 (fun c -> c * (1. - w - b) + w)
            | "oklab" | "oklch" | "lab" | "lch" ->
                let ok = name[0] = 'o'
                let lightnessScale = if ok then 1. else 100.
                let l = Math.Clamp(scaledSpan(lightnessScale, x), 0., lightnessScale)
                let a, b =
                    if name[name.Length - 1] = 'h' then
                        let c = max 0. (scaledSpan((if ok then 0.4 else 150.), y))
                        let angle = hueSpan z * (Math.PI / 180.)
                        c * Math.Cos angle, c * Math.Sin angle
                    else
                        let scale = if ok then 0.4 else 125.
                        scaledSpan(scale, y), scaledSpan(scale, z)
                if a = 0. && b = 0. && (l = 0. || l = lightnessScale) then
                    let endpoint = if l = 0. then 0. else 1.
                    endpoint, endpoint, endpoint
                elif ok then oklab l a b
                else
                    let c = ColorSpace.fromLab { Lightness = l; A = a; B = b; Alpha = 1. }
                    c.Red, c.Green, c.Blue
            | "color" ->
                fromProfileSpan(p0, (scaledSpan(1., x), scaledSpan(1., y), scaledSpan(1., z)))
            | _ -> invalid ()
        let result = channels rgb opacity
        if forInterpolation && legacy && (name = "hsl" || name = "hsla") && clamp (scaledSpan(100., y) * 0.01) <> 0. then
            result[0] <- PropertyNumbers.round 1. result[0]
            result[1] <- PropertyNumbers.round 1. result[1]
            result[2] <- PropertyNumbers.round 1. result[2]
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

    let private tryParse forInterpolation (value: string) =
        if isNull value then None
        else
            let span = value.AsSpan().Trim()
            if span.IsEmpty then None
            elif span[0] = '#' then
                tryHexSpan(span, forInterpolation)
            else
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
                            Some(functionalSpan(forInterpolation, fnName, span.Slice(i + 1, next - i - 2)))
                elif span.Length <= 24 then
                    let lookup = namedColors.GetAlternateLookup<ReadOnlySpan<char>>()
                    let mutable rgba = Unchecked.defaultof<struct(float * float * float * float)>
                    if lookup.TryGetValue(span, &rgba) then
                        let struct(r, g, b, a) = rgba
                        Some [| r; g; b; a |]
                    else None
                else None

    let tryParseEndpoint value = tryParse true value

    let parse value =
        match tryParse false value with Some color -> color | None -> invalid ()
