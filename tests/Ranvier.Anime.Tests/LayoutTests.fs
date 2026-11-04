module Ranvier.Anime.Tests.LayoutTests

open System
open System.Collections.Generic
open System.Reflection
open System.IO
open System.Text.Json
open Expecto
open Ranvier
open Ranvier.Dom
open Ranvier.Anime

type LayoutProxy() =
    inherit DispatchProxy()
    member val Handler:MethodInfo -> obj array -> obj=(fun methodInfo _ -> failwith methodInfo.Name) with get,set
    override this.Invoke(methodInfo,args)=this.Handler methodInfo args

let private proxy<'t> handler =
    let value=DispatchProxy.Create<'t,LayoutProxy>()
    (box value :?> LayoutProxy).Handler<-handler
    value

type private Style() =
    let values=Dictionary<string,string*string>()
    member val AfterWrite:unit -> unit=ignore with get,set
    member _.Value name=match values.TryGetValue name with true,(value,_) -> value | _ -> ""
    member _.Priority name=match values.TryGetValue name with true,(_,priority) -> priority | _ -> ""
    member this.Seed(name,value,priority)=values[name]<-value,priority
    interface CSSStyleDeclaration with
        member this.getPropertyValue name=this.Value name
        member this.getPropertyPriority name=this.Priority name
        member this.setProperty(name,value,priority)=
            for leaf in CssPropertyMetadata.leafNames name do this.Seed(leaf,value,defaultArg priority "")
            this.AfterWrite()
        member this.removeProperty name=
            let value=this.Value name
            for leaf in CssPropertyMetadata.leafNames name do values.Remove leaf |> ignore
            this.AfterWrite()
            value
        member _.item index=values.Keys |> Seq.item(int index)
        member _.length=uint values.Count
        member _.cssText with get()=failwith "whole style read" and set _=failwith "whole style write"
        member _.parentRule=Unchecked.defaultof<CSSRule>
        member _.cssRules=Unchecked.defaultof<CSSRuleList>

