namespace Ranvier.Anime

open System
open Ranvier.Dom

/// <summary>
/// Documented stub APIs for upstream capabilities whose identical native implementation requires changes
/// in external dependencies outside the scope of <c>Ranvier.Anime</c>.
/// </summary>
[<RequireQualifiedAccess>]
module DeferredCapabilities =
    /// <summary>
    /// Deferred stub for capturing an opaque pending CSS variable shorthand snapshot when an overlapping
    /// longhand override with differing priority crosses outside the animated property's scope.
    /// </summary>
    /// <remarks>
    /// WebCore's CSSOM serializes pending <c>var(--token)</c> shorthand longhands as empty strings, and
    /// <c>Ranvier.Dom</c> does not currently expose WebKit's internal opaque pending-substitution value handle.
    /// Scoped shorthand variables without cross-scope priority conflicts ARE supported by <c>CssPropertySnapshot</c>.
    /// </remarks>
    let captureOpaquePendingCssSnapshot (style: CSSStyleDeclaration) (propertyName: string) : IDisposable =
        if isNull (box style) then nullArg "style"
        if String.IsNullOrWhiteSpace propertyName then invalidArg "propertyName" "A CSS property name is required."
        raise (NotSupportedException(
            "Opaque cross-scope pending CSS shorthand snapshots require native pending-substitution handles in Ranvier.Dom (" + propertyName + ")."))
