namespace Ranvier.Anime

open System
open Ranvier.Dom

/// <summary>A morph progress accessor with source-attribute restoration and actual owner guards.</summary>
type SvgMorph internal (source:SVGGeometryElement,destination:SVGGeometryElement,precision:float,previous:string option) as this =
    let lifetime=SvgOperationLifetime()
    let attribute=if lifetime.ReadConstruction(fun()->source.tagName)="path" then "d" else "points"
    let original=lifetime.ReadConstruction(fun()->source.getAttribute attribute)
    let mutable interpolate:(float -> int -> (float -> float) -> (unit -> bool) -> ValueInterpolation.Output option) option=None
    let mutable currentValue=0.
    let mutable mutated=false
    let progress=NumericProperty((fun()->currentValue),(fun value->
        if lifetime.Active then
            let current=lifetime.BeginOperation()
            try
                match interpolate with
                | Some prepare ->
                    match prepare value 4 id current with
                    | Some output when current() ->
                        mutated<-true
                        source.setAttribute(attribute,output.Text)
                        if current() then currentValue<-value
                    | _ -> ()
                | None -> ()
            with _ -> lifetime.Close();reraise()))
    /// <summary>Registers only this outer snapshot and attaches it to the selected actual owner before mutation.</summary>
    member internal _.Register()=
        ScopeRegistration.register this.Revert
        lifetime.Attach(this:>IDisposable)
    /// <summary>Captures guarded endpoints without a second hidden morph restoration registration.</summary>
    member internal _.Initialize()=
        try
            lifetime.EnsureConstruction()
            let first,last=Svg.morphEndpoints source destination precision previous (fun()->lifetime.ConstructionCurrent) (fun()->mutated<-true)
            lifetime.EnsureConstruction()
            interpolate<-Some(ValueInterpolation.prepare (ValueInterpolation.Text first) (ValueInterpolation.Text first) (ValueInterpolation.Text last))
            lifetime.EnsureConstruction()
            lifetime.Commit()
        with _ ->
            if lifetime.ConstructionCurrent then try this.Revert() with _ -> ()
            else lifetime.Close()
            reraise()
    /// <summary>Normalized progress; writes after restoration, supersession or owner disposal are ignored.</summary>
    member _.Progress=progress
    /// <summary>Restores the original source attribute once, stopping after owner or scope-generation reentry.</summary>
    member _.Revert()=
        match lifetime.BeginRestore() with
        | None -> ()
        | Some current ->
            try
                if mutated && current() then
                    if isNull original then source.removeAttribute attribute else source.setAttribute(attribute,original)
            finally lifetime.EndRestore()
    interface IDisposable with member _.Dispose()=this.Revert()

/// <summary>Numeric property accessors composing SVG operations with existing owned animation runners.</summary>
module SvgProperties =
    /// <summary>Creates a distance accessor with owner and generation checks after every geometry read and before applying a sample.</summary>
    /// <remarks>The initial distance is zero. Graph-free callers may use it without creating a graph; scoped motion attaches to the selected actual owner.</remarks>
    let motion (path:SVGGeometryElement) inSvg offset (apply:SvgMotionSample -> unit) =
        let lifetime=SvgOperationLifetime()
        let mutable currentValue=0.
        let close ()=lifetime.Close()
        try
            lifetime.EnsureConstruction()
            ScopeRegistration.register close
            lifetime.Attach {new IDisposable with member _.Dispose()=close()}
            lifetime.EnsureConstruction()
            let sample=Svg.captureMotion path inSvg offset (fun()->lifetime.ConstructionCurrent)
            lifetime.EnsureConstruction()
            lifetime.Commit()
            NumericProperty((fun()->currentValue),(fun value->
                if lifetime.Active then
                    let current=lifetime.BeginOperation()
                    try
                        match sample current value with
                        | Some position when current() ->
                            apply position
                            if current() then currentValue<-value
                        | _ -> ()
                    with _ -> close();reraise()))
        with _ -> close();reraise()

    /// <summary>Creates start/end accessors whose accepted writes redraw and then commit their current fractions.</summary>
    /// <remarks>Nested setters supersede the interrupted outer write. Existing runners supply scheduling.</remarks>
    let drawRange (drawable:Svg.Drawable) initialStart initialEnd =
        let mutable start,finish=initialStart,initialEnd
        let first=NumericProperty((fun()->start),(fun value->
            if not drawable.IsReverted && drawable.TryDraw(value,finish) then start<-value))
        let last=NumericProperty((fun()->finish),(fun value->
            if not drawable.IsReverted && drawable.TryDraw(start,value) then finish<-value))
        first,last

    /// <summary>Creates normalized morph progress with one early snapshot registration and first-fault constructor rollback.</summary>
    let morph (source:SVGGeometryElement) (destination:SVGGeometryElement) precision previous =
        let result=new SvgMorph(source,destination,precision,previous)
        try
            result.Register()
            result.Initialize()
            result
        with _ ->
            try result.Revert() with _ -> ()
            reraise()
