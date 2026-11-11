module Ranvier.Anime.Tests.CssScopeRestorationTests

open System
open System.Collections.Generic
open Expecto
open Ranvier.Dom
open Ranvier.Css
open Ranvier.Anime

type private Style() =
    let values = Dictionary<string,string*string>(StringComparer.Ordinal)
    let names = ResizeArray<string>()
    let writes = ResizeArray<string*string*string>()
    let mutable reads = 0
    member val BeforeRead: string -> unit = ignore with get,set
    member val BeforeWrite: string -> string -> unit = (fun _ _ -> ()) with get,set
    member val AfterWrite: string -> string -> unit = (fun _ _ -> ()) with get,set
    member _.Writes = writes
    member _.Reads = reads
    member _.Value name = match values.TryGetValue name with true,(value,_) -> value | _ -> ""
    member _.Priority name = match values.TryGetValue name with true,(_,priority) -> priority | _ -> ""
    member _.Present name = values.ContainsKey name
    member _.Seed(name,value,priority) =
        if not(values.ContainsKey name) then names.Add name
        values[name] <- value,priority
    interface CSSStyleDeclaration with
        member this.getPropertyValue name =
            reads <- reads+1
            this.BeforeRead name
            this.Value name
        member this.getPropertyPriority name =
            reads <- reads+1
            this.BeforeRead name
            this.Priority name
        member this.setProperty(name,value,priority) =
            this.BeforeWrite name value
            let priority = defaultArg priority ""
            if name="margin" then
                for leaf in CssPropertyMetadata.leafNames "margin" do this.Seed(leaf,value,priority)
            else this.Seed(name,value,priority)
            writes.Add(name,value,priority)
            this.AfterWrite name value
        member this.removeProperty name =
            this.BeforeWrite name ""
            let previous = this.Value name
            values.Remove name |> ignore
            names.Remove name |> ignore
            writes.Add(name,"","")
            this.AfterWrite name ""
            previous
        member this.item index =
            reads <- reads+1
            this.BeforeRead "item"
            names[int index]
        member this.length =
            reads <- reads+1
            this.BeforeRead "length"
            uint names.Count
        member _.cssText
            with get() = failwith "unnecessary whole style read"
            and set _ = failwith "whole style replacement"
        member _.parentRule = Unchecked.defaultof<CSSRule>
        member _.cssRules = Unchecked.defaultof<CSSRuleList>

type private Playback =
    { Clock: Timer
      Cancel: unit -> unit
      Complete: unit -> unit
      Cancelled: unit -> bool }

let private start custom scheduler (style:CSSStyleDeclaration) =
    if custom then
        let animation = CssAnimation.customPropertyWithOptions PlaybackOptions.Default scheduler style
                            (RuntimeApi.createCustomProperty "--gap") (Generated.number 0. "px") (Generated.number 100. "px") 100.
        { Clock=animation.PlaybackClock;Cancel=animation.Cancel;Complete=(fun () -> animation.Complete());Cancelled=(fun () -> animation.Cancelled) }
    else
        let clock = CssAnimation.declaration scheduler style (Declaration.create "width" "0px") (Declaration.create "width" "100px") 100u
        { Clock=clock;Cancel=clock.Cancel;Complete=(fun () -> clock.Complete());Cancelled=(fun () -> clock.Cancelled) }

