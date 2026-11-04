module Ranvier.Anime.Tests.SvgReentryTests

open System
open System.Reflection
open System.Runtime.ExceptionServices
open Expecto
open Ranvier
open Ranvier.Dom
open Ranvier.Anime
open Ranvier.Anime.Tests.SvgTests

let private computed cap vector =
    proxy<CSSStyleDeclaration>(fun methodInfo args ->
        if methodInfo.Name="getPropertyValue" then box(if string args[0]="stroke-linecap" then cap else vector)
        else null)

let private intercept (element:SVGGeometryElement) before after =
    proxy<SVGGeometryElement>(fun methodInfo args ->
        before methodInfo.Name args
        let result =
            try methodInfo.Invoke(element,args)
            with :? TargetInvocationException as error when not(isNull error.InnerException) ->
                ExceptionDispatchInfo.Capture(error.InnerException).Throw()
                null
        after methodInfo.Name args
        result)

let private noHook (_:string) (_:obj array)=()

let svgReentryTests=testList "SVG scope owner and native-call reentry regressions" [
    testCase "dynamic persistent drawable and motion survive ambient epoch disposal" <| fun _ ->
        use graph=new Graph()
        use active=graph.Activate()
        use scope=new Scope()
        let path,_,_=geometry "path" 10. "line" Unchecked.defaultof<SVGMatrix>
        let resources=ResizeArray<Svg.Drawable * NumericProperty * Owner>()
        let mutable applied=0
        scope.Add(fun current->
            current.AddOnce(fun _->None) |> ignore
            let ambient=Api.getOwner()
            let drawable=Svg.createDrawable path (computed "round" "none") 0. 0.
            let motion=SvgProperties.motion path true 0. (fun _->applied<-applied+1)
            resources.Add(drawable,motion,ambient)
            None) |> ignore
        let drawable,motion,ambient=resources[0]
        scope.Refresh() |> ignore
        Expect.isTrue ambient.IsDisposed "ambient regular owner ended"
        Expect.isFalse drawable.IsReverted "actual selected persistent owner retained"
        motion.Value<-5.
        Expect.equal applied 1 "retained motion applies"
        scope.Revert()
        Expect.isTrue drawable.IsReverted "final scope owns restoration"
        motion.Value<-7.
        Expect.equal applied 1 "final disposal ends motion"

    testCase "drawable registration precedes normalization and preserves reentrant replacement" <| fun _ ->
        use graph=new Graph()
        use active=graph.Activate()
        use scope=new Scope()
        let raw,attrs,_=geometry "path" 10. "line" Unchecked.defaultof<SVGMatrix>
        let mutable trigger=true
        let path=intercept raw noHook (fun name args->
            if trigger && name="setAttribute" && string args[0]="pathLength" then
                trigger<-false
                scope.Revert()
                attrs["pathLength"]<-"17"
                attrs["draw"]<-"replacement")
        Expect.throwsT<ObjectDisposedException> (fun()->
            scope.Execute(fun _->Svg.createDrawable path (computed "round" "none") 0.2 0.8 |> ignore) |> ignore) "stale constructor rejected"
        Expect.equal attrs["pathLength"] "17" "replacement path length"
        Expect.equal attrs["draw"] "replacement" "no late initialization/rollback overwrite"

    testCase "drawable draw stops after refresh reconstructs a newer scope generation" <| fun _ ->
        use graph=new Graph()
        use active=graph.Activate()
        use scope=new Scope()
        let raw,attrs,_=geometry "path" 10. "line" Unchecked.defaultof<SVGMatrix>
        let mutable trigger=false
        let path=intercept raw noHook (fun name args->
            if trigger && name="setAttribute" && string args[0]="stroke-dashoffset" then
                trigger<-false
                scope.Refresh() |> ignore)
        let mutable latest:Svg.Drawable option=None
        scope.Add(fun _->latest<-Some(Svg.createDrawable path (computed "round" "none") 0.2 0.8);None) |> ignore
        let old=latest.Value
        trigger<-true
        old.Draw(0.7,0.9)
        Expect.equal attrs["draw"] "0.2 0.8" "new generation wins"
        Expect.isTrue old.IsReverted "old lifetime ended"
        Expect.isFalse latest.Value.IsReverted "replacement is live"

    testCase "drawable restoration stops after refresh installs a replacement" <| fun _ ->
        use graph=new Graph()
        use active=graph.Activate()
        use scope=new Scope()
        let raw,attrs,_=geometry "path" 10. "line" Unchecked.defaultof<SVGMatrix>
        let mutable trigger=false
        let path=intercept raw noHook (fun name args->
            if trigger && name="setAttribute" && string args[0]="pathLength" then
                trigger<-false
                scope.Refresh() |> ignore)
        let mutable constructions=0
        scope.Add(fun _->
            constructions<-constructions+1
            let start,finish=if constructions=1 then 0.2,0.8 else 0.3,0.6
            Svg.createDrawable path (computed "round" "none") start finish |> ignore
            None) |> ignore
        scope.Execute(fun _->
            use extra=Svg.createDrawable path (computed "round" "none") 0. 1.
            extra.Draw(0.5,0.5)
            trigger<-true
            extra.Revert()) |> ignore
        Expect.equal attrs["draw"] "0.3 0.6" "replacement draw survives remaining restoration"

    testCase "drawable initialization rolls back all snapshots and preserves first fault" <| fun _ ->
        use graph=new Graph()
        use active=graph.Activate()
        let raw,attrs,styles=geometry "path" 10. "line" Unchecked.defaultof<SVGMatrix>
        raw.style.setProperty("stroke-linecap","square",priority="important")
        attrs["stroke-dashoffset"]<-"7"
        let mutable failed=false
        let path=intercept raw (fun name args->
            if not failed && name="setAttribute" && string args[0]="stroke-dashoffset" then
                failed<-true
                raise(InvalidOperationException "first SVG write failure")
            if failed && name="removeAttribute" && string args[0]="draw" then
                raise(ApplicationException "secondary restoration failure")) noHook
        let message=Expect.throwsC (fun()->Svg.createDrawable path (computed "round" "none") 0. 0. |> ignore) (fun error->error.Message)
        Expect.equal message "first SVG write failure" "initial fault preserved"
        Expect.isFalse (attrs.ContainsKey "pathLength") "normalization rolled back"
        Expect.equal attrs["stroke-dashoffset"] "7" "later restoration attempted"
        Expect.equal styles["stroke-linecap"] "square" "shared CSS snapshot restored"
        Expect.equal (raw.style.getPropertyPriority "stroke-linecap") "important" "priority restored"

    testCase "motion sampling reentry cannot apply after scope revert" <| fun _ ->
        use graph=new Graph()
        use active=graph.Activate()
        use scope=new Scope()
        let raw,_,_=geometry "path" 10. "line" Unchecked.defaultof<SVGMatrix>
        let mutable trigger=false
        let mutable pointReads=0
        let path=intercept raw noHook (fun name _->
            if name="getPointAtLength" then
                pointReads<-pointReads+1
                if trigger then trigger<-false;scope.Revert())
        let mutable applied=0
        let motion=scope.Execute(fun _->SvgProperties.motion path true 0. (fun _->applied<-applied+1))
        trigger<-true
        motion.Value<-5.
        Expect.equal applied 0 "no stale typed setter"
        Expect.equal pointReads 1 "no later point reads"
        Expect.equal motion.Value 0. "distance commit suppressed"

    testCase "motion capture rejects CTM reentry before returning an accessor" <| fun _ ->
        use graph=new Graph()
        use active=graph.Activate()
        use scope=new Scope()
        let raw,_,_=geometry "path" 10. "line" Unchecked.defaultof<SVGMatrix>
        let path=intercept raw noHook (fun name _->if name="getCTM" then scope.Revert())
        Expect.throwsT<ObjectDisposedException> (fun()->
            scope.Execute(fun _->SvgProperties.motion path false 0. ignore |> ignore) |> ignore) "CTM superseded constructor"

    testCase "motion callback first fault ends later effects" <| fun _ ->
        let path,_,_=geometry "path" 10. "line" Unchecked.defaultof<SVGMatrix>
        let mutable calls=0
        let motion=SvgProperties.motion path true 0. (fun _->calls<-calls+1;failwith "motion setter")
        Expect.throws (fun()->motion.Value<-1.) "first callback fault"
        motion.Value<-2.
        Expect.equal calls 1 "fault is terminal"
        Expect.equal motion.Value 0. "failed effect did not commit distance"

    testCase "morph registers outer snapshot before restoring previous tween text" <| fun _ ->
        use graph=new Graph()
        use active=graph.Activate()
        use scope=new Scope()
        let raw,attrs,_=geometry "path" 10. "line" Unchecked.defaultof<SVGMatrix>
        let destination,_,_=geometry "path" 20. "line" Unchecked.defaultof<SVGMatrix>
        let mutable trigger=true
        let mutable points=0
        let source=intercept raw noHook (fun name args->
            if name="getPointAtLength" then points<-points+1
            if trigger && name="setAttribute" && string args[0]="d" then
                trigger<-false
                scope.Revert()
                attrs["d"]<-"replacement")
        Expect.throwsT<ObjectDisposedException> (fun()->
            scope.Execute(fun _->SvgProperties.morph source destination 0.1 (Some "M9 8 L7 6") |> ignore) |> ignore) "stale morph constructor"
        Expect.equal attrs["d"] "replacement" "no late endpoint/rollback overwrite"
        Expect.equal points 0 "geometry not sampled after reentry"

    testCase "morph constructor fault restores previous text immediately" <| fun _ ->
        use graph=new Graph()
        use active=graph.Activate()
        use scope=new Scope()
        let source,attrs,_=geometry "path" 10. "line" Unchecked.defaultof<SVGMatrix>
        let raw,_,_=geometry "path" 20. "line" Unchecked.defaultof<SVGMatrix>
        let destination=intercept raw (fun name _->if name="getTotalLength" then failwith "destination geometry failure") noHook
        Expect.throws (fun()->
            scope.Execute(fun _->SvgProperties.morph source destination 0.1 (Some "M9 8 L7 6") |> ignore) |> ignore) "geometry fault"
        Expect.equal attrs["d"] "M1 2 L3 4" "constructor rollback before scope teardown"

    testCase "morph hidden endpoints produce only one outer restoration registration" <| fun _ ->
        use graph=new Graph()
        use active=graph.Activate()
        use owner=new Owner()
        let source,attrs,_=geometry "path" 10. "line" Unchecked.defaultof<SVGMatrix>
        let destination,_,_=geometry "path" 20. "line" Unchecked.defaultof<SVGMatrix>
        let registered=ResizeArray<unit -> unit>()
        let morph=ScopeRegistration.runOwned registered.Add (fun()->owner) (fun()->true) (fun()->
            SvgProperties.morph source destination 0.1 (Some "M9 8 L7 6"))
        Expect.equal registered.Count 1 "outer only"
        morph.Progress.Value<-0.5
        registered[0]()
        Expect.equal attrs["d"] "M1 2 L3 4" "immutable original"
        morph.Progress.Value<-1.
        Expect.equal attrs["d"] "M1 2 L3 4" "restored accessor closed"
]
