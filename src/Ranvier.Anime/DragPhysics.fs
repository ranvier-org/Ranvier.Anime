namespace Ranvier.Anime

open System

/// <summary>Axis snapping uses a step or ordered points; equal-distance points preserve their authoring order.</summary>
[<RequireQualifiedAccess>]
type DragSnap =
    | None
    | Step of float
    | Points of float list

/// <summary>Translation limits in CSS pixels, ordered top, right, bottom, left.</summary>
[<Struct>]
type DragBounds =
    { Top:float; Right:float; Bottom:float; Left:float }
    /// <summary>Finite source-compatible unconstrained translation limits.</summary>
    static member Unbounded = {Top= -1e12; Right=1e12; Bottom=1e12; Left= -1e12}

/// <summary>Deterministic draggable calculations derived from Anime.js 4.5.0.</summary>
[<RequireQualifiedAccess>]
module DragPhysics =
    let private round places value =
        let scale=Math.Pow(10.,float places)
        Math.Floor(value*scale+0.5)/scale

    /// <summary>Rounds to five decimal places using JavaScript half-up ties, including negative values.</summary>
    let coordinate value = round 5 value

    /// <summary>Snaps by increment or the nearest ordered point; zero steps preserve the input.</summary>
    let snap rule value =
        match rule with
        | DragSnap.None -> value
        | DragSnap.Step step -> if step=0. then value else Math.Floor(value/step+0.5)*step
        | DragSnap.Points [] -> invalidArg "rule" "Snap points must be nonempty."
        | DragSnap.Points(first::rest) ->
            rest |> List.fold(fun nearest point -> if abs(point-value)<abs(nearest-value) then point else nearest) first

    /// <summary>Uses Math.Clamp for ordered bounds and preserves the source's first comparison for inverted limits.</summary>
    let clampAxis low high value =
        if Double.IsNaN value then value
        elif low>high then (if value<low then low else high)
        else Math.Clamp(value,low,high)

    /// <summary>Applies the source's out-of-bounds friction before snapping or release clamping.</summary>
    let resist low high friction speed value =
        let factor=(1.-Math.Clamp(friction,0.,1.))*speed
        if value>high then high+(value-high)*factor
        elif value<low then low+(value-low)*factor
        else value

    /// <summary>Returns the x/y out-of-bounds bit mask, excluding disabled axes.</summary>
    let outOfBounds contained disabledX disabledY bounds x y =
        if not contained then 0
        else
            let xOutside=not disabledX && (x<bounds.Left || x>bounds.Right)
            let yOutside=not disabledY && (y<bounds.Top || y>bounds.Bottom)
            (if xOutside then 1 else 0) ||| (if yOutside then 2 else 0)

    /// <summary>Only displacement strictly greater than the threshold begins a drag.</summary>
    let threshold disabledX disabledY (distance:float) (x:float) (y:float) =
        (not disabledX && abs x>distance) || (not disabledY && abs y>distance)

    /// <summary>Computes inertial destination and frictional overshoot; snapping precedes final container clamping.</summary>
    let releaseAxis low high friction speed snapRule value displacement =
        let overshoot=resist low high friction speed (value+displacement)
        let destination=clampAxis low high (coordinate(snap snapRule overshoot))
        struct(destination,overshoot)

    /// <summary>Axis autoscroll increment rounded to whole CSS pixels.</summary>
    let edgeScroll minimum maximum paddingBefore paddingAfter threshold speed value =
        let negative=Math.Floor(Math.Clamp((value-minimum+paddingBefore)/threshold,-1.,0.)*speed*10.+0.5)
        let positive=Math.Floor(Math.Clamp((value-maximum-paddingAfter)/threshold,0.,1.)*speed*10.+0.5)
        if negative<>0. then negative else positive

/// <summary>Source three-sample maximum velocity with a minimum 17 ms sampling interval.</summary>
type DragVelocity(initialTime:float) =
    let values=Array.zeroCreate<float> 3
    let mutable index=0
    let mutable time=initialTime
    let mutable velocity=0.
    /// <summary>Retains the current velocity when less than 17 ms elapsed; otherwise samples magnitude in pixels per millisecond.</summary>
    member _.Sample(now,dx,dy,multiplier,minimum,maximum) =
        let elapsed=now-time
        if elapsed>=17. then
            time<-now
            values[index]<-DragPhysics.coordinate(DragPhysics.clampAxis minimum maximum (Math.Sqrt(dx*dx+dy*dy)/elapsed*multiplier))
            velocity<-Array.max values
            index<-(index+1)%3
        velocity
    /// <summary>Clears the stack while preserving the source's last sampling time.</summary>
    member _.Reset() = Array.Clear values; index<-0; velocity<-0.
    /// <summary>Maximum of the most recent three eligible samples.</summary>
    member _.Value=velocity
