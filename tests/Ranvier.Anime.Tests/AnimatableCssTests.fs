module Ranvier.Anime.Tests.AnimatableCssTests

open System
open System.IO
open System.Text.Json
open System.Collections.Generic
open Expecto
open Ranvier
open Ranvier.Dom
open Ranvier.Css
open Ranvier.Anime

type private CssCaptureStyle(?log:string -> string -> unit) =
    let values=Dictionary<string,string*string>()
    let writes=ResizeArray<string*string*string>()
    member val BeforeRead:unit -> unit=ignore with get,set
    member val AfterWrite:string -> unit=ignore with get,set
    member val BeforeRemove:string -> unit=ignore with get,set
    member val Serialized:string option=None with get,set
    member _.Writes=writes
    member _.Seed(name,value,priority)=values[name]<-value,priority
    member _.Value name=match values.TryGetValue name with true,(value,_) -> value | _ -> ""
    member _.Priority name=match values.TryGetValue name with true,(_,priority) -> priority | _ -> ""
    member _.Contains name=values.ContainsKey name
    interface CSSStyleDeclaration with
        member this.getPropertyValue name=this.BeforeRead();this.Value name
        member this.getPropertyPriority name=this.BeforeRead();this.Priority name
        member this.setProperty(name,value,priority)=
            let priority=defaultArg priority ""
            values[name]<-value,priority
            writes.Add(name,value,priority)
            log |> Option.iter(fun callback -> callback name value)
            this.AfterWrite name
        member this.removeProperty name=
            this.BeforeRemove name
            let value=this.Value name
            values.Remove name |> ignore
            value
        member this.item index=this.BeforeRead();values.Keys |> Seq.tryItem(int index) |> Option.defaultValue ""
        member this.length=this.BeforeRead();uint32 values.Count
        member this.cssText
            with get()=this.Serialized |> Option.defaultWith(fun () -> failwith "Whole style access")
            and set _=failwith "Whole style replacement"
        member _.parentRule=Unchecked.defaultof<CSSRule>
        member _.cssRules=Unchecked.defaultof<CSSRuleList>

let private linear={AnimatablePropertyOptions.Default with Ease=id}
let private optional name (item:JsonElement)=match item.TryGetProperty(name:string) with true,value -> Some value | _ -> None
let private fixtures () =
    let root=Environment.GetEnvironmentVariable "RANVIER_ANIME_REFERENCE_ROOT"
    let root=if String.IsNullOrWhiteSpace root then Path.Combine(AppContext.BaseDirectory,"reference") else root
    use doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"animatable.json")))
    Expect.equal (doc.RootElement.GetProperty("upstreamCommit").GetString()) "2c9cf8ea00329f6768c7d7902252ed977d75ce42" "pinned source"
    doc.RootElement.GetProperty("fixtures").EnumerateArray()
    |> Seq.filter(fun fixture -> List.contains (fixture.GetProperty("name").GetString()) ["unitScalar";"unitFromProperty";"compoundPartial";"compoundModifier";"componentEmpty";"color";"hexColor";"multipleComponents";"revert"])
    |> Seq.map _.Clone() |> Seq.toList
