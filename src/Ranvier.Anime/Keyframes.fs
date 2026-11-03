namespace Ranvier.Anime

open System
open Ranvier
open Ranvier.Dom
open Ranvier.Anime.ValueInterpolation

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
    /// <summary>Checks that construction still belongs to this lifetime.</summary>
    member internal _.EnsureLive()=if not active then raise(ObjectDisposedException(nameof KeyframeAnimation))
    /// <summary>Captures the current construction or restoration generation.</summary>
    member internal _.Guard()=
        let expected=generation
        fun () -> active && expected=generation
    /// <summary>Registers captured restoration before initialization invokes user code.</summary>
    member internal _.SetRestore(action)=restore<-Some action
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
            driver |> Option.iter(fun value -> value.Clock.Cancel())
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

module private KeyframeBinding =
    let validate (duration:float,options:KeyframeAnimationOptions) =
        if isNull(box options) || isNull(box options.Playback) then nullArg "options"
        let finite name value=if not(Double.IsFinite value) then invalidArg name "Expected finite milliseconds."
        finite "duration" duration
        if duration<0. then invalidArg "duration" "Expected nonnegative milliseconds."
        let rounded=PropertyNumbers.round 1e12 (if duration=0. then 1e-11 else duration)
        if rounded=0. then invalidArg "duration" "Positive duration rounds to zero at source construction precision."
        for name,value in ["delay",options.Playback.Delay;"loopDelay",options.Playback.LoopDelay;"speed",options.Playback.Speed;"frameRate",options.Playback.FrameRate] do finite name value
        for action in [box options.Ease;box options.Modifier;box options.OnBegin;box options.OnBeforeUpdate;box options.OnRender
                       box options.OnUpdate;box options.OnLoop;box options.OnPause;box options.OnComplete] do
            if isNull action then invalidArg "options" "Callbacks are required."
        match options.Precision with None -> -1 | Some value when value<=308u -> int value | _ -> invalidArg "options" "Precision exceeds 308."
    let numeric (scheduler:FrameScheduler,targets:NumericProperty array,authored:Keyframe<NumericProperty,float> array,
                 duration:float,options:KeyframeAnimationOptions,owner:Owner option) =
        let precision=validate(duration,options)
        if isNull targets || targets.Length=0 then invalidArg "targets" "Expected nonempty numeric properties."
        if isNull authored then nullArg "frames"
        let targets=Array.copy targets
        let authored=Array.copy authored
        if targets |> Array.exists(fun target -> isNull(box target)) then invalidArg "targets" "Numeric properties are required."
        if authored |> Array.exists(fun frame -> isNull(box frame)) then invalidArg "frames" "Frames are required."
        let convert value =
            if not(Double.IsFinite value) then invalidArg "value" "Expected finite numeric endpoints."
            Number value
        let templates=authored |> Array.map(fun frame -> frame.Template convert)
        let properties=targets |> Array.mapi(fun index _ ->
            {FrameProperty.Target=index;Name="value";Original=Number 0.;Frames=templates;ConvertUnit=None})
        let defaults={FrameDefaults.Default with Duration=duration;Delay=options.Playback.Delay;Ease=options.Ease;Modifier=options.Modifier}
        KeyframeCompilation.validate(defaults,properties)
        let animation=new KeyframeAnimation(targets.Length)
        try
            owner |> Option.iter(fun owner -> owner.Attach animation)
            let current=animation.Guard()
            let read index =
                animation.EnsureLive()
                let value=targets[index].Value
                animation.EnsureLive()
                value
            let originals=if authored.Length=0 then [||] else targets |> Array.mapi(fun index _ -> read index)
            animation.SetRestore(fun current ->
                let mutable failure:exn option=None
                for index in 0..originals.Length-1 do
                    if current() then
                        try targets[index].Value<-originals[index]
                        with error -> if failure.IsNone then failure<-Some error
                failure |> Option.iter(fun error -> Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw()))
            let resolve target frame previous =
                let previous=previous |> Option.bind(fun (tween:FrameTween) -> tween.CachedEnd.Scalar)
                let context=KeyframeContext(targets[target],uint target,uint targets.Length,previous)
                authored[frame].Resolve(context,convert,current)
            let program=KeyframeCompilation.compileDynamic(options.Composition=KeyframeComposition.Replace,defaults,properties,resolve,(fun index -> Number(read index)),current)
            animation.EnsureLive()
            let program=program |> Option.defaultWith(fun () -> invalidOp "Keyframe construction was superseded.")
            let driver=new KeyframePlayback(scheduler,program,(fun target _ output -> targets[target].Value<-output.Scalar.Value),
                                           options.Composition=KeyframeComposition.Replace,false,precision,options.Playback,
                                           onBegin=options.OnBegin,onBeforeUpdate=options.OnBeforeUpdate,onRender=options.OnRender,
                                           onUpdate=options.OnUpdate,onLoop=options.OnLoop,onPause=options.OnPause,onComplete=options.OnComplete)
            animation.SetDriver driver
            driver.Clock.OnFault<-animation.Cancel
            driver.Clock.OnCancel<-animation.Cancel
            driver.Initialize(notifyConstruction=not options.Autoplay)
            if current() && options.Autoplay then driver.Clock.Resume()
            animation
        with _ ->
            try animation.Cancel() with _ -> ()
            reraise()

/// <summary>Typed keyframe sequence construction on the native shared scheduler.</summary>
/// <remarks>Empty frame arrays complete as an epsilon no-op and do not read their property accessors.</remarks>
[<AbstractClass;Sealed>]
type Keyframes private () =
    /// <summary>Animates nonempty numeric property handles using finite nonnegative precise milliseconds.</summary>
    /// <remarks>Pass a Ranvier owner to attach before capture and callbacks. The caller owns disposal when omitted.</remarks>
    static member NumericPrecise(scheduler:FrameScheduler,targets:NumericProperty array,frames:Keyframe<NumericProperty,float> array,
                                 duration:float,?options:KeyframeAnimationOptions,?owner:Owner) =
        KeyframeBinding.numeric(scheduler,targets,frames,duration,defaultArg options KeyframeAnimationOptions.Default,owner)
    /// <summary>Animates one numeric property using finite nonnegative precise milliseconds.</summary>
    static member NumericPrecise(scheduler:FrameScheduler,target:NumericProperty,frames:Keyframe<NumericProperty,float> array,
                                 duration:float,?options:KeyframeAnimationOptions,?owner:Owner) =
        Keyframes.NumericPrecise(scheduler,[|target|],frames,duration,?options=options,?owner=owner)
    /// <summary>Animates nonempty numeric property handles; unsigned total default duration is 1000 ms.</summary>
    static member Numeric(scheduler:FrameScheduler,targets:NumericProperty array,frames:Keyframe<NumericProperty,float> array,
                          ?duration:uint,?options:KeyframeAnimationOptions,?owner:Owner) =
        Keyframes.NumericPrecise(scheduler,targets,frames,float(defaultArg duration 1000u),?options=options,?owner=owner)
    /// <summary>Animates one numeric property; unsigned total default duration is 1000 ms.</summary>
    static member Numeric(scheduler:FrameScheduler,target:NumericProperty,frames:Keyframe<NumericProperty,float> array,
                          ?duration:uint,?options:KeyframeAnimationOptions,?owner:Owner) =
        Keyframes.Numeric(scheduler,[|target|],frames,?duration=duration,?options=options,?owner=owner)
