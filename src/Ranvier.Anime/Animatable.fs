namespace Ranvier.Anime

open System
open Ranvier
open Ranvier.Dom

/// <summary>Per-property playback, easing, modifiers and rounding for retargetable animatables.</summary>
[<NoEquality; NoComparison; Diagnostics.DebuggerDisplay("{ToString(),nq}")>]
type AnimatablePropertyOptions =
    { Playback: PlaybackOptions
      Ease: float -> float
      Modifier: float -> float
      /// <summary>Intermediate decimal places up to 308; None preserves unrounded values.</summary>
      Precision: uint option }
    /// <summary>Active scope clock, easing and modifier defaults with four decimal places.</summary>
    static member Default =
        { Playback=PlaybackDefaults.current(); Ease=(AnimationDefaults.current()).Ease; Modifier=AnimationDefaults.modifier id; Precision=Some 4u }
    /// <summary>An invariant clock/precision diagnostic with callback functions omitted.</summary>
    override this.ToString() =
        let precision=this.Precision |> Option.map(fun value -> value.ToString(Globalization.CultureInfo.InvariantCulture)) |> Option.defaultValue "unrounded"
        "AnimatablePropertyOptions(Playback="+this.Playback.ToString()+"; Precision="+precision+")"

module private AnimatableInputs =
    let validate duration (options:AnimatablePropertyOptions) =
        if isNull(box options) then nullArg "options"
        if isNull(box options.Playback) then invalidArg "options" "Playback settings are required."
        if not(Double.IsFinite duration) || duration<0. then invalidArg "duration" "Expected finite nonnegative milliseconds."
        if duration>0. && PropertyNumbers.round 1e12 duration=0. then
            invalidArg "duration" "Positive duration rounds to zero at source timing precision."
        for value in [options.Playback.Delay;options.Playback.LoopDelay;options.Playback.Speed;options.Playback.FrameRate] do
            if not(Double.IsFinite value) then invalidArg "options" "Playback inputs must be finite."
        if isNull(box options.Ease) || isNull(box options.Modifier) then invalidArg "options" "Easing and modifier callbacks are required."
        match options.Precision with
        | None -> -1
        | Some value when value<=308u -> int value
        | _ -> invalidArg "options" "Precision exceeds the finite decimal scale."

/// <summary>A retargetable numeric property group whose getter reads the first target's modified animation cache.</summary>
/// <remarks>Retargeting retains omitted timing and easing. Cached reads remain available after cancellation.</remarks>
[<Sealed>]
type AnimatableNumber internal (track:AnimatableTrack,live:unit -> unit) =
    /// <summary>Cached first-target value with its getter modifier applied again; an interrupted getter raises InvalidOperationException.</summary>
    member _.Value =
        match track.ScalarValue 0 with
        | Some value -> value
        | None -> invalidOp "The cached getter was superseded."
    /// <summary>The property's shared playback controller, including seek, pause and stretch controls.</summary>
    member _.Playback=track.Clock
    /// <summary>Number of captured borrowed targets.</summary>
    member _.TargetCount=track.Count
    /// <summary>Retargets every captured property using optional unsigned whole milliseconds and easing.</summary>
    member _.To(value:float,?duration:uint,?ease:float -> float) =
        live()
        track.RetargetScalar(value,?newDuration=(duration |> Option.map float),?newEase=ease)
    /// <summary>Retargets every captured property using optional finite nonnegative precise milliseconds and easing.</summary>
    member _.ToPrecise(value:float,?duration:float,?ease:float -> float) =
        live()
        track.RetargetScalar(value,?newDuration=duration,?newEase=ease)
    /// <summary>ToPrecise without option wrappers, for a per-frame retarget: NaN keeps the duration, null keeps the ease.</summary>
    member internal _.RetargetPrecise(value:float,duration:float,ease:float -> float) =
        live()
        track.RetargetScalarPrecise(value,duration,ease)

type private AnimatableRegistration(restore:(unit -> bool) -> unit) =
    member val Track:AnimatableTrack option=None with get,set
    member _.Restore current=restore current

