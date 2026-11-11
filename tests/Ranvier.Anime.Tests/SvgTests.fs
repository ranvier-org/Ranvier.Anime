module Ranvier.Anime.Tests.SvgTests

open System
open System.Collections.Generic
open System.IO
open System.Reflection
open System.Text.Json
open Expecto
open Ranvier.Dom
open Ranvier.Anime

/// <summary>Recording interfaces used exclusively by compiled SVG tests.</summary>
type SvgRecordingProxy() =
    inherit DispatchProxy()
    /// <summary>Dispatches a recorded interface method.</summary>
    member val Handler : MethodInfo -> obj array -> obj = fun _ _ -> null with get,set
    override this.Invoke(methodInfo,args) = this.Handler methodInfo args

let internal proxy<'T> handler =
    let value=DispatchProxy.Create<'T,SvgRecordingProxy>()
    (box value :?> SvgRecordingProxy).Handler<-handler
    value

let private point x y = proxy<SVGPoint>(fun m _ ->
    match m.Name with "get_x" -> box(float32 x) | "get_y" -> box(float32 y) | _ -> null)

let private text (value:JsonElement) (name:string) = value.GetProperty(name).GetString()
let private number (value:JsonElement) =
    if value.ValueKind=JsonValueKind.Number then value.GetDouble()
    else match value.GetString() with "NaN" -> nan | "Infinity" -> infinity | "-Infinity" -> -infinity | value -> float value
let private field (value:JsonElement) (name:string) = number(value.GetProperty name)
let private matrix (value:JsonElement) =
    if value.ValueKind=JsonValueKind.Null then Unchecked.defaultof<SVGMatrix>
    else proxy<SVGMatrix>(fun m _ -> box(field value (m.Name.Substring 4)))
let internal geometry tag length shape ctm =
    let attrs=Dictionary<string,string>()
    let styles=Dictionary<string,string>()
    let priorities=Dictionary<string,string>()
    attrs["d"]<-"M1 2 L3 4"
    attrs["points"]<-"1,2 3,4"
    let style=proxy<CSSStyleProperties>(fun m args ->
        match m.Name with
        | "setProperty" ->
            styles[string args[0]]<-string args[1]
            priorities[string args[0]]<-(unbox<string option> args[2] |> Option.defaultValue "")
            null
        | "get_length" -> box(uint32 styles.Count)
        | "item" -> box(styles.Keys |> Seq.tryItem(int(unbox<uint32> args[0])) |> Option.defaultValue "")
        | "get_cssText" -> box(styles |> Seq.map(fun pair->pair.Key+":"+pair.Value+";") |> String.concat "")
        | "getPropertyPriority" -> match priorities.TryGetValue(string args[0]) with true,v -> box v | _ -> box ""
        | "removeProperty" -> styles.Remove(string args[0]) |> ignore; priorities.Remove(string args[0]) |> ignore; box ""
        | "getPropertyValue" -> match styles.TryGetValue(string args[0]) with true,v -> box v | _ -> box ""
        | _ -> null)
    let path=proxy<SVGGeometryElement>(fun m args ->
        match m.Name with
        | "get_tagName" -> box tag
        | "getTotalLength" -> box(float32 length)
        | "getCTM" -> box ctm
        | "get_style" -> box style
        | "getPointAtLength" ->
            let value=float(unbox<float32> args[0])
            box(if shape="line" then point value (value*2.) else point (cos(value/10.)*10.) (sin(value/10.)*10.))
        | "getAttribute" -> match attrs.TryGetValue(string args[0]) with true,v -> box v | _ -> null
        | "setAttribute" -> attrs[string args[0]]<-string args[1]; null
        | "removeAttribute" -> attrs.Remove(string args[0]) |> ignore; null
        | _ -> null)
    path,attrs,styles

let private fixture () =
    let root=Environment.GetEnvironmentVariable "RANVIER_ANIME_REFERENCE_ROOT"
    let path=Path.Combine((if String.IsNullOrWhiteSpace root then Path.Combine(AppContext.BaseDirectory,"reference") else root),"svg-reference.json")
    use doc=JsonDocument.Parse(File.ReadAllText path)
    doc.RootElement.Clone()

