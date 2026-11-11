namespace Ranvier.Anime

/// <summary>Scope reconstruction through the existing managed animation clocks.</summary>
[<AutoOpen>]
module ScopeAnimationExtensions =
    type Scope with
        /// <summary>Recreates a numeric animation with retained clock state and its captured construction-value restoration.</summary>
        member this.KeepTime(constructor: Scope -> NumericAnimation) : NumericAnimation =
            this.KeepTime(constructor,
                (fun animation -> animation.CaptureScopeTime()),
                (fun animation snapshot -> animation.RestoreScopeTime snapshot),
                (fun animation -> animation.Revert()))
        /// <summary>Recreates a numeric animation with retained clock state and caller-supplied restoration.</summary>
        /// <remarks>NumericAnimation.Cancel retains rendered values; provide restoration that also ends playback.</remarks>
        member this.KeepTime(constructor: Scope -> NumericAnimation, revert: NumericAnimation -> unit) : NumericAnimation =
            this.KeepTime(constructor,
                (fun animation -> animation.CaptureScopeTime()),
                (fun animation snapshot -> animation.RestoreScopeTime snapshot),
                revert)
        /// <summary>Recreates a keyframe sequence with retained clock state and its captured-property Revert operation.</summary>
        member this.KeepTime(constructor: Scope -> KeyframeAnimation) : KeyframeAnimation =
            this.KeepTime(constructor,
                (fun animation -> animation.CaptureScopeTime()),
                (fun animation snapshot -> animation.RestoreScopeTime snapshot),
                (fun animation -> animation.Revert()))
        /// <summary>Recreates a timeline with retained clock state and captured-property restoration for its children.</summary>
        member this.KeepTime(constructor: Scope -> Timeline) : Timeline =
            this.KeepTime(constructor,
                (fun timeline -> timeline.CaptureScopeTime()),
                (fun timeline snapshot -> timeline.RestoreScopeTime snapshot),
                (fun timeline -> timeline.Revert()))
        /// <summary>Recreates a timeline with retained clock state and caller-supplied restoration for its children.</summary>
        /// <remarks>Timeline.Cancel releases playback resources and preserves rendered values.</remarks>
        member this.KeepTime(constructor: Scope -> Timeline, revert: Timeline -> unit) : Timeline =
            this.KeepTime(constructor,
                (fun timeline -> timeline.CaptureScopeTime()),
                (fun timeline snapshot -> timeline.RestoreScopeTime snapshot),
                revert)