/// <summary>Retargetable properties with ordered shared frames, independent group callbacks and property-scoped restoration.</summary>
/// <remarks>Construction creates an empty group. Adding properties captures and renders their initial values immediately.
/// Cancel and disposal retain the last values; Revert restores captured values. All operations use the scheduler's thread.</remarks>
[<Sealed>]
type Animatable(scheduler:FrameScheduler,?onBegin:NumericAnimation -> unit,?onBeforeUpdate:NumericAnimation -> unit,
                ?onRender:NumericAnimation -> unit,?onUpdate:NumericAnimation -> unit,?onLoop:NumericAnimation -> unit,
                ?onPause:NumericAnimation -> unit,?onComplete:NumericAnimation -> unit) as this =
    let ownerAllowsWrite=ScopeRegistration.captureWriteGuard()
    let group=new AnimatableGroup(scheduler,?onBegin=onBegin,?onBeforeUpdate=onBeforeUpdate,?onRender=onRender,?onUpdate=onUpdate,
                                  ?onLoop=onLoop,?onPause=onPause,?onComplete=onComplete)
    let registrations=ResizeArray<AnimatableRegistration>()
    let mutable state=0
    let live () = if state<>0 || group.Cancelled || not(ownerAllowsWrite()) then raise(ObjectDisposedException(nameof Animatable))
    let cleanupAll actions =
        let mutable failure:exn option=None
        for action in actions do
            try action() with error -> if failure.IsNone then failure<-Some error
        failure |> Option.iter(fun error -> Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw())
    do ScopeRegistration.register this.Revert
    /// <summary>The independent callback animation; its default duration is 1000 ms.</summary>
    member _.Callbacks=group.Callbacks
    /// <summary>Number of attached property handles.</summary>
    member _.Count=group.Count
    /// <summary>Whether cancellation, owner disposal, revert or a callback fault ended the group.</summary>
    member _.Cancelled=state=2 || group.Cancelled
    /// <summary>Checks the complete group lifetime before adapter capture or mutation.</summary>
    member internal _.EnsureLive()=live()
    /// <summary>Validates adapter settings before property reads or contextual measurements.</summary>
    member internal _.Prepare(duration,options)=live();AnimatableInputs.validate duration options
    /// <summary>Registers restoration before initializing a typed adapter's guarded property track.</summary>
    member internal _.Register(kernels,write,restore,duration,options:AnimatablePropertyOptions,precision) =
        live()
        let registration=AnimatableRegistration restore
        registrations.Add registration
        try
            let track=group.Add(kernels,(fun index output -> if state<>2 && ownerAllowsWrite() then write index output),
                                duration,options.Playback,options.Ease,options.Modifier,precision)
            registration.Track<-Some track
            live()
            track
        with _ ->
            registrations.Remove registration |> ignore
            reraise()
    /// <summary>Captures nonempty borrowed numeric targets with precise milliseconds, initializes and returns their handle.</summary>
    /// <remarks>Captures original accessor values for Revert. Invalid timing/options reject before reading accessors.</remarks>
    member _.NumericPrecise(properties:NumericProperty array,duration:float,?options:AnimatablePropertyOptions) =
        live()
        let options=defaultArg options AnimatablePropertyOptions.Default
        let precision=AnimatableInputs.validate duration options
        if isNull properties then nullArg "properties"
        if properties.Length=0 then invalidArg "properties" "Expected at least one borrowed target."
        let properties=Array.copy properties
        if properties |> Array.exists(fun property -> isNull(box property)) then invalidArg "properties" "Targets are required."
        let originals=Array.zeroCreate properties.Length
        for index in 0..properties.Length-1 do
            live()
            originals[index]<-properties[index].Value
            live()
        let kernels=originals |> Array.map(fun value -> AnimatableValue(ValueInterpolation.Number value,None,false,None))
        let restore current =
            cleanupAll (seq {for index in 0..properties.Length-1 do
                                yield fun () -> if current() then properties[index].Value<-originals[index]})
        let track=this.Register(kernels,(fun index output -> properties[index].Value<-output.Number),restore,duration,options,precision)
        AnimatableNumber(track,live)
    /// <summary>Captures one borrowed numeric target with precise milliseconds and optional property settings.</summary>
    member _.NumericPrecise(property:NumericProperty,duration:float,?options:AnimatablePropertyOptions) =
        this.NumericPrecise([|property|],duration,?options=options)
    /// <summary>Captures nonempty borrowed numeric targets with optional unsigned milliseconds; omitted duration inherits scope defaults.</summary>
    member _.Numeric(properties:NumericProperty array,?duration:uint,?options:AnimatablePropertyOptions) =
        this.NumericPrecise(properties,AnimationDefaults.duration(duration |> Option.map float),?options=options)
    /// <summary>Captures one borrowed numeric target with optional unsigned milliseconds; omitted duration inherits scope defaults.</summary>
    member _.Numeric(property:NumericProperty,?duration:uint,?options:AnimatablePropertyOptions) =
        this.Numeric([|property|],?duration=duration,?options=options)
    /// <summary>Ends every owned driver and releases captures; repeated cancellation is harmless.</summary>
    member _.Cancel() =
        if state<>2 then
            state<-2
            try group.Cancel() finally registrations.Clear()
    /// <summary>Rewinds properties silently, restores captured values in authoring order, then ends the group.</summary>
    /// <remarks>Unrelated properties survive. Cancellation during restoration stops later writes.
    /// Cleanup preserves its first exception.</remarks>
    member _.Revert() =
        if state=0 && group.Cancelled then this.Cancel()
        elif state=0 then
            state<-1
            let current () = state=1 && ownerAllowsWrite()
            let captured=registrations.ToArray()
            cleanupAll (seq {
                for registration in captured do
                    yield fun () -> if current() then registration.Track |> Option.iter(fun track -> track.Clock.RewindForRevert())
                    yield fun () -> if current() then registration.Restore current
                yield fun () -> if current() then group.CreatedCallbacks |> ValueOption.iter(fun callbacks -> callbacks.PlaybackClock.RewindForRevert())
                yield this.Cancel })
    interface IDisposable with member _.Dispose()=this.Cancel()

/// <summary>Ranvier ownership for retargetable property groups.</summary>
[<RequireQualifiedAccess>]
module Animatable =
    /// <summary>Attaches the group to the current Ranvier owner, which cancels every member on disposal.</summary>
    let own (animatable:Animatable) =
        ScopeRegistration.attach(animatable:>IDisposable)
        animatable
