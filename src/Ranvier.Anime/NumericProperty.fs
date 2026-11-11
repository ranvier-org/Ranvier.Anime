namespace Ranvier.Anime

open System

/// <summary>An explicit numeric property identity backed by F# accessors; reuse one handle for composed tracks.</summary>
/// <remarks>Accessors run on the controller's owning thread and must return/accept finite numbers.</remarks>
[<Sealed>]
type NumericProperty(read: unit -> float, write: float -> unit) =
    /// <summary>Reads or writes the borrowed property through its supplied accessors.</summary>
    member _.Value
        with get() =
            let value = read()
            if not(Double.IsFinite value) then invalidArg "read" "Expected a finite property value."
            value
        and set value =
            if not(Double.IsFinite value) then invalidArg "value" "Expected a finite property value."
            write value
