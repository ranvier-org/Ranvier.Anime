From RanvierAnime.Pure Require Import
  TimingEngine CurvesStagger InterpolationColor CompositionTimeline DomScrollDragMotion.

(** * Assumption Audit — Every theorem must report `Closed under the global context` *)

(** ** TimingEngine.v *)
Print Assumptions playback_sample_bounds.
Print Assumptions direction_xor_involution.
Print Assumptions zero_duration_repeat_toggle_safe.
Print Assumptions playback_generation_fences_reentrant_seek.
Print Assumptions engine_clock_phase_drift_bounded.
Print Assumptions priority_insert_preserves_sorted.
Print Assumptions deferred_writes_depth_balanced.

(** ** CurvesStagger.v *)
Print Assumptions ease_out_reflection_endpoints_and_involution.
Print Assumptions ease_inout_midpoint_and_symmetry.
Print Assumptions spring_stretch_preserves_completion_ratio.
Print Assumptions stagger_grid3d_bijection.
Print Assumptions stagger_flat_step_simd_eq_scalar.

(** ** InterpolationColor.v *)
Print Assumptions js_half_ceil_round_ties_toward_pos_inf.
Print Assumptions lerp_exact_endpoints.
Print Assumptions homogenize_pads_shorter_to_equal_length.
Print Assumptions gamut_clip_in_bounds_and_idempotent.
Print Assumptions hcl_shortest_hue_arc_bounded.
Print Assumptions achromatic_hue_borrowing_constant.
Print Assumptions lab_white_snap_preserves_in_gamut.
Print Assumptions angle_grad_turn_deg_homomorphism.

(** ** CompositionTimeline.v *)
Print Assumptions timeline_position_relative_ops_sound.
Print Assumptions timeline_remove_resizes_iteration_duration.
Print Assumptions mvcc_rollback_preserves_live_siblings.
Print Assumptions muted_animation_defers_sibling_hijack.
Print Assumptions stretch_preserves_host_epoch_and_validates_first.
Print Assumptions blend_telescoping_initial_conservation_and_target_convergence.
Print Assumptions timeline_track_intervals_disjoint.

(** ** DomScrollDragMotion.v *)
Print Assumptions transform_clear_revert_never_resurrects_stale_cache.
Print Assumptions drag_resist_inverse_roundtrip.
Print Assumptions drag_edge_scroll_and_release_bounds_safe.
Print Assumptions scroll_observer_forced_boundary_once.
Print Assumptions flip_reparent_delta_preserves_viewport_origin.
Print Assumptions motion_animate_on_reverse_revert_restores_initial_style.
Print Assumptions motion_slot_close_is_terminal.
