namespace Ranvier.Anime

/// <summary>Contextual source unit conversion using the caller's existing native DOM element.</summary>
module internal CssUnits =
    open System
    open Ranvier.Dom
    /// <summary>Converts source angle ratios or two recorded baseline widths; zero measurements use 100.</summary>
    let convertMeasured measure value currentUnit targetUnit =
        if not(Double.IsFinite value) then invalidArg "value" "Expected a finite value."
        let angle = function "deg" -> Some 1. | "rad" -> Some(180./Math.PI) | "turn" -> Some 360. | _ -> None
        let result =
            if currentUnit=targetUnit then value
            else
                match angle currentUnit,angle targetUnit with
                | Some first,Some last -> value*first/last
                | Some _,None | None,Some _ -> invalidArg "targetUnit" "Cannot convert between angles and measured length units."
                | None,None ->
                    let first,last=measure currentUnit targetUnit
                    let first=if first=0 then 100. else float first
                    let last=if last=0 then 100. else float last
                    first/last*value
        if not(Double.IsFinite result) then invalidArg "value" "Converted value is not finite."
        result
    /// <summary>Measures a shallow native clone once per conversion and removes it on success or failure.</summary>
    let convert (element:HTMLElement) value currentUnit targetUnit =
        if isNull(box element) then nullArg "element"
        let measure first last =
            let original=element:>Node
            let document=original.ownerDocument
            let parent=original.parentNode
            let container=if isNull(box parent) || parent.nodeType=9us then document.body:>Node else parent
            let clone=original.cloneNode():?>HTMLElement
            let node=clone:>Node
            let style=(clone:>ElementCSSInlineStyle).style
            let width unitName =
                style.setProperty("width","100"+(if isNull unitName then "null" else unitName))
                clone.offsetWidth
            let mutable failed=false
            try
                try
                    container.appendChild node |> ignore
                    width first,width last
                with _ -> failed<-true; reraise()
            finally
                try
                    let parent=node.parentNode
                    if not(isNull(box parent)) then parent.removeChild node |> ignore
                with _ when failed -> ()
        convertMeasured measure value currentUnit targetUnit
