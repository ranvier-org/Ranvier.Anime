namespace Ranvier.Anime

/// <summary>Shares source engine root admission with timers and timelines.</summary>
module internal AnimatableFrames =
    /// <summary>Allocates a root in the host scheduler's engine queue.</summary>
    let createRoot scheduler = EngineFrames.createRoot scheduler