let private text (value:JsonElement)=if value.ValueKind=JsonValueKind.Number then ValueInterpolation.number(value.GetDouble()) else value.GetString()
let private replay (fixture:JsonElement) =
    use scheduler=new ManualFrameScheduler()
    let writes=ResizeArray<string*int*string>()
    let events=ResizeArray<string>()
    let mutable checking=true
    use group=new Animatable(scheduler,onBegin=(fun _ -> if checking then events.Add "Begin"),
        onBeforeUpdate=(fun _ -> if checking then events.Add "BeforeUpdate"),onRender=(fun _ -> if checking then events.Add "Render"),
        onUpdate=(fun _ -> if checking then events.Add "Update"),onLoop=(fun _ -> if checking then events.Add "Loop"),
        onPause=(fun _ -> if checking then events.Add "Pause"),onComplete=(fun _ -> if checking then events.Add "Complete"))
    use cleanup={new IDisposable with member _.Dispose()=checking<-false}
    let styles =
        fixture.GetProperty("targets").EnumerateArray()
        |> Seq.mapi(fun index target ->
            let style=CssCaptureStyle(log=(fun name value -> writes.Add(name.Substring(2),index,value)))
            for property in target.EnumerateObject() do style.Seed("--"+property.Name,text property.Value,"")
            style)
        |> Seq.toArray
    let handles=Dictionary<string,AnimatableDeclaration>()
    let parameters=fixture.GetProperty("parameters")
    let initial=fixture.GetProperty("initial")
    for property in initial.GetProperty("state").GetProperty("properties").EnumerateObject() do
        let parameter=parameters.GetProperty property.Name
        let duration=if parameter.ValueKind=JsonValueKind.Object then parameter.GetProperty("duration").GetDouble() else parameter.GetDouble()
        let modifier=if optional "modifier" fixture |> Option.exists(fun name -> name.GetString()="double") then (fun value -> value*2.) else id
        let settings={linear with Modifier=modifier}
        handles.Add(property.Name,group.CaptureInlinePrecise(styles |> Array.map(fun style -> style:>CSSStyleDeclaration),"--"+property.Name,duration,options=settings))
    let verify (sample:JsonElement) =
        let state=sample.GetProperty("state")
        let expectedWrites=sample.GetProperty("writes")
        Expect.equal writes.Count (expectedWrites.GetArrayLength()) "source write count"
        for index in 0..writes.Count-1 do
            let name,target,value=writes[index]
            let expected=expectedWrites[index]
            Expect.equal (name,target,value) (expected.GetProperty("property").GetString(),expected.GetProperty("target").GetInt32(),text(expected.GetProperty("value"))) "source write order/value"
        let expectedEvents=sample.GetProperty("events").EnumerateArray() |> Seq.map(fun item -> item.GetProperty("event").GetString()) |> Seq.toArray
        Expect.sequenceEqual events expectedEvents "source callback phases"
        let callbacks=state.GetProperty("callbacks")
        Expect.equal group.Callbacks.CurrentTime (callbacks.GetProperty("time").GetDouble()) "callback clock"
        Expect.equal group.Callbacks.Completed (callbacks.GetProperty("completed").GetBoolean()) "callback completion"
        for index in 0..styles.Length-1 do
            let targets=state.GetProperty("targets")
            for property in targets[index].EnumerateObject() do
                Expect.equal (styles[index].Value("--"+property.Name)) (text property.Value) "source final target"
        for property in state.GetProperty("properties").EnumerateObject() do
            let handle=handles[property.Name]
            let getter=property.Value.GetProperty("value")
            if getter.ValueKind=JsonValueKind.Array then
                let expected=getter.EnumerateArray() |> Seq.map _.GetDouble() |> Seq.toArray
                Expect.sequenceEqual handle.Components expected "cached components"
            else Expect.equal handle.Magnitude (getter.GetDouble()) "cached scalar"
            Expect.equal handle.Playback.CurrentTime (property.Value.GetProperty("time").GetDouble()) "property clock"
    verify initial
    for sample in fixture.GetProperty("samples").EnumerateArray() do
        writes.Clear()
        events.Clear()
        let step=sample.GetProperty("step")
        let property=optional "property" step |> Option.map _.GetString() |> Option.defaultValue ""
        match step.GetProperty("op").GetString() with
        | "retarget" ->
            let value=step.GetProperty("value")
            let duration=optional "duration" step |> Option.map _.GetDouble()
            let action () =
                if value.ValueKind=JsonValueKind.Array then
                    handles[property].ToComponentsPrecise(value.EnumerateArray() |> Seq.map _.GetDouble() |> Seq.toArray,?duration=duration)
                else handles[property].ToNumberPrecise(value.GetDouble(),?duration=duration)
            if group.Cancelled then Expect.throwsT<ObjectDisposedException> action "terminal handle adaptation" else action()
        | "frame" -> scheduler.Advance(step.GetProperty("after").GetDouble());scheduler.RunFrame()
        | "revert" -> group.Revert()
        | operation -> failtest ("Unknown CSS source operation "+operation)
        verify sample

