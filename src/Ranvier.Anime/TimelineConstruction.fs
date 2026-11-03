namespace Ranvier.Anime

open System
open Ranvier.Anime.ValueInterpolation

/// <summary>Guarded access to timeline scheduling, insertion position, pending lifetime and staged siblings during construction.</summary>
type internal TimelineChildContext(scheduler:Ranvier.Dom.FrameScheduler,offset:float,canWrite:unit -> bool,
                                   attach:IDisposable -> unit,siblings:FrameSiblingRegistry) =
    /// <summary>The timeline's scheduler; children share its clock thread.</summary>
    member _.Scheduler=scheduler
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
type internal TimelinePreparedChild<'T>(clock:Timer,renderVersion:unit -> int64,commit:unit -> bool,result:'T) =
    /// <summary>The child controller whose scheduling and disposal belong to the timeline.</summary>
    member _.Clock=clock
    /// <summary>The current target dispatch generation, unchanged by cache-only or skipped samples.</summary>
    member _.RenderVersion()=renderVersion()
    /// <summary>Publishes staged sibling changes if all captured guards still hold.</summary>
    member _.Commit()=commit()
    /// <summary>The factory's observable child handle.</summary>
    member _.Result=result

/// <summary>Owned driver installation with staged same-timeline replacement publication.</summary>
module internal TimelineKeyframeInstallation =
    /// <summary>Compiles guarded captured properties and installs one owned driver with optional staged replacement siblings.</summary>
    let private installCore finiteScalar (context:TimelineChildContext,animation:KeyframeAnimation,defaults:FrameDefaults,properties:FrameProperty array,
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
                let driver=new KeyframePlayback(context.Scheduler,program,write,replacing,css,precision,options.Playback,
                    onBegin=options.OnBegin,onBeforeUpdate=options.OnBeforeUpdate,onRender=options.OnRender,
                    onUpdate=options.OnUpdate,onLoop=options.OnLoop,onPause=options.OnPause,onComplete=options.OnComplete,?siblings=nodes,finiteScalar=finiteScalar)
                animation.SetDriver driver
                driver.Clock.OnCancel<-animation.Cancel
                driver.Clock.OnFault<-animation.Cancel
                driver.Initialize(timelineChild=true,notifyConstruction=true,canWrite=current)
                if not(current()) then None
                else
                    handedOff<-true
                    let commit()=match transaction with Some siblings -> siblings.Commit() | None -> true
                    Some(TimelinePreparedChild(driver.Clock,(fun () -> driver.RenderVersion),commit,animation))
            | _ -> None
        finally
            if not handedOff then transaction |> Option.iter(fun siblings -> siblings.Rollback())

    /// <summary>Installs source-compatible numeric or CSS keyframe interpolation.</summary>
    let install args=installCore false args
    /// <summary>Installs scalar facade interpolation with its existing finite-endpoint arithmetic.</summary>
    let scalar args=installCore true args
