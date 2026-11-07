namespace Ranvier.Anime

open System
open System.Collections.Generic
open Ranvier.Anime.ValueInterpolation

/// <summary>Mutable playback geometry and sampled cache for one frame, linked across children of the same timeline.</summary>
[<AllowNullLiteral>]
type internal FrameSibling(definition:FrameTween,child:obj) =
    /// <summary>The trimmed immutable endpoints and authoring metadata used by this playback node.</summary>
    member val Definition=definition with get,set
    /// <summary>Owning animation identity, distinct from target identity.</summary>
    member _.Child=child
    /// <summary>The live node represented by a construction clone; null on new or live nodes.</summary>
    member val Original:FrameSibling=null with get,set
    /// <summary>Local start; binding extracts the child's initial delay before publication.</summary>
    member val Start=definition.Start with get,set
    /// <summary>Frame delay after child initial-delay extraction, in milliseconds.</summary>
    member val Delay=definition.Delay with get,set
    /// <summary>Untruncated duration used as the interpolation denominator, in milliseconds.</summary>
    member val Update=definition.UpdateDuration with get,set
    /// <summary>Active duration after replacement truncation; overridden nodes retain source epsilon.</summary>
    member val Change=definition.UpdateDuration with get,set
    /// <summary>Precision-matched absolute frame start across timeline children.</summary>
    member val AbsoluteStart=definition.AbsoluteStart with get,set
    /// <summary>Precision-matched preceding end before this frame's delay.</summary>
    member val AbsoluteUpdateStart=definition.AbsoluteUpdateStart with get,set
    /// <summary>Absolute untruncated end used when a later sibling takes over.</summary>
    member val AbsoluteEnd=definition.AbsoluteEnd with get,set
    /// <summary>Clamped local sample history used by source write eligibility.</summary>
    member val Time=0. with get,set
    /// <summary>Previous replacement sibling across children, including retained overridden nodes.</summary>
    member val Previous:FrameSibling=null with get,set
    /// <summary>Next replacement sibling across children, including retained overridden nodes.</summary>
    member val Next:FrameSibling=null with get,set
    /// <summary>Whether replacement permanently suppresses this node's dispatch.</summary>
    member val Overridden=false with get,set
    /// <summary>Whether a later replacement shortened or overrode this node.</summary>
    member val Overlapped=false with get,set
    /// <summary>Most recent modified output cache, separate from raw destination metadata.</summary>
    member val Value=definition.CachedEnd with get,set
    /// <summary>Whether the owning child has more than one iteration.</summary>
    member val Repeats=false with get,set
    /// <summary>Child total duration minus one iteration, including repeated gaps.</summary>
    member val LoopExtension=0. with get,set
    /// <summary>Marks an overridden frame using source epsilon geometry and history.</summary>
    member this.Override() =
        this.Overlapped<-true
        this.Overridden<-true
        this.Change<-1e-11
        this.Time<-1e-11
    /// <summary>Snapshots raw endpoints and current output without exposing the retained component cache.</summary>
    member this.Snapshot() =
        let frame=this.Definition
        FrameTween(frame.Target,frame.Name,frame.First,frame.Last,this.Start,this.Delay,this.Update,
                   this.AbsoluteStart,this.AbsoluteUpdateStart,this.AbsoluteEnd,frame.HasFrom,frame.Ease,frame.Modifier,
                   this.Value.CopyComponents())
    /// <summary>Captures a publication guard that rejects intervening playback or geometry changes.</summary>
    member this.Observe() =
        let definition=this.Definition
        let value=this.Value
        let previous=this.Previous
        let next=this.Next
        let geometry=(this.Start,this.Delay,this.Update,this.Change,this.AbsoluteStart,this.AbsoluteUpdateStart,this.AbsoluteEnd,this.Time,this.LoopExtension)
        let flags=(this.Overridden,this.Overlapped,this.Repeats)
        fun () ->
            obj.ReferenceEquals(definition,this.Definition) && obj.ReferenceEquals(value,this.Value) &&
            obj.ReferenceEquals(previous,this.Previous) && obj.ReferenceEquals(next,this.Next) &&
            geometry=(this.Start,this.Delay,this.Update,this.Change,this.AbsoluteStart,this.AbsoluteUpdateStart,this.AbsoluteEnd,this.Time,this.LoopExtension) &&
            flags=(this.Overridden,this.Overlapped,this.Repeats)
    /// <summary>Copies mutable state and maps links to another registry generation.</summary>
    member this.CopyTo(target:FrameSibling,resolve:FrameSibling -> FrameSibling) =
        target.Definition<-this.Definition
        target.Start<-this.Start
        target.Delay<-this.Delay
        target.Update<-this.Update
        target.Change<-this.Change
        target.AbsoluteStart<-this.AbsoluteStart
        target.AbsoluteUpdateStart<-this.AbsoluteUpdateStart
        target.AbsoluteEnd<-this.AbsoluteEnd
        target.Time<-this.Time
        target.Previous<-resolve this.Previous
        target.Next<-resolve this.Next
        target.Overridden<-this.Overridden
        target.Overlapped<-this.Overlapped
        target.Value<-this.Value
        target.Repeats<-this.Repeats
        target.LoopExtension<-this.LoopExtension
    /// <summary>Stages finite stretched geometry while retaining endpoints, current cache and cross-child links.</summary>
    member this.Scaled(scale:float) =
        let normalize value =
            if value<=1e-11 then 1e-11
            else let rounded=PropertyNumbers.round 1e11 value in if Double.IsPositiveInfinity rounded then 1e12 else rounded
        let next=FrameSibling(this.Definition,this.Child)
        this.CopyTo(next,id)
        next.Start<-this.Start*scale
        next.Delay<-this.Delay*scale
        next.Update<-normalize(this.Update*scale)
        next.Change<-normalize(this.Change*scale)
        next.AbsoluteStart<-this.AbsoluteStart*scale
        next.AbsoluteUpdateStart<-this.AbsoluteUpdateStart*scale
        next.AbsoluteEnd<-this.AbsoluteEnd*scale
        next.Time<-this.Time*scale
        next.LoopExtension<-this.LoopExtension*scale
        if [next.Start;next.Delay;next.AbsoluteStart;next.AbsoluteUpdateStart;next.AbsoluteEnd;next.Time;next.LoopExtension]
           |> List.exists(fun value -> not(Double.IsFinite value)) then invalidArg "scale" "Scaled frame geometry must remain finite."
        next
    /// <summary>An invariant diagnostic without formatting endpoint data or callbacks.</summary>
    override this.ToString()="FrameSibling("+this.Definition.Name+")"

