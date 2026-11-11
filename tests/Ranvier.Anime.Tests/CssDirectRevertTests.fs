module Ranvier.Anime.Tests.CssDirectRevertTests

open System
open System.Collections.Generic
open Expecto
open Ranvier.Dom
open Ranvier.Css
open Ranvier.Anime

type private Style() =
    let values=Dictionary<string,string*string>()
    let names=ResizeArray<string>()
    let writes=ResizeArray<string*string*string>()
    member val BeforeWrite: string -> string -> unit = (fun _ _ -> ()) with get,set
    member val AfterWrite: string -> string -> unit = (fun _ _ -> ()) with get,set
    member _.Writes=writes
    member _.Value name=match values.TryGetValue name with true,(value,_) -> value | _ -> ""
    member _.Priority name=match values.TryGetValue name with true,(_,priority) -> priority | _ -> ""
    member _.Seed(name,value,priority)=
        if not(values.ContainsKey name) then names.Add name
        values[name]<-value,priority
    interface CSSStyleDeclaration with
        member this.getPropertyValue name=this.Value name
        member this.getPropertyPriority name=this.Priority name
        member this.setProperty(name,value,priority)=
            this.BeforeWrite name value
            let priority=defaultArg priority ""
            this.Seed(name,value,priority)
            writes.Add(name,value,priority)
            this.AfterWrite name value
        member this.removeProperty name=
            this.BeforeWrite name ""
            let old=this.Value name
            values.Remove name |> ignore
            names.Remove name |> ignore
            writes.Add(name,"","")
            this.AfterWrite name ""
            old
        member _.length=uint names.Count
        member _.item index=names[int index]
        member _.cssText with get()=failwith "whole style read" and set _=failwith "whole style replacement"
        member _.parentRule=Unchecked.defaultof<CSSRule>
        member _.cssRules=Unchecked.defaultof<CSSRuleList>

let private create scheduler style =
    CssAnimation.customProperty scheduler style (RuntimeApi.createCustomProperty "--gap")
        (Generated.number 0. "px") (Generated.number 100. "px") 100.

