namespace Ranvier.Anime

open System
open System.Threading
open Ranvier

/// <summary>Ambient scope restoration and selection of the existing Ranvier resource owner.</summary>
module internal ScopeRegistration =
    type private Context =
        { Register: (unit -> unit) -> unit
          ResourceOwner: (unit -> Owner) option
          CanWrite: unit -> bool }
        override _.ToString() = "ScopeRegistration.Context"

    let private current = new ThreadLocal<Context option>(fun () -> None)

    /// <summary>Registers restoration with the executing scope, when one exists.</summary>
    let register revert = current.Value |> Option.iter(fun context -> context.Register revert)

    /// <summary>Selects the scope's current actual Ranvier owner, or the ambient owner outside a scope.</summary>
    let resourceOwner () =
        current.Value
        |> Option.bind _.ResourceOwner
        |> Option.map(fun select -> select())
        |> Option.defaultWith Api.getOwner

    /// <summary>Whether the executing constructor still belongs to its scoped generation.</summary>
    let canConstruct () = current.Value |> Option.forall(fun context -> context.CanWrite())

    /// <summary>Captures the actual resource owner's disposable lifetime, including persistent resources retained through refresh.</summary>
    let captureWriteGuard () =
        match current.Value |> Option.bind _.ResourceOwner with
        | None -> fun () -> true
        | Some selectOwner ->
            let owner = selectOwner()
            fun () -> not owner.IsDisposed

    /// <summary>Attaches cancellation to the selected actual owner; stale constructor work is cancelled immediately.</summary>
    let attach (resource: IDisposable) =
        if current.Value |> Option.exists(fun context -> not(context.CanWrite())) then resource.Dispose()
        else (resourceOwner()).Attach resource

    /// <summary>Runs with a registration callback while retaining the current dynamic resource-owner selector.</summary>
    let run registration callback =
        let previous = current.Value
        current.Value <-
            Some { Register=registration
                   ResourceOwner=previous |> Option.bind _.ResourceOwner
                   CanWrite=previous |> Option.map _.CanWrite |> Option.defaultValue(fun () -> true) }
        try callback()
        finally current.Value <- previous

    /// <summary>Runs with restoration and a selector evaluated when each resource is attached.</summary>
    let runOwned registration selectOwner canWrite callback =
        let previous = current.Value
        current.Value <- Some { Register=registration; ResourceOwner=Some selectOwner; CanWrite=canWrite }
        try callback()
        finally current.Value <- previous
