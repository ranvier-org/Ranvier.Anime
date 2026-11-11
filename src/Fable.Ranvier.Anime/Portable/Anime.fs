namespace Ranvier.Anime

open Fable.Core

/// <summary>anime.js's easing curves as functions of progress.</summary>
[<RequireQualifiedAccess>]
module Ease =
    [<Import("eases", "animejs")>]
    let private eases: obj = jsNative

    // An entry of anime's table: the curve itself, or, for the power, back and elastic families, its factory called with
    // anime's default parameters. Null for a name the table lacks.
    [<Emit("((e, n) => { const f = typeof n === 'string' && Object.prototype.hasOwnProperty.call(e, n) ? e[n] : null; return !f ? null : /^(in|out|inOut|outIn)(Back|Elastic)?$/.test(n) ? f() : f; })($0, $1)")>]
    let private lookup (table: obj) (name: string) : float -> float = jsNative

    /// <summary>The anime.js curve of that name with its default parameters, such as <c>"outQuad"</c> or <c>"inOutBack"</c>.</summary>
    /// <exception cref="T:System.ArgumentException">The name is not an anime.js easing name.</exception>
    let named (name: string) : float -> float =
        let ease = lookup eases name

        if isNull (box ease) then
            invalidArg "name" "Unknown easing name."

        ease

    /// <summary>A CSS-style cubic Bézier curve through the control points (x1, y1) and (x2, y2).</summary>
    let cubicBezier (x1: float) (y1: float) (x2: float) (y2: float) : float -> float =
        unbox (Fable.Ranvier.Anime.Ease.cubicBezier (x1, y1, x2, y2))

    /// <summary>A staircase of <c>count</c> equal steps; <c>fromStart</c> jumps at the start of each step.</summary>
    /// <exception cref="T:System.ArgumentException"><c>count</c> is zero.</exception>
    let steps (count: uint) (fromStart: bool) : float -> float =
        if count = 0u then
            invalidArg "count" "At least one step is required."

        unbox (Fable.Ranvier.Anime.Ease.steps (int count, fromStart))

/// <summary>Animations and timelines on anime.js's engine.</summary>
[<RequireQualifiedAccess>]
module Anime =
    /// <summary>Animates the properties of the targets, playing at once unless <c>Autoplay</c> is false.</summary>
    let animateTargets (targets: obj array) (specs: PropertySpec array) (options: ComposedAnimationOptions) : ComposedAnimation =
        Fable.Ranvier.Anime.Anime.animate (Fable.Ranvier.Anime.Targets.object targets, unbox (BrowserParams.animation specs options false))
        :> ComposedAnimation

    /// <summary>Animates the properties of the target, playing at once unless <c>Autoplay</c> is false.</summary>
    let animate (target: obj) (specs: PropertySpec array) (options: ComposedAnimationOptions) : ComposedAnimation =
        Fable.Ranvier.Anime.Anime.animate (Fable.Ranvier.Anime.Targets.object target, unbox (BrowserParams.animation specs options false))
        :> ComposedAnimation

    /// <summary>An empty timeline; children are added with <c>Add</c>.</summary>
    let timeline () : ComposedTimeline =
        Fable.Ranvier.Anime.Anime.timeline () :> ComposedTimeline

    /// <summary>
    /// Pauses the object at its current values when it is a tickable, or an animatable, whose property animations are
    /// paused; any other revertible is left as it is.
    /// </summary>
    [<Emit("(r => { if (typeof r.pause === 'function') r.pause(); else if (r.animations) for (const k in r.animations) r.animations[k].pause(); })($0)")>]
    let pause (revertible: IRevertible) : unit = jsNative
