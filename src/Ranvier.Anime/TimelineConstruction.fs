namespace Ranvier.Anime

open System
open Ranvier.Anime.ValueInterpolation

/// <summary>Guarded access to timeline scheduling, insertion position, pending lifetime and staged siblings during construction.</summary>
type internal TimelineChildContext(scheduler:Ranvier.Dom.FrameScheduler,offset:float,canWrite:unit -> bool,
                                   attach:IDisposable -> unit,siblings:FrameSiblingRegistry,isMuted:unit -> bool) =
    /// <summary>The timeline's scheduler; children share its clock thread.</summary>
    member _.Scheduler=scheduler
    /// <summary>The persistent mute gate inherited by an owned clock before initialization.</summary>
    member _.IsMuted()=isMuted()
    /// <summary>Resolved signed insertion position before child delay, including the zero-duration epsilon adjustment.</summary>
    member _.Offset=offset
    /// <summary>Whether the parent and construction generation still permit user effects and publication.</summary>
    member _.CanWrite()=canWrite()
    /// <summary>Registers child cancellation before getters or other construction callbacks can dispose the parent.</summary>
    member _.Attach(child:IDisposable)=attach child
    /// <summary>Captures a staged sibling transaction for reference identities and ordinal property names.</summary>
    member _.BeginSiblings(keys)=siblings.Begin(keys,canWrite)
    /// <summary>Unlinks a cancelled child's replacement nodes while preserving prior source override history.</summary>
    member _.RemoveSiblings(nodes)=siblings.Remove nodes

/// <summary>A constructed child awaiting guarded timeline publication.</summary>
type internal TimelinePreparedChild<'T>(clock:Timer,renderVersion:unit -> int64,commit:unit -> bool,result:'T,
                                       restore:(unit -> bool) -> unit,?removeTarget:(obj -> struct(bool*bool))) =
    let removeTargetFn = defaultArg removeTarget (fun _ -> struct(false, false))
    /// <summary>The child controller whose scheduling and disposal belong to the timeline.</summary>
    member _.Clock=clock
    /// <summary>The current target dispatch generation, unchanged by cache-only or skipped samples.</summary>
    member _.RenderVersion()=renderVersion()
    /// <summary>Publishes staged sibling changes if all captured guards still hold.</summary>
    member _.Commit()=commit()
    /// <summary>The factory's observable child handle.</summary>
    member _.Result=result
    /// <summary>Restores captured properties while the parent's lifetime and operation remain current.</summary>
    member _.Restore(current)=restore current
    /// <summary>Removes a matching target identity, returning whether any target matched and whether all targets were removed.</summary>
    member _.RemoveTarget(candidate:obj)=removeTargetFn candidate