let cssFacadeTests=testList "public CSS animatable" [
    for fixture in fixtures() do testCase ("source "+fixture.GetProperty("name").GetString()) <| fun _ -> replay fixture
    testCase "typed declarations retain inline importance and restore the original property only" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let recorder=CssCaptureStyle()
        recorder.Seed("width","7px","important")
        let style=recorder:>CSSStyleDeclaration
        use group=new Animatable(scheduler)
        let handle=group.Declaration(style,Css.width(Css.px 10.),duration=100u,options=linear)
        handle.To(Css.width(Css.px 110.))
        handle.Playback.Seek 50.
        Expect.equal (Declaration.value handle.Value) "60px" "typed cached getter"
        Expect.equal (recorder.Value "width") "60px" "typed style dispatch"
        Expect.isTrue (Declaration.isImportant handle.Value) "getter importance"
        style.setProperty("height","900px")
        group.Revert()
        Expect.equal (recorder.Value "width",recorder.Priority "width") ("7px","important") "original inline property"
        Expect.equal (recorder.Value "height") "900px" "unrelated edit"
        Expect.throwsT<ObjectDisposedException> (fun () -> handle.To(Css.width(Css.px 50.))) "terminal handle"
    testCase "revert restores absence and an explicitly present empty custom property" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let recorder=CssCaptureStyle()
        recorder.Seed("--empty","","important")
        let style=recorder:>CSSStyleDeclaration
        use group=new Animatable(scheduler)
        let absent=group.Declaration(style,Css.opacity(Css.number 0.),duration=100u)
        let empty=group.CaptureInline(style,"--empty",duration=100u)
        absent.To(Css.opacity(Css.number 1.))
        empty.ToNumber 1.
        absent.Playback.Seek 50.
        empty.Playback.Seek 50.
        group.Revert()
        Expect.isFalse (recorder.Contains "opacity") "missing inline declaration removed"
        Expect.isTrue (recorder.Contains "--empty") "empty declaration presence retained"
        Expect.equal (recorder.Value "--empty",recorder.Priority "--empty") ("","important") "empty declaration priority retained"
    testCase "multiple inline targets retain individual caches and priorities" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let first,second=CssCaptureStyle(),CssCaptureStyle()
        first.Seed("width","10px","important")
        second.Seed("width","50px","")
        use group=new Animatable(scheduler)
        let handle=group.CaptureInline([|first:>CSSStyleDeclaration;second:>CSSStyleDeclaration|],"width",duration=100u,options=linear)
        handle.ToNumber 110.
        handle.Playback.Seek 50.
        Expect.equal (first.Value "width",second.Value "width") ("60px","80px") "independent caches"
        Expect.equal (first.Priority "width",second.Priority "width") ("important","") "individual priorities"
        group.Revert()
        Expect.equal (first.Value "width",second.Value "width") ("10px","50px") "individual originals"
    testCase "typed component destinations preserve cached components and reject mismatched templates" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let style=CssCaptureStyle():>CSSStyleDeclaration
        use group=new Animatable(scheduler)
        let handle=group.Declaration(style,Declaration.create "transform" "translate(10px,20px)",duration=100u,options=linear)
        handle.To(Declaration.create "transform" "translate(110px,220px)")
        handle.Playback.Seek 50.
        let components=handle.Components
        components[0]<-999.
        Expect.equal (Declaration.value handle.Value) "translate(60px,120px)" "independent component getter"
        Expect.throwsT<ArgumentException> (fun () -> handle.To(Declaration.create "transform" "translate(1em,2em)")) "template units"
        handle.ToComponents [|210.|]
        handle.Playback.Seek 50.
        Expect.equal (Declaration.value handle.Value) "translate(135px,120px)" "omitted endpoints persist"
    testCase "typed color destinations retain source channel interpolation" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let style=CssCaptureStyle():>CSSStyleDeclaration
        use group=new Animatable(scheduler)
        let handle=group.Declaration(style,Declaration.create "color" "#ff0000",duration=100u,options=linear)
        handle.To(Declaration.create "color" "#00ff00")
        handle.Playback.Seek 50.
        Expect.equal (Declaration.value handle.Value) "rgba(180,180,0,1)" "source gamma-like midpoint"
    testCase "important destinations commit with accepted endpoints and restore the original priority" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let recorder=CssCaptureStyle()
        recorder.Seed("width","7px","")
        use group=new Animatable(scheduler)
        let handle=group.Declaration(recorder,Css.width(Css.px 10.),duration=100u,options=linear)
        let writes=recorder.Writes.Count
        handle.To(Css.width(Css.px 100.) |> Declaration.important)
        Expect.equal recorder.Writes.Count writes "retarget remains cache-only"
        Expect.isTrue (Declaration.isImportant handle.Value) "accepted importance"
        handle.Playback.Seek 50.
        Expect.equal (recorder.Priority "width") "important" "important destination applied"
        group.Revert()
        Expect.equal (recorder.Priority "width") "" "original priority restored"
    testCase "superseded retargets discard requested importance" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let recorder=CssCaptureStyle()
        let mutable active:Timer option=None
        let mutable armed=false
        let modifier value =
            if armed then armed<-false;active |> Option.iter(fun clock -> clock.Pause())
            value
        use group=new Animatable(scheduler)
        let handle=group.Declaration(recorder,Css.width(Css.px 10.),duration=100u,options={linear with Modifier=modifier})
        active<-Some handle.Playback
        armed<-true
        handle.To(Css.width(Css.px 100.) |> Declaration.important)
        Expect.isFalse (Declaration.isImportant handle.Value) "uncommitted importance discarded"
        handle.ToNumber 100.
        handle.Playback.Seek 50.
        Expect.equal (recorder.Priority "width") "" "later writes keep original priority"
    testCase "precise CSS retarget timing and external edits retain animation cache" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let recorder=CssCaptureStyle()
        use group=new Animatable(scheduler)
        let handle=group.DeclarationPrecise(recorder,Css.width(Css.px 0.),0.125,options=linear)
        handle.ToPrecise(Css.width(Css.px 100.),duration=0.25)
        handle.Playback.Seek 0.125
        (recorder:>CSSStyleDeclaration).setProperty("width","900px")
        Expect.equal handle.Magnitude 50. "getter retains cache"
        handle.To(Css.width(Css.px 200.))
        handle.Playback.Seek 0.125
        Expect.equal (recorder.Value "width") "125px" "retarget starts from cache and retains duration"
    testCase "invalid settings and declarations reject before style reads or writes" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let recorder=CssCaptureStyle()
        recorder.BeforeRead<-fun () -> failtest "invalid authoring read styles"
        use group=new Animatable(scheduler)
        for duration in [nan;infinity;-1.;1e-15] do
            Expect.throwsT<ArgumentException> (fun () -> group.DeclarationPrecise(recorder,Css.width(Css.px 10.),duration) |> ignore) "invalid duration"
        Expect.throwsT<ArgumentException> (fun () -> group.Declaration(recorder,Css.width(Css.px 10.),options={linear with Precision=Some 309u}) |> ignore) "precision"
        Expect.throwsT<ArgumentException> (fun () -> group.Declaration(recorder,Declaration.create "width" "+=10px") |> ignore) "relative initial"
        Expect.equal recorder.Writes.Count 0 "invalid authoring has no writes"
    testCase "capture cancellation prevents reading later style targets" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        use group=new Animatable(scheduler)
        let first,second=CssCaptureStyle(),CssCaptureStyle()
        first.BeforeRead<-fun () -> group.Cancel()
        second.BeforeRead<-fun () -> failtest "later style read"
        Expect.throwsT<ObjectDisposedException> (fun () -> group.CaptureInline([|first:>CSSStyleDeclaration;second:>CSSStyleDeclaration|],"width") |> ignore) "capture interruption"
    testCase "revert during initial modifier restores pending inline snapshots" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        use group=new Animatable(scheduler)
        let recorder=CssCaptureStyle()
        recorder.Seed("width","7px","important")
        let settings={linear with Modifier=(fun value -> group.Revert();value)}
        Expect.throwsT<ObjectDisposedException> (fun () -> group.Declaration(recorder,Css.width(Css.px 10.),options=settings) |> ignore) "initialization interruption"
        Expect.equal (recorder.Value "width",recorder.Priority "width") ("7px","important") "pending snapshot restored"
        Expect.equal recorder.Writes.Count 1 "only restoration writes"
    testCase "owner disposal in a component modifier stops later components and targets" <| fun _ ->
        use graph=new Graph()
        use activation=graph.Activate()
        use scheduler=new ManualFrameScheduler()
        let first,second=CssCaptureStyle(),CssCaptureStyle()
        let mutable armed=false
        let mutable calls=0
        let owner,group,handle=Api.createRoot(fun owner ->
            let group=new Animatable(scheduler) |> Animatable.own
            let modify value =
                calls<-calls+1
                if armed then owner.Dispose()
                value
            let settings={linear with Modifier=modify}
            let handle=group.Declaration([|first:>CSSStyleDeclaration;second:>CSSStyleDeclaration|],Declaration.create "transform" "translate(0px,0px)",duration=100u,options=settings)
            owner,group,handle)
        use group=group
        handle.ToComponents [|100.;200.|]
        let writes=first.Writes.Count+second.Writes.Count
        calls<-0
        armed<-true
        handle.Playback.Seek 50.
        Expect.equal calls 1 "later components suppressed"
        Expect.equal (first.Writes.Count+second.Writes.Count) writes "no final style commit"
        Expect.isTrue group.Cancelled "owner cancellation"
        owner.Dispose()
    testCase "restoration cancellation suppresses remaining style writes" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        use group=new Animatable(scheduler)
        let first,second=CssCaptureStyle(),CssCaptureStyle()
        let handle=group.Declaration([|first:>CSSStyleDeclaration;second:>CSSStyleDeclaration|],Css.width(Css.px 0.),duration=100u)
        handle.To(Css.width(Css.px 100.))
        handle.Playback.Seek 50.
        let stopped=second.Writes.Count
        first.BeforeRemove<-fun _ -> group.Cancel()
        group.Revert()
        Expect.equal second.Writes.Count (stopped+1) "only the preceding raw rewind writes"
        Expect.isTrue (second.Contains "width") "remaining absence restoration suppressed"
        Expect.isTrue handle.Playback.Cancelled "driver closed"
    testCase "typed destinations reject an explicitly cancelled property while cached getters remain readable" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        use group=new Animatable(scheduler)
        let style=CssCaptureStyle():>CSSStyleDeclaration
        let handle=group.Declaration(style,Css.width(Css.px 10.),duration=100u)
        handle.Playback.Cancel()
        Expect.throwsT<ObjectDisposedException> (fun () -> handle.To(Css.width(Css.px 100.))) "unsigned typed retarget is terminal"
        Expect.throwsT<ObjectDisposedException> (fun () -> handle.ToPrecise(Css.width(Css.px 100.))) "precise typed retarget is terminal"
        Expect.equal (Declaration.value handle.Value) "10px" "cached getter survives cancellation"
        Expect.isFalse group.Cancelled "independent group lifetime"
    testCase "restoration errors preserve the first exception and restore other style targets" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        use group=new Animatable(scheduler)
        let primary=InvalidOperationException "remove failed"
        let first,second=CssCaptureStyle(),CssCaptureStyle()
        let handle=group.Declaration([|first:>CSSStyleDeclaration;second:>CSSStyleDeclaration|],Css.width(Css.px 0.),duration=100u)
        handle.To(Css.width(Css.px 100.))
        handle.Playback.Seek 50.
        first.BeforeRemove<-fun _ -> raise primary
        let mutable actual:exn option=None
        try group.Revert() with error -> actual<-Some error
        Expect.isTrue (actual |> Option.exists(fun error -> obj.ReferenceEquals(error,primary))) "primary exception identity"
        Expect.isFalse (second.Contains "width") "other target absence restored"
        Expect.isTrue (group.Cancelled && handle.Playback.Cancelled) "all drivers closed"

    testCase "opaque shorthand admission failure leaves the group usable and unregistered" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        use group=new Animatable(scheduler)
        let recorder=CssCaptureStyle()
        for name in CssPropertyMetadata.leafNames "margin" do recorder.Seed(name,"","")
        recorder.Serialized<-Some "margin-top:;margin-right:;margin-bottom:;margin-left:7px !important;"
        Expect.throwsT<NotSupportedException> (fun () ->
            group.Declaration(recorder,Declaration.create "margin" "0px") |> ignore) "opaque native snapshot required"
        Expect.equal group.Count 0 "no registered track"
        Expect.isFalse group.Cancelled "group lifetime retained"
        Expect.isEmpty recorder.Writes "no initialization dispatch"
        let handle=group.Declaration(recorder,Css.width(Css.px 0.),duration=100u,options=linear)
        handle.To(Css.width(Css.px 100.))
        handle.Playback.Seek 50.
        Expect.equal (recorder.Value "width") "50px" "later valid property operates"
        group.Revert()
        Expect.isFalse (recorder.Contains "width") "later property restores absence"

    testCase "snapshot enumeration cancellation stops later targets before registration" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        use group=new Animatable(scheduler)
        let first,second=CssCaptureStyle(),CssCaptureStyle()
        first.Seed("width","7px","")
        let mutable firstReads=0
        let mutable secondReads=0
        first.BeforeRead<-fun () -> firstReads<-firstReads+1;if firstReads=3 then group.Cancel()
        second.BeforeRead<-fun () -> secondReads<-secondReads+1
        Expect.throwsT<ObjectDisposedException> (fun () ->
            group.Declaration([|first:>CSSStyleDeclaration;second:>CSSStyleDeclaration|],Css.width(Css.px 0.)) |> ignore) "capture cancellation"
        Expect.equal (firstReads,secondReads,group.Count) (3,0,0) "guarded capture order"
        Expect.isTrue group.Cancelled "group cancellation"
        Expect.isEmpty first.Writes "no first initialization"
        Expect.isEmpty second.Writes "no later initialization"

    testCase "shorthand restoration continues through leaves and targets after the first fault" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        use group=new Animatable(scheduler)
        let first,second=CssCaptureStyle(),CssCaptureStyle()
        first.Seed("margin-left","7px","important")
        first.Seed("margin-right","8px","")
        second.Seed("margin-left","17px","")
        second.Seed("margin-right","18px","important")
        let handle=group.Declaration([|first:>CSSStyleDeclaration;second:>CSSStyleDeclaration|],Declaration.create "margin" "0px",duration=100u,options=linear)
        handle.To(Declaration.create "margin" "100px")
        handle.Playback.Seek 50.
        for style in [first;second] do
            style.Seed("margin-left","50px","")
            style.Seed("margin-right","50px","")
        let primary=InvalidOperationException "first leaf restoration"
        first.AfterWrite<-fun name -> if name="margin-left" then raise primary
        let mutable seen:exn option=None
        try group.Revert() with error -> seen<-Some error
        Expect.isTrue (seen |> Option.exists(fun error -> obj.ReferenceEquals(error,primary))) "primary identity"
        Expect.equal (first.Value "margin-right",second.Value "margin-left",second.Value "margin-right") ("8px","17px","18px") "remaining originals restored"
        Expect.equal (first.Priority "margin-left",second.Priority "margin-right") ("important","important") "individual priority"
        Expect.isTrue (group.Cancelled && handle.Playback.Cancelled) "terminal cleanup"

    testCase "shorthand restoration cancellation stops later leaves and targets" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        use group=new Animatable(scheduler)
        let first,second=CssCaptureStyle(),CssCaptureStyle()
        for style in [first;second] do
            style.Seed("margin-left","7px","")
            style.Seed("margin-right","8px","")
        let handle=group.Declaration([|first:>CSSStyleDeclaration;second:>CSSStyleDeclaration|],Declaration.create "margin" "0px",duration=100u,options=linear)
        handle.To(Declaration.create "margin" "100px")
        handle.Playback.Seek 50.
        first.Writes.Clear()
        second.Writes.Clear()
        first.AfterWrite<-fun name -> if name="margin-left" then group.Cancel()
        group.Revert()
        Expect.isFalse (first.Writes |> Seq.exists(fun (name,_,_) -> name="margin-right")) "later leaf suppressed"
        Expect.isFalse (second.Writes |> Seq.exists(fun (name,_,_) -> name="margin-left" || name="margin-right")) "later target snapshot suppressed"
        Expect.isTrue (group.Cancelled && handle.Playback.Cancelled) "terminal cancellation"
]
