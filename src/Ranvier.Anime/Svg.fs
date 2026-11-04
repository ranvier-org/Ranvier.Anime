// Derived from Anime.js v4.5.0 (2c9cf8ea), Copyright (c) 2025 Julian Garnier. MIT License.
// Full upstream license: upstream/anime/LICENSE.md.
namespace Ranvier.Anime

open System
open Ranvier
open Ranvier.Dom

/// <summary>Generation checks around SVG effects using the selected actual Ranvier owner.</summary>
type internal SvgOperationLifetime() =
    let scopedOwnerAllowsWrite=ScopeRegistration.captureWriteGuard()
    let owner=match Graph.TryCurrent with ValueSome _ -> Some(ScopeRegistration.resourceOwner()) | ValueNone -> None
    let ownerAllowsWrite ()=scopedOwnerAllowsWrite() && (owner |> Option.forall(fun owner->not owner.IsDisposed))
    let scope=Scope.Current
    let scopeGeneration ()=scope |> Option.map _.RestorationGeneration
    let constructionGeneration=scopeGeneration()
    let mutable active=true
    let mutable generation=0
    let mutable initializing=true
    let mutable reverting=false
    let constructionCurrent ()=ownerAllowsWrite() && scopeGeneration()=constructionGeneration && ScopeRegistration.canConstruct()
    /// <summary>Whether constructor mutation still requires rollback on failure.</summary>
    member _.Initializing=initializing
    /// <summary>Whether normal SVG effects remain admitted by the actual owner.</summary>
    member _.Active=active && not reverting && ownerAllowsWrite()
    /// <summary>Whether the captured construction still belongs to its scope generation.</summary>
    member _.ConstructionCurrent=active && not reverting && constructionCurrent()
    /// <summary>Rejects stale construction before or after an external native call.</summary>
    member _.EnsureConstruction()=
        if not active || reverting || not(constructionCurrent()) then raise(ObjectDisposedException "SVG construction")
    /// <summary>Checks actual owner and constructor generation around one external read.</summary>
    member this.ReadConstruction(action:unit -> 'T)=
        this.EnsureConstruction()
        let value=action()
        this.EnsureConstruction()
        value
    /// <summary>Starts one setter/sample operation, superseding interrupted nested work.</summary>
    member _.BeginOperation()=
        generation<-generation+1
        let expected=generation
        let expectedScope=scopeGeneration()
        fun () -> active && not reverting && generation=expected && ownerAllowsWrite() &&
                  scopeGeneration()=expectedScope && (not initializing || constructionCurrent())
    /// <summary>Ends constructor-only admission while retaining owner checks.</summary>
    member _.Commit()=initializing<-false
    /// <summary>Terminates later effects and invalidates previously captured operations.</summary>
    member _.Close()=if active then active<-false;generation<-generation+1
    /// <summary>Admits disposal restoration and captures guards against later owner/scope reentry.</summary>
    member _.BeginRestore()=
        if not active || reverting then None
        else
            reverting<-true
            generation<-generation+1
            let expected=generation
            let expectedScope=scopeGeneration()
            let admittedWithOwner=ownerAllowsWrite()
            Some(fun () -> active && reverting && generation=expected && scopeGeneration()=expectedScope &&
                           (not admittedWithOwner || ownerAllowsWrite()))
    /// <summary>Closes the restoration lifetime after success or its first recorded failure.</summary>
    member this.EndRestore()=this.Close();reverting<-false
    /// <summary>Uses the selected actual owner when a graph exists; standalone callers own disposal.</summary>
    member _.Attach(resource:IDisposable)=
        match Graph.TryCurrent with ValueSome _ -> ScopeRegistration.attach resource | ValueNone -> ()

/// <summary>Calculations shared by SVG sampling and drawing.</summary>
module internal SvgMath =
    /// <summary>Samples local geometry and tangent in user units, then transforms the position.</summary>
    let sample length offset progress (point:float -> struct(float*float)) (transform:float -> float -> struct(float*float)) =
        let location lookup =
            let value=progress+offset*length+lookup
            let distance=if offset=0. then Math.Clamp(value,0.,length) else (value % length+length)%length
            point distance
        let struct(x,y)=location 0.
        let struct(x0,y0)=location -1.
        let struct(x1,y1)=location 1.
        let struct(tx,ty)=transform x y
        struct(tx,ty,Math.Atan2(y1-y0,x1-x0)*180./Math.PI)
    /// <summary>Computes dash offset and lengths for a normalized 1000-unit path, preserving fraction overshoot.</summary>
    let stroke start finish scale =
        let offset = start * -1000. * scale
        let first = finish * 1000. * scale + offset
        let extra = if (start = 0. && finish = 1.) || (start = 1. && finish = 0.) then 0. else 10. * scale
        struct(offset, first, 1000. * scale + extra - first)
    /// <summary>Formats SVG numeric attributes with the shared ECMAScript number serializer.</summary>
    let number (value:float) = ValueInterpolation.number value
    /// <summary>Rounds sampled x coordinates to three fractional places, with midpoint ties toward positive infinity.</summary>
    let roundX value = Math.Floor(value * 1000. + 0.5) / 1000.


/// <summary>A sampled path position and tangent angle in degrees.</summary>
[<Struct>]
type SvgMotionSample = { X:float; Y:float; Angle:float }

/// <summary>Identifies superseded internal reads separately from exceptions raised by native callbacks.</summary>
type internal SvgSupersededOperation() =
    inherit ObjectDisposedException("SVG operation")

/// <summary>SVG geometry operations. Call on the document's owner thread while its host is alive.</summary>
module Svg =
    let private read current action =
        if not(current()) then raise(SvgSupersededOperation())
        let value=action()
        if not(current()) then raise(SvgSupersededOperation())
        value

    /// <summary>Captures geometry under construction guards and supplies optional samples under per-operation guards.</summary>
    let internal captureMotion (path:SVGGeometryElement) inSvg offset current =
        let length=float(read current path.getTotalLength)
        let matrix=read current path.getCTM
        let transform =
            if inSvg || isNull(box matrix) then fun x y -> struct(x,y)
            else
                let a=read current (fun()->matrix.a)
                let b=read current (fun()->matrix.b)
                let c=read current (fun()->matrix.c)
                let d=read current (fun()->matrix.d)
                let e=read current (fun()->matrix.e)
                let f=read current (fun()->matrix.f)
                fun x y -> struct(x*a+y*c+e,x*b+y*d+f)
        fun current progress ->
            let point distance =
                if not(current()) then struct(nan,nan)
                else
                    let point=path.getPointAtLength(float32 distance)
                    if not(current()) then struct(nan,nan)
                    else
                        let x=float point.x
                        if not(current()) then struct(nan,nan)
                        else
                            let y=float point.y
                            if current() then struct(x,y) else struct(nan,nan)
            if not(current()) then None
            else
                let struct(x,y,angle)=SvgMath.sample length offset progress point transform
                if current() then Some {X=x;Y=y;Angle=angle} else None

    /// <summary>Captures length and CTM, then samples live geometry at a distance in SVG user units.</summary>
    /// <remarks>SVG targets use local coordinates; other targets use the captured CTM. Tangents use local coordinates.</remarks>
    let createMotionPath (path:SVGGeometryElement) (inSvg:bool) (offset:float) =
        let lifetime=SvgOperationLifetime()
        lifetime.EnsureConstruction()
        let sample=
            try captureMotion path inSvg offset (fun()->lifetime.ConstructionCurrent)
            with :? SvgSupersededOperation -> raise(ObjectDisposedException "SVG construction")
        lifetime.Commit()
        fun progress ->
            let current=lifetime.BeginOperation()
            match sample current progress with
            | Some value -> value
            | None -> raise(ObjectDisposedException "SVG motion sample")

    /// <summary>A typed draw setter for an SVG element; fractions may extend beyond zero and one.</summary>
    type Drawable internal (path:SVGGeometryElement, computed:CSSStyleDeclaration) as this =
        let lifetime=SvgOperationLifetime()
        let cap=lifetime.ReadConstruction(fun()->computed.getPropertyValue "stroke-linecap")
        let nonScaling=lifetime.ReadConstruction(fun()->computed.getPropertyValue "vector-effect")="non-scaling-stroke"
        let mutable currentCap=cap
        let attributes=[|"pathLength";"draw";"stroke-dashoffset";"stroke-dasharray"|]
                       |> Array.map(fun name->name,lifetime.ReadConstruction(fun()->path.getAttribute name))
        let style=lifetime.ReadConstruction(fun()->path.style)
        let capSnapshot=CssPropertySnapshot.Capture(style,"stroke-linecap",lifetime.EnsureConstruction)
        let mutable mutated=false
        /// <summary>Registers restoration and attaches disposal before the first SVG mutation.</summary>
        member internal _.Register()=
            ScopeRegistration.register this.Revert
            lifetime.Attach(this:>IDisposable)
        /// <summary>Normalizes path length with constructor rollback and first-fault preservation.</summary>
        member internal _.Initialize(start,finish)=
            try
                lifetime.EnsureConstruction()
                if snd attributes[0]<>"1000" then
                    mutated<-true
                    path.setAttribute("pathLength","1000")
                    lifetime.EnsureConstruction()
                    this.Draw(start,finish)
                    lifetime.EnsureConstruction()
                lifetime.Commit()
            with _ ->
                if lifetime.ConstructionCurrent then try this.Revert() with _ -> ()
                else lifetime.Close()
                reraise()
        /// <summary>Writes one guarded segment and reports whether nested work superseded the write.</summary>
        member internal _.TryDraw(start,finish)=
            if not lifetime.Active then false
            else
                let current=lifetime.BeginOperation()
                try
                    let matrix=if nonScaling then read current path.getCTM else Unchecked.defaultof<SVGMatrix>
                    let scale=
                        if isNull(box matrix) then 1.
                        else
                            let a=read current (fun()->matrix.a)
                            let b=read current (fun()->matrix.b)
                            let c=read current (fun()->matrix.c)
                            let d=read current (fun()->matrix.d)
                            (sqrt(a*a+b*b)+sqrt(c*c+d*d))/2.
                    let struct(offset,first,last)=SvgMath.stroke start finish scale
                    if current() && cap<>"butt" then
                        let nextCap=if start=finish then "butt" else cap
                        if nextCap<>currentCap then
                            mutated<-true
                            style.setProperty("stroke-linecap",nextCap)
                            if current() then currentCap<-nextCap
                    if current() then mutated<-true;path.setAttribute("stroke-dashoffset",SvgMath.number offset)
                    if current() then path.setAttribute("stroke-dasharray",SvgMath.number first+" "+SvgMath.number last)
                    if current() then path.setAttribute("draw",SvgMath.number start+" "+SvgMath.number finish)
                    current()
                with
                | :? SvgSupersededOperation -> false
                | _ ->
                    if not lifetime.Initializing then lifetime.Close()
                    reraise()
        /// <summary>Writes draw, stroke dash offset/array, and the line cap for an empty segment.</summary>
        member _.Draw(start:float,finish:float)=
            ObjectDisposedException.ThrowIf(not lifetime.Active,"SVG drawable")
            this.TryDraw(start,finish) |> ignore
        /// <summary>The underlying geometry receives all attribute and inline-style writes.</summary>
        member _.Element=path
        /// <summary>Restores captured attributes and inline line cap once, retaining unrelated properties.</summary>
        member _.Revert()=
            match lifetime.BeginRestore() with
            | None -> ()
            | Some current ->
                let mutable failure:exn option=None
                let attempt action=try action() with error->if failure.IsNone then failure<-Some error
                if mutated then
                    for name,value in attributes do
                        if current() then attempt(fun()->if isNull value then path.removeAttribute name else path.setAttribute(name,value))
                    if current() then attempt(fun()->capSnapshot.Restore(style,current))
                lifetime.EndRestore()
                failure |> Option.iter(fun error->Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw())
        /// <summary>Whether restoration, owner disposal or a fault ended drawing accessors.</summary>
        member _.IsReverted=not lifetime.Active
        interface IDisposable with member _.Dispose()=this.Revert()

    /// <summary>Normalizes pathLength to 1000 and initializes the draw range when normalization changes.</summary>
    /// <remarks>Pass the owning document's computed style. Standalone graph-free callers own explicit disposal.</remarks>
    let createDrawable (path:SVGGeometryElement) (computed:CSSStyleDeclaration) start finish =
        let result=new Drawable(path,computed)
        try
            result.Register()
            result.Initialize(start,finish)
            result
        with _ ->
            try result.Revert() with _ -> ()
            reraise()

    /// <summary>Samples source endpoint strings under an outer facade's construction and restoration guards.</summary>
    let internal morphEndpoints (source:SVGGeometryElement) (destination:SVGGeometryElement) precision previous current beforeWrite =
        let validate (path:SVGGeometryElement)=
            let tag=read current (fun()->path.tagName)
            match tag.ToLowerInvariant() with
            | "path" | "polygon" | "polyline" -> ()
            | _ -> invalidArg "path" ("Can't morph a <"+tag+"> SVG element.")
        validate source
        validate destination
        let isPath=read current (fun()->source.tagName)="path"
        let attribute=if isPath then "d" else "points"
        previous |> Option.iter(fun value->
            if current() then beforeWrite();source.setAttribute(attribute,value)
            read current (fun()->()) |> ignore)
        if precision=0. || Double.IsNaN precision then
            read current (fun()->source.getAttribute attribute),read current (fun()->destination.getAttribute attribute)
        else
            let length1=float(read current source.getTotalLength)
            let length2=float(read current destination.getTotalLength)
            let count=Math.Max(0.,max (Math.Ceiling(length1*precision)) (Math.Ceiling(length2*precision)))
            if count>float Int32.MaxValue then invalidArg "precision" "The sample count exceeds Int32.MaxValue."
            let first,last=System.Text.StringBuilder(),System.Text.StringBuilder()
            let append (text:System.Text.StringBuilder) index (point:SVGPoint)=
                let x=read current (fun()->float point.x)
                let y=read current (fun()->float point.y)
                let prefix=if isPath then (if index=0 then "M" else "L") else ""
                let separator=if isPath then " " else ","
                text.Append(prefix).Append(SvgMath.number(SvgMath.roundX x)).Append(separator)
                    .Append(SvgMath.number y).Append(' ') |> ignore
            for index in 0 .. int count-1 do
                let progress=float index/(count-1.)
                append first index (read current (fun()->source.getPointAtLength(float32(length1*progress))))
                append last index (read current (fun()->destination.getPointAtLength(float32(length2*progress))))
            first.ToString(),last.ToString()

    /// <summary>Samples matching point counts for morph keyframes; precision zero returns the original attributes.</summary>
    /// <remarks>The source tag selects d or points for both inputs. Previous-value restoration registers before its first write.</remarks>
    let morphTo (source:SVGGeometryElement) (destination:SVGGeometryElement) (precision:float) (previous:string option) =
        let lifetime=SvgOperationLifetime()
        lifetime.EnsureConstruction()
        let attribute=if lifetime.ReadConstruction(fun()->source.tagName)="path" then "d" else "points"
        let original=if previous.IsSome then lifetime.ReadConstruction(fun()->source.getAttribute attribute) else null
        let mutable mutated=false
        let restore ()=
            match lifetime.BeginRestore() with
            | None -> ()
            | Some current ->
                try
                    if mutated && current() then
                        if isNull original then source.removeAttribute attribute else source.setAttribute(attribute,original)
                finally lifetime.EndRestore()
        try
            if previous.IsSome then
                ScopeRegistration.register restore
                lifetime.Attach {new IDisposable with member _.Dispose()=restore()}
            lifetime.EnsureConstruction()
            let result=morphEndpoints source destination precision previous (fun()->lifetime.ConstructionCurrent) (fun()->mutated<-true)
            lifetime.EnsureConstruction()
            lifetime.Commit()
            result
        with _ ->
            if lifetime.ConstructionCurrent then try restore() with _ -> ()
            else lifetime.Close()
            reraise()
