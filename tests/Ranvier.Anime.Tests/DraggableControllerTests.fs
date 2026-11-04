namespace Ranvier.Anime.Tests

open System
open System.Collections.Generic
open Expecto
open Ranvier.Dom
open Ranvier.Anime

type private DragManualFrames() =
    let frames=Dictionary<int,float -> unit>()
    let mutable identifier=0
    let mutable time=0.
    let scheduler=
        { new FrameScheduler with
            member _.Now()=time
            member _.Frame callback =
                identifier<-identifier+1
                let token=identifier
                frames[token]<-callback
                {new IDisposable with member _.Dispose()=frames.Remove token |> ignore}
            member _.Timeout(_,_)={new IDisposable with member _.Dispose()=()}
            member _.Microtask _={new IDisposable with member _.Dispose()=()}
          interface IDisposable with member _.Dispose()=frames.Clear() }
    member _.Scheduler=scheduler
    member _.Step(milliseconds:float) =
        time<-time+milliseconds
        let callbacks=frames.Values |> Seq.toArray
        frames.Clear()
        for callback in callbacks do callback time
    member _.Pending=frames.Count

module DraggableControllerTests =
    let controllerTests=
        testList "Draggable controller" [
            testCase "strict threshold and unchanged pointer emit one render update" <| fun _ ->
                let frames=DragManualFrames()
                let mutable x=0.
                let mutable y=0.
                let mutable updates=0
                use controller=new Draggable(frames.Scheduler,NumericProperty((fun()->x),(fun value->x<-value)),NumericProperty((fun()->y),(fun value->y<-value)),onUpdate=(fun _->updates<-updates+1))
                controller.Grab(0.,0.)
                controller.Move(3.,0.)
                Expect.isFalse controller.Dragged "Threshold equality"
                controller.Move(10.,0.)
                for _ in 1..6 do frames.Step 17.
                controller.Move(10.,0.)
                for _ in 1..6 do frames.Step 17.
                Expect.equal (x,y,updates) (10.,0.,1) "Only changed coordinates emit onUpdate"
            testCase "cancel drops pending frames and retains the last value" <| fun _ ->
                let frames=DragManualFrames()
                let mutable x=0.
                let mutable y=0.
                let mutable writes=0
                let horizontal=NumericProperty((fun()->x),(fun value->x<-value;writes<-writes+1))
                use controller=new Draggable(frames.Scheduler,horizontal,NumericProperty((fun()->y),(fun value->y<-value)))
                controller.Grab(0.,0.)
                controller.Move(40.,20.)
                controller.Cancel()
                let before=writes,x,y
                for _ in 1..12 do frames.Step 17.
                Expect.equal (writes,x,y) before "Cancellation prevents later writes"
                Expect.equal frames.Pending 0 "All owned frames removed"
            testCase "disabled axis stays captured while enabled axis moves" <| fun _ ->
                let frames=DragManualFrames()
                let mutable x=7.
                let mutable y=0.
                let settings ()={DraggableSettings.Default with XEnabled=false}
                use controller=new Draggable(frames.Scheduler,NumericProperty((fun()->x),(fun value->x<-value)),NumericProperty((fun()->y),(fun value->y<-value)),settings=settings)
                controller.Grab(0.,0.)
                controller.Move(30.,20.)
                for _ in 1..6 do frames.Step 17.
                Expect.equal (x,y) (7.,20.) "Disabled x axis"
            testCase "origin measurement restores captured coordinates" <| fun _ ->
                let frames=DragManualFrames()
                let mutable x=20.
                let mutable y=30.
                use controller=new Draggable(frames.Scheduler,NumericProperty((fun()->x),(fun value->x<-value)),NumericProperty((fun()->y),(fun value->y<-value)))
                let measured=controller.WithOrigin(fun()->struct(x,y))
                Expect.equal measured (struct(0.,0.)) "Origin measured synchronously"
                Expect.equal (x,y) (20.,30.) "Original coordinates restored"
            testCase "onSettle can reset both axes without recursion" <| fun _ ->
                let frames=DragManualFrames()
                let mutable x=0.
                let mutable y=0.
                let mutable settled=0
                use controller=new Draggable(frames.Scheduler,NumericProperty((fun()->x),(fun value->x<-value)),NumericProperty((fun()->y),(fun value->y<-value)),
                    onSettle=(fun self->settled<-settled+1;if settled=1 then self.Reset()))
                controller.AnimateTo(100.,100.,10u,id)
                for _ in 1..12 do frames.Step 17.
                Expect.equal (x,y) (0.,0.) "Reset rendered both axes"
                Expect.isGreaterThan settled 0 "onSettle ran"
        ]

