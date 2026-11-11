namespace Ranvier.Anime

/// <summary>Scope reconstruction for the managed timer clock.</summary>
[<AutoOpen>]
module ScopeTimerExtensions =
    type Scope with
        /// <summary>Recreates the positional timer while retaining source iteration progress, direction mapping and scheduling anchor.</summary>
        member this.KeepTime(constructor: Scope -> Timer) : Timer =
            this.KeepTime(constructor,
                (fun timer -> timer.CaptureScopeTime()),
                (fun timer snapshot -> timer.RestoreScopeTime snapshot),
                (fun timer -> timer.RewindForRevert()))
