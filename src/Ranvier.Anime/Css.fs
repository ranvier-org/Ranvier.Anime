namespace Ranvier.Anime

open System
open System.Globalization
open Ranvier.Css
open Ranvier.Css.Dom
open Ranvier.Dom

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