/// <summary>Source ordering and replacement geometry shared by standalone and same-parent playback nodes.</summary>
module internal FrameSiblingComposition =
    /// <summary>Selects the latest live predecessor and overrides future nodes encountered by the source forward scan.</summary>
    let select (nodes:ResizeArray<FrameSibling>) absoluteStart =
        let mutable selected:FrameSibling=null
        let mutable index=0
        while index<nodes.Count && nodes[index].AbsoluteStart<=absoluteStart do
            if not nodes[index].Overridden then selected<-nodes[index]
            index<-index+1
            if index<nodes.Count && nodes[index].AbsoluteStart>=absoluteStart then
                while index<nodes.Count do
                    nodes[index].Override()
                    index<-index+1
        selected
    /// <summary>Inserts a replacement node, preserving overridden tail entries and source overlap/loop truncation.</summary>
    let add (nodes:ResizeArray<FrameSibling>) (node:FrameSibling) =
        let mutable previous=nodes.Count-1
        while previous>=0 && (nodes[previous].Overridden || nodes[previous].AbsoluteStart>node.AbsoluteStart) do previous<-previous-1
        let prior=if previous<0 then null else nodes[previous]
        let next=if previous+1<nodes.Count then nodes[previous+1] else null
        node.Previous<-prior
        node.Next<-next
        if not(isNull prior) then prior.Next<-node
        if not(isNull next) then next.Previous<-node
        nodes.Insert(previous+1,node)
        if not(isNull prior) then
            if not(obj.ReferenceEquals(prior.Child,node.Child)) && prior.Repeats && prior.AbsoluteEnd+prior.LoopExtension>node.AbsoluteStart then
                prior.Override()
                let mutable earlier=prior.Previous
                while not(isNull earlier) && obj.ReferenceEquals(earlier.Child,prior.Child) do
                    earlier.Override()
                    earlier<-earlier.Previous
            if prior.AbsoluteEnd>node.AbsoluteUpdateStart then
                let offset=prior.AbsoluteEnd-(prior.Start+prior.Update)
                let changed=PropertyNumbers.round 1e12 (node.AbsoluteUpdateStart-offset-prior.Start)
                prior.Change<-changed
                prior.Time<-changed
                prior.Overlapped<-true
                if changed<1e-11 then prior.Override()