let directCssRestoreTests = testList "CSS direct numeric revert" [
    testCase "returned handle Revert restores inline CSS before later scope Revert" <| fun _ ->
        use graph=new Ranvier.Graph()
        use active=graph.Activate()
        use scheduler=new ManualFrameScheduler()
        use scope=new Scope()
        let style=Style()
        style.Seed("--gap","7px","important")
        let mutable handle:NumericAnimation option=None
        scope.Add(fun _ -> handle<-Some(create scheduler style);None) |> ignore
        handle.Value.Seek 50.
        let before=style.Writes.Count
        handle.Value.Revert()
        Expect.equal (style.Value "--gap",style.Priority "--gap") ("7px","important") "actual original CSS"
        Expect.equal (style.Writes.Count-before) 2 "one clock rewind and one snapshot write"
        let count=style.Writes.Count
        scope.Revert()
        handle.Value.Revert()
        Expect.equal style.Writes.Count count "restoration callback released and no duplicate scope rewind"

    testCase "returned handle Revert restores original inline CSS outside any graph or scope" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let style=Style()
        style.Seed("--gap","7px","important")
        use handle=create scheduler style
        handle.Seek 50.
        handle.Revert()
        Expect.equal (style.Value "--gap",style.Priority "--gap") ("7px","important") "standalone original"
        Expect.isTrue handle.Cancelled "terminal after revert"

    testCase "returned handle Revert restores original absence" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let style=Style()
        use handle=create scheduler style
        handle.Revert()
        Expect.equal (style.Value "--gap") "" "absence restored"

    testCase "snapshot setter fault retains first error and releases both restoration callbacks" <| fun _ ->
        use graph=new Ranvier.Graph()
        use active=graph.Activate()
        use scheduler=new ManualFrameScheduler()
        use scope=new Scope()
        let style=Style()
        style.Seed("--gap","7px","important")
        let mutable handle:NumericAnimation option=None
        scope.Add(fun _ -> handle<-Some(create scheduler style);None) |> ignore
        let fault=InvalidOperationException "original inline setter"
        style.BeforeWrite<-fun name value -> if name="--gap" && value="7px" then raise fault
        let mutable seen:exn option=None
        try handle.Value.Revert() with error -> seen<-Some error
        Expect.isTrue (seen |> Option.exists(fun error -> Object.ReferenceEquals(error,fault))) "first failure identity"
        Expect.isTrue handle.Value.Cancelled "fault still cancels"
        let count=style.Writes.Count
        scope.Revert()
        handle.Value.Revert()
        Expect.equal style.Writes.Count count "released callbacks cannot retry failing restoration"

    testCase "owner disposal during rewind suppresses the later original CSS setter" <| fun _ ->
        use graph=new Ranvier.Graph()
        use active=graph.Activate()
        use scheduler=new ManualFrameScheduler()
        use scope=new Scope()
        let style=Style()
        style.Seed("--gap","7px","")
        let mutable handle:NumericAnimation option=None
        let mutable owner:Ranvier.Owner option=None
        scope.Add(fun _ ->
            owner<-Some(ScopeRegistration.resourceOwner())
            handle<-Some(create scheduler style)
            None) |> ignore
        let mutable disposed=false
        let mutable lateWrites=0
        style.BeforeWrite<-fun _ _ -> if disposed then lateWrites<-lateWrites+1
        style.AfterWrite<-fun name value ->
            if name="--gap" && value="0px" && not disposed then
                disposed<-true
                owner.Value.Dispose()
        handle.Value.Revert()
        scope.Revert()
        Expect.isTrue disposed "actual owner disposal path"
        Expect.equal lateWrites 0 "no native write after owner closed"
        Expect.equal (style.Value "--gap") "0px" "snapshot excluded after owner closure"

    testCase "scope refresh from the original CSS setter supersedes later direct restoration work" <| fun _ ->
        use graph=new Ranvier.Graph()
        use active=graph.Activate()
        use scheduler=new ManualFrameScheduler()
        use scope=new Scope()
        let style=Style()
        style.Seed("--gap","7px","")
        let handles=ResizeArray<NumericAnimation>()
        scope.Add(fun _ -> handles.Add(create scheduler style);None) |> ignore
        let mutable armed=true
        style.AfterWrite<-fun name value ->
            if armed && name="--gap" && value="7px" then
                armed<-false
                scope.Refresh() |> ignore
        handles[0].Revert()
        Expect.equal handles.Count 2 "refresh recreated replacement"
        Expect.equal (style.Value "--gap") "0px" "older completion did not overwrite replacement"
        Expect.isFalse handles[1].Cancelled "replacement remained live"

    testCase "explicit Cancel prevents direct and later scope restoration" <| fun _ ->
        use graph=new Ranvier.Graph()
        use active=graph.Activate()
        use scheduler=new ManualFrameScheduler()
        use scope=new Scope()
        let style=Style()
        style.Seed("--gap","7px","")
        let mutable handle:NumericAnimation option=None
        scope.Add(fun _ -> handle<-Some(create scheduler style);None) |> ignore
        handle.Value.Seek 50.
        handle.Value.Cancel()
        let count=style.Writes.Count
        handle.Value.Revert()
        scope.Revert()
        Expect.equal style.Writes.Count count "terminal explicit cancellation"

    for mode in ["refresh";"revert"] do
        testCase ("completed direct restore excludes duplicate snapshot during nested scope "+mode) <| fun _ ->
            use graph=new Ranvier.Graph()
            use active=graph.Activate()
            use scheduler=new ManualFrameScheduler()
            use scope=new Scope()
            let style=Style()
            style.Seed("--gap","7px","")
            let handles=ResizeArray<NumericAnimation>()
            scope.Add(fun _ -> handles.Add(create scheduler style);None) |> ignore
            handles[0].Complete()
            let mutable entered=false
            let mutable originals=0
            style.AfterWrite<-fun name value ->
                if name="--gap" && value="7px" then
                    originals<-originals+1
                    if not entered then
                        entered<-true
                        if mode="refresh" then scope.Refresh() |> ignore else scope.Revert()
            handles[0].Revert()
            Expect.equal originals 1 "the outer post-rewind restoration owns the snapshot"
            if mode="refresh" then
                Expect.equal (style.Value "--gap") "0px" "replacement wins"
                Expect.isFalse handles[1].Cancelled "replacement lifetime remains live"
            else Expect.isTrue scope.IsDisposed "nested teardown finishes"
            Expect.isTrue handles[0].Cancelled "original lifetime closes"

    testCase "plain NumericAnimation still restores its immutable original" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let mutable value=7.
        use handle=new NumericAnimation(scheduler,(fun () -> value),(fun next -> value<-next),100.,100.)
        handle.Reset()
        handle.Seek 50.
        handle.Retarget 200.
        handle.Revert()
        Expect.equal value 7. "default restoration callback"
]
