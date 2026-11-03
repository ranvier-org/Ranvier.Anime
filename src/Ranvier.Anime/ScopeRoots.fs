namespace Ranvier.Anime

// Derived from Anime.js v4.5.0 scope.js, Copyright (c) 2025 Julian Garnier, MIT.
// The upstream MIT notice is retained in THIRD-PARTY-NOTICES.md.

open System
open Ranvier.Dom

/// <summary>Root routing shared by native selection and deterministic contract fixtures.</summary>
module internal ScopeRootRouting =
    let resolve document activeRoot query selector =
        match selector with
        | None -> document
        | Some selector -> query activeRoot selector |> Option.defaultValue document

/// <summary>Native target selection through the current scope's existing DOM root.</summary>
[<RequireQualifiedAccess>]
module ScopeTargets =
    /// <summary>Selects the active scope root, falling back to the supplied owning document.</summary>
    let root (document: Document) : ParentNode =
        if isNull(box document) then nullArg "document"
        match Scope.Current with
        | Some scope ->
            if scope.IsDisposed then raise(ObjectDisposedException "Scope")
            defaultArg scope.Root (document :> ParentNode)
        | None -> document :> ParentNode

    /// <summary>Queries the active native root; invalid selectors retain the DOM engine's exception behavior.</summary>
    let select (document: Document) (selector: string) : NodeList =
        (root document).querySelectorAll selector

/// <summary>Scope construction with an explicit native document and source-compatible selector routing.</summary>
[<AutoOpen>]
module ScopeRootExtensions =
    type Scope with
        /// <summary>Creates a scope with a selector relative to the active root; omitted or unmatched selectors use the supplied document.</summary>
        /// <remarks>Requires an active Ranvier graph. Selected nodes and document must belong to the same native host.</remarks>
        static member ForDocument(document: Document, ?selector: string, ?defaults: PlaybackOptions,
                                  ?clockDefaults: PlaybackDefaults, ?animationDefaults: AnimationDefaults, ?media: MediaQueryProvider,
                                  ?mediaQueries: seq<string * string>) =
            if isNull(box document) then nullArg "document"
            if not(ScopeRegistration.canConstruct()) then raise(ObjectDisposedException "Scope")
            let query (root: ParentNode) selector =
                let selected = root.querySelector selector
                if isNull(box selected) then None else Some(selected :> ParentNode)
            let root = ScopeRootRouting.resolve (document :> ParentNode) (ScopeTargets.root document) query selector
            if not(ScopeRegistration.canConstruct()) then raise(ObjectDisposedException "Scope")
            new Scope(root=root,?defaults=defaults,?clockDefaults=clockDefaults,?animationDefaults=animationDefaults,?media=media,?mediaQueries=mediaQueries)
