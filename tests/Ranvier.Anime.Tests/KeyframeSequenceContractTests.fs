module Ranvier.Anime.Tests.KeyframeSequenceContractTests

open System
open System.Collections.Generic
open Expecto
open Ranvier
open Ranvier.Dom
open Ranvier.Css
open Ranvier.Anime

type private Cell(initial:float) as this =
    let mutable value=initial
    let mutable reads=0
    let writes=ResizeArray<float>()
    let handle=NumericProperty((fun () -> reads<-reads+1;this.BeforeRead();value),(fun next -> value<-next;writes.Add next;this.AfterWrite next))
    member val BeforeRead:unit -> unit=ignore with get,set
    member val AfterWrite:float -> unit=ignore with get,set
    member _.Handle=handle
    member _.Value=value
    member _.Reads=reads
    member _.Writes=writes

type private Style() =
    let values=Dictionary<string,string*string>(StringComparer.Ordinal)
    let writes=ResizeArray<string*string*string>()
    member val BeforeRead:unit -> unit=ignore with get,set
    member val AfterWrite:string -> unit=ignore with get,set
    member _.Seed(name,value,priority)=values[name]<-value,priority
    member _.Value name=match values.TryGetValue name with true,(value,_) -> value | _ -> ""
    member _.Priority name=match values.TryGetValue name with true,(_,priority) -> priority | _ -> ""
    member _.Contains name=values.ContainsKey name
    member _.Writes=writes
    interface CSSStyleDeclaration with
        member this.getPropertyValue name=this.BeforeRead();this.Value name
        member this.getPropertyPriority name=this.BeforeRead();this.Priority name
        member this.setProperty(name,value,priority)=
            values[name]<-value,defaultArg priority ""
            writes.Add(name,value,defaultArg priority "")
            this.AfterWrite name
        member this.removeProperty name=
            let value=this.Value name
            values.Remove name |> ignore
            this.AfterWrite name
            value
        member this.item index=this.BeforeRead();values.Keys |> Seq.tryItem(int index) |> Option.defaultValue ""
        member this.length=this.BeforeRead();uint values.Count
        member _.cssText with get()=failtest "whole style read" and set _=failtest "whole style replacement"
        member _.parentRule=Unchecked.defaultof<CSSRule>
        member _.cssRules=Unchecked.defaultof<CSSRuleList>

let private linear={Ranvier.Anime.KeyframeAnimationOptions.Default with Autoplay=false;Ease=id}
let private rows values=KeyframeSequence<NumericProperty,float>.Rows [|KeyframeRow.values values|]
let private offsets()=KeyframeSequence<NumericProperty,float>.Offsets [|
    KeyframeOffset.at 0. [|"x",0.;"y",0.|]
    KeyframeOffset.at 1. [|"x",100.;"y",200.|] |]

