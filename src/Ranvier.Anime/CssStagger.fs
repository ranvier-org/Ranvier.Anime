namespace Ranvier.Anime

open Ranvier.Css

/// <summary>Stagger distributions preserving existing typed CSS units and checked constructors.</summary>
[<RequireQualifiedAccess>]
module CssStagger =
    let private numeric name (value: Value<'syntax>) =
        if isNull (box value) then nullArg name
        match RuntimeApi.tryNumericValue value with
        | Some(number, unitName) when System.Double.IsFinite number -> number, unitName
        | _ -> invalidArg name "Expected a finite numeric CSS literal."

    let private construct<'syntax, 'value when 'value :> Value<'syntax>>
        (create: float -> 'value) unitName (numbers: float array) =
        if numbers |> Array.exists(fun number -> not(System.Double.IsFinite number)) then
            invalidArg "options" "Stagger distribution produced a nonfinite CSS number."
        numbers |> Array.map(fun number ->
            let value = create number
            let actual, suffix = numeric "create" value
            if actual <> number || suffix <> unitName then
                invalidArg "create" "Constructor must preserve magnitude and unit."
            value)

    /// <summary>Distributes a typed numeric step; the constructor must preserve its magnitude and unit.</summary>
    /// <remarks>Rejects nonnumeric inputs and nonfinite outputs. Options use input-unit magnitudes; constructor failures propagate.</remarks>
    let step<'syntax, 'value when 'value :> Value<'syntax>>
        (create: float -> 'value) (value: Value<'syntax>) (options: StaggerOptions) (targetCount: uint) : 'value array =
        let number, unitName = numeric "value" value
        Stagger.distribute (StaggerValue.Step number) options targetCount |> construct create unitName

    /// <summary>Distributes typed numeric endpoints with matching units through the supplied checked constructor.</summary>
    /// <remarks>Options use magnitudes in the endpoint unit. Rejects mixed units and nonfinite results; constructor validation propagates.</remarks>
    let range<'syntax, 'value when 'value :> Value<'syntax>>
        (create: float -> 'value) (first: Value<'syntax>) (last: Value<'syntax>) (options: StaggerOptions) (targetCount: uint) : 'value array =
        let firstNumber, firstUnit = numeric "first" first
        let lastNumber, lastUnit = numeric "last" last
        if firstUnit <> lastUnit then invalidArg "last" "Endpoint units must match."
        Stagger.distribute (StaggerValue.Range(firstNumber, lastNumber)) options targetCount |> construct create firstUnit