type private Element(name:string,rect:float*float*float*float) as this =
    let style=Style()
    let inlineStyle=proxy<CSSStyleProperties>(fun m args ->
        let declaration=style:>CSSStyleDeclaration
        match m.Name with
        | "setProperty" ->
            let priority=match box args[2] with :? string as s -> s | :? (string option) as o -> defaultArg o "" | _ -> ""
            for leaf in CssPropertyMetadata.leafNames(unbox<string> args[0]) do style.Seed(leaf,unbox<string> args[1],priority)
            style.AfterWrite()
            null
        | "removeProperty" -> box(declaration.removeProperty(unbox<string> args[0]))
        | "getPropertyValue" -> box(declaration.getPropertyValue(unbox<string> args[0]))
        | "getPropertyPriority" -> box(declaration.getPropertyPriority(unbox<string> args[0]))
        | "get_length" -> box(declaration.length)
        | "item" -> box(declaration.item(unbox<uint32> args[0]))
        | name -> failwith name)
    let children=ResizeArray<Element>()
    let computed=Dictionary<string,string>()
    let attributes=Dictionary<string,string>()
    let classes=HashSet<string>()
    let nativeCalls=ResizeArray<string>()
    let nativeFrames=ResizeArray<Ranvier.Dom.Keyframe array>()
    let nativeOptions=ResizeArray<Ranvier.Dom.KeyframeAnimationOptions>()
    let mutable element=Unchecked.defaultof<HTMLElement>
    let mutable parent:Element option=None
    let mutable bounds=rect
    let mutable borderLeft=0.
    let mutable borderTop=0.
    let mutable text=false
    let tokens=proxy<DOMTokenList>(fun methodInfo args ->
        match methodInfo.Name with
        | "add" -> (unbox<string array> args[0] |> Array.iter(fun name -> classes.Add name |> ignore)); null
        | "remove" -> (unbox<string array> args[0] |> Array.iter(fun name -> classes.Remove name |> ignore)); null
        | "contains" -> box(classes.Contains(unbox args[0]))
        | _ -> failwith("token "+methodInfo.Name))
    do
        for name,value in ["display","block";"visibility","visible";"position","static";"transform","none";"opacity","1";
                          "font-size","16px";"color","rgb(0,0,0)";"background-color","rgba(0,0,0,0)";
                          "border-radius","0px";"border","0px";"filter","none";"clip-path","none"] do
            computed[name]<-value
        element<-proxy<HTMLElement>(fun methodInfo args ->
            let left,top,width,height=bounds
            match methodInfo.Name with
            | "get_firstChild" -> box(children |> Seq.tryHead |> Option.map(fun c -> c.Dom:>Node) |> Option.defaultValue Unchecked.defaultof<Node>)
            | "get_lastChild" -> box(children |> Seq.tryLast |> Option.map(fun c -> c.Dom:>Node) |> Option.defaultValue Unchecked.defaultof<Node>)
            | "get_parentElement" -> box(parent |> Option.map(fun p -> p.Dom:>Ranvier.Dom.Element) |> Option.defaultValue Unchecked.defaultof<Ranvier.Dom.Element>)
            | "get_nextSibling" | "get_previousSibling" ->
                match parent with
                | None -> null
                | Some p ->
                    let index=p.Children.IndexOf this + (if methodInfo.Name="get_nextSibling" then 1 else -1)
                    if index<0 || index>=p.Children.Count then null else box(p.Children[index].Dom:>Node)
            | "get_nodeType" -> box(if text then 3us else 1us)
            | "get_textContent" -> box(if text then "adjacent text" else "")
            | "get_style" -> box inlineStyle
            | "get_ownerDocument" -> null
            | "getAttribute" -> box(match attributes.TryGetValue(unbox args[0]) with true,value -> value | _ -> null)
            | "setAttribute" -> attributes[unbox args[0]]<-unbox args[1];null
            | "removeAttribute" -> attributes.Remove(unbox args[0]) |> ignore;null
            | "animate" ->
                nativeFrames.Add(unbox args[0])
                nativeOptions.Add(unbox args[1])
                let effect=proxy<AnimationEffect>(fun methodInfo args ->
                    match methodInfo.Name with
                    | "updateTiming" ->
                        let timing=unbox<OptionalEffectTiming option> args[0] |> Option.get
                        nativeCalls.Add("ease:"+timing.easing.Value)
                        null
                    | _ -> failwith("native effect "+methodInfo.Name))
                box(proxy<WebAnimation>(fun methodInfo args ->
                    match methodInfo.Name with
                    | "pause" -> nativeCalls.Add "pause";null
                    | "cancel" -> nativeCalls.Add "cancel";null
                    | "commitStyles" -> nativeCalls.Add "commit";null
                    | "get_effect" -> box effect
                    | "set_currentTime" -> nativeCalls.Add("time:"+sprintf "%A" args[0]);null
                    | _ -> failwith("native animation "+methodInfo.Name)))
            | "get_classList" -> box tokens
            | "get_clientLeft" -> box(int borderLeft)
            | "get_clientTop" -> box(int borderTop)
            | "isSameNode" -> box(Object.ReferenceEquals(element,args[0]))
            | "contains" -> box(this.Contains(unbox args[0]))
            | "matches" -> box(unbox<string> args[0]="*" || unbox<string> args[0]=name)
            | "getBoundingClientRect" ->
                box(proxy<DOMRect>(fun m _ ->
                    match m.Name with
                    | "get_left" | "get_x" -> box left
                    | "get_top" | "get_y" -> box top
                    | "get_width" -> box width
                    | "get_height" -> box height
                    | _ -> failwith("rect "+m.Name)))
            | _ -> failwith("element "+methodInfo.Name))
    member _.Name=name
    member _.Dom=element
    member _.Style=style
    member _.Classes=classes
    member _.NativeCalls=nativeCalls
    member _.NativeFrames=nativeFrames
    member _.NativeOptions=nativeOptions
    member _.Children:ResizeArray<Element>=children
    member _.Computed=computed
    member _.Attributes=attributes
    member _.Rect with get()=bounds and set value=bounds<-value
    member _.Parent with get()=parent and set value=parent<-value
    member _.Text with get()=text and set value=text<-value
    member _.Border with set(left,top)=borderLeft<-left;borderTop<-top
    member _.Contains(node:Node)=Object.ReferenceEquals(element,node) || children |> Seq.exists(fun c -> c.Contains node)
    member _.Append(child:Element)=
        child.Parent |> Option.iter(fun p -> p.Children.Remove child |> ignore)
        child.Parent<-Some this
        children.Add child
    member _.ComputedStyle =
        {new CSSStyleDeclaration with
            member _.getPropertyValue name=
                let value=style.Value name
                if value<>"" then value else match computed.TryGetValue name with true,value -> value | _ -> ""
            member _.getPropertyPriority _=""
            member _.setProperty(_,_,_)=failwith "computed write"
            member _.removeProperty _=failwith "computed remove"
            member _.item _=""
            member _.length=0u
            member _.cssText with get()="" and set _=failwith "computed write"
            member _.parentRule=Unchecked.defaultof<CSSRule>
            member _.cssRules=Unchecked.defaultof<CSSRuleList>}

