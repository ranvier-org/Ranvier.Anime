namespace Ranvier.Anime.NativeSmoke

open System
open Ranvier.Dom

/// <summary>Observes actual native wrapper identity without assuming canonical wrappers.</summary>
module CompositionNativeIdentityProbe =
    /// <summary>Six actual wrapper and DOM identity observations.</summary>
    [<NoEquality; NoComparison>]
    type InlineStyleIdentityObservation =
        { SameElementReference:bool
          SameNativeNode:bool
          SameStyleOnRepeatedAccess:bool
          SameStyleAcrossElementAliases:bool
          StyleEqualityOnRepeatedAccess:bool
          StyleEqualityAcrossElementAliases:bool }
        /// <summary>Formats six labeled booleans without generated record formatting or reflection.</summary>
        override this.ToString() =
            let text value=if value then "true" else "false"
            "SameElementReference="+text this.SameElementReference+
            "; SameNativeNode="+text this.SameNativeNode+
            "; SameStyleOnRepeatedAccess="+text this.SameStyleOnRepeatedAccess+
            "; SameStyleAcrossElementAliases="+text this.SameStyleAcrossElementAliases+
            "; StyleEqualityOnRepeatedAccess="+text this.StyleEqualityOnRepeatedAccess+
            "; StyleEqualityAcrossElementAliases="+text this.StyleEqualityAcrossElementAliases

    /// <summary>Inspects actual element handles without changing DOM or CSS.</summary>
    /// <param name="first">The first actual native element handle.</param>
    /// <param name="alias">An independently obtained handle for the same DOM node.</param>
    /// <remarks>Reference and equality observations do not assert wrapper canonicalization.</remarks>
    let inspect (first:HTMLElement) (alias:HTMLElement) =
        let firstStyle=((first:>ElementCSSInlineStyle).style:>CSSStyleDeclaration)
        let repeatedStyle=((first:>ElementCSSInlineStyle).style:>CSSStyleDeclaration)
        let aliasStyle=((alias:>ElementCSSInlineStyle).style:>CSSStyleDeclaration)
        { SameElementReference=Object.ReferenceEquals(first,alias)
          SameNativeNode=(first:>Node).isSameNode(alias:>Node)
          SameStyleOnRepeatedAccess=Object.ReferenceEquals(firstStyle,repeatedStyle)
          SameStyleAcrossElementAliases=Object.ReferenceEquals(firstStyle,aliasStyle)
          StyleEqualityOnRepeatedAccess=Object.Equals(firstStyle,repeatedStyle)
          StyleEqualityAcrossElementAliases=Object.Equals(firstStyle,aliasStyle) }

    /// <summary>Creates a temporary actual element, prints six identity observations, then removes it.</summary>
    /// <param name="document">The compiled native host's actual document.</param>
    /// <remarks>Only DOM node identity is asserted. Style wrapper canonicalization remains an observation.</remarks>
    let run (document:Document) =
        let id="__ranvier_anime_inline_style_identity_probe"
        if not(isNull(box(document.getElementById id))) then invalidOp "Identity probe element already exists."
        let element=document.createElement("div") :?> HTMLElement
        (element:>Element).setAttribute("id",id)
        try
            (document.body:>Node).appendChild(element:>Node) |> ignore
            let alias=document.getElementById(id) :?> HTMLElement
            let observation=inspect element alias
            if not observation.SameNativeNode then failwith "Independent element query returned a different native node."
            Console.WriteLine("COMPOSITION_INLINE_STYLE_IDENTITY "+observation.ToString())
        finally
            (element:>ChildNode).remove()