let cssScopeTests = testList "legacy CSS scope restoration" [
    for custom in [false;true] do
        let label = if custom then "custom" else "declaration"
        let name = if custom then "--gap" else "width"
        testCase (label+" restores original value priority and unrelated edit") <| fun _ ->
            use graph = new Ranvier.Graph()
            use active = graph.Activate()
            use scheduler = new ManualFrameScheduler()
            use scope = new Scope()
            let style = Style()
            style.Seed(name,"7px","important")
            style.Seed("color","red","")
            let mutable playback : Playback option = None
            scope.Add(fun _ -> playback<-Some(start custom scheduler style);None) |> ignore
            playback.Value.Clock.Seek 50.
            style.Seed("color","blue","")
            scope.Revert()
            Expect.equal (style.Value name,style.Priority name) ("7px","important") "captured inline property"
            Expect.equal (style.Value "color") "blue" "unrelated edit"
            Expect.isTrue (playback.Value.Cancelled()) "driver cancelled"
        testCase (label+" restores originally absent property") <| fun _ ->
            use graph = new Ranvier.Graph()
            use active = graph.Activate()
            use scheduler = new ManualFrameScheduler()
            use scope = new Scope()
            let style = Style()
            scope.Add(fun _ -> start custom scheduler style |> ignore;None) |> ignore
            scope.Revert()
            Expect.isFalse (style.Present name) "original absence"
        testCase (label+" Complete retains restoration") <| fun _ ->
            use graph = new Ranvier.Graph()
            use active = graph.Activate()
            use scheduler = new ManualFrameScheduler()
            use scope = new Scope()
            let style = Style()
            style.Seed(name,"7px","important")
            let mutable playback : Playback option = None
            scope.Add(fun _ -> playback<-Some(start custom scheduler style);None) |> ignore
            playback.Value.Complete()
            scope.Revert()
            Expect.equal (style.Value name) "7px" "completed original restored"
        testCase (label+" Complete then Cancel closes restoration") <| fun _ ->
            use graph = new Ranvier.Graph()
            use active = graph.Activate()
            use scheduler = new ManualFrameScheduler()
            use scope = new Scope()
            let style = Style()
            style.Seed(name,"7px","important")
            let mutable playback : Playback option = None
            scope.Add(fun _ -> playback<-Some(start custom scheduler style);None) |> ignore
            playback.Value.Complete()
            playback.Value.Cancel()
            let count = style.Writes.Count
            scope.Revert()
            Expect.equal (style.Value name) "100px" "explicit terminal cancellation retains value"
            Expect.equal style.Writes.Count count "no hidden controller rewind"
        testCase (label+" refresh recaptures original after restoration") <| fun _ ->
            use graph = new Ranvier.Graph()
            use active = graph.Activate()
            use scheduler = new ManualFrameScheduler()
            use scope = new Scope()
            let style = Style()
            style.Seed(name,"7px","important")
            let clocks = ResizeArray<Playback>()
            scope.Add(fun _ -> clocks.Add(start custom scheduler style);None) |> ignore
            clocks[0].Clock.Seek 50.
            scope.Refresh() |> ignore
            Expect.isTrue (clocks[0].Cancelled()) "old driver ended"
            scope.Revert()
            Expect.equal (style.Value name) "7px" "original survived recapture"
            let count = style.Writes.Count
            scheduler.Advance 1000.
            scheduler.RunFrame()
            Expect.equal style.Writes.Count count "no pending writes"
        testCase (label+" snapshot getter disposal prevents later native accesses") <| fun _ ->
            use graph = new Ranvier.Graph()
            use active = graph.Activate()
            use scheduler = new ManualFrameScheduler()
            use scope = new Scope()
            let style = Style()
            style.BeforeRead <- fun _ -> scope.Revert()
            Expect.throwsT<ObjectDisposedException>(fun () ->
                scope.Add(fun _ -> start custom scheduler style |> ignore;None) |> ignore) "owner disposed from getter"
            Expect.equal style.Reads 1 "first capture read alone"
            Expect.equal style.Writes.Count 0 "no write after disposal"
        testCase (label+" failed initial style setter restores before cancellation") <| fun _ ->
            use graph = new Ranvier.Graph()
            use active = graph.Activate()
            use scheduler = new ManualFrameScheduler()
            use scope = new Scope()
            let style = Style()
            style.Seed(name,"7px","important")
            let fault = InvalidOperationException "initial write"
            style.BeforeWrite <- fun property value -> if property=name && value="0px" then raise fault
            let mutable seen : exn option = None
            try scope.Add(fun _ -> start custom scheduler style |> ignore;None) |> ignore
            with error -> seen<-Some error
            Expect.isTrue (seen |> Option.exists(fun error -> Object.ReferenceEquals(error,fault))) "first fault identity"
            Expect.equal (style.Value name) "7px" "failed initialization original"
            scope.Revert()
            let count=style.Writes.Count
            scheduler.Advance 1000.
            scheduler.RunFrame()
            Expect.equal style.Writes.Count count "failed driver ended"

        testCase (label+" partially committed initial setter restores the captured inline declaration") <| fun _ ->
            use graph = new Ranvier.Graph()
            use active = graph.Activate()
            use scheduler = new ManualFrameScheduler()
            use scope = new Scope()
            let style = Style()
            style.Seed(name,"7px","important")
            let fault = InvalidOperationException "after initial write"
            style.AfterWrite <- fun property value -> if property=name && value="0px" then raise fault
            let mutable seen : exn option = None
            try scope.Add(fun _ -> start custom scheduler style |> ignore;None) |> ignore
            with error -> seen<-Some error
            Expect.isTrue (seen |> Option.exists(fun error -> Object.ReferenceEquals(error,fault))) "primary fault identity"
            Expect.equal (style.Value name,style.Priority name) ("7px","important") "partial mutation restored"
            scope.Revert()

        testCase (label+" explicit cancellation during restoration prevents later native restoration") <| fun _ ->
            use graph = new Ranvier.Graph()
            use active = graph.Activate()
            use scheduler = new ManualFrameScheduler()
            use scope = new Scope()
            let style = Style()
            style.Seed(name,"7px","important")
            let mutable playback : Playback option = None
            scope.Add(fun _ -> playback<-Some(start custom scheduler style);None) |> ignore
            let mutable cancelled = false
            let stop () =
                if not cancelled then
                    cancelled<-true
                    playback.Value.Cancel()
            if custom then style.AfterWrite <- fun property value -> if property=name && value="0px" then stop()
            else style.BeforeRead <- fun _ -> stop()
            scope.Revert()
            Expect.isTrue cancelled "restoration reentry exercised"
            Expect.equal (style.Value name) "0px" "closed controller blocks captured original write"

    testCase "shorthand restoration uses existing expanded leaves and priorities" <| fun _ ->
        use graph = new Ranvier.Graph()
        use active = graph.Activate()
        use scheduler = new ManualFrameScheduler()
        use scope = new Scope()
        let style = Style()
        for name,value,priority in ["margin-top","1px","important";"margin-right","2px","";"margin-bottom","3px","";"margin-left","4px","important"] do
            style.Seed(name,value,priority)
        scope.Add(fun _ ->
            CssAnimation.declaration scheduler style (Declaration.create "margin" "0px") (Declaration.create "margin" "100px") 100u |> ignore
            None) |> ignore
        scope.Revert()
        Expect.equal (style.Value "margin-top",style.Priority "margin-top") ("1px","important") "top"
        Expect.equal (style.Value "margin-right",style.Priority "margin-right") ("2px","") "right"
        Expect.equal (style.Value "margin-left",style.Priority "margin-left") ("4px","important") "left"

    testCase "restoration attempts later leaves after the first setter fault" <| fun _ ->
        use graph = new Ranvier.Graph()
        use active = graph.Activate()
        use scheduler = new ManualFrameScheduler()
        use scope = new Scope()
        let style = Style()
        for name,value in ["margin-top","1px";"margin-right","2px";"margin-bottom","3px";"margin-left","4px"] do style.Seed(name,value,"")
        let mutable clock : Timer option = None
        scope.Add(fun _ ->
            clock<-Some(CssAnimation.declaration scheduler style (Declaration.create "margin" "0px") (Declaration.create "margin" "100px") 100u)
            None) |> ignore
        let fault = InvalidOperationException "restore top"
        style.BeforeWrite <- fun property value -> if property="margin-top" && value="1px" then raise fault
        Expect.throws scope.Revert "first restoration fault"
        Expect.equal (style.Value "margin-right",style.Value "margin-bottom",style.Value "margin-left") ("2px","3px","4px") "later leaves attempted"
        Expect.isTrue clock.Value.Cancelled "fault still cancels"
        Expect.equal scope.Errors.Count 1 "one first failure retained"

    testCase "owner disposal during snapshot counterpart reads suppresses restoration writes" <| fun _ ->
        use graph = new Ranvier.Graph()
        use active = graph.Activate()
        use scheduler = new ManualFrameScheduler()
        use scope = new Scope()
        let style = Style()
        style.Seed("width","7px","")
        let mutable owner : Ranvier.Owner option = None
        let mutable disposed = false
        let mutable lateWrites = 0
        scope.Add(fun _ ->
            owner<-Some(ScopeRegistration.resourceOwner())
            CssAnimation.declaration scheduler style (Declaration.create "width" "0px") (Declaration.create "width" "100px") 100u |> Timer.own |> ignore
            None) |> ignore
        style.BeforeRead <- fun _ -> if not disposed then disposed<-true;owner.Value.Dispose()
        style.BeforeWrite <- fun _ _ -> if disposed then lateWrites<-lateWrites+1
        scope.Revert()
        Expect.equal lateWrites 0 "no write through closed owner"

    testCase "reentrant scope refresh supersedes the older expanded restoration" <| fun _ ->
        use graph = new Ranvier.Graph()
        use active = graph.Activate()
        use scheduler = new ManualFrameScheduler()
        use scope = new Scope()
        let style = Style()
        for name,value in ["margin-top","1px";"margin-right","2px";"margin-bottom","3px";"margin-left","4px"] do style.Seed(name,value,"")
        let mutable created = 0
        scope.Add(fun _ ->
            created<-created+1
            CssAnimation.declaration scheduler style (Declaration.create "margin" "0px") (Declaration.create "margin" "100px") 100u |> Timer.own |> ignore
            None) |> ignore
        let mutable armed = true
        style.AfterWrite <- fun property value ->
            if armed && property="margin-top" && value="1px" then
                armed<-false
                scope.Refresh() |> ignore
        scope.Refresh() |> ignore
        Expect.equal created 2 "nested refresh alone rebuilt"
        Expect.equal (style.Value "margin-right") "0px" "stale restoration did not overwrite replacement"

    testCase "standalone suppressed factories do not require a graph" <| fun _ ->
        use scheduler = new ManualFrameScheduler()
        let style = Style()
        use declaration = CssAnimation.declaration scheduler style (Declaration.create "width" "0px") (Declaration.create "width" "100px") 100u
        use custom = CssAnimation.customProperty scheduler style (RuntimeApi.createCustomProperty "--gap") (Generated.number 0. "px") (Generated.number 100. "px") 100.
        Expect.equal (style.Value "--gap") "0px" "standalone initialization"
]
