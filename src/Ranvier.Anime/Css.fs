namespace Ranvier.Anime

open System
open System.Globalization
open Ranvier.Css
open Ranvier.Css.Dom
open Ranvier.Dom

/// <summary>Playback, easing, component modifiers and callbacks for typed CSS declaration animation.</summary>
[<NoEquality; NoComparison; System.Diagnostics.DebuggerDisplay("{ToString(),nq}")>]
type DeclarationAnimationOptions =
    { Playback: PlaybackOptions
      /// <summary>Maps normalized iteration progress to finite interpolation progress; may overshoot.</summary>
      Ease: float -> float
      /// <summary>Transforms each interpolated component before dispatch; RGB integer rounding follows this callback.</summary>
      Modifier: float -> float
      /// <summary>Intermediate decimal places, at most 308; None disables rounding. Exact endpoints retain source precision.</summary>
      Precision: uint option
      OnBegin: Timer -> unit
      OnBeforeUpdate: Timer -> unit
      /// <summary>Runs after a live positive-time style commit, before the update callback.</summary>
      OnRender: Timer -> unit
      OnUpdate: Timer -> unit
      OnLoop: Timer -> unit
      OnPause: Timer -> unit
      OnComplete: Timer -> unit }
    /// <summary>One forward iteration, linear easing, identity modifier, four decimal places and silent callbacks.</summary>
    static member Default =
        { Playback=PlaybackOptions.Default; Ease=id; Modifier=id; Precision=Some 4u
          OnBegin=ignore; OnBeforeUpdate=ignore; OnRender=ignore; OnUpdate=ignore
          OnLoop=ignore; OnPause=ignore; OnComplete=ignore }
    /// <summary>An invariant clock/precision diagnostic; callback functions are omitted.</summary>
    override this.ToString() =
        let precision=this.Precision |> Option.map(fun value -> value.ToString(CultureInfo.InvariantCulture)) |> Option.defaultValue "unrounded"
        "DeclarationAnimationOptions(Playback="+this.Playback.ToString()+"; Precision="+precision+")"