/// <summary>Owned driver installation with staged same-timeline replacement publication.</summary>
module internal TimelineKeyframeInstallation =
    /// <summary>Compiles guarded captured properties and installs one owned driver with optional staged replacement siblings.</summary>
    let private installCore finiteScalar (writeScalarOpt:(int -> string -> float -> unit) option)
                (context:TimelineChildContext,animation:KeyframeAnimation,defaults:FrameDefaults,properties:FrameProperty array,
                 keys:struct(obj*string) array,resolve,original,write,css,precision,options:KeyframeAnimationOptions,current) =
        let replacing=options.Composition=KeyframeComposition.Replace
        let transaction=if replacing then Some(context.BeginSiblings keys) else None
        let mutable handedOff=false
        try
            let defaults={defaults with Offset=context.Offset}
            let program=
                match transaction with
                | Some siblings -> KeyframeCompilation.compileWithSiblings(defaults,properties,resolve,original,siblings.Compilation,current)
                | None -> KeyframeCompilation.compileDynamic(false,defaults,properties,resolve,original,current)
            match program with
            | Some program when current() ->
                let iterations=match options.Playback.Repeat with PlaybackRepeat.Count count -> float count+1. | PlaybackRepeat.Forever -> infinity
                let total=PlaybackTiming(program.IterationDuration,iterations,program.Delay,options.Playback.LoopDelay).Duration
                let nodes=transaction |> Option.map(fun siblings -> siblings.Bind(program,iterations,total))
                animation.SetCleanup(fun () ->
                    transaction |> Option.iter(fun siblings -> siblings.Rollback())
                    nodes |> Option.iter context.RemoveSiblings)
                let maxTarget =
                    let mutable m = -1
                    for i = 0 to properties.Length - 1 do
                        if properties[i].Target > m then m <- properties[i].Target
                    m
                let targetKeys = Array.zeroCreate<obj> (maxTarget + 1)
                let bound = min properties.Length keys.Length
                for i = 0 to bound - 1 do
                    let struct(k, _) = keys[i]
                    let t = properties[i].Target
                    if uint t < uint targetKeys.Length then targetKeys[t] <- k
                let removedTargets = Array.create (maxTarget + 1) false
                let guardedWrite target name output =
                    if uint target >= uint removedTargets.Length || not removedTargets[target] then
                        write target name output
                let guardedWriteScalar =
                    writeScalarOpt |> Option.map(fun ws ->
                        fun target name value ->
                            if uint target >= uint removedTargets.Length || not removedTargets[target] then
                                ws target name value)
                let removeTarget (candidate: obj) =
                    let mutable matched = false
                    for t = 0 to targetKeys.Length - 1 do
                        let keyObj = targetKeys[t]
                        let isMatch =
                            not (isNull keyObj) &&
                            (Object.ReferenceEquals(keyObj, candidate) ||
                             (match candidate with
                              | :? Ranvier.Dom.ElementCSSInlineStyle as styled ->
                                  Object.ReferenceEquals(keyObj, box styled.style)
                              | _ -> false))
                        if isMatch && not removedTargets[t] then
                            removedTargets[t] <- true
                            matched <- true
                    let allRemoved = targetKeys.Length > 0 && Array.forall id removedTargets
                    struct(matched, allRemoved)
                let driver=new KeyframePlayback(context.Scheduler,program,guardedWrite,replacing,css,precision,options.Playback,
                    onBegin=options.OnBegin,onBeforeUpdate=options.OnBeforeUpdate,onRender=options.OnRender,
                    onUpdate=options.OnUpdate,onLoop=options.OnLoop,onPause=options.OnPause,onComplete=options.OnComplete,
                    ?siblings=nodes,finiteScalar=finiteScalar,?writeScalar=guardedWriteScalar)
                driver.Clock.InheritMute context.IsMuted
                ScopeRegistration.run ignore (fun () -> animation.SetDriver driver)
                driver.Clock.OnCancel<-animation.Cancel
                driver.Clock.OnFault<-animation.Cancel
                driver.Initialize(timelineChild=true,notifyConstruction=true,canWrite=current)
                if not(current()) then None
                else
                    handedOff<-true
                    let commit()=match transaction with Some siblings -> siblings.Commit() | None -> true
                    Some(TimelinePreparedChild(driver.Clock,(fun () -> driver.RenderVersion),commit,animation,animation.RestoreFromParent,removeTarget))
            | _ -> None
        finally
            if not handedOff then transaction |> Option.iter(fun siblings -> siblings.Rollback())

    /// <summary>Installs source-compatible numeric or CSS keyframe interpolation.</summary>
    let install args=installCore false None args
    /// <summary>Installs source-compatible numeric keyframe interpolation with an unboxed scalar writer.</summary>
    let installWithScalar writeScalar args=installCore false (Some writeScalar) args
    /// <summary>Installs scalar facade interpolation with its existing finite-endpoint arithmetic.</summary>
    let scalar args=installCore true None args
    /// <summary>Installs scalar facade interpolation with an unboxed scalar writer.</summary>
    let scalarDirect writeScalar args=installCore true (Some writeScalar) args
