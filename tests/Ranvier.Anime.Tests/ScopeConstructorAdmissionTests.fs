module Ranvier.Anime.Tests.ScopeConstructorAdmissionTests

open System
open Expecto
open Ranvier.Dom
open Ranvier.Anime

type private Target() =
    let objects=ResizeArray<EventListenerObject>()
    member val BeforeAdd: unit -> unit = ignore with get,set
    member _.Count=objects.Count
    interface EventTarget with
        member _.addEventListener(_:string,_:Event -> unit,_:bool option)=()
        member this.addEventListener(_:string,callback:EventListenerObject,_:bool option)=this.BeforeAdd();objects.Add callback
        member _.addEventListener(_:string,_:Event -> unit,_:AddEventListenerOptions)=()
        member this.addEventListener(_:string,callback:EventListenerObject,_:AddEventListenerOptions)=this.BeforeAdd();objects.Add callback
        member _.removeEventListener(_:string,_:Event -> unit,_:bool option)=()
        member _.removeEventListener(_:string,callback:EventListenerObject,_:bool option)=objects.Remove callback |> ignore
        member _.removeEventListener(_:string,_:Event -> unit,_:EventListenerOptions)=()
        member _.removeEventListener(_:string,callback:EventListenerObject,_:EventListenerOptions)=objects.Remove callback |> ignore
        member _.dispatchEvent _=true

let scopeConstructorAdmissionTests = testList "scope constructor admission" [
    testCase "stale once constructor cannot create a scope after refresh while its owner remains alive" <| fun _ ->
        use graph=new Ranvier.Graph()
        use active=graph.Activate()
        use outer=new Scope()
        let mutable rejected=false
        let mutable ownerAlive=false
        let mutable late:Scope option=None
        outer.Add(fun scope ->
            scope.AddOnce(fun _ ->
                scope.Refresh() |> ignore
                ownerAlive<-not(Ranvier.Api.getOwner().IsDisposed)
                try late<-Some(new Scope())
                with :? ObjectDisposedException -> rejected<-true
                None) |> ignore
            None) |> ignore
        late |> Option.iter _.Revert()
        Expect.isTrue ownerAlive "persistent owner remained alive"
        Expect.isTrue rejected "generation admission rejects before allocating a new scope lifetime"

    testCase "stale constructor rejects before invoking a media provider" <| fun _ ->
        use graph=new Ranvier.Graph()
        use active=graph.Activate()
        use outer=new Scope()
        let mutable calls=0
        let mutable late:Scope option=None
        let provider:MediaQueryProvider={MatchMedia=(fun _ -> calls<-calls+1;failwith "provider must not run");Preferences=None}
        outer.Add(fun scope ->
            scope.AddOnce(fun _ ->
                scope.Refresh() |> ignore
                try late<-Some(new Scope(media=provider,mediaQueries=[("all","all")])) with _ -> ()
                None) |> ignore
            None) |> ignore
        late |> Option.iter _.Revert()
        Expect.equal calls 0 "native lookup excluded by admission"

    testCase "media query returned after refresh is disposed before listener adoption" <| fun _ ->
        use graph=new Ranvier.Graph()
        use active=graph.Activate()
        use outer=new Scope()
        let target=Target()
        let mutable disposals=0
        let query={new ViewportMediaQuery with
                    member _.Media="all"
                    member _.Matches=true
                    member _.Target=target :> EventTarget
                    member _.Dispose()=disposals<-disposals+1}
        let provider:MediaQueryProvider={MatchMedia=(fun _ -> outer.Refresh() |> ignore;query);Preferences=None}
        let mutable late:Scope option=None
        let mutable rejected=false
        outer.Add(fun scope ->
            scope.AddOnce(fun _ ->
                try late<-Some(new Scope(media=provider,mediaQueries=[("all","all")]))
                with :? ObjectDisposedException -> rejected<-true
                None) |> ignore
            None) |> ignore
        let leakedListeners=target.Count
        let disposedBeforeCleanup=disposals
        late |> Option.iter _.Revert()
        Expect.isTrue rejected "interrupted construction does not escape"
        Expect.equal leakedListeners 0 "late listener never adopted"
        Expect.equal disposedBeforeCleanup 1 "late query disposed exactly once"

    testCase "media query returned after parent owner disposal is released" <| fun _ ->
        use graph=new Ranvier.Graph()
        use active=graph.Activate()
        let target=Target()
        let mutable disposals=0
        let query={new ViewportMediaQuery with
                    member _.Media="all"
                    member _.Matches=true
                    member _.Target=target :> EventTarget
                    member _.Dispose()=disposals<-disposals+1}
        let parent,outer=Ranvier.Api.createRoot(fun owner -> owner,new Scope())
        let provider:MediaQueryProvider={MatchMedia=(fun _ -> parent.Dispose();query);Preferences=None}
        let mutable late:Scope option=None
        let mutable rejected=false
        try
            outer.Execute(fun _ ->
                try late<-Some(new Scope(media=provider,mediaQueries=[("all","all")]))
                with :? ObjectDisposedException -> rejected<-true)
            Expect.isTrue rejected "parent closure excludes returned construction"
            Expect.equal target.Count 0 "no late listener"
            Expect.equal disposals 1 "query released"
        finally
            late |> Option.iter _.Revert()
            parent.Dispose()

    for phase in ["target";"subscribe"] do
        testCase ("media "+phase+" lookup reentry releases the query and any late listener") <| fun _ ->
            use graph=new Ranvier.Graph()
            use active=graph.Activate()
            use outer=new Scope()
            let target=Target()
            let mutable disposals=0
            let mutable armed=true
            let interrupt () =
                if armed then
                    armed<-false
                    outer.Refresh() |> ignore
            let query={new ViewportMediaQuery with
                        member _.Media="all"
                        member _.Matches=true
                        member _.Target=
                            if phase="target" then interrupt()
                            target :> EventTarget
                        member _.Dispose()=disposals<-disposals+1}
            if phase="subscribe" then target.BeforeAdd<-interrupt
            let provider:MediaQueryProvider={MatchMedia=(fun _ -> query);Preferences=None}
            let mutable late:Scope option=None
            let mutable rejected=false
            outer.Add(fun scope ->
                scope.AddOnce(fun _ ->
                    try late<-Some(new Scope(media=provider,mediaQueries=[("all","all")]))
                    with :? ObjectDisposedException -> rejected<-true
                    None) |> ignore
                None) |> ignore
            let leakedListeners=target.Count
            let disposedBeforeCleanup=disposals
            late |> Option.iter _.Revert()
            Expect.isTrue rejected "interrupted construction rejected"
            Expect.equal leakedListeners 0 "late native listener removed"
            Expect.equal disposedBeforeCleanup 1 "query disposed once"
]