let private close expected actual =
    if Double.IsNaN expected then Expect.isTrue (Double.IsNaN actual) "NaN"
    elif Double.IsInfinity expected then Expect.equal actual expected "infinity"
    else Expect.isLessThanOrEqual (abs(expected-actual)) 1e-9 "source sample"

let private attributesMatch (expected:JsonElement) (actual:Dictionary<string,string>) =
    for property in expected.EnumerateObject() do
        Expect.isTrue (actual.ContainsKey property.Name) property.Name
        Expect.equal actual[property.Name] (property.Value.GetString()) property.Name

/// <summary>The oracle records JavaScript camelCase style names; setProperty receives the CSS name.</summary>
let private styleMatch (expected:JsonElement) (actual:Dictionary<string,string>) =
    for property in expected.EnumerateObject() do
        let name=if property.Name="strokeLinecap" then "stroke-linecap" else property.Name
        Expect.isTrue (actual.ContainsKey name) name
        Expect.equal actual[name] (property.Value.GetString()) name

let svgSourceTests = testList "SVG pinned source" [
    testCase "drawable owner restoration preserves unrelated properties and disables accessors" <| fun _ ->
        use graph=new Ranvier.Graph()
        use active=graph.Activate()
        let path,attrs,styles=geometry "path" 10. "line" Unchecked.defaultof<SVGMatrix>
        attrs["stroke-dashoffset"]<-"7"
        path.style.setProperty("stroke-linecap","square",priority="important")
        path.style.setProperty("fill","red")
        let computed=proxy<CSSStyleDeclaration>(fun _ args -> box(if string args[0]="stroke-linecap" then "round" else "none"))
        let owner,drawable,first,last=Ranvier.Api.createRoot(fun owner ->
            let drawable=Svg.createDrawable path computed 0. 0.
            let first,last=SvgProperties.drawRange drawable 0. 0.
            owner,drawable,first,last)
        last.Value<-1.
        attrs["data-concurrent"]<-"keep"
        path.style.setProperty("fill","blue")
        (owner:>IDisposable).Dispose()
        Expect.isTrue drawable.IsReverted "owner disposes drawing handle"
        Expect.isFalse (attrs.ContainsKey "pathLength") "initial absence restored"
        Expect.isFalse (attrs.ContainsKey "draw") "synthetic draw absence restored"
        Expect.equal attrs["stroke-dashoffset"] "7" "existing offset restored"
        Expect.equal attrs["data-concurrent"] "keep" "unrelated attribute survives"
        Expect.equal styles["fill"] "blue" "unrelated inline style survives"
        Expect.equal styles["stroke-linecap"] "square" "inline cap restored"
        Expect.equal (path.style.getPropertyPriority "stroke-linecap") "important" "priority restored"
        first.Value<-0.5
        last.Value<-0.5
        Expect.isFalse (attrs.ContainsKey "draw") "disposed accessors cannot write"
    testCase "scope and owner revert morph progress and previous tween restoration" <| fun _ ->
        use graph=new Ranvier.Graph()
        use active=graph.Activate()
        let source,attrs,_=geometry "path" 10. "line" Unchecked.defaultof<SVGMatrix>
        let destination,_,_=geometry "path" 20. "line" Unchecked.defaultof<SVGMatrix>
        use scope=new Scope()
        let mutable morph:SvgMorph option=None
        scope.Execute(fun _ ->
            let value=SvgProperties.morph source destination 0.1 (Some "M9 8 L7 6")
            value.Progress.Value<-0.5
            morph<-Some value) |> ignore
        Expect.equal attrs["d"] "M0 0 L15 30 " "shared interpolation morph midpoint"
        attrs["data-concurrent"]<-"keep"
        scope.Revert()
        Expect.equal attrs["d"] "M1 2 L3 4" "initial attribute restored"
        morph.Value.Progress.Value<-1.
        Expect.equal attrs["d"] "M1 2 L3 4" "disposed progress ignores later writes"
        Expect.equal attrs["data-concurrent"] "keep" "unrelated edit survives"
    testCase "SVG accessors retain fractions and sample typed target setters" <| fun _ ->
        let path,attrs,_=geometry "path" 10. "line" Unchecked.defaultof<SVGMatrix>
        let computed=proxy<CSSStyleDeclaration>(fun _ args -> box(if string args[0]="stroke-linecap" then "butt" else "none"))
        let drawable=Svg.createDrawable path computed 0. 0.
        let first,last=SvgProperties.drawRange drawable 0. 0.
        last.Value<-0.75
        first.Value<-0.25
        Expect.equal first.Value 0.25 "start accessor"
        Expect.equal last.Value 0.75 "end accessor"
        Expect.equal attrs["stroke-dasharray"] "500 510" "shared range"
        let mutable position=Unchecked.defaultof<SvgMotionSample>
        let motion=SvgProperties.motion path true 0. (fun value -> position<-value)
        motion.Value<-5.
        Expect.equal motion.Value 5. "distance accessor"
        Expect.equal (position.X,position.Y) (5.,10.) "typed output"
    testCase "motion path source samples and CTM" <| fun _ ->
        let cases=(fixture()).GetProperty("motion").EnumerateArray() |> Seq.toArray
        Expect.equal cases.Length 144 "case count"
        for item in cases do
            let path,_,_=geometry "path" (field item "length") (text item "shape") (matrix(item.GetProperty "matrix"))
            let sample=Svg.createMotionPath path (item.GetProperty("inSvg").GetBoolean()) (field item "offset")
            for expected in item.GetProperty("samples").EnumerateArray() do
                let actual=sample(field expected "progress")
                close (field expected "x") actual.X
                close (field expected "y") actual.Y
                close (field expected "angle") actual.Angle
    testCase "drawable source attributes caps scales and pre-normalized initialization" <| fun _ ->
        let cases=(fixture()).GetProperty("draw").EnumerateArray() |> Seq.toArray
        Expect.equal cases.Length 36 "case count"
        for item in cases do
            let path,attrs,styles=geometry "path" 10. "line" (matrix(item.GetProperty "matrix"))
            if item.GetProperty("normalized").GetBoolean() then attrs["pathLength"]<-"1000"
            let computed=proxy<CSSStyleDeclaration>(fun _ args ->
                box(if string args[0]="stroke-linecap" then text item "cap" else text item "vector"))
            let drawable=Svg.createDrawable path computed 0.1 0.2
            attributesMatch (item.GetProperty "initial") attrs
            styleMatch (item.GetProperty "initialStyle") styles
            for expected in item.GetProperty("samples").EnumerateArray() do
                drawable.Draw(field expected "start",field expected "end")
                attributesMatch (expected.GetProperty "attributes") attrs
                styleMatch (expected.GetProperty "style") styles
    testCase "morph source strings tag casing zero precision and previous value" <| fun _ ->
        let cases=(fixture()).GetProperty("morph").EnumerateArray() |> Seq.toArray
        Expect.equal cases.Length 192 "case count"
        for item in cases do
            let source,attrs,_=geometry (text item "tag") 10. "line" Unchecked.defaultof<SVGMatrix>
            let destination,_,_=geometry (text item "targetTag") 20. "curve" Unchecked.defaultof<SVGMatrix>
            let prior=item.GetProperty("previous")
            let previous=if prior.ValueKind=JsonValueKind.Null then None else Some(prior.GetString())
            let actual=Svg.morphTo source destination (field item "precision") previous
            let values=item.GetProperty("values").EnumerateArray() |> Seq.map _.GetString() |> Seq.toArray
            Expect.equal actual (values[0],values[1]) "morph pair"
            attributesMatch (item.GetProperty "attributes") attrs
            use morph=SvgProperties.morph source destination (field item "precision") previous
            let attribute=if text item "tag"="path" then "d" else "points"
            for sample in item.GetProperty("samples").EnumerateArray() do
                morph.Progress.Value<-field sample "progress"
                Expect.equal attrs[attribute] (text sample "value") "shared interpolation source sample"
    testCase "morph rejects unsupported source and target tags before geometry sampling" <| fun _ ->
        let valid,_,_=geometry "path" 10. "line" Unchecked.defaultof<SVGMatrix>
        for tag in ["rect";"circle";"text";"g"] do
            let invalid,_,_=geometry tag 10. "line" Unchecked.defaultof<SVGMatrix>
            Expect.throwsT<ArgumentException> (fun () -> Svg.morphTo invalid valid 0.33 None |> ignore) "source tag"
            Expect.throwsT<ArgumentException> (fun () -> Svg.morphTo valid invalid 0.33 None |> ignore) "target tag"
]
