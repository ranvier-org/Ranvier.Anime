module Ranvier.Anime.Tests.CssSnapshotTests

open System
open System.Collections.Generic
open Expecto
open Ranvier.Dom
open Ranvier.Anime

type private Style() =
    let values=Dictionary<string,string*string>()
    let names=ResizeArray<string>()
    member val BeforeRead:unit -> unit=ignore with get,set
    member val BeforeWrite:string -> unit=ignore with get,set
    member val AfterWrite:string -> unit=ignore with get,set
    member val Serialized:string option=None with get,set
    member val BeforeSerialization:unit -> unit=ignore with get,set
    member _.Seed(name,value,priority)=
        names.Remove name |> ignore
        names.Add name
        values[name]<-value,priority
    member _.Names=names.ToArray()
    member _.Value name=match values.TryGetValue name with true,(value,_) -> value | _ -> ""
    member _.Priority name=match values.TryGetValue name with true,(_,priority) -> priority | _ -> ""
    interface CSSStyleDeclaration with
        member this.getPropertyValue name=this.BeforeRead();this.Value name
        member this.getPropertyPriority name=this.BeforeRead();this.Priority name
        member this.setProperty(name,value,priority)=
            this.BeforeWrite name
            this.Seed(name,value,defaultArg priority "")
            this.AfterWrite name
        member this.removeProperty name=
            this.BeforeWrite name
            let value=this.Value name
            values.Remove name |> ignore
            names.Remove name |> ignore
            this.AfterWrite name
            value
        member this.item index=this.BeforeRead();names[int index]
        member this.length=this.BeforeRead();uint names.Count
        member this.cssText
            with get()=this.BeforeSerialization();this.Serialized |> Option.defaultWith(fun () -> failwith "unnecessary whole style read")
            and set _=failwith "whole style replacement"
        member _.parentRule=Unchecked.defaultof<CSSRule>
        member _.cssRules=Unchecked.defaultof<CSSRuleList>

