namespace Ranvier.Anime

/// <summary>Anime.js endpoint decomposition, homogenization and scalar/color/compound composition.</summary>
module internal ValueInterpolation =
    open System
    open System.Globalization
    open System.Text.RegularExpressions
    open Ranvier.Anime
    /// <summary>The source decomposition category.</summary>
    type ValueKind = Number = 0 | Unit = 1 | Color = 2 | Complex = 3
    /// <summary>A numeric or serialized endpoint before source decomposition.</summary>
    [<System.Diagnostics.DebuggerDisplay("{ToString(),nq}")>]
    type Literal =
        | Number of float
        | Text of string
        /// <summary>An invariant case/value diagnostic without structural formatting.</summary>
        override this.ToString() =
            match this with
            | Number value -> "Number "+value.ToString("R",CultureInfo.InvariantCulture)
            | Text value -> "Text "+(if isNull value then "<null>" else value)
    /// <summary>Source value kind, scalar, unit/operator and compound components/template.</summary>
    [<System.Diagnostics.DebuggerDisplay("{ToString(),nq}")>]
    type Data =
        { Kind:ValueKind; Number:float; Unit:string; Operator:char option; Components:float array; Strings:string array }
        /// <summary>An invariant value-kind/scalar diagnostic.</summary>
        override this.ToString() =
            "ValueData("+this.Kind.ToString()+", "+this.Number.ToString("R",CultureInfo.InvariantCulture)+")"
    /// <summary>A composed scalar or string with the sampled component values.</summary>
    [<System.Diagnostics.DebuggerDisplay("{ToString(),nq}")>]
    type Output =
        { Scalar:float option; Text:string; Components:float array }
        /// <summary>The invariant scalar or composed text.</summary>
        override this.ToString() =
            match this.Scalar with Some value -> value.ToString("R",CultureInfo.InvariantCulture) | None -> this.Text
    let private finite name value = if not(Double.IsFinite value) then invalidArg name "Expected finite input."
    /// <summary>Formats source-style shortest numbers and exponent thresholds, including IEEE values.</summary>
    let number (value:float) =
        if value=0. then "0"
        elif Double.IsNaN value then "NaN"
        elif Double.IsPositiveInfinity value then "Infinity"
        elif Double.IsNegativeInfinity value then "-Infinity"
        else
            let raw=value.ToString("R",CultureInfo.InvariantCulture)
            let split=raw.Split('E','e')
            if split.Length=1 then raw
            else
                let exponent=Int32.Parse(split[1],CultureInfo.InvariantCulture)
                if abs value>=1e-6 && abs value<1e21 then
                    let negative=split[0].StartsWith "-"
                    let mantissa=split[0].TrimStart('-')
                    let dot=mantissa.IndexOf('.')
                    let digits=mantissa.Replace(".","")
                    let position=(if dot<0 then digits.Length else dot)+exponent
                    let result=
                        if position<=0 then "0."+String('0',-position)+digits
                        elif position>=digits.Length then digits+String('0',position-digits.Length)
                        else digits.Insert(position,".")
                    (if negative then "-" else "")+result
                else split[0]+"e"+(if exponent>=0 then "+" else "-")+string(abs exponent)
    let private regex pattern = Regex(pattern,RegexOptions.CultureInvariant ||| RegexOptions.IgnoreCase)
    let private unitPattern=regex @"^([-+]?[0-9]*\.?[0-9]+(?:e[-+]?[0-9]+)?)([a-z]+|%)$"
    let private colorTailPattern=regex @"\)\s*[-.0-9]"
    let private radixPattern=regex @"^0(?:x[0-9a-f]+|b[01]+|o[0-7]+)$"
    let private tryNumber (text:string) =
        let text=text.Trim()
        if text.Length=0 then Some 0.
        else
            match Double.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture) with
            | true,value when Double.IsNaN value -> None
            | true,value when Double.IsInfinity value && text.EndsWith("infinity",StringComparison.OrdinalIgnoreCase) &&
                              text<>"Infinity" && text<>"+Infinity" && text<>"-Infinity" -> None
            | true,value -> Some value
            | _ when radixPattern.IsMatch text ->
                let radix=match Char.ToLowerInvariant text[1] with 'x' -> 16 | 'b' -> 2 | _ -> 8
                let mutable value=System.Numerics.BigInteger.Zero
                for index in 2..text.Length-1 do
                    let digit="0123456789abcdef".IndexOf(Char.ToLowerInvariant text[index])
                    value<-value*System.Numerics.BigInteger(radix)+System.Numerics.BigInteger(digit)
                Some(float value)
            | _ -> None
    let private digitsPattern=regex @"[-+]?[0-9]*\.?[0-9]+(?:e[-+]?[0-9])?"
    let private hexPattern=regex @"^(?:#(?:[0-9a-f]{3}){1,2}|#(?:[0-9a-f]{4}){1,2})$"
    let private rgbPattern=regex @"rgb\(\s*([0-9]+)\s*,\s*([0-9]+)\s*,\s*([0-9]+)\s*\)"
    let private rgbaPattern=regex @"rgba\(\s*([0-9]+)\s*,\s*([0-9]+)\s*,\s*([0-9]+)\s*,\s*(-?[0-9]+|-?[0-9]*.[0-9]+)\s*\)"
    let private hslPattern=regex @"hsl\(\s*(-?[0-9]+|-?[0-9]*.[0-9]+)\s*,\s*(-?[0-9]+|-?[0-9]*.[0-9]+)%\s*,\s*(-?[0-9]+|-?[0-9]*.[0-9]+)%\s*\)"
    let private hslaPattern=regex @"hsla\(\s*(-?[0-9]+|-?[0-9]*.[0-9]+)\s*,\s*(-?[0-9]+|-?[0-9]*.[0-9]+)%\s*,\s*(-?[0-9]+|-?[0-9]*.[0-9]+)%\s*,\s*(-?[0-9]+|-?[0-9]*.[0-9]+)\s*\)"
    let private parse (text:string) =
        let value=Double.Parse(text,NumberStyles.Float,CultureInfo.InvariantCulture)
        finite "value" value
        value
    let private color (text:string) =
        if hexPattern.IsMatch text then
            let short=text.Length=4 || text.Length=5
            let channelByte index =
                let part=if short then String(text[index],2) else text.Substring(index,2)
                float(Int32.Parse(part,NumberStyles.HexNumber,CultureInfo.InvariantCulture))
            let red=channelByte 1
            let green=channelByte (if short then 2 else 3)
            let blue=channelByte (if short then 3 else 5)
            let alpha=if text.Length=5 || text.Length=9 then Math.Round(channelByte(if short then 4 else 7)/255.,3,MidpointRounding.AwayFromZero) else 1.
            [|red;green;blue;alpha|]
        else
            let primary,secondary=if text.StartsWith("rgb",StringComparison.Ordinal) then rgbPattern,rgbaPattern else hslPattern,hslaPattern
            let matched=let first=primary.Match text in if first.Success then first else secondary.Match text
            if not matched.Success then invalidArg "value" "Unsupported source color syntax."
            let first,second,third=parse matched.Groups[1].Value,parse matched.Groups[2].Value,parse matched.Groups[3].Value
            let alpha=if matched.Groups[4].Success then parse matched.Groups[4].Value else 1.
            if text.StartsWith("rgb",StringComparison.Ordinal) then [|first;second;third;alpha|]
            else
                let hue,saturation,lightness=first/360.,second/100.,third/100.
                if saturation=0. then [|lightness;lightness;lightness;alpha|]
                else
                    let q=if lightness<0.5 then lightness*(1.+saturation) else lightness+saturation-lightness*saturation
                    let p=2.*lightness-q
                    let hue2rgb time =
                        let time=if time<0. then time+1. else time
                        let time=if time>1. then time-1. else time
                        if time<1./6. then p+(q-p)*6.*time
                        elif time<0.5 then q
                        elif time<2./3. then p+(q-p)*(2./3.-time)*6.
                        else p
                    [| PropertyNumbers.round 1. (hue2rgb(hue+1./3.)*255.)
                       PropertyNumbers.round 1. (hue2rgb hue*255.)
                       PropertyNumbers.round 1. (hue2rgb(hue-1./3.)*255.); alpha |]
    /// <summary>Decomposes finite scalars and source unit/color/compound syntax without native CSS resolution.</summary>
    let decompose literal =
        let scalar number = {Kind=ValueKind.Number;Number=number;Unit=null;Operator=None;Components=[||];Strings=[||]}
        match literal with
        | Number value -> finite "value" value; scalar value
        | Text text ->
            if String.IsNullOrWhiteSpace text then scalar 0.
            else
                match tryNumber text with
                | Some value -> finite "value" value; scalar value
                | _ ->
                    let op=if text.Length>1 && text[1]='=' then Some text[0] else None
                    let text=if op.IsSome then text.Substring 2 else text
                    let unitMatch=if text.Contains(' ') then Match.Empty else unitPattern.Match text
                    if unitMatch.Success then
                        {scalar(parse unitMatch.Groups[1].Value) with Kind=ValueKind.Unit;Unit=unitMatch.Groups[2].Value;Operator=op}
                    elif op.IsSome then
                        let value=match tryNumber text with Some value -> value | None -> invalidArg "value" "Expected a finite relative operand."
                        finite "value" value
                        {scalar value with Operator=op}
                    elif hexPattern.IsMatch text ||
                         ((text.StartsWith("rgb",StringComparison.Ordinal) || text.StartsWith("hsl",StringComparison.Ordinal)) &&
                          (text.EndsWith(")",StringComparison.Ordinal) || not(colorTailPattern.IsMatch text))) then
                        {scalar 0. with Kind=ValueKind.Color;Components=color text}
                    else
                        let components=digitsPattern.Matches text |> Seq.cast<Match> |> Seq.map(fun item -> parse item.Value) |> Seq.toArray
                        {scalar 0. with Kind=ValueKind.Complex;Components=components;Strings=digitsPattern.Split text}
    let private relative first last op = match op with '-' -> first-last | '+' -> first+last | _ -> first*last
    let private prepareWith roundNumbers (convertUnit:(float -> string -> string -> float) option) original first last =
        let mutable first=decompose first
        let mutable last=decompose last
        match first.Operator with Some op -> first<-{first with Number=relative (decompose original).Number first.Number op} | None -> ()
        match last.Operator with Some op -> last<-{last with Number=relative first.Number last.Number op} | None -> ()
        if first.Kind<>last.Kind then
            if first.Kind=ValueKind.Complex || last.Kind=ValueKind.Complex then
                let complex=if first.Kind=ValueKind.Complex then first else last
                let convert value={value with Kind=ValueKind.Complex;Components=Array.create complex.Components.Length value.Number;Strings=Array.copy complex.Strings}
                if first.Kind=ValueKind.Complex then last<-convert last else first<-convert first
            elif first.Kind=ValueKind.Unit || last.Kind=ValueKind.Unit then
                if first.Kind=ValueKind.Unit then last<-{last with Kind=ValueKind.Unit;Unit=first.Unit}
                else first<-{first with Kind=ValueKind.Unit;Unit=last.Unit}
            elif first.Kind=ValueKind.Color || last.Kind=ValueKind.Color then
                if first.Kind=ValueKind.Color then last<-{last with Kind=ValueKind.Color;Components=Array.zeroCreate 4}
                else first<-{first with Kind=ValueKind.Color;Components=Array.zeroCreate 4}
        if first.Unit<>last.Unit then
            match convertUnit with
            | None -> invalidArg "last" "Endpoint units require native conversion."
            | Some convert ->
                if not(String.IsNullOrEmpty last.Unit) then
                    let value=convert first.Number first.Unit last.Unit
                    finite "converted value" value
                    first<-{first with Number=value;Unit=last.Unit}
                else
                    let value=convert last.Number last.Unit first.Unit
                    finite "converted value" value
                    last<-{last with Number=value;Unit=first.Unit}
        if first.Components.Length<>last.Components.Length then
            let longest=if first.Components.Length>last.Components.Length then first else last
            let fill value={value with Components=Array.init longest.Components.Length (fun i -> if i<value.Components.Length then value.Components[i] else 0.);Strings=Array.copy longest.Strings}
            if first.Components.Length>last.Components.Length then last<-fill last else first<-fill first
        fun progress precision (modifier:float -> float) (canWrite:unit -> bool) ->
            finite "progress" progress
            let precision=if progress=0. || progress=1. || (first.Kind=ValueKind.Number && not roundNumbers) then -1 else precision
            let round value=if precision<0 then value else PropertyNumbers.round (Math.Pow(10.,float precision)) value
            let lerp first last = if progress=1. then last elif progress=0. then first else first+(last-first)*progress
            let modify value =
                if not(canWrite()) then None
                else
                    let result=modifier value
                    if canWrite() then Some result else None
            if not(canWrite()) then None
            elif first.Kind=ValueKind.Number || first.Kind=ValueKind.Unit then
                modify(round(lerp first.Number last.Number)) |> Option.map(fun value ->
                    {Scalar=(if first.Kind=ValueKind.Number then Some value else None)
                     Text=number value+(if first.Kind=ValueKind.Unit then first.Unit else "");Components=[||]})
            else
                let values=Array.zeroCreate last.Components.Length
                let mutable alive=true
                for index in 0..values.Length-1 do
                    if alive then
                        let value=
                            if first.Kind=ValueKind.Color && index<3 then
                                Math.Sqrt(first.Components[index]*first.Components[index]*(1.-progress)+last.Components[index]*last.Components[index]*progress)
                            elif first.Kind=ValueKind.Color then lerp first.Components[index] last.Components[index]
                            else round(lerp first.Components[index] last.Components[index])
                        match modify value with Some value -> values[index]<-value | None -> alive<-false
                if not alive || not(canWrite()) then None
                else
                    let text=
                        if first.Kind=ValueKind.Color then
                            "rgba("+String.concat "," [for i in 0..3 -> number(if i<3 then PropertyNumbers.round 1. values[i] else values[i])]+")"
                        else
                            let result=System.Text.StringBuilder(last.Strings[0])
                            for index in 0..values.Length-1 do result.Append(number values[index]).Append(last.Strings[index+1]) |> ignore
                            result.ToString()
                    Some {Scalar=None;Text=text;Components=values}

    /// <summary>Captures object endpoints and returns a guarded sampler; differing units require native conversion.</summary>
    let prepare original first last = prepareWith false None original first last

    /// <summary>Captures CSS endpoints, rounding intermediate unitless scalars before modifiers as source CSS dispatch does.</summary>
    let prepareCss original first last = prepareWith true None original first last

    /// <summary>Converts differing scalar unit metadata after source relative operators and endpoint homogenization.</summary>
    let prepareCssWith convertUnit original first last = prepareWith true (Some convertUnit) original first last
