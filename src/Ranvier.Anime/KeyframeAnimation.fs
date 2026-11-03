namespace Ranvier.Anime

open System

/// <summary>Sequence-local endpoint arbitration; independent-animation composition remains separate.</summary>
[<RequireQualifiedAccess; Diagnostics.DebuggerDisplay("{ToString(),nq}")>]
type KeyframeComposition =
    | None
    | Replace
    /// <summary>The invariant composition mode name.</summary>
    override this.ToString()=match this with KeyframeComposition.None -> "None" | KeyframeComposition.Replace -> "Replace"

/// <summary>Shared keyframe playback, interpolation, precision, startup and lifecycle callbacks.</summary>
[<NoEquality;NoComparison;Diagnostics.DebuggerDisplay("{ToString(),nq}")>]
type KeyframeAnimationOptions =
    { Playback:PlaybackOptions
      Ease:float -> float
      Modifier:float -> float
      Precision:uint option
      Autoplay:bool
      Composition:KeyframeComposition
      OnBegin:Timer -> unit
      OnBeforeUpdate:Timer -> unit
      OnRender:Timer -> unit
      OnUpdate:Timer -> unit
      OnLoop:Timer -> unit
      OnPause:Timer -> unit
      OnComplete:Timer -> unit }
    /// <summary>Autoplay, source out-quad easing, replace composition, four decimals and silent callbacks.</summary>
    static member Default=
        {Playback=PlaybackOptions.Default;Ease=Ease.named "outQuad";Modifier=id;Precision=Some 4u
         Autoplay=true;Composition=KeyframeComposition.Replace;OnBegin=ignore;OnBeforeUpdate=ignore
         OnRender=ignore;OnUpdate=ignore;OnLoop=ignore;OnPause=ignore;OnComplete=ignore}
    /// <summary>A diagnostic identifying startup and composition settings without formatting callback functions.</summary>
    override this.ToString()="KeyframeAnimationOptions(Autoplay="+this.Autoplay.ToString()+"; Composition="+this.Composition.ToString()+")"

/// <summary>An owned or explicitly disposable keyframe sequence with shared playback and captured-property restoration.</summary>
/// <remarks>
/// Explicit cancellation retains rendered values and closes restoration. Completion preserves Revert.
/// Revert rewinds then restores pre-construction snapshots; unrelated properties persist.
/// </remarks>
[<Sealed>]
type KeyframeAnimation internal(targetCount:int) =
    let mutable driver:KeyframePlayback option=None
    let mutable active=true
    let mutable reverting=false
    let mutable generation=0
    let mutable restore:((unit -> bool) -> unit) option=None
    let mutable cleanup:(unit -> unit) option=None
    /// <summary>Checks that construction still belongs to this lifetime.</summary>
    member internal _.EnsureLive()=if not active then raise(ObjectDisposedException(nameof KeyframeAnimation))
    /// <summary>Captures the current construction or restoration generation.</summary>
    member internal _.Guard()=
        let expected=generation
        fun () -> active && expected=generation
    /// <summary>Registers captured restoration before initialization invokes user code.</summary>
    member internal _.SetRestore(action)=restore<-Some action
    /// <summary>Registers terminal sibling cleanup before construction can publish or invoke user callbacks.</summary>
    member internal _.SetCleanup(action)=cleanup<-Some action
    /// <summary>Installs the prepared shared driver before initialization.</summary>
    member internal _.SetDriver(value)=driver<-Some value
    /// <summary>The existing shared Timer controller, including precise seek, repeat, direction and stretch controls.</summary>
    member _.Playback=match driver with Some driver -> driver.Clock | None -> invalidOp "The sequence is still being constructed."
    /// <summary>Number of captured targets.</summary>
    member _.TargetCount=targetCount
    /// <summary>Whether explicit disposal, ownership, completion or a callback fault has cancelled playback.</summary>
    member _.Cancelled=not active || (driver |> Option.exists(fun value -> value.Clock.Cancelled))
    /// <summary>Stops future writes and releases restoration captures; repeated cancellation is harmless.</summary>
    member _.Cancel()=
        if active then
            active<-false
            generation<-generation+1
            restore<-None
            let release=cleanup
            cleanup<-None
            let mutable failure:exn option=None
            try release |> Option.iter(fun action -> action()) with error -> failure<-Some error
            try driver |> Option.iter(fun value -> value.Clock.Cancel())
            with error -> if failure.IsNone then failure<-Some error
            failure |> Option.iter(fun error -> Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw())
    /// <summary>Rewinds and restores captured properties, then cancels; owner disposal suppresses later restoration writes.</summary>
    member this.Revert()=
        if active && not reverting then
            reverting<-true
            let current=this.Guard()
            let mutable failure:exn option=None
            let attempt action=try action() with error -> if failure.IsNone then failure<-Some error
            attempt(fun () -> driver |> Option.iter(fun value -> value.Clock.RewindForRevert()))
            if current() then attempt(fun () -> restore |> Option.iter(fun action -> action current))
            attempt this.Cancel
            reverting<-false
            failure |> Option.iter(fun error -> Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw())
    interface IDisposable with member this.Dispose()=this.Cancel()