let private setup options =
    let root=Element("root",(10.,20.,300.,300.))
    let child=Element("child",(30.,40.,100.,100.))
    root.Append child
    let all=ResizeArray<Element>([root;child])
    let computed=
        {new ComputedStyleService with
            member _.Get(element,_)=
                (all |> Seq.find(fun e -> Object.ReferenceEquals(e.Dom,element))).ComputedStyle}
    let scheduler=new ManualFrameScheduler()
    let layout=new AutoLayout(root.Dom,computed,scheduler:>FrameScheduler,options=options)
    root,child,all,computed,scheduler,layout

let private linear={LayoutOptions.Default with Duration=(fun _ -> 100.);Ease=id}
let private close expected actual=Expect.floatClose Accuracy.high actual expected "source sample"
let private number (style:Style) name=Double.Parse((style.Value name).Replace("px",""),Globalization.CultureInfo.InvariantCulture)
let private fixturePath() =
    let specified=Environment.GetEnvironmentVariable "RANVIER_ANIME_LAYOUT_FIXTURE"
    if not(String.IsNullOrWhiteSpace specified) then specified
    else
        let root=Environment.GetEnvironmentVariable "RANVIER_ANIME_REFERENCE_ROOT"
        Path.Combine((if String.IsNullOrWhiteSpace root then Path.Combine(AppContext.BaseDirectory,"reference") else root),"layout.json")

let private unownedTestCase=testCase
let private testCase name action =
    unownedTestCase name (fun argument ->
        use graph=new Graph()
        use activation=graph.Activate()
        Api.createRoot(fun owner -> try action argument finally owner.Dispose()))