let tests=testList "scoped CSS snapshots" [
    testCase "margin expands into four canonical sides" <| fun _ ->
        Expect.equal (CssPropertyMetadata.leafNames "MARGIN")
            [|"margin-top";"margin-right";"margin-bottom";"margin-left"|] "matched engine expansion"

    testCase "border includes reset-only border image leaves" <| fun _ ->
        let leaves=CssPropertyMetadata.leafNames "border"
        Expect.equal leaves.Length 17 "twelve sides and five reset-only leaves"
        Expect.contains leaves "border-image-source" "reset source"
        Expect.contains leaves "border-image-repeat" "reset repeat"

    testCase "font includes conditional and reset-only leaves" <| fun _ ->
        let leaves=CssPropertyMetadata.leafNames "font"
        for name in ["font-size-adjust";"font-kerning";"font-optical-sizing";"font-variation-settings"] do
            Expect.contains leaves name "matched font expansion"

    testCase "vendor aliases resolve shorthands and single longhands" <| fun _ ->
        Expect.equal (CssPropertyMetadata.leafNames "-WEBKIT-BORDER-RADIUS") (CssPropertyMetadata.leafNames "border-radius") "shorthand alias"
        Expect.equal (CssPropertyMetadata.leafNames "-webkit-margin-start") [|"margin-inline-start"|] "logical alias"
        Expect.equal (CssPropertyMetadata.leafNames "font-stretch") [|"font-width"|] "canonical renamed property"

    testCase "custom property spelling remains case sensitive" <| fun _ ->
        Expect.equal (CssPropertyMetadata.leafNames "--Margin") [|"--Margin"|] "custom property"
        Expect.equal (CssPropertyMetadata.counterparts [|"--Margin"|]) [||] "no inferred logical family"

    testCase "all expands standard longhands and excludes direction" <| fun _ ->
        let leaves=CssPropertyMetadata.leafNames "all"
        Expect.contains leaves "width" "universal expansion"
        Expect.contains leaves "margin-inline-start" "logical expansion"
        Expect.isFalse (leaves |> Array.contains "all") "self reference is not a stored leaf"
        Expect.isFalse (leaves |> Array.contains "direction") "excluded direction"
        Expect.isFalse (leaves |> Array.contains "unicode-bidi") "excluded bidi"

    testCase "returned expansion arrays cannot corrupt shared metadata" <| fun _ ->
        let first=CssPropertyMetadata.leafNames "margin"
        first[0]<-"height"
        Expect.equal (CssPropertyMetadata.leafNames "margin").[0] "margin-top" "isolated result"

    for logicalFirst in [false;true] do
        testCase ("mixed priorities and mapping precedence "+string logicalFirst) <| fun _ ->
            let style=Style()
            if logicalFirst then style.Seed("margin-inline-start","11px","")
            style.Seed("margin-left","7px","important")
            if not logicalFirst then style.Seed("margin-inline-start","11px","")
            style.Seed("margin-right","8px","")
            style.Seed("height","9px","")
            let snapshot=CssPropertySnapshot.Capture(style,"margin",ignore)
            for leaf in CssPropertyMetadata.leafNames "margin" do style.Seed(leaf,"100px","")
            style.Seed("height","23px","")
            style.Seed("margin-inline-start","17px","")
            snapshot.Restore(style,(fun () -> true))
            Expect.equal (style.Value "margin-left",style.Priority "margin-left",style.Value "margin-right",style.Priority "margin-right")
                ("7px","important","8px","") "individual priorities"
            Expect.equal (style.Value "height",style.Value "margin-inline-start") ("23px","17px") "current counterpart and unrelated values"
            Expect.equal (Array.findIndex ((=) "margin-inline-start") style.Names < Array.findIndex ((=) "margin-left") style.Names)
                logicalFirst "original precedence"
            Expect.isFalse (style.Names |> Array.contains "margin-top") "absent leaf"

    testCase "new logical edits remain after restored originals" <| fun _ ->
        let style=Style()
        style.Seed("width","7px","")
        let snapshot=CssPropertySnapshot.Capture(style,"width",ignore)
        style.Seed("width","100px","")
        style.Seed("inline-size","23px","important")
        snapshot.Restore(style,(fun () -> true))
        Expect.equal (style.Value "width",style.Value "inline-size",style.Priority "inline-size") ("7px","23px","important") "current logical edit retained"
        Expect.equal style.Names [|"width";"inline-size"|] "new counterpart follows restored original"

    testCase "deleted logical counterparts are not revived" <| fun _ ->
        let style=Style()
        style.Seed("width","7px","")
        style.Seed("inline-size","11px","")
        let snapshot=CssPropertySnapshot.Capture(style,"width",ignore)
        (style:>CSSStyleDeclaration).removeProperty "inline-size" |> ignore
        style.Seed("width","100px","")
        snapshot.Restore(style,(fun () -> true))
        Expect.equal style.Names [|"width"|] "external removal retained"

    testCase "empty shorthand restores absence without unsigned underflow" <| fun _ ->
        let style=Style()
        let snapshot=CssPropertySnapshot.Capture(style,"margin",ignore)
        style.Seed("margin-top","100px","")
        snapshot.Restore(style,(fun () -> true))
        Expect.isEmpty style.Names "all affected leaves absent"

    testCase "uniform root importance remains available to the renderer" <| fun _ ->
        let style=Style()
        style.Seed("width","7px","important")
        let snapshot=CssPropertySnapshot.Capture(style,"width",ignore)
        Expect.isTrue snapshot.Important "root priority"

    testCase "restoration continues after the first setter fault" <| fun _ ->
        let style=Style()
        style.Seed("margin-left","7px","important")
        style.Seed("margin-right","8px","")
        let snapshot=CssPropertySnapshot.Capture(style,"margin",ignore)
        for leaf in CssPropertyMetadata.leafNames "margin" do style.Seed(leaf,"100px","")
        let fault=InvalidOperationException "first snapshot setter"
        style.BeforeWrite<-fun name -> if name="margin-left" then raise fault
        let mutable seen:exn option=None
        try snapshot.Restore(style,(fun () -> true)) with error -> seen<-Some error
        Expect.isTrue (seen |> Option.exists(fun error -> obj.ReferenceEquals(error,fault))) "first fault identity"
        Expect.equal (style.Value "margin-right") "8px" "later original restored"
        Expect.isFalse (style.Names |> Array.contains "margin-top") "later absent leaf removed"

    testCase "restoration stops between affected leaves after owner cancellation" <| fun _ ->
        let style=Style()
        style.Seed("margin-left","7px","")
        style.Seed("margin-right","8px","")
        let snapshot=CssPropertySnapshot.Capture(style,"margin",ignore)
        for leaf in CssPropertyMetadata.leafNames "margin" do style.Seed(leaf,"100px","")
        let mutable current=true
        style.AfterWrite<-fun _ -> current<-false
        snapshot.Restore(style,(fun () -> current))
        Expect.equal (style.Value "margin-left",style.Value "margin-right") ("7px","100px") "guarded restoration"

    testCase "capture checks ownership after every style read" <| fun _ ->
        let style=Style()
        let mutable current=true
        let mutable reads=0
        style.BeforeRead<-fun () -> reads<-reads+1;current<-false
        let ensure()=if not current then raise(ObjectDisposedException "snapshot")
        Expect.throwsT<ObjectDisposedException> (fun () -> CssPropertySnapshot.Capture(style,"margin",ensure) |> ignore) "capture interruption"
        Expect.equal reads 1 "no later access"

    testCase "counterpart read cancellation suppresses all restoration writes" <| fun _ ->
        let style=Style()
        style.Seed("width","7px","")
        style.Seed("inline-size","11px","")
        let snapshot=CssPropertySnapshot.Capture(style,"width",ignore)
        style.Seed("width","100px","")
        let mutable current=true
        style.BeforeRead<-fun () -> current<-false
        style.BeforeWrite<-fun _ -> failtest "cancelled restoration write"
        snapshot.Restore(style,(fun () -> current))
        Expect.equal (style.Value "width") "100px" "interrupted state retained"

    testCase "counterpart read faults preserve primary identity and still restore owned leaves" <| fun _ ->
        let style=Style()
        style.Seed("width","7px","")
        let snapshot=CssPropertySnapshot.Capture(style,"width",ignore)
        style.Seed("width","100px","")
        let fault=InvalidOperationException "counterpart read"
        style.BeforeRead<-fun () -> raise fault
        let mutable seen:exn option=None
        try snapshot.Restore(style,(fun () -> true)) with error -> seen<-Some error
        Expect.isTrue (seen |> Option.exists(fun error -> obj.ReferenceEquals(error,fault))) "first read fault"
        Expect.equal (style.Value "width") "7px" "owned restoration still attempted"

    testCase "pending shorthand keeps its native variable expression" <| fun _ ->
        let style=Style()
        for name in CssPropertyMetadata.leafNames "margin" do style.Seed(name,"","")
        style.Serialized<-Some "margin:var(--m) !important;"
        let snapshot=CssPropertySnapshot.Capture(style,"margin",ignore)
        for name in CssPropertyMetadata.leafNames "margin" do style.Seed(name,"100px","")
        snapshot.Restore(style,(fun () -> true))
        Expect.equal (style.Value "margin",style.Priority "margin") ("var(--m)","important") "raw shorthand restored"

    testCase "pending logical counterparts preserve their current serialized expression" <| fun _ ->
        let style=Style()
        style.Seed("margin-left","7px","")
        style.Seed("margin-inline-start","","")
        style.Seed("margin-inline-end","","")
        let snapshot=CssPropertySnapshot.Capture(style,"margin",ignore)
        for name in CssPropertyMetadata.leafNames "margin" do style.Seed(name,"100px","")
        style.Serialized<-Some "margin:100px;margin-inline:var(--current);"
        snapshot.Restore(style,(fun () -> true))
        Expect.equal (style.Value "margin-inline") "var(--current)" "current counterpart source retained"

    testCase "lossy pending serialization rejects before any write" <| fun _ ->
        let style=Style()
        for name in CssPropertyMetadata.leafNames "margin" do style.Seed(name,"","")
        style.Serialized<-Some "margin-top:;margin-right:;margin-bottom:;margin-left:7px !important;"
        style.BeforeWrite<-fun _ -> failtest "lossy construction dispatched"
        Expect.throwsT<NotSupportedException> (fun () -> CssPropertySnapshot.Capture(style,"margin",ignore) |> ignore) "opaque snapshot required"

    testCase "pending shorthand crossing a captured longhand rejects before dispatch" <| fun _ ->
        let style=Style()
        for name in CssPropertyMetadata.leafNames "margin" do style.Seed(name,"","")
        style.Serialized<-Some "margin:var(--m);"
        style.BeforeWrite<-fun _ -> failtest "cross-scope construction dispatched"
        Expect.throwsT<NotSupportedException> (fun () -> CssPropertySnapshot.Capture(style,"margin-left",ignore) |> ignore) "opaque leaf snapshot required"

    testCase "pending serialization read checks ownership before returning a snapshot" <| fun _ ->
        let style=Style()
        for name in CssPropertyMetadata.leafNames "margin" do style.Seed(name,"","")
        style.Serialized<-Some "margin:var(--m);"
        let mutable current=true
        style.BeforeSerialization<-fun () -> current<-false
        let ensure()=if not current then raise(ObjectDisposedException "snapshot")
        Expect.throwsT<ObjectDisposedException> (fun () -> CssPropertySnapshot.Capture(style,"margin",ensure) |> ignore) "post-serialization ownership"

    testCase "native serialized declaration boundaries retain nested and quoted delimiters" <| fun _ ->
        let text="""margin:var(--m)!IMPORTANT;--x:';:!important';background-image:url("data:a;b:c");--block:{a:b;c:d;};"""
        Expect.equal (CssSerialization.declarations text)
            [|struct("margin","var(--m)","important");struct("--x","';:!important'","");
              struct("background-image","url(\"data:a;b:c\")","");struct("--block","{a:b;c:d;}","")|] "raw values retained"

    testCase "native serialization scanner respects comments and escaped delimiters" <| fun _ ->
        let text="""--x:a\;b;width:7px/*;:!important*/;height:8px"""
        Expect.equal (CssSerialization.declarations text)
            [|struct("--x",@"a\;b","");struct("width","7px/*;:!important*/","");struct("height","8px","")|] "comments and escape scope"

    testCase "empty native serialization yields no declarations" <| fun _ ->
        Expect.isEmpty (CssSerialization.declarations " ; ") "empty serialization"
]