module private FrameSiblingGroups =
    let comparer =
        { new IEqualityComparer<struct(obj*string)> with
            member _.Equals(struct(a,nameA),struct(b,nameB))=obj.ReferenceEquals(a,b) && StringComparer.Ordinal.Equals(nameA,nameB)
            member _.GetHashCode(struct(target,name))=HashCode.Combine(Runtime.CompilerServices.RuntimeHelpers.GetHashCode target,StringComparer.Ordinal.GetHashCode name) }
    let relink (nodes:ResizeArray<FrameSibling>) =
        for index in 0..nodes.Count-1 do
            nodes[index].Previous<-(if index=0 then null else nodes[index-1])
            nodes[index].Next<-(if index+1=nodes.Count then null else nodes[index+1])

/// <summary>A private construction generation; existing sibling mutations publish only after a successful guarded commit.</summary>
type internal FrameSiblingTransaction internal
    (keys:struct(obj*string) array,groups:Dictionary<struct(obj*string),ResizeArray<FrameSibling>>,
     canWrite:unit -> bool,publish:Dictionary<struct(obj*string),ResizeArray<FrameSibling>> -> bool) =
    let child=obj()
    let added=ResizeArray<FrameSibling>()
    let mutable finished=false
    let key property =
        if property<0 || property>=keys.Length then invalidArg "property" "Unknown captured property index."
        keys[property]
    let ensure()=if finished then invalidOp "Sibling construction is already closed."
    /// <summary>The current list tail used by source input providers before frame timing is resolved.</summary>
    member _.Tail(property:int) =
        ensure()
        match groups.TryGetValue(key property) with
        | true,nodes when nodes.Count>0 -> Some(nodes[nodes.Count-1].Snapshot())
        | _ -> None
    /// <summary>Selects the latest live predecessor and stages source future-sibling overrides at the resolved absolute start.</summary>
    member _.Select(property:int,absoluteStart:float) =
        ensure()
        match groups.TryGetValue(key property) with
        | true,nodes ->
            let selected=FrameSiblingComposition.select nodes absoluteStart
            if isNull selected then None else Some(selected.Snapshot())
        | _ -> None
    /// <summary>Stages one compiled frame and source replace truncation across local and earlier child frames.</summary>
    member _.Register(property:int,frame:FrameTween) =
        ensure()
        let key=key property
        let nodes=
            match groups.TryGetValue key with
            | true,nodes -> nodes
            | _ -> let nodes=ResizeArray<FrameSibling>() in groups.Add(key,nodes);nodes
        let node=FrameSibling(frame,child)
        FrameSiblingComposition.add nodes node
        added.Add node
    /// <summary>Compiler hooks retain the pre-timing input tail and resolved-start selection as separate operations.</summary>
    member this.Compilation:FrameCompilationSiblings =
        {Tail=this.Tail;Select=(fun property start -> this.Select(property,start));Register=(fun property frame -> this.Register(property,frame))}
    /// <summary>Applies final initial-delay trimming and child loop metadata to staged nodes.</summary>
    member _.Bind(program:FrameProgram,iterations:float,totalDuration:float) =
        ensure()
        if program.Tweens.Length<>added.Count then invalidArg "program" "Compiled frame count differs from staged siblings."
        for index in 0..added.Count-1 do
            let node=added[index]
            let frame=program.Tweens[index]
            node.Definition<-frame
            node.Start<-frame.Start
            node.Delay<-frame.Delay
            node.Repeats<-iterations>1.
            node.LoopExtension<-totalDuration-program.IterationDuration
        added.ToArray()
    /// <summary>Publishes all staged links and geometry atomically if construction and registry generations remain current.</summary>
    member _.Commit() =
        ensure()
        finished<-true
        try canWrite() && publish groups
        finally
            groups.Clear()
            added.Clear()
    /// <summary>Discards this construction generation without altering existing playback nodes.</summary>
    member _.Rollback()=
        finished<-true
        groups.Clear()
        added.Clear()

