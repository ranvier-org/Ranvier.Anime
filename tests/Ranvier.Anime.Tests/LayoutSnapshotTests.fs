module Ranvier.Anime.Tests.LayoutSnapshotTests

open System
open System.Collections.Generic
open Expecto
open Ranvier.Dom
open Ranvier.Anime

type private Style() =
    let values=Dictionary<string,string*string>()
    let names=ResizeArray<string>()
    member val FailWrite:string option=None with get,set
    member _.Value name=match values.TryGetValue name with true,(value,_) -> value | _ -> ""
    member _.Priority name=match values.TryGetValue name with true,(_,priority) -> priority | _ -> ""
    member _.Seed(name,value,priority)=
        if not(values.ContainsKey name) then names.Add name
        values[name]<-value,priority
    member _.Names=names.ToArray()
    interface CSSStyleDeclaration with
        member this.getPropertyValue name=this.Value name
        member this.getPropertyPriority name=this.Priority name
        member this.setProperty(name,value,priority)=
            if this.FailWrite=Some name then failwith("write "+name)
            for leaf in CssPropertyMetadata.leafNames name do this.Seed(leaf,value,defaultArg priority "")
        member this.removeProperty name=
            if this.FailWrite=Some name then failwith("write "+name)
            let value=this.Value name
            for leaf in CssPropertyMetadata.leafNames name do values.Remove leaf |> ignore;names.Remove leaf |> ignore
            value
        member _.item index=names[int index]
        member _.length=uint names.Count
        member _.cssText with get()=failwith "opaque serialization" and set _=failwith "whole style replacement"
        member _.parentRule=Unchecked.defaultof<CSSRule>
        member _.cssRules=Unchecked.defaultof<CSSRuleList>

let private write (style:Style) name value=(style:>CSSStyleDeclaration).setProperty(name,value)
let tests=testList "layout expanded snapshots" [
    testCase "asymmetric border leaf priorities restore" <| fun _ ->
        let style=Style()
        style.Seed("border-left-width","17px","important")
        style.Seed("border-right-width","8px","")
        let snapshot=LayoutStyleSnapshot(style,"border")
        write style "border" "10px"
        snapshot.MarkWritten()
        snapshot.Restore()
        Expect.equal (style.Value "border-left-width",style.Priority "border-left-width") ("17px","important") "left priority"
        Expect.equal (style.Value "border-right-width",style.Priority "border-right-width") ("8px","") "right priority"
        Expect.equal style.Names [|"border-left-width";"border-right-width"|] "absent leaves removed"

    testCase "concurrent owned leaf changes survive" <| fun _ ->
        let style=Style()
        style.Seed("border-left-width","17px","important")
        let snapshot=LayoutStyleSnapshot(style,"border")
        write style "border" "10px"
        snapshot.MarkWritten()
        style.Seed("border-top-width","99px","important")
        snapshot.Restore()
        Expect.equal (style.Value "border-top-width",style.Priority "border-top-width") ("99px","important") "concurrent top"
        Expect.equal (style.Value "border-left-width") "17px" "owned left restored"

    testCase "current logical counterparts retain values and priority" <| fun _ ->
        let style=Style()
        style.Seed("margin-left","7px","important")
        style.Seed("margin-inline-start","11px","")
        let snapshot=LayoutStyleSnapshot(style,"margin")
        write style "margin" "100px"
        snapshot.MarkWritten()
        style.Seed("margin-inline-start","23px","important")
        snapshot.Restore()
        Expect.equal (style.Value "margin-left",style.Priority "margin-left") ("7px","important") "physical original"
        Expect.equal (style.Value "margin-inline-start",style.Priority "margin-inline-start") ("23px","important") "current logical"

    testCase "transition shorthand mutes restore asymmetric timing leaves" <| fun _ ->
        let style=Style()
        style.Seed("transition-duration","1s","important")
        style.Seed("transition-timing-function","ease","")
        let snapshot=LayoutStyleSnapshot(style,"transition")
        (style:>CSSStyleDeclaration).setProperty("transition","none",priority="important")
        snapshot.MarkWritten()
        snapshot.Restore()
        Expect.equal (style.Value "transition-duration",style.Priority "transition-duration") ("1s","important") "duration"
        Expect.equal (style.Value "transition-timing-function",style.Priority "transition-timing-function") ("ease","") "easing"

    testCase "original absence is restored after own value" <| fun _ ->
        let style=Style()
        let snapshot=LayoutStyleSnapshot(style,"width")
        write style "width" "100px"
        snapshot.MarkWritten()
        snapshot.Restore()
        Expect.isEmpty style.Names "absence"

    testCase "priority-only edits count as concurrent changes" <| fun _ ->
        let style=Style()
        style.Seed("width","7px","")
        let snapshot=LayoutStyleSnapshot(style,"width")
        write style "width" "100px"
        snapshot.MarkWritten()
        style.Seed("width","100px","important")
        snapshot.Restore()
        Expect.equal (style.Value "width",style.Priority "width") ("100px","important") "priority edit retained"

    testCase "restoration continues after a leaf setter fault" <| fun _ ->
        let style=Style()
        style.Seed("margin-left","7px","important")
        style.Seed("margin-right","8px","")
        let snapshot=LayoutStyleSnapshot(style,"margin")
        write style "margin" "100px"
        snapshot.MarkWritten()
        style.FailWrite<-Some "margin-left"
        Expect.throws (fun () -> snapshot.Restore()) "first error propagated"
        Expect.equal (style.Value "margin-right") "8px" "later leaf restored"

    testCase "repeated restoration retains restored originals" <| fun _ ->
        let style=Style()
        style.Seed("width","7px","important")
        let snapshot=LayoutStyleSnapshot(style,"width")
        write style "width" "100px"
        snapshot.MarkWritten()
        snapshot.Restore()
        snapshot.Restore()
        Expect.equal (style.Value "width",style.Priority "width") ("7px","important") "idempotent"
]