/// <summary>Animations authored with existing Ranvier.Css values and custom-property handles.</summary>
[<RequireQualifiedAccess>]
module CssAnimation =
    /// <summary>Animates explicit numeric endpoints with matching units and preserves the property's inline priority.</summary>
    /// <remarks>Renders the initial directional value immediately. The caller owns cancellation; disposal retains the last value.</remarks>
    let customPropertyWithOptions (options: PlaybackOptions) (scheduler: FrameScheduler) (style: CSSStyleDeclaration) (variable: CustomProperty<'syntax>)
                       (first: Value<'syntax>) (last: Value<'syntax>) (duration: float) : NumericAnimation =
        let startValue, finishValue, unitName =
            match RuntimeApi.tryNumericValue first, RuntimeApi.tryNumericValue last with
            | Some (startValue, startUnit), Some (finishValue, finishUnit) when startUnit = finishUnit ->
                startValue, finishValue, startUnit
            | Some _, Some _ -> invalidArg "last" "Endpoint units must match."
            | _ -> invalidArg "first" "Endpoints must be finite numeric CSS literals."
        let name = RuntimeApi.customPropertyName variable
        let priority = style.getPropertyPriority name
        let mutable rendered = startValue
        let apply (value: float) =
            let text = value.ToString("G17", CultureInfo.InvariantCulture) + unitName
            let declaration = Declaration.create name text
            let declaration = if priority = "important" then Declaration.important declaration else declaration
            StyleDeclaration.apply style declaration
            rendered <- value
        Animation.animateWithOptions options id scheduler (fun () -> rendered) apply finishValue duration

    /// <summary>Animates matching numeric CSS endpoints with default playback options and retained inline priority.</summary>
    let customProperty scheduler style variable first last duration =
        customPropertyWithOptions PlaybackOptions.Default scheduler style variable first last duration

    /// <summary>Starts source-compatible declaration playback with precise milliseconds and explicit interpolation/callback options.</summary>
    /// <remarks>Endpoints must name one property. Scalar endpoint units must match.
    /// Color syntax and compound templates follow the source without contextual conversion.
    /// Retains inline importance; an important endpoint requests importance throughout. The returned Timer owns cancellation.</remarks>
    let declarationPreciseWith (options:DeclarationAnimationOptions) (scheduler:FrameScheduler) (style:CSSStyleDeclaration)
                               (first:Declaration) (last:Declaration) (duration:float) =
        if isNull(box options) then nullArg "options"
        if isNull(box options.Playback) then invalidArg "options" "Playback settings are required."
        if isNull(box style) then nullArg "style"
        let finite name value = if not(Double.IsFinite value) then invalidArg name "Expected a finite value."
        finite "duration" duration
        if duration<0. then invalidArg "duration" "Expected nonnegative milliseconds."
        for name,value in ["Delay",options.Playback.Delay;"LoopDelay",options.Playback.LoopDelay;"Speed",options.Playback.Speed;"FrameRate",options.Playback.FrameRate] do
            finite name value
        for name,callback in ["Ease",box options.Ease;"Modifier",box options.Modifier;"OnBegin",box options.OnBegin
                              "OnBeforeUpdate",box options.OnBeforeUpdate;"OnRender",box options.OnRender;"OnUpdate",box options.OnUpdate
                              "OnLoop",box options.OnLoop;"OnPause",box options.OnPause;"OnComplete",box options.OnComplete] do
            if isNull callback then invalidArg "options" (name+" callback is required.")
        let precision=
            match options.Precision with
            | None -> -1
            | Some value when value<=308u -> int value
            | _ -> invalidArg "options" "Precision exceeds the finite decimal scale."
        let name=Declaration.name first
        if String.IsNullOrWhiteSpace name || name<>Declaration.name last then invalidArg "last" "Endpoints must name one nonempty property."
        let original=style.getPropertyValue name
        let interpolate=ValueInterpolation.prepareCss (ValueInterpolation.Text original)
                            (ValueInterpolation.Text(Declaration.value first)) (ValueInterpolation.Text(Declaration.value last))
        let important=Declaration.isImportant first || Declaration.isImportant last || style.getPropertyPriority name="important"
        let iterationDuration=PropertyNumbers.round 1e12 (if duration=0. then 1e-11 else duration)
        if iterationDuration=0. then invalidArg "duration" "Positive duration rounds to zero at source timing precision."
        let mutable tweenTime=0.
        let clock=new Timer(scheduler,iterationDuration,options=options.Playback,
                            onBegin=options.OnBegin,onBeforeUpdate=options.OnBeforeUpdate,onUpdate=options.OnUpdate,
                            onLoop=options.OnLoop,onPause=options.OnPause,onComplete=options.OnComplete)
        let render (sample:PlaybackSample) mode muted =
            let canWrite=clock.CaptureWriteGuard()
            let delta=sample.AbsoluteTime-clock.PreviousAbsoluteTime
            let force=mode=PlaybackTickMode.Force || (if sample.Backwards then -delta else delta)>=200.
            let absolute=PropertyNumbers.round 1e12 (options.Playback.Delay+sample.IterationTime)
            let start=PropertyNumbers.round 1e12 options.Playback.Delay
            let eligible=force || ((tweenTime<>iterationDuration || absolute<=start+iterationDuration) &&
                                  (tweenTime<>0. || absolute>=start))
            if eligible && canWrite() then
                tweenTime<-Math.Clamp(sample.IterationTime,0.,iterationDuration)
                let progress=options.Ease(tweenTime/iterationDuration)
                if canWrite() then
                    match interpolate progress precision options.Modifier canWrite with
                    | Some output when canWrite() ->
                        let declaration=Declaration.create name output.Text
                        StyleDeclaration.apply style (if important then Declaration.important declaration else declaration)
                        if canWrite() && sample.AboveZero && not muted then options.OnRender clock
                    | _ -> ()
        try
            clock.ConfigureRenderer(render,?totalDuration=(if iterationDuration=1e-11 then Some 1e-11 else None))
            clock.Reset()
            clock.Resume()
            clock
        with _ ->
            (clock:>IDisposable).Dispose()
            reraise()

    /// <summary>Starts declaration playback with unsigned whole milliseconds and explicit interpolation/callback options.</summary>
    let declarationWith options scheduler style first last (duration:uint) =
        declarationPreciseWith options scheduler style first last (float duration)

    /// <summary>Starts linear declaration playback with precise milliseconds and default options.</summary>
    let declarationPrecise scheduler style first last duration =
        declarationPreciseWith DeclarationAnimationOptions.Default scheduler style first last duration

    /// <summary>Starts linear declaration playback with unsigned whole milliseconds and default options.</summary>
    let declaration scheduler style first last (duration:uint) =
        declarationWith DeclarationAnimationOptions.Default scheduler style first last duration
