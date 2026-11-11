module Ranvier.Anime.Tests.ScrollDebugTests

open System
open System.Collections.Generic
open System.IO
open System.Text.Json
open System.Text.RegularExpressions
open Expecto
open Ranvier.Dom
open Ranvier.Anime

/// <summary>Fake DOM state behind one proxied element or document.</summary>
[<AllowNullLiteral>]
type DebugNode() =
    member val Children = ResizeArray<DebugNode>()
    member val Parent : DebugNode = null with get,set
    member val Element : obj = null with get,set
    member val CssText = "" with get,set
    member val Properties = Dictionary<string,string>(StringComparer.Ordinal)
    member val Text = "" with get,set

/// <summary>Routes DOM interface members to a handler and keeps the node each proxy represents.</summary>
type DebugProxy() =
    inherit Reflection.DispatchProxy()
    member val Node : DebugNode = null with get,set
    member val Handler : Reflection.MethodInfo -> obj array -> obj = (fun methodInfo _ -> failwith methodInfo.Name) with get,set
    override this.Invoke(methodInfo,args) = this.Handler methodInfo args

let private wrap<'T> (node:DebugNode) (handler:Reflection.MethodInfo -> obj array -> obj) : 'T =
    let result = Reflection.DispatchProxy.Create<'T,DebugProxy>()
    let proxy = box result :?> DebugProxy
    proxy.Node <- node
    proxy.Handler <- handler
    result

let private nodeOf (element:HTMLElement) = (box element :?> DebugProxy).Node

let private newElement (documentRef:obj ref) : HTMLElement =
    let node = DebugNode()
    let style = wrap<CSSStyleProperties> node (fun methodInfo args ->
        match methodInfo.Name with
        | "get_cssText" -> box node.CssText
        | "set_cssText" -> node.CssText <- unbox<string> args[0]; null
        | "setProperty" -> node.Properties[unbox<string> args[0]] <- unbox<string> args[1]; null
        | name -> failwith name)
    let element = wrap<HTMLElement> node (fun methodInfo args ->
        match methodInfo.Name with
        | "get_style" -> box style
        | "get_ownerDocument" -> documentRef.Value
        | "get_parentNode" -> if isNull node.Parent then null else node.Parent.Element
        | "get_textContent" -> box node.Text
        | "set_textContent" -> node.Text <- unbox<string> args[0]; null
        | "setAttribute" -> null
        | "querySelector" -> null
        | "appendChild" ->
            let child = (args[0] :?> DebugProxy).Node
            child.Parent <- node
            node.Children.Add child
            args[0]
        | "removeChild" ->
            let child = (args[0] :?> DebugProxy).Node
            node.Children.Remove child |> ignore
            child.Parent <- null
            args[0]
        | name -> failwith name)
    node.Element <- box element
    element

let private newRoot () : HTMLElement =
    let documentRef : obj ref = ref null
    let document = wrap<Document> (DebugNode()) (fun methodInfo _ ->
        match methodInfo.Name with
        | "createElement" -> box (newElement documentRef)
        | name -> failwith name)
    documentRef.Value <- box document
    newElement documentRef

/// <summary>Collapses a CSS declaration list into ordered (name, value) pairs, ignoring whitespace layout.</summary>
let private declarations (css:string) =
    css.Split ';'
    |> Array.map(fun declaration -> declaration.Trim())
    |> Array.filter((<>) "")
    |> Array.map(fun declaration ->
        let split = declaration.IndexOf ':'
        declaration.Substring(0,split).Trim(), Regex.Replace(declaration.Substring(split+1).Trim(),@"\s+"," "))

let private fixture () = JsonDocument.Parse(File.ReadAllText(Path.Combine(Path.Combine(AppContext.BaseDirectory,"reference"),"scroll-debug-reference.json")))
let private property name (value:JsonElement) = value.GetProperty(name:string)
let private number name value = (property name value).GetDouble()
let private boolean name value = (property name value).GetBoolean()
let private text name value = (property name value).GetString()

// The fixture pins the source container at height 400 and scroll extent 2400 for every case.
let private containerHeight = 400.
let private scrollExtent = 2400.

/// <summary>Builds a ready observer whose coordinates resolve to the fixture's [0,100,600,0].</summary>
let private readyObserver (options:ScrollOptions) (offset:float) =
    let observer = new ScrollObserver((fun () -> 0.,false),
        (fun () -> { Offset=offset; TargetSize=100.; ContainerSize=600.; ScrollSize=scrollExtent }),
        options=(fun () -> options))
    observer.Refresh()
    observer

let private caseKeys () =
    use document = fixture()
    (property "cases" document.RootElement).EnumerateArray()
    |> Seq.map(fun sample -> boolean "horizontal" sample, number "width" sample, number "offset" sample)
    |> Seq.toList

let private checkCase (index:int) =
    use document = fixture()
    let sample = (property "cases" document.RootElement).EnumerateArray() |> Seq.item index
    let horizontal,width,offset = boolean "horizontal" sample,number "width" sample,number "offset" sample
    let options = { ScrollOptions.Default with Horizontal=horizontal; Sync=ScrollSync.Disabled }
    use observer = readyObserver options offset
    let root = newRoot()
    let rootNode = nodeOf root
    let released = ref 0
    let overlay = ScrollDebugOverlay(root,(fun () -> width,containerHeight,scrollExtent,scrollExtent),(fun () -> 0),0,"probe",
        (fun () -> { new IDisposable with member _.Dispose() = released.Value <- released.Value+1 }))
    overlay.Render(observer,options)

    Expect.hasLength rootNode.Children 1 "render attaches one overlay"
    let overlayNode = rootNode.Children[0]
    Expect.sequenceEqual (declarations overlayNode.CssText) (declarations (text "overlayCSS" sample)) "overlayCSS"

    let parts = overlayNode.Children
    Expect.hasLength parts 2 "thresholds and triggers parts"
    let labels =
        [| for partIndex in 0..1 do
            for label in parts[partIndex].Children do
                yield partIndex=0, label |]
    let expectedLabels = (property "labels" sample).EnumerateArray() |> Seq.toArray
    Expect.equal labels.Length expectedLabels.Length "label count"
    let positionKey = if horizontal then "left" else "top"
    for i in 0..labels.Length-1 do
        let view,label = labels[i]
        let expected = expectedLabels[i]
        let name = sprintf "label %d" i
        Expect.equal view (boolean "view" expected) (name+" view")
        Expect.equal label.Children[0].Text (text "text" expected) (name+" text")
        Expect.sequenceEqual (declarations label.CssText) (declarations (text "css" expected)) (name+" css")
        let position = match label.Properties.TryGetValue positionKey with | true,value -> value | _ -> "<unset>"
        Expect.equal position (text "position" expected) (name+" position")

    overlay.Remove()
    Expect.equal rootNode.Children.Count 0 "removeDebug removes the ruler DOM"
    Expect.equal released.Value 1 "removeDebug releases the position lease once"

let tests =
    testList "scroll debug overlay" [
        for index,(horizontal,width,offset) in List.indexed (caseKeys()) ->
            testCase (sprintf "debug overlay matches fixture horizontal=%b width=%g offset=%g" horizontal width offset) <| fun _ ->
                checkCase index
    ]