/// <summary>Mutable same-timeline replace siblings keyed by captured target identity and ordinal property name.</summary>
type internal FrameSiblingRegistry() =
    let groups=Dictionary<struct(obj*string),ResizeArray<FrameSibling>>(FrameSiblingGroups.comparer)
    let mutable generation=0
    /// <summary>Captures an isolated construction view; caller guards reject reentrant parent operations at commit.</summary>
    member _.Begin(keys:struct(obj*string) array,canWrite:unit -> bool) =
        if isNull keys then nullArg "keys"
        if isNull(box canWrite) then nullArg "canWrite"
        let keys=Array.copy keys
        for struct(target,name) in keys do
            if isNull target || String.IsNullOrWhiteSpace name then invalidArg "keys" "Target identities and property names are required."
        let expected=generation
        let observations=ResizeArray<unit -> bool>()
        let copies=Dictionary<FrameSibling,FrameSibling>()
        for nodes in groups.Values do
            for node in nodes do
                observations.Add(node.Observe())
                let copy=FrameSibling(node.Definition,node.Child)
                copy.Original<-node
                copies.Add(node,copy)
        let clone node=if isNull node then null else copies[node]
        let captured=Dictionary<struct(obj*string),ResizeArray<FrameSibling>>(FrameSiblingGroups.comparer)
        for KeyValue(key,nodes) in groups do
            let values=ResizeArray<FrameSibling>()
            for node in nodes do
                let copy=copies[node]
                node.CopyTo(copy,clone)
                values.Add copy
            captured.Add(key,values)
        let publish (staged:Dictionary<struct(obj*string),ResizeArray<FrameSibling>>) =
            if expected<>generation || not(canWrite()) || observations |> Seq.exists(fun unchanged -> not(unchanged())) then false
            else
                let resolve (node:FrameSibling)=
                    if isNull node then null
                    elif isNull node.Original then node
                    else node.Original
                for nodes in staged.Values do for node in nodes do node.CopyTo(resolve node,resolve)
                groups.Clear()
                for KeyValue(key,nodes) in staged do groups.Add(key,ResizeArray<FrameSibling>(nodes |> Seq.map resolve))
                generation<-generation+1
                true
        FrameSiblingTransaction(keys,captured,canWrite,publish)
    /// <summary>Unlinks cancelled child nodes while retaining prior source truncation and override history.</summary>
    member _.Remove(nodes:FrameSibling array) =
        let removed=HashSet<FrameSibling>(nodes)
        let empty=ResizeArray<struct(obj*string)>()
        for KeyValue(key,values) in groups do
            values.RemoveAll(fun node -> removed.Contains node) |> ignore
            FrameSiblingGroups.relink values
            if values.Count=0 then empty.Add key
        for key in empty do groups.Remove key |> ignore
        for node in nodes do node.Previous<-null;node.Next<-null
        generation<-generation+1
    /// <summary>Releases all cross-child links when the owning timeline closes.</summary>
    member _.Clear() =
        for nodes in groups.Values do for node in nodes do node.Previous<-null;node.Next<-null
        groups.Clear()
        generation<-generation+1
