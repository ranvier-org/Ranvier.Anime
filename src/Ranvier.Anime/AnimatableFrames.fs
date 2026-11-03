namespace Ranvier.Anime

open System
open System.Collections.Generic
open System.Runtime.CompilerServices
open Ranvier.Dom

type private AnimatableFrameEntry() =
    member val Callback:(float -> unit) option=None with get,set
    member val Node:LinkedListNode<AnimatableFrameEntry> option=None with get,set
    member val Version=0L with get,set
    member val Closed=false with get,set

/// <summary>Orders animatable roots on one host frame using the source engine's cached-next traversal.</summary>
type internal AnimatableFrameQueue(scheduler:FrameScheduler) =
    let entries=LinkedList<AnimatableFrameEntry>()
    let mutable pending:IDisposable option=None
    let mutable pumping=false
    let mutable lastTick=scheduler.Now()
    let pruneClosed () =
        let mutable cursor=entries.First
        while not(isNull cursor) do
            let next=cursor.Next
            if cursor.Value.Closed then
                cursor.Value.Node<-None
                entries.Remove cursor
            cursor<-next
    let rec schedule () =
        if entries.Count>0 && pending.IsNone && not pumping then
            pending<-Some(scheduler.Frame(fun now ->
                pending<-None
                pumping<-true
                let mutable failure:exn option=None
                try
                    let elapsed=now-lastTick
                    let duration=1000./240.
                    if elapsed+Math.Min(duration*0.25,4.)>=duration then
                        lastTick<-if elapsed>=duration then now-elapsed%duration else now
                        let mutable cursor=entries.First
                        while not(isNull cursor) do
                            let next=cursor.Next
                            let entry=cursor.Value
                            match entry.Callback with
                            | Some callback ->
                                entry.Callback<-None
                                try callback now with error -> if failure.IsNone then failure<-Some error
                            | None ->
                                entries.Remove cursor
                                entry.Node<-None
                            cursor<-next
                finally
                    pumping<-false
                    pruneClosed()
                    try schedule() with error -> if failure.IsNone then failure<-Some error
                failure |> Option.iter(fun error -> System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw())))
    /// <summary>Creates an independent root identity; repeated frame requests retain its linked-list position.</summary>
    member _.CreateScheduler() =
        let entry=AnimatableFrameEntry()
        let mutable disposed=false
        let work=HashSet<IDisposable>()
        let live () = if disposed then raise(ObjectDisposedException(nameof FrameScheduler))
        let scheduleWork (schedule:(unit -> unit) -> IDisposable) callback =
            live()
            let mutable active=true
            let mutable registration:IDisposable option=None
            let mutable inner:IDisposable option=None
            let finish dispose =
                active<-false
                let current=registration
                registration<-None
                current |> Option.iter(fun lease -> work.Remove lease |> ignore)
                let previous=inner
                inner<-None
                if dispose then previous |> Option.iter(fun lease -> lease.Dispose())
            let lease={new IDisposable with member _.Dispose()=if active then finish true}
            registration<-Some lease
            work.Add lease |> ignore
            try
                let pending=schedule(fun () ->
                    if active then
                        finish false
                        if not disposed then callback())
                if active then inner<-Some pending else pending.Dispose()
                lease
            with _ ->
                finish true
                reraise()
        { new FrameScheduler with
            member _.Now()=scheduler.Now()
            member _.Frame(callback) =
                live()
                entry.Version<-entry.Version+1L
                let expected=entry.Version
                entry.Callback<-Some callback
                if entry.Node.IsNone then entry.Node<-Some(entries.AddLast entry)
                schedule()
                { new IDisposable with
                    member _.Dispose() = if expected=entry.Version then entry.Callback<-None }
            member _.Timeout(delay,callback)=scheduleWork (fun action -> scheduler.Timeout(delay,action)) callback
            member _.Microtask(callback)=scheduleWork scheduler.Microtask callback
            member _.Dispose()=
                disposed<-true
                entry.Closed<-true
                entry.Version<-entry.Version+1L
                entry.Callback<-None
                let mutable failure:exn option=None
                for lease in work |> Seq.toArray do
                    try lease.Dispose() with error -> if failure.IsNone then failure<-Some error
                work.Clear()
                if not pumping then
                    pruneClosed()
                    if entries.Count=0 then
                        let previous=pending
                        pending<-None
                        previous |> Option.iter(fun lease ->
                            try lease.Dispose() with error -> if failure.IsNone then failure<-Some error)
                failure |> Option.iter(fun error -> System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw()) }

/// <summary>Shares animatable root ordering for each existing Ranvier frame scheduler.</summary>
module internal AnimatableFrames =
    let private queues=ConditionalWeakTable<FrameScheduler,AnimatableFrameQueue>()
    /// <summary>Allocates one root identity in the scheduler's shared animatable frame queue.</summary>
    let createRoot scheduler =
        if isNull(box scheduler) then nullArg "scheduler"
        queues.GetValue(scheduler,fun scheduler -> AnimatableFrameQueue(scheduler)).CreateScheduler()
