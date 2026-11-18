namespace Ranvier.Anime

open System

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
    /// <summary>Active scope clock and animation defaults, engine decimal precision and silent callbacks.</summary>
    static member Default=
        {Playback=PlaybackDefaults.current();Ease=(AnimationDefaults.current()).Ease;Modifier=AnimationDefaults.modifier id;Precision=EngineDefaults.precision()
         Autoplay=AnimationDefaults.autoplay true;Composition=AnimationDefaults.composition KeyframeComposition.Replace;OnBegin=ignore;OnBeforeUpdate=ignore
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
    let ownerAllowsWrite=ScopeRegistration.captureWriteGuard()
    let mutable constructionComplete=false
    let mutable driver:KeyframePlayback option=None
    let mutable active=true
    let mutable reverting=false
    let mutable generation=0
    let mutable restore:((unit -> bool) -> unit) option=None
    let mutable cleanup:(unit -> unit) option=None
    /// <summary>Checks that construction still belongs to this lifetime.</summary>
    member internal _.EnsureLive()=
        if not active || not(ownerAllowsWrite()) || (not constructionComplete && not(ScopeRegistration.canConstruct())) then
            raise(ObjectDisposedException(nameof KeyframeAnimation))
    /// <summary>Captures the current construction or restoration generation.</summary>
    member internal _.Guard()=
        let expected=generation
        fun () -> active && expected=generation && ownerAllowsWrite() && (constructionComplete || ScopeRegistration.canConstruct())
    member private _.AllowsRestorationWrite = driver |> Option.forall(fun value -> not value.Clock.EffectivelyMuted)
    /// <summary>Registers captured restoration before initialization invokes user code.</summary>
    member internal _.SetRestore(action)=restore<-Some action
    /// <summary>Registers terminal sibling cleanup before construction can publish or invoke user callbacks.</summary>
    member internal _.SetCleanup(action)=cleanup<-Some action
    /// <summary>Installs the prepared shared driver before initialization.</summary>
    member internal this.SetDriver(value)=
        driver<-Some value
        value.Clock.OnSoftRevert <- fun () -> this.Revert(false)
        if ScopeRegistration.canConstruct() && ownerAllowsWrite() then
            constructionComplete<-true
            ScopeRegistration.register this.Revert
        else this.Cancel()
    /// <summary>The existing shared Timer controller, including precise seek, repeat, direction and stretch controls.</summary>
    member _.Playback=match driver with Some driver -> driver.Clock | None -> invalidOp "The sequence is still being constructed."
    member internal _.RestoreHeld(previous,time,cacheOnly,current)=
        active && (driver |> Option.exists(fun playback -> playback.RestoreHeld(previous,time,cacheOnly,current)))
    /// <summary>Suppresses property writes, lifecycle callbacks and completion delivery while time advances.</summary>
    member this.Muted with get() = this.Playback.Muted and set value = this.Playback.Muted <- value
    /// <summary>Captures the installed clock's iteration, direction and scheduling anchor.</summary>
    member internal this.CaptureScopeTime() = this.Playback.CaptureScopeTime()
    /// <summary>Restores the installed clock while this captured-property lifetime remains live.</summary>
    member internal this.RestoreScopeTime(snapshot: ScopeClockSnapshot) =
        this.EnsureLive()
        this.Playback.RestoreScopeTime snapshot
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
    member this.Revert() = this.Revert(true)
    /// <summary>Restores captured properties and cancels playback, optionally skipping the rewind sample.</summary>
    member this.Revert(seekToStart: bool) =
        if active && not reverting then
            reverting<-true
            let ownCurrent=this.Guard()
            let current()=ownCurrent() && this.AllowsRestorationWrite
            let mutable failure:exn option=None
            let attempt action=try action() with error -> if failure.IsNone then failure<-Some error
            attempt(fun () -> driver |> Option.iter(fun value -> value.Clock.StopForRevert(seekToStart)))
            if current() then attempt(fun () -> restore |> Option.iter(fun action -> action current))
            attempt this.Cancel
            reverting<-false
            failure |> Option.iter(fun error -> Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw())
    /// <summary>Restores captured properties after the parent rewind, then closes this child's lifetime.</summary>
    member internal this.RestoreFromParent(parentCurrent:unit -> bool)=
        if active && not reverting then
            reverting<-true
            let ownCurrent=this.Guard()
            let current()=ownCurrent() && parentCurrent() && this.AllowsRestorationWrite
            let mutable failure:exn option=None
            try
                if current() then restore |> Option.iter(fun action -> action current)
            with error -> failure<-Some error
            try this.Cancel() with error -> if failure.IsNone then failure<-Some error
            reverting<-false
            failure |> Option.iter(fun error -> Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw())
    /// <summary>Maps observable completion to the callback result.</summary>
    member this.ThenResult<'T>(callback: KeyframeAnimation -> 'T) : Threading.Tasks.Task<'T> =
        this.Playback.ThenResult(fun _ -> callback this)

    /// <summary>Awaits the task returned by the completion callback.</summary>
    member this.ThenAsync<'T>(callback: KeyframeAnimation -> Threading.Tasks.Task<'T>) : Threading.Tasks.Task<'T> =
        Threading.Tasks.TaskExtensions.Unwrap(this.ThenResult(callback))

    /// <summary>Awaits a completion callback that produces no result.</summary>
    member this.ThenAsync(callback: KeyframeAnimation -> Threading.Tasks.Task) : Threading.Tasks.Task =
        Threading.Tasks.TaskExtensions.Unwrap(this.ThenResult(callback))

    /// <summary>Runs the optional completion callback and returns this instance.</summary>
    member this.Then(?callback: KeyframeAnimation -> unit) : Threading.Tasks.Task<KeyframeAnimation> =
        this.ThenResult(CompletionTask.withInstance callback)
    /// <summary>Returns a task settling with this keyframe animation upon completion.</summary>
    member this.AsTask() : Threading.Tasks.Task<KeyframeAnimation> = this.Then()
    interface IDisposable with member this.Dispose()=this.Cancel()
