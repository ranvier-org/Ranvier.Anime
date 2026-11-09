namespace Ranvier.Anime

open Fable.Core
open Ranvier.Css

/// <summary>Numeric-token checks shared by typed animation parameters and reactive retargeting.</summary>
module internal CssAnimationRuntime =
    /// <summary>Requires a finite numeric token carrying generated metadata and returns its unit suffix.</summary>
    let unitOf (value: Ranvier.Css.Value<'syntax>) =
        match RuntimeApi.tryNumericValue value with
        | Some (number, suffix) when
            not (
                System.Double.IsNaN number
                || System.Double.IsInfinity number
            )
            ->
            suffix
        | _ -> invalidArg "value" "Animation endpoints require numeric CSS tokens. Animate a variable consumed by the CSS expression."

    /// <summary>Checks the unit suffix against the first endpoint before animation parameters are changed.</summary>
    let requireUnit expected value =
        if unitOf value <> expected then
            invalidArg "value" "Animation endpoints must use the same unit suffix. Convert endpoints to a common unit first."

    /// <summary>Captures a custom property's inline value and priority for restoration on owner disposal.</summary>
    [<Emit("(() => { const style = $0.style; const value = style.getPropertyValue($1); const priority = style.getPropertyPriority($1); return () => { if (value) style.setProperty($1, value, priority); else style.removeProperty($1); }; })()")>]
    let capture (element: TS.Dom.Element) (name: string) : unit -> unit = jsNative

    /// <summary>Copies animatable timing parameters using the numeric endpoint's unit suffix.</summary>
    [<Emit("Object.assign({}, $0, {unit: $1})")>]
    let withUnit (parameters: AnimatableProp) (suffix: string) : AnimatableProp = jsNative

/// <summary>Typed numeric CSS custom-property endpoints for Anime's JavaScript animation engine.</summary>
[<AutoOpen>]
module CssAnimationExtensions =
    type Anim with
        /// <summary>Animates a typed custom property to a numeric token of its grammar marker.</summary>
        /// <remarks>The current property value must use the endpoint's unit. CSS expressions can consume the animated variable.</remarks>
        /// <exception cref="T:System.ArgumentException">The endpoint is a compound expression or lacks numeric token metadata.</exception>
        member this.With(property: CustomProperty<'syntax>, value: Ranvier.Css.Value<'syntax>) : Anim =
            CssAnimationRuntime.unitOf value |> ignore
            this.With (RuntimeApi.customPropertyName property, Value.op_Implicit (RuntimeApi.renderValue value))

        /// <summary>Animates a typed custom property between numeric tokens with the same grammar marker and unit suffix.</summary>
        /// <exception cref="T:System.ArgumentException">An endpoint lacks numeric token metadata or the unit suffixes differ.</exception>
        member this.With(property: CustomProperty<'syntax>, start: Ranvier.Css.Value<'syntax>, finish: Ranvier.Css.Value<'syntax>) : Anim =
            CssAnimationRuntime.requireUnit (CssAnimationRuntime.unitOf start) finish
            let first = Value.op_Implicit (RuntimeApi.renderValue start)
            let last = Value.op_Implicit (RuntimeApi.renderValue finish)
            this.With (RuntimeApi.customPropertyName property, Value.fromTo (first, last))

        /// <summary>Animates a typed custom property through numeric keyframes sharing a grammar marker and unit suffix.</summary>
        /// <exception cref="T:System.ArgumentException">Frames are empty, lack numeric token metadata, or have different unit suffixes.</exception>
        member this.With(property: CustomProperty<'syntax>, frames: #Ranvier.Css.Value<'syntax> array) : Anim =
            if isNull frames || frames.Length = 0 then
                invalidArg "frames" "Supply at least one numeric CSS keyframe."

            let suffix = CssAnimationRuntime.unitOf frames[0]

            for frame in frames do
                CssAnimationRuntime.requireUnit suffix frame

            let values =
                frames
                |> Array.map (fun frame -> Value.op_Implicit (RuntimeApi.renderValue frame))

            this.With (RuntimeApi.customPropertyName property, Value.keys values)
