namespace Ranvier.Anime.Tests

open System
open Expecto
open Ranvier
open Ranvier.Anime
open Ranvier.Dom

type private FacadeManualFrames() =
    let callbacks=System.Collections.Generic.Dictionary<int,float -> unit>()
    let mutable next=0
    let mutable now=0.
    member _.Scheduler=
        {new FrameScheduler with
            member _.Now()=now
            member _.Frame callback =
                next<-next+1
                let token=next
                callbacks[token]<-callback
                {new IDisposable with member _.Dispose()=callbacks.Remove token |> ignore}
            member _.Timeout(_,_)={new IDisposable with member _.Dispose()=()}
            member _.Microtask _={new IDisposable with member _.Dispose()=()}
         interface IDisposable with member _.Dispose()=callbacks.Clear()}
    member _.Advance()=
        now<-now+17.
        let work=callbacks.Values |> Seq.toArray
        callbacks.Clear()
        for callback in work do callback now
    member _.Pending=callbacks.Count


module NativeDraggableLifetimeTests =
    let private make scheduler (x:float ref) (y:float ref) log captures =
        let controller=ScopeRegistration.run ignore (fun()->new Draggable(scheduler,NumericProperty((fun()->x.Value),(fun value->x.Value<-value;log "axis")),NumericProperty((fun()->y.Value),(fun value->y.Value<-value;log "axis"))))
        let facade=new NativeDraggable(controller,None,None,captures)
        ScopeRegistration.register facade.Revert
        NativeDraggable.own facade
    let lifetimeTests = testList "native draggable composite lifetime" [
        testCase "scope restores axes then captures before owner cancellation" <| fun _ ->
            use graph=new Graph()
            use active=graph.Activate()
            use scope=new Scope()
            let frames=FacadeManualFrames()
            let x,y=ref 12.,ref 23.
            let log=ResizeArray<string>()
            let mutable facade:NativeDraggable option=None
            let captures=[|{Restore=(fun current->if current() then log.Add "css");Cancel=(fun()->log.Add "cancel-capture")}|]
            scope.Add(fun _ -> facade<-Some(make frames.Scheduler x y log.Add captures);None) |> ignore
            facade.Value.X<-40.
            facade.Value.Y<-50.
            log.Clear()
            scope.Revert()
            Expect.equal (x.Value,y.Value) (12.,23.) "original axes restored"
            Expect.equal (log |> Seq.filter((<>) "axis") |> Seq.toList) ["css";"cancel-capture"] "CSS restore precedes cancellation"
            Expect.isTrue facade.Value.Cancelled "actual scope owner ended lifetime"
            // Anime.js removes cancelled tickables on the next engine update and releases the frame request one frame later.
            frames.Advance()
            Expect.equal frames.Pending 1 "source engine retains one cleanup frame"
            frames.Advance()
            Expect.equal frames.Pending 0 "all source roots removed"
        testCase "suppressed hidden resources leave exactly one outer registration" <| fun _ ->
            use owner=new Owner()
            let frames=FacadeManualFrames()
            let registered=ResizeArray<unit -> unit>()
            let facade=ScopeRegistration.runOwned registered.Add (fun()->owner) (fun()->true) (fun()->make frames.Scheduler (ref 1.) (ref 2.) ignore [||])
            Expect.equal registered.Count 1 "one outer revert"
            registered[0]()
            Expect.isTrue facade.Cancelled "outer owns hidden controller"
        testCase "capture failure still cancels every child" <| fun _ ->
            use graph=new Graph()
            use active=graph.Activate()
            let frames=FacadeManualFrames()
            let log=ResizeArray<string>()
            let captures=[|{Restore=(fun _->log.Add "restore1";failwith "capture");Cancel=(fun()->log.Add "cancel1")};{Restore=(fun _->log.Add "restore2");Cancel=(fun()->log.Add "cancel2")}|]
            let facade=make frames.Scheduler (ref 1.) (ref 2.) ignore captures
            Expect.throws (fun()->facade.Revert()) "preserves cleanup error"
            Expect.equal (Seq.toList log) ["restore1";"restore2";"cancel1";"cancel2"] "complete cleanup"
            Expect.isTrue facade.Cancelled "terminal after failure"
        testCase "reentrant cancel stops later captured property restoration" <| fun _ ->
            use graph=new Graph()
            use active=graph.Activate()
            let frames=FacadeManualFrames()
            let mutable facade:NativeDraggable option=None
            let log=ResizeArray<string>()
            let captures=[|{Restore=(fun _->facade.Value.Cancel());Cancel=ignore};{Restore=(fun _->log.Add "stale");Cancel=ignore}|]
            let current=make frames.Scheduler (ref 1.) (ref 2.) ignore captures
            facade<-Some current
            current.Revert()
            Expect.equal log.Count 0 "no later restoration after terminal callback"
        testCase "actual selected owner disposal prevents future writes" <| fun _ ->
            use owner=new Owner()
            let frames=FacadeManualFrames()
            let x,y=ref 1.,ref 2.
            let facade=ScopeRegistration.runOwned ignore (fun()->owner) (fun()->true) (fun()->make frames.Scheduler x y ignore [||])
            facade.Controller.Grab(0.,0.)
            facade.Controller.Move(30.,20.)
            owner.Dispose()
            frames.Advance()
            Expect.equal (x.Value,y.Value) (1.,2.) "queued root cancelled by actual owner"
            Expect.isTrue facade.Cancelled "facade reflects real owner lifetime"
    ]

