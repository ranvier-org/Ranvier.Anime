namespace Ranvier.Anime

open System
open Ranvier

/// <summary>Ambient scope restoration and selection of the existing Ranvier resource owner.</summary>
module internal ScopeRegistration =
    [<AllowNullLiteral>]
    type private Context(register: (unit -> unit) -> unit, resourceOwner: (unit -> Owner) option, canWrite: unit -> bool) =
        member _.Register = register
        member _.ResourceOwner = resourceOwner
        member _.CanWrite = canWrite
        override _.ToString() = "ScopeRegistration.Context"

    [<AbstractClass; Sealed>]
    type private CurrentContext =
        [<ThreadStatic; DefaultValue>]
        static val mutable private value: Context
        static member Value
            with get () = CurrentContext.value
            and set (v: Context) = CurrentContext.value <- v

    /// <summary>Registers restoration with the executing scope, when one exists.</summary>
    let register revert =
        let context = CurrentContext.Value
        if not(isNull context) then context.Register revert

    /// <summary>Selects the scope's current actual Ranvier owner, or the ambient owner outside a scope.</summary>
    let resourceOwner () =
        let context = CurrentContext.Value
        if isNull context then Api.getOwner()
        else
            match context.ResourceOwner with
            | Some select -> select()
            | None -> Api.getOwner()

    /// <summary>Whether the executing constructor still belongs to its scoped generation.</summary>
    let canConstruct () =
        let context = CurrentContext.Value
        isNull context || context.CanWrite()

    /// <summary>Captures the actual resource owner's disposable lifetime, including persistent resources retained through refresh.</summary>
    let captureWriteGuard () =
        let context = CurrentContext.Value
        if isNull context then fun () -> true
        else
            match context.ResourceOwner with
            | None -> fun () -> true
            | Some selectOwner ->
                let owner = selectOwner()
                fun () -> not owner.IsDisposed

    /// <summary>Attaches cancellation to the selected actual owner; stale constructor work is cancelled immediately.</summary>
    let attach (resource: IDisposable) =
        let context = CurrentContext.Value
        if not(isNull context) && not(context.CanWrite()) then resource.Dispose()
        else (resourceOwner()).Attach resource

    /// <summary>Runs with a registration callback while retaining the current dynamic resource-owner selector.</summary>
    let run registration callback =
        let previous = CurrentContext.Value
        let owner = if isNull previous then None else previous.ResourceOwner
        let canWrite = if isNull previous then (fun () -> true) else previous.CanWrite
        CurrentContext.Value <- Context(registration, owner, canWrite)
        try callback()
        finally CurrentContext.Value <- previous

    /// <summary>Runs with restoration and a selector evaluated when each resource is attached.</summary>
    let runOwned registration selectOwner canWrite callback =
        let previous = CurrentContext.Value
        CurrentContext.Value <- Context(registration, Some selectOwner, canWrite)
        try callback()
        finally CurrentContext.Value <- previous