let tests=testList "public sequence contracts" [
    testCase "rows and named handle arrays are captured before construction callbacks mutate them" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let first,second,wrong=Cell(10.),Cell(20.),Cell(999.)
        let handles=[|first.Handle;second.Handle|]
        let authored=[|KeyframeRow<NumericProperty,float>.Create [||];KeyframeRow.values [|"x",200.|]|]
        authored[0]<-(KeyframeRow<NumericProperty,float>.Create [||]).WithValueBy("x",(fun _ ->
            handles[1]<-wrong.Handle
            authored[1]<-KeyframeRow.values [|"x",999.|]
            100.))
        let sequence=KeyframeSequence.Rows authored
        use animation=Keyframes.Numeric(scheduler,[|"x",handles|],sequence,duration=200u,options=linear)
        animation.Playback.Seek 150.
        Expect.equal (first.Value,second.Value,wrong.Value) (150.,150.,999.) "immutable inputs"
        Expect.equal animation.TargetCount 2 "target count excludes columns"

    testCase "duplicate row values replace in place and retain first property order" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let x,y=Cell(0.),Cell(0.)
        let order=ResizeArray<string>()
        x.AfterWrite<-fun _ -> order.Add "x"
        y.AfterWrite<-fun _ -> order.Add "y"
        let sequence=rows [|"x",1.;"y",2.;"x",3.|]
        use animation=Keyframes.Numeric(scheduler,[|"y",y.Handle;"x",x.Handle|],sequence,duration=100u,options=linear)
        order.Clear()
        animation.Playback.Seek 100.
        Expect.equal (x.Value,y.Value) (3.,2.) "last values"
        Expect.sequenceEqual order ["x";"y"] "row insertion order overrides binding order"

    testCase "uint offset duration and shared stretching preserve both property midpoints" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let x,y=Cell(5.),Cell(10.)
        use animation=Keyframes.Numeric(scheduler,[|"x",x.Handle;"y",y.Handle|],offsets(),duration=UInt32.MaxValue,options=linear)
        animation.Playback.Seek(float UInt32.MaxValue/2.)
        Expect.equal (x.Value,y.Value) (50.,100.) "unsigned duration"
        animation.Playback.Stretch 1000.
        animation.Playback.Seek 500.
        Expect.equal (x.Value,y.Value) (50.,100.) "shared stretched geometry"

    testCase "invalid constant endpoints reject before every numeric getter" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let x,y=Cell(0.),Cell(0.)
        x.BeforeRead<-fun () -> failtest "numeric getter before endpoint validation"
        y.BeforeRead<-x.BeforeRead
        Expect.throwsT<ArgumentException> (fun () ->
            Keyframes.Numeric(scheduler,[|"x",x.Handle;"y",y.Handle|],rows [|"x",100.;"y",nan|]) |> ignore) "finite constants"

    testCase "unequal target counts reject before snapshots" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let x,y=Cell(0.),Cell(0.)
        Expect.throwsT<ArgumentException> (fun () ->
            Keyframes.Numeric(scheduler,[|"x",[|x.Handle;y.Handle|];"y",[|y.Handle|]|],rows [|"x",100.;"y",200.|]) |> ignore) "equal target counts"
        Expect.equal (x.Reads,y.Reads) (0,0) "no reads"

    testCase "missing and duplicate named bindings reject before snapshots" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let x=Cell(0.)
        Expect.throwsT<ArgumentException> (fun () ->
            Keyframes.Numeric(scheduler,[|"x",x.Handle|],rows [|"y",100.|]) |> ignore) "missing binding"
        Expect.throwsT<ArgumentException> (fun () ->
            Keyframes.Numeric(scheduler,[|"x",x.Handle;"x",x.Handle|],rows [|"x",100.|]) |> ignore) "duplicate binding"
        Expect.equal x.Reads 0 "pure binding preflight"

    testCase "subprecision default splits reject before snapshots" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let x=Cell(0.)
        let sequence=KeyframeSequence.Rows [|KeyframeRow.values [|"x",1.|];KeyframeRow.values [|"x",2.|];KeyframeRow.values [|"x",3.|]|]
        Expect.throwsT<ArgumentException> (fun () -> Keyframes.NumericPrecise(scheduler,[|"x",x.Handle|],sequence,1e-12) |> ignore) "divided precision"
        Expect.equal x.Reads 0 "pure timing preflight"

    testCase "dynamic row duration overrides remain deferred during preflight" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let x=Cell(0.)
        let mutable calls=0
        let sequence=KeyframeSequence.Rows [|for value in [1.;2.;3.] -> (KeyframeRow.values [|"x",value|]).WithDurationBy(fun _ -> calls<-calls+1;100u)|]
        use animation=Keyframes.NumericPrecise(scheduler,[|"x",x.Handle|],sequence,1e-12,options=linear)
        animation.Playback.Seek 150.
        Expect.equal (calls,x.Value) (3,1.5) "computed timing replaces defaults"

    testCase "empty and zero-only sequences skip unused numeric and CSS access" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let x=Cell(0.)
        x.BeforeRead<-fun () -> failtest "unused numeric read"
        let sequence=KeyframeSequence<NumericProperty,float>.Offsets [|KeyframeOffset.at 0. [|"x",100.|]|]
        use numeric=Keyframes.Numeric(scheduler,[|"x",x.Handle|],sequence,options=linear)
        numeric.Playback.Seek 1.
        Expect.isTrue numeric.Playback.Completed "numeric no-op"
        numeric.Revert()
        let style=Style()
        style.BeforeRead<-fun () -> failtest "unused CSS read"
        let sequence=KeyframeSequence<CSSStyleDeclaration,Declaration>.Rows [||]
        use css=Keyframes.CaptureInline(scheduler,(style:>CSSStyleDeclaration),sequence,options=linear)
        css.Playback.Seek 1.
        Expect.isTrue css.Playback.Completed "CSS no-op"
        css.Revert()
        Expect.isEmpty style.Writes "no CSS dispatch"

    testCase "constant CSS name mismatches reject before style access" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let style=Style()
        style.BeforeRead<-fun () -> failtest "CSS getter before endpoint validation"
        let sequence=KeyframeSequence<CSSStyleDeclaration,Declaration>.Rows [|KeyframeRow.values [|"width",Css.height(Css.px 100.)|]|]
        Expect.throwsT<ArgumentException> (fun () -> Keyframes.CaptureInline(scheduler,(style:>CSSStyleDeclaration),sequence) |> ignore) "matching declaration names"

    testCase "missing explicit CSS starts reject before style access" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let style=Style()
        style.BeforeRead<-fun () -> failtest "CSS getter before initial validation"
        let sequence=KeyframeSequence<CSSStyleDeclaration,Declaration>.Rows [|KeyframeRow.declarations [|Css.width(Css.px 100.);Css.height(Css.px 200.)|]|]
        Expect.throwsT<ArgumentException> (fun () ->
            Keyframes.Declaration(scheduler,(style:>CSSStyleDeclaration),[|Css.width(Css.px 0.)|],sequence) |> ignore) "complete explicit starts"

    testCase "late dynamic CSS mismatches cannot publish an earlier property" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let style=Style()
        style.Seed("width","7px","")
        style.Seed("height","11px","")
        let row=(KeyframeRow<CSSStyleDeclaration,Declaration>.Create [|"width",Css.width(Css.px 100.)|])
                    .WithValueBy("height",(fun _ -> Css.width(Css.px 200.)))
        let sequence=KeyframeSequence.Rows [|row|]
        Expect.throwsT<ArgumentException> (fun () -> Keyframes.CaptureInline(scheduler,(style:>CSSStyleDeclaration),sequence) |> ignore) "dynamic property mismatch"
        Expect.isEmpty style.Writes "atomic construction"

    testCase "explicit multi-property CSS snapshots retain priority absence and unrelated edits" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let style=Style()
        style.Seed("height","11px","important")
        let sequence=KeyframeSequence<CSSStyleDeclaration,Declaration>.Rows [|KeyframeRow.declarations [|Css.width(Css.px 100.);Css.height(Css.px 200.)|]|]
        use animation=Keyframes.Declaration(scheduler,(style:>CSSStyleDeclaration),[|Css.width(Css.px 0.);Css.height(Css.px 0.)|],sequence,duration=100u,options=linear)
        animation.Playback.Seek 50.
        Expect.equal (style.Value "width",style.Priority "width",style.Value "height",style.Priority "height") ("50px","","100px","important") "per-property importance"
        style.Seed("color","blue","")
        animation.Revert()
        Expect.isFalse (style.Contains "width") "original width absent"
        Expect.equal (style.Value "height",style.Priority "height",style.Value "color") ("11px","important","blue") "scoped originals"

    testCase "owner disposal in a destination suppresses row timing and later properties" <| fun _ ->
        use graph=new Graph()
        use activation=graph.Activate()
        let owner=Api.createRoot id
        use scheduler=new ManualFrameScheduler()
        let x,y=Cell(0.),Cell(0.)
        let effects=ResizeArray<string>()
        let row=(KeyframeRow<NumericProperty,float>.Create [||])
                    .WithValueBy("x",(fun _ -> effects.Add "x";owner.Dispose();100.))
                    .WithValueBy("y",(fun _ -> effects.Add "y";200.))
                    .WithDurationBy(fun _ -> effects.Add "duration";100u)
        Expect.throwsT<ObjectDisposedException> (fun () ->
            Keyframes.Numeric(scheduler,[|"x",x.Handle;"y",y.Handle|],KeyframeSequence.Rows [|row|],owner=owner) |> ignore) "owned construction"
        Expect.sequenceEqual effects ["x"] "later construction effects suppressed"
        Expect.isEmpty x.Writes "no x initialization"
        Expect.isEmpty y.Writes "no y initialization"

    testCase "owner disposal in the first snapshot suppresses later getters" <| fun _ ->
        use graph=new Graph()
        use activation=graph.Activate()
        let owner=Api.createRoot id
        use scheduler=new ManualFrameScheduler()
        let x,y=Cell(0.),Cell(0.)
        x.BeforeRead<-fun () -> owner.Dispose()
        Expect.throwsT<ObjectDisposedException> (fun () ->
            Keyframes.Numeric(scheduler,[|"x",x.Handle;"y",y.Handle|],rows [|"x",100.;"y",200.|],owner=owner) |> ignore) "snapshot ownership"
        Expect.equal (x.Reads,y.Reads) (1,0) "later getter suppressed"

    testCase "owner disposal during a sample stops all property writes and callbacks" <| fun _ ->
        use graph=new Graph()
        use activation=graph.Activate()
        let owner=Api.createRoot id
        use scheduler=new ManualFrameScheduler()
        let x,y=Cell(0.),Cell(0.)
        let mutable armed=false
        let mutable calls=0
        let mutable callbacks=0
        let modify value=if armed then calls<-calls+1;owner.Dispose();value else value
        use animation=Keyframes.Numeric(scheduler,[|"x",x.Handle;"y",y.Handle|],rows [|"x",100.;"y",200.|],duration=100u,owner=owner,
            options={linear with Modifier=modify;OnRender=(fun _ -> callbacks<-callbacks+1)})
        x.Writes.Clear()
        y.Writes.Clear()
        callbacks<-0
        armed<-true
        animation.Playback.Seek 50.
        Expect.equal (calls,callbacks) (1,0) "interrupted sample effects"
        Expect.isEmpty x.Writes "partial x output suppressed"
        Expect.isEmpty y.Writes "later property output suppressed"
        Expect.isTrue animation.Cancelled "one owned lifetime"

    testCase "CSS setter reentry supersedes later outer properties and callbacks" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let style=Style()
        style.Seed("width","7px","")
        style.Seed("height","11px","")
        let sequence=KeyframeSequence<CSSStyleDeclaration,Declaration>.Rows [|
            (KeyframeRow.declarations [|Css.width(Css.px 107.)|]).WithDuration 100u
            (KeyframeRow.declarations [|Css.height(Css.px 211.)|]).WithDuration 100u |]
        let mutable callbacks=0
        use animation=Keyframes.CaptureInline(scheduler,(style:>CSSStyleDeclaration),sequence,options={linear with OnRender=(fun _ -> callbacks<-callbacks+1)})
        let mutable armed=true
        style.AfterWrite<-fun name ->
            if armed && name="width" then
                armed<-false
                animation.Playback.Seek(150.,muteCallbacks=true)
        callbacks<-0
        animation.Playback.Seek 50.
        Expect.equal (style.Value "width",style.Value "height",callbacks) ("107px","111px",0) "inner sample wins"

    testCase "restoration preserves first faults and restores later properties" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let x,y=Cell(5.),Cell(10.)
        use animation=Keyframes.Numeric(scheduler,[|"x",x.Handle;"y",y.Handle|],offsets(),duration=100u,options=linear)
        animation.Playback.Seek 50.
        let primary=InvalidOperationException "first property snapshot"
        x.AfterWrite<-fun value -> if value=5. then raise primary
        let mutable seen:exn option=None
        try animation.Revert() with error -> seen<-Some error
        Expect.isTrue (seen |> Option.exists(fun error -> obj.ReferenceEquals(error,primary))) "primary restoration identity"
        Expect.equal y.Value 10. "later original restored"
        Expect.isTrue animation.Cancelled "terminal cleanup"

    testCase "restoration ownership interruption stops later property snapshots" <| fun _ ->
        use graph=new Graph()
        use activation=graph.Activate()
        let owner=Api.createRoot id
        use scheduler=new ManualFrameScheduler()
        let x,y=Cell(5.),Cell(10.)
        use animation=Keyframes.Numeric(scheduler,[|"x",x.Handle;"y",y.Handle|],offsets(),duration=100u,options=linear,owner=owner)
        animation.Playback.Seek 50.
        x.AfterWrite<-fun value -> if value=5. then owner.Dispose()
        animation.Revert()
        Expect.equal (x.Value,y.Value) (5.,0.) "later property retains only raw rewind"
        Expect.isTrue animation.Cancelled "owned restoration closes lifetime"

    testCase "completion preserves every original while explicit shared cancellation closes restoration" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let x,y=Cell(5.),Cell(10.)
        use completed=Keyframes.Numeric(scheduler,[|"x",x.Handle;"y",y.Handle|],offsets(),duration=100u,options=linear)
        completed.Playback.Complete()
        completed.Revert()
        Expect.equal (x.Value,y.Value) (5.,10.) "completion keeps originals"
        use cancelled=Keyframes.Numeric(scheduler,[|"x",x.Handle;"y",y.Handle|],offsets(),duration=100u,options=linear)
        cancelled.Playback.Seek 50.
        cancelled.Playback.Cancel()
        x.Writes.Clear()
        y.Writes.Clear()
        cancelled.Revert()
        Expect.isEmpty x.Writes "cancelled x restoration"
        Expect.isEmpty y.Writes "cancelled y restoration"
]