let tests=testList "native layout transitions" [
    testCase "source movement resize at quarter and three quarters" <| fun _ ->
        let _,child,_,_,_,layout=setup linear
        use layout=layout
        child.Rect<-(60.,80.,160.,70.)
        let animation=layout.Animate()
        animation.Seek(25.,muteCallbacks=true)
        close 115. (number child.Style "width")
        close 92.5 (number child.Style "height")
        Expect.equal (child.Style.Value "translate") "27.5px 30px" "source quarter translation"
        animation.Seek(75.,muteCallbacks=true)
        close 145. (number child.Style "width")
        Expect.equal (child.Style.Value "translate") "42.5px 50px" "source three-quarter translation"

    testCase "record starts from the current visual geometry" <| fun _ ->
        let _,child,_,_,_,layout=setup linear
        use layout=layout
        child.Rect<-(60.,80.,160.,70.)
        layout.Record()
        child.Rect<-(100.,100.,200.,90.)
        let animation=layout.Animate()
        animation.Seek(50.,muteCallbacks=true)
        close 180. (number child.Style "width")

    testCase "parent borders subtract from local coordinates" <| fun _ ->
        let root,child,_,_,_,layout=setup linear
        use layout=layout
        root.Border<-(3.,5.)
        layout.Record()
        child.Rect<-(60.,80.,100.,100.)
        let animation=layout.Animate()
        animation.Seek(50.,muteCallbacks=true)
        Expect.equal (child.Style.Value "translate") "32px 35px" "source border-relative midpoint"

    testCase "fixed roots use viewport coordinate mode" <| fun _ ->
        let root,child,_,_,_,layout=setup linear
        use layout=layout
        root.Computed["position"]<-"fixed"
        layout.Record()
        child.Rect<-(60.,80.,100.,100.)
        let animation=layout.Animate()
        animation.Seek(50.,muteCallbacks=true)
        Expect.equal (child.Style.Value "position") "fixed" "source child positioning"

    testCase "new elements fade from typed entry declarations" <| fun _ ->
        let root,_,all,_,_,layout=setup linear
        use layout=layout
        let node=Element("new",(40.,50.,30.,20.))
        root.Append node
        all.Add node
        let animation=layout.Animate()
        animation.Seek(25.,muteCallbacks=true)
        Expect.equal layout.Entering [|node.Dom|] "newly selected node"
        close 0.25 (number node.Style "opacity")

    testCase "hidden elements fade out while retaining old geometry" <| fun _ ->
        let _,child,_,_,_,layout=setup linear
        use layout=layout
        child.Computed["display"]<-"none"
        let animation=layout.Animate()
        animation.Seek(25.,muteCallbacks=true)
        Expect.equal layout.Leaving [|child.Dom|] "leaving target"
        close 0.75 (number child.Style "opacity")
        close 100. (number child.Style "width")
        Expect.equal (child.Style.Value "display") "block" "visible while leaving"
        layout.Revert()
        Expect.equal (child.Style.Value "display") "" "underlying hidden display resumes"

    testCase "inline neighbors retain text flow" <| fun _ ->
        let root,child,all,_,_,layout=setup linear
        use layout=layout
        let text=Element("text",(0.,0.,0.,0.))
        text.Text<-true
        text.Parent<-Some root
        root.Children.Insert(0,text)
        all.Add text
        layout.Record()
        child.Rect<-(60.,80.,160.,70.)
        let animation=layout.Animate()
        animation.Seek(25.,muteCallbacks=true)
        Expect.equal (child.Style.Value "position",child.Style.Value "translate") ("","") "flow retained"
        close 115. (number child.Style "width")

    testCase "concurrent unrelated and same-property writes survive revert" <| fun _ ->
        let _,child,_,_,_,layout=setup linear
        use layout=layout
        child.Style.Seed("width","17px","important")
        child.Style.Seed("color","rgb(10,20,30)","important")
        layout.Record()
        child.Rect<-(60.,80.,160.,70.)
        let animation=layout.Animate()
        animation.Seek(25.,muteCallbacks=true)
        child.Style.Seed("height","999px","important")
        child.Style.Seed("background","red","")
        layout.Revert()
        Expect.equal (child.Style.Value "width",child.Style.Priority "width") ("17px","important") "original restored"
        Expect.equal (child.Style.Value "height",child.Style.Priority "height") ("999px","important") "concurrent owned edit retained"
        Expect.equal (child.Style.Value "background") "red" "unrelated declaration retained"

    testCase "completion releases temporary styles and class" <| fun _ ->
        let root,child,_,_,_,layout=setup linear
        use layout=layout
        child.Rect<-(60.,80.,160.,70.)
        let animation=layout.Animate()
        Expect.isTrue (root.Classes.Contains "is-animated") "active"
        animation.Complete()
        Expect.equal (child.Style.Value "width",child.Style.Value "translate") ("","") "layout CSS resumes"
        Expect.isFalse (root.Classes.Contains "is-animated") "completed"

    testCase "disposed layout rejects captures and cannot write on later frames" <| fun _ ->
        let _,child,_,_,scheduler,layout=setup linear
        child.Rect<-(60.,80.,160.,70.)
        layout.Animate() |> ignore
        (layout:>IDisposable).Dispose()
        child.Style.Seed("width","555px","")
        scheduler.Advance 1000.
        scheduler.RunFrame()
        Expect.equal (child.Style.Value "width") "555px" "cancelled frame"
        Expect.throwsT<ObjectDisposedException> (fun () -> layout.Record()) "terminal lifetime"

    testCase "mutation failures restore styles and preserve exception identity" <| fun _ ->
        let _,_,_,_,_,layout=setup linear
        use layout=layout
        let error=InvalidOperationException "mutation fault"
        let mutable caught:exn option=None
        try layout.Update(fun _ -> raise error) |> ignore with failure -> caught<-Some failure
        Expect.isTrue (caught |> Option.exists(fun x -> Object.ReferenceEquals(x,error))) "primary exception"

    testCase "source oracle provenance and implemented move samples agree" <| fun _ ->
        use document=JsonDocument.Parse(File.ReadAllText(fixturePath()))
        Expect.equal (document.RootElement.GetProperty("upstreamCommit").GetString())
            "2c9cf8ea00329f6768c7d7902252ed977d75ce42" "clean pinned source"
        let fixture=document.RootElement.GetProperty("fixtures").EnumerateArray() |> Seq.find(fun f -> f.GetProperty("name").GetString()="moveResize")
        let _,child,_,_,_,layout=setup linear
        use layout=layout
        child.Rect<-(60.,80.,160.,70.)
        let animation=layout.Animate()
        for sample in fixture.GetProperty("samples").EnumerateArray() do
            animation.Seek(sample.GetProperty("time").GetDouble(),muteCallbacks=true)
            let value=sample.GetProperty("values").EnumerateArray() |> Seq.find(fun v -> v.GetProperty("name").GetString()="child")
            close (Double.Parse(value.GetProperty("width").GetString().Replace("px",""),Globalization.CultureInfo.InvariantCulture)) (number child.Style "width")
            Expect.equal (child.Style.Value "translate") (value.GetProperty("translate").GetString()) "source translation"

    testCase "owner disposal cancels the layout and subsequent frames" <| fun _ ->
        use graph=new Graph()
        use activation=graph.Activate()
        let _,child,_,_,scheduler,layout=setup linear
        let owner=Api.createRoot(fun owner -> layout.Own() |> ignore;owner)
        child.Rect<-(60.,80.,160.,70.)
        layout.Animate() |> ignore
        owner.Dispose()
        child.Style.Seed("width","777px","")
        scheduler.Advance 1000.
        scheduler.RunFrame()
        Expect.equal (child.Style.Value "width") "777px" "owner cancelled all writes"

    testCase "timing callbacks receive exact selected group count" <| fun _ ->
        let contexts=ResizeArray<LayoutContext>()
        let configured={linear with Duration=(fun context -> contexts.Add context;100.)}
        let _,child,_,_,_,layout=setup configured
        use layout=layout
        child.Rect<-(60.,80.,160.,70.)
        layout.Animate() |> ignore
        Expect.equal (contexts |> Seq.map(fun c -> c.Index,c.Count) |> Seq.toArray) [|0u,1u;0u,1u|] "root shares first child context"

    testCase "detached elements are absent from subsequent transitions" <| fun _ ->
        let root,child,_,_,_,layout=setup linear
        use layout=layout
        root.Children.Remove child |> ignore
        child.Parent<-None
        layout.Animate() |> ignore
        Expect.isEmpty layout.Leaving "source detached behavior"
        Expect.equal (child.Style.Value "position",child.Style.Value "width") ("","") "no writes to detached node"

    testCase "empty layout change completes immediately" <| fun _ ->
        let root,_,_,_,_,layout=setup linear
        use layout=layout
        let animation=layout.Animate()
        Expect.isTrue animation.Completed "no effects"
        Expect.isFalse (root.Classes.Contains "is-animated") "idle class"

    testCase "native transforms preserve exact endpoints and overshoot progress" <| fun _ ->
        let configured={linear with Ease=(fun t -> if t=0. then 0. else 1.25)}
        let _,child,_,_,_,layout=setup configured
        use layout=layout
        child.Computed["transform"]<-"matrix(1,0,0,1,110,0)"
        let animation=layout.Animate()
        animation.Seek(25.,muteCallbacks=true)
        Expect.equal child.NativeFrames.Count 1 "one native transform effect"
        let frames=child.NativeFrames[0]
        Expect.equal (frames[0].properties[0].value,frames[1].properties[0].value)
            ("none","matrix(1,0,0,1,110,0)") "native transform endpoints"
        let expected="linear("+String.concat ", " ([|"0"|] |> Array.append <| Array.create 100 "1.25")+")"
        Expect.equal child.NativeOptions[0].easing expected "source 101 rounded samples retain overshoot"
        layout.Revert()
        Expect.contains child.NativeCalls "cancel" "native effect cleanup"

    testCase "pause restores leaving visibility and emits completion before pause" <| fun _ ->
        let events=ResizeArray<string>()
        let callbacks={LayoutCallbacks.Default with OnComplete=(fun _ -> events.Add "complete");OnPause=(fun _ -> events.Add "pause")}
        let root,child,_,_,_,layout=setup {linear with Callbacks=callbacks}
        use layout=layout
        child.Computed["display"]<-"none"
        let animation=layout.Animate()
        animation.Seek(25.,muteCallbacks=true)
        animation.Pause()
        Expect.sequenceEqual events ["complete";"pause"] "source pause sequence"
        Expect.equal (child.Style.Value "display",child.Style.Value "visibility") ("","") "post-mutation visibility"
        Expect.isFalse (root.Classes.Contains "is-animated") "paused marker"

    testCase "linked completion retains layout declarations" <| fun _ ->
        let events=ResizeArray<string>()
        let callbacks={LayoutCallbacks.Default with OnComplete=(fun _ -> events.Add "complete")}
        let root,child,_,_,_,layout=setup {linear with Callbacks=callbacks;RetainLayoutOnComplete=true}
        use layout=layout
        child.Rect<-(60.,80.,160.,70.)
        let animation=layout.Animate()
        animation.Seek 100.
        Expect.sequenceEqual events ["complete"] "linked completion"
        Expect.equal (child.Style.Value "width") "160px" "retained controlled layout"
        Expect.isTrue (root.Classes.Contains "is-animated") "linked marker remains"

    testCase "autoplay false keeps the initialized layout paused" <| fun _ ->
        let _,child,_,_,scheduler,layout=setup {linear with Autoplay=false}
        use layout=layout
        child.Rect<-(60.,80.,160.,70.)
        let animation=layout.Animate()
        Expect.isFalse animation.Playing "paused controller"
        scheduler.Advance 50.
        scheduler.RunFrame()
        close 100. (number child.Style "width")
        animation.Seek(25.,muteCallbacks=true)
        close 115. (number child.Style "width")

    testCase "playback repeats preserve one shared layout clock" <| fun _ ->
        let configured={linear with Autoplay=false;Playback={PlaybackOptions.Default with Repeat=PlaybackRepeat.Count 1u;Alternate=true}}
        let _,child,_,_,_,layout=setup configured
        use layout=layout
        child.Rect<-(60.,80.,160.,70.)
        let animation=layout.Animate()
        close 200. animation.Duration
        animation.Seek(150.,muteCallbacks=true)
        close 130. (number child.Style "width")


    testCase "visible outside twin supplies hidden in-root measurements" <| fun _ ->
        let root,child,all,computed,scheduler,initial=setup linear
        (initial:>IDisposable).Dispose()
        child.Attributes["data-layout-id"]<-"shared"
        child.Computed["display"]<-"none"
        let outside=Element("outside",(100.,120.,100.,100.))
        outside.Attributes["data-layout-id"]<-"shared"
        all.Add outside
        use layout=new AutoLayout(root.Dom,computed,scheduler:>FrameScheduler,
                                  options={linear with Targets=Some [|child.Dom;outside.Dom|]})
        outside.Rect<-(160.,180.,160.,70.)
        let animation=layout.Animate()
        animation.Seek(25.,muteCallbacks=true)
        close 115. (number child.Style "width")
        Expect.equal (child.Style.Value "translate") "105px 115px" "source external twin local geometry"
        Expect.equal (child.Style.Value "visibility",outside.Style.Value "visibility") ("visible","hidden") "visibility swap"
        Expect.equal layout.Animating [|child.Dom|] "in-root render target"
        layout.Revert()
        Expect.equal (child.Style.Value "visibility",outside.Style.Value "visibility") ("","") "both inline states restored"
        Expect.equal child.Attributes["data-layout-id"] "shared" "caller identity retained"

    testCase "native easing serialization matches the actual source request" <| fun _ ->
        use document=JsonDocument.Parse(File.ReadAllText(fixturePath()))
        let fixture=document.RootElement.GetProperty("fixtures").EnumerateArray()
                    |> Seq.find(fun f -> f.GetProperty("name").GetString()="nativeTransformOvershootRequest")
        let request=fixture.GetProperty("nativeRequests").EnumerateArray()
                    |> Seq.find(fun r -> r.GetProperty("keyframes").TryGetProperty("transform") |> fst)
        let configured={linear with Ease=(fun t -> if t=0. then 0. else 1.25)}
        let _,child,_,_,_,layout=setup configured
        use layout=layout
        child.Computed["transform"]<-"matrix(1,0,0,1,10,0)"
        layout.Record()
        child.Computed["transform"]<-"matrix(1,0,0,1,110,0)"
        layout.Animate() |> ignore
        Expect.equal child.NativeOptions[0].easing (request.GetProperty("options").GetProperty("easing").GetString()) "actual upstream timing serialization"

    testCase "pause callback order matches actual source capture" <| fun _ ->
        use document=JsonDocument.Parse(File.ReadAllText(fixturePath()))
        let fixture=document.RootElement.GetProperty("fixtures").EnumerateArray()
                    |> Seq.find(fun f -> f.GetProperty("name").GetString()="moveResize")
        let expected=fixture.GetProperty("pause").GetProperty("events").EnumerateArray()
                     |> Seq.map(fun e -> e.GetProperty("event").GetString()) |> Seq.toArray
        let events=ResizeArray<string>()
        let callbacks={LayoutCallbacks.Default with OnComplete=(fun _ -> events.Add "Complete");OnPause=(fun _ -> events.Add "Pause")}
        let _,child,_,_,_,layout=setup {linear with Callbacks=callbacks;Autoplay=false}
        use layout=layout
        child.Rect<-(60.,80.,160.,70.)
        let animation=layout.Animate()
        animation.Seek(99.,muteCallbacks=true)
        animation.Resume()
        animation.Pause()
        Expect.sequenceEqual events expected "captured source event order"

    testCase "asymmetric border leaves and concurrent edits survive revert" <| fun _ ->
        let _,child,_,_,_,layout=setup linear
        use layout=layout
        child.Style.Seed("border-left-width","17px","important")
        child.Style.Seed("border-right-width","8px","")
        layout.Record()
        child.Computed["border"]<-"10px"
        let animation=layout.Animate()
        animation.Seek(25.,muteCallbacks=true)
        child.Style.Seed("border-top-width","99px","important")
        layout.Revert()
        Expect.equal (child.Style.Value "border-left-width",child.Style.Priority "border-left-width") ("17px","important") "asymmetric authored left"
        Expect.equal (child.Style.Value "border-right-width",child.Style.Priority "border-right-width") ("8px","") "asymmetric authored right"
        Expect.equal (child.Style.Value "border-top-width",child.Style.Priority "border-top-width") ("99px","important") "concurrent leaf"

    testCase "measurement restores independently authored transition leaves" <| fun _ ->
        let _,child,_,_,_,initial=setup linear
        use initial=initial
        child.Style.Seed("transition-duration","1s","important")
        child.Style.Seed("transition-timing-function","ease","")
        initial.Record()
        Expect.equal (child.Style.Value "transition-duration",child.Style.Priority "transition-duration") ("1s","important") "duration priority"
        Expect.equal (child.Style.Value "transition-timing-function",child.Style.Priority "transition-timing-function") ("ease","") "independent easing"

    testCase "ambient scope collects only the outer layout restoration" <| fun _ ->
        use graph=new Graph()
        use activation=graph.Activate()
        let owner=Api.createRoot id
        let restores=ResizeArray<unit -> unit>()
        let _,child,_,_,_,layout=
            ScopeRegistration.runOwned restores.Add (fun () -> owner) (fun () -> true) (fun () ->
                let root,child,all,computed,scheduler,layout=setup linear
                child.Rect<-(60.,80.,160.,70.)
                layout.Animate() |> ignore
                root,child,all,computed,scheduler,layout)
        use layout=layout
        Expect.equal restores.Count 1 "one outer restoration"
        restores[0]()
        Expect.equal (child.Style.Value "width") "" "outer style restoration"
        owner.Dispose()

    testCase "explicit selector scope resolves an external matching twin" <| fun _ ->
        let root,child,all,computed,scheduler,initial=setup linear
        (initial:>IDisposable).Dispose()
        child.Attributes["data-layout-id"]<-"shared"
        child.Computed["display"]<-"none"
        let outside=Element("outside",(100.,120.,100.,100.))
        outside.Attributes["data-layout-id"]<-"shared"
        all.Add outside
        let selection=proxy<ParentNode>(fun methodInfo _ ->
            match methodInfo.Name with
            | "querySelectorAll" ->
                box(proxy<NodeList>(fun methodInfo args ->
                    match methodInfo.Name with
                    | "get_length" -> box 1u
                    | "item" -> if unbox<uint> args[0]=0u then box(outside.Dom:>Node) else null
                    | _ -> failwith methodInfo.Name))
            | _ -> failwith methodInfo.Name)
        use layout=new AutoLayout(root.Dom,computed,scheduler:>FrameScheduler,
                                  options={linear with Children="outside";SelectionRoot=Some selection})
        outside.Rect<-(160.,180.,160.,70.)
        let animation=layout.Animate()
        animation.Seek(25.,muteCallbacks=true)
        Expect.equal layout.Animating [|child.Dom|] "matched external identity"
        close 115. (number child.Style "width")

    testCase "per-update timing overrides preserve the captured selection" <| fun _ ->
        let _,child,_,_,_,layout=setup linear
        use layout=layout
        let animation=layout.Update((fun _ -> child.Rect<-(60.,80.,160.,70.)),animationOptions={linear with Duration=(fun _ -> 200.)})
        close 200. animation.Duration
        animation.Seek(50.,muteCallbacks=true)
        close 115. (number child.Style "width")

    testCase "playback easing remaps layout time and retains controller time" <| fun _ ->
        let _,child,_,_,_,layout=setup {linear with PlaybackEase=Some(fun time -> time*time)}
        use layout=layout
        child.Rect<-(60.,80.,160.,70.)
        let animation=layout.Animate()
        animation.Seek(50.,muteCallbacks=true)
        close 50. animation.CurrentTime
        close 115. (number child.Style "width")

    testCase "typed boundary providers resolve before temporary layout writes" <| fun _ ->
        let contexts=ResizeArray<LayoutContext>()
        let effect=
            {LayoutEffect.Default with
                PropertiesBy=Some(fun context ->
                    contexts.Add context
                    Expect.equal ((context.Element:>ElementCSSInlineStyle).style.getPropertyValue "width") "" "provider sees post-mutation DOM"
                    [|Ranvier.Css.Declaration.create "opacity" "0.2"|])
                DurationBy=Some(fun _ -> 200.);DelayBy=Some(fun _ -> 20.)}
        let _,child,_,_,_,layout=setup {linear with EnterFrom=effect}
        use layout=layout
        child.Computed["display"]<-"none"
        layout.Record()
        child.Computed["display"]<-"block"
        let animation=layout.Animate()
        animation.Seek(70.,muteCallbacks=true)
        close 0.4 (number child.Style "opacity")
        Expect.equal (contexts |> Seq.map(fun context -> context.Index,context.Count) |> Seq.toArray) [|0u,1u|] "entry context"

    testCase "non-target swaps follow the eased timeline and callback mute" <| fun _ ->
        let root,child,all,computed,scheduler,initial=setup linear
        (initial:>IDisposable).Dispose()
        let nested=Element("unselected",(35.,45.,20.,20.))
        child.Append nested
        all.Add nested
        use layout=new AutoLayout(root.Dom,computed,scheduler:>FrameScheduler,
                                  options={linear with Children="child";Targets=Some [|child.Dom|];Autoplay=false;PlaybackEase=Some(fun time -> time*time)})
        child.Rect<-(30.,40.,200.,200.)
        nested.Rect<-(35.,45.,50.,50.)
        let animation=layout.Animate()
        animation.Seek 50.
        close 20. (number nested.Style "width")
        close 25. animation.IterationCurrentTime
        animation.Seek(75.,muteCallbacks=true)
        close 20. (number nested.Style "width")
        animation.Seek 75.
        close 50. (number nested.Style "width")
        close 56.25 animation.IterationCurrentTime

    testCase "completion restores CSS transition leaves on the next frame" <| fun _ ->
        let _,child,_,_,scheduler,layout=setup linear
        use layout=layout
        child.Style.Seed("transition-duration","1s","important")
        layout.Record()
        child.Rect<-(60.,80.,160.,70.)
        let animation=layout.Animate()
        animation.Complete()
        Expect.notEqual (child.Style.Value "transition-duration") "1s" "transition remains muted for completion commit"
        scheduler.RunFrame()
        Expect.equal (child.Style.Value "transition-duration",child.Style.Priority "transition-duration") ("1s","important") "next-frame transition restore"




]

