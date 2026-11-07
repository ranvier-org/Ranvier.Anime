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
    let private finite name value = if not(Double.IsFinite value) then invalidArg name "Expected finite input."
    /// <summary>Formats source-style shortest numbers and exponent thresholds, including IEEE values.</summary>
    let number (value:float) =
        if value=0. then "0"
        elif Double.IsNaN value then "NaN"
        elif Double.IsPositiveInfinity value then "Infinity"
        elif Double.IsNegativeInfinity value then "-Infinity"
        else
            let raw=value.ToString("R",CultureInfo.InvariantCulture)
            if raw.IndexOf('E')<0 && raw.IndexOf('e')<0 then raw
            else
                let split=raw.Split('E','e')
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
    let private numberText value=number value
    /// <summary>A composed scalar or string with the sampled component values.</summary>
    /// <remarks>Text is composed on first read: a numeric writer reads <c>Number</c> and never formats.</remarks>
    [<Sealed; AllowNullLiteral; System.Diagnostics.DebuggerDisplay("{ToString(),nq}")>]
    type Output internal (kind:ValueKind, number:float, unitSuffix:string, components:float array, text:string) =
        let mutable text=text
        /// <summary>Whether the value is a unitless number, so <c>Number</c> is its scalar.</summary>
        member _.HasScalar=kind=ValueKind.Number
        /// <summary>The composed scalar; zero for color and compound values.</summary>
        member _.Number=number
        /// <summary>Whether the value is a number or a number with a unit, so its text is <c>Number</c> then <c>UnitSuffix</c>.</summary>
        member _.IsScalar=kind=ValueKind.Number || kind=ValueKind.Unit
        /// <summary>The unit a scalar's text ends with.</summary>
        member _.UnitSuffix=unitSuffix
        /// <summary>The scalar for unitless numbers, None otherwise.</summary>
        member _.Scalar=if kind=ValueKind.Number then Some number else None
        /// <summary>The composed CSS text, formatted on first read.</summary>
        member _.Text=
            if isNull text then text<-(numberText number)+unitSuffix
            text
        /// <summary>The sampled component values.</summary>
        member _.Components=components
        /// <summary>A copy with independent components.</summary>
        member _.CopyComponents()=Output(kind,number,unitSuffix,Array.copy components,text)
        /// <summary>The invariant scalar or composed text.</summary>
        override this.ToString() =
            if kind=ValueKind.Number then number.ToString("R",CultureInfo.InvariantCulture) else this.Text
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
        let scalar number : Data = {Kind=ValueKind.Number;Number=number;Unit=null;Operator=None;Components=[||];Strings=[||]}
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
    /// <summary>Normalizes decomposed relative endpoints, units and component templates using a captured original value.</summary>
    let normalizeData (convertUnit:(float -> string -> string -> float) option, original:Data, first:Data, last:Data) =
        let mutable first=first
        let mutable last=last
        match first.Operator with Some op -> first<-{first with Number=relative original.Number first.Number op} | None -> ()
        match last.Operator with Some op -> last<-{last with Number=relative first.Number last.Number op} | None -> ()
        if first.Kind<>last.Kind then
            if first.Kind=ValueKind.Complex || last.Kind=ValueKind.Complex then
                let complex=if first.Kind=ValueKind.Complex then first else last
                let convert (value:Data)={value with Kind=ValueKind.Complex;Components=Array.create complex.Components.Length value.Number;Strings=Array.copy complex.Strings}
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
            let fill (value:Data)={value with Components=Array.init longest.Components.Length (fun i -> if i<value.Components.Length then value.Components[i] else 0.);Strings=Array.copy longest.Strings}
            if first.Components.Length>last.Components.Length then last<-fill last else first<-fill first
        first,last

    /// <summary>Normalizes relative endpoints, unit metadata and component templates during capture.</summary>
    let normalize (convertUnit:(float -> string -> string -> float) option) original first last =
        let first=decompose first
        let last=decompose last
        let original=if first.Operator.IsSome then decompose original else first
        normalizeData(convertUnit,original,first,last)

    /// <summary>Composes modified cached values using captured unit and component metadata.</summary>
    /// <remarks>The output retains the supplied component array.</remarks>
    let compose (template:Data, scalar:float, values:float array) : Output =
        if template.Kind=ValueKind.Number || template.Kind=ValueKind.Unit then
            Output(template.Kind,scalar,(if template.Kind=ValueKind.Unit then template.Unit else ""),Array.Empty<float>(),null)
        else
            let text=
                if template.Kind=ValueKind.Color then
                    "rgba("+String.concat "," [for i in 0..3 -> number(if i<3 then PropertyNumbers.round 1. values[i] else values[i])]+")"
                else
                    let result=System.Text.StringBuilder(template.Strings[0])
                    for index in 0..values.Length-1 do result.Append(number values[index]).Append(template.Strings[index+1]) |> ignore
                    result.ToString()
            Output(template.Kind,0.,null,values,text)

    /// <summary>Samples scalar endpoints held outside their template into an output, or null when the guard fails.</summary>
    /// <remarks>Allocates only the output; <c>template</c> supplies the kind and unit.</remarks>
    let sampleScalar (roundNumbers:bool, template:Data, fromNumber:float, toNumber:float, progress:float, precision:int,
                      modifier:float -> float, canWrite:unit -> bool) : Output =
        finite "progress" progress
        let precision=if progress=0. || progress=1. || (template.Kind=ValueKind.Number && not roundNumbers) then -1 else precision
        let value=if progress=1. then toNumber elif progress=0. then fromNumber else fromNumber+(toNumber-fromNumber)*progress
        let value=if precision<0 then value else PropertyNumbers.round (Math.Pow(10.,float precision)) value
        if not(canWrite()) || not(canWrite()) then null
        else
            let result=modifier value
            if canWrite() then compose(template,result,Array.Empty<float>()) else null

    /// <summary>Samples normalized endpoints into a composed output, or null when the guard fails around a modifier.</summary>
    /// <remarks>The unwrapped core of <c>sample</c>.</remarks>
    let sampleOutput (roundNumbers:bool, first:Data, last:Data, progress:float, precision:int, modifier:float -> float, canWrite:unit -> bool) : Output =
        if first.Kind=ValueKind.Number || first.Kind=ValueKind.Unit then
            sampleScalar(roundNumbers,first,first.Number,last.Number,progress,precision,modifier,canWrite)
        else
            finite "progress" progress
            let precision=if progress=0. || progress=1. || (first.Kind=ValueKind.Number && not roundNumbers) then -1 else precision
            let scale=if precision<0 then 0. else Math.Pow(10.,float precision)
            let inline round value=if precision<0 then value else PropertyNumbers.round scale value
            let inline lerp a b = if progress=1. then b elif progress=0. then a else a+(b-a)*progress
            if not(canWrite()) then null
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
                        if not(canWrite()) then alive<-false
                        else
                            let result=modifier value
                            if canWrite() then values[index]<-result else alive<-false
                if not alive || not(canWrite()) then null
                else compose(last,0.,values)

    /// <summary>Samples normalized endpoints and returns the modified scalar cache with the composed output.</summary>
    let sample (roundNumbers:bool, first:Data, last:Data, progress:float, precision:int, modifier:float -> float, canWrite:unit -> bool) =
        match sampleOutput(roundNumbers,first,last,progress,precision,modifier,canWrite) with
        | null -> None
        | output -> Some(struct(output.Number,output))

    let private prepareWith roundNumbers convertUnit original first last =
        let first,last=normalize convertUnit original first last
        fun progress precision modifier canWrite ->
            match sampleOutput(roundNumbers,first,last,progress,precision,modifier,canWrite) with
            | null -> None
            | output -> Some output

    /// <summary>Captures object endpoints and returns a guarded sampler; differing units require native conversion.</summary>
    let prepare original first last = prepareWith false None original first last

    /// <summary>Captures CSS endpoints, rounding intermediate unitless scalars before modifiers as source CSS dispatch does.</summary>
    let prepareCss original first last = prepareWith true None original first last

    /// <summary>Converts differing scalar unit metadata after source relative operators and endpoint homogenization.</summary>
    let prepareCssWith convertUnit original first last = prepareWith true (Some convertUnit) original first last
