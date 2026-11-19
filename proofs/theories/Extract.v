From stdpp Require Import prelude.
From Stdlib Require Extraction.
From Stdlib Require Import Arith Lia List Bool ZArith.
From RanvierAnime.Pure Require Import
  TimingEngine CurvesStagger InterpolationColor CompositionTimeline DomScrollDragMotion.
Import ListNotations.

Local Arguments Nat.eqb : simpl never.
Open Scope Z_scope.

(** * Executable runners for conformance and OCaml <-> F# differential testing *)

(** ** 1. `PlaybackTiming` + `EngineClock` + priority queue runner *)

Fixpoint build_prio_queue (items : list (Z * nat)) : list (Z * nat) :=
  match items with
  | [] => []
  | (p, id) :: rest => insert_priority p id (build_prio_queue rest)
  end.

Fixpoint insert_prio_seq (acc : list (Z * nat)) (items : list (Z * nat)) : list (Z * nat) :=
  match items with
  | [] => acc
  | (p, id) :: rest => insert_prio_seq (insert_priority p id acc) rest
  end.

Definition observe_timing_engine
    (iter_dur iters delay loop_delay : Z)
    (alternate reversed : bool)
    (raw_t : Z) (iter_idx : nat)
    (last_tick now frame_dur : Z)
    (prios : list (Z * nat))
    : Z * Z * bool * Z * list nat :=
  let cfg :=
    {| tc_iter_dur := iter_dur;
       tc_iters := iters;
       tc_delay := delay;
       tc_loop_delay := loop_delay;
       tc_alternate := alternate;
       tc_reversed := reversed |} in
  let anchor' :=
    if Z.leb frame_dur 0 then last_tick
    else advance_clock_anchor last_tick now frame_dur in
  let ordered_ids := List.map snd (insert_prio_seq [] prios) in
  (total_duration cfg,
   sample_bounded_time cfg raw_t,
   sample_direction reversed alternate iter_idx,
   anchor',
   ordered_ids).

(** ** 2. `Stagger.flatStep` + `Grid3D` coordinate runner *)

Fixpoint seq_z (start : Z) (count : nat) : list Z :=
  match count with
  | 0%nat => []
  | S c => start :: seq_z (start + 1) c
  end.

Definition observe_stagger_grid
    (cols rows origin_idx step start_off : Z) (count : nat)
    : list Z * list (Z * Z * Z) :=
  let offsets := flat_step_simd_chunks count 4%nat 0 count origin_idx step start_off in
  let safe_cols := Z.max 1 cols in
  let safe_rows := Z.max 1 rows in
  let coords := List.map (grid3_decode safe_cols safe_rows) (seq_z 0 (Nat.min 6%nat count)) in
  (offsets, coords).

(** ** 3. `PropertyNumbers.round` + `ValueInterpolation` + `ColorSpace` + `CssUnits` runner *)

Definition observe_round_and_color
    (half_nums : list Z)
    (xs ys : list Z)
    (c0_zero : bool) (h0 h1 : Z)
    (grad_hund : Z)
    : list Z * (list Z * list Z) * Z * Z :=
  let rounded := List.map js_round_half_int half_nums in
  let homog := homogenize_components xs ys in
  let hue_mid :=
    if c0_zero then (Z.modulo (Z.modulo h1 360 + 360) 360) * 2
    else
      let h0n := Z.modulo (Z.modulo h0 360 + 360) 360 in
      let h1n := Z.modulo (Z.modulo h1 360 + 360) 360 in
      let d := hcl_shortest_delta h0n h1n in
      Z.modulo (Z.modulo (h0n * 2 + d) 720 + 720) 720 in
  let deg_x10 := (grad_hund * 9) / 10 in
  (rounded, homog, hue_mid, deg_x10).

(** ** 4. `Composition` (`Muted`, `Blend`, `Stretch`) + `Timeline.Remove` runner *)

Fixpoint apply_blend_targets (init_val : Z) (targets : list (bool * Z)) : Z :=
  match targets with
  | [] => init_val
  | (is_muted, tgt) :: rest =>
    let next_val := if is_muted then init_val else tgt in
    apply_blend_targets next_val rest
  end.

Fixpoint index_child_durs (idx : nat) (durs : list Z) : list (nat * Z * Z) :=
  match durs with
  | [] => []
  | d :: rest => (idx, 0, Z.max 1 d) :: index_child_durs (S idx) rest
  end.

Definition observe_composition_timeline
    (init_val : Z) (blend_targets : list (bool * Z))
    (host_epoch clock_delay tw_start old_dur new_dur : Z)
    (child_durs : list Z) (remove_idx : nat)
    : Z * option Z * Z :=
  let settled_blend := apply_blend_targets init_val blend_targets in
  let tw0 :=
    {| tt_host_epoch := host_epoch;
       tt_clock_delay := clock_delay;
       tt_start := tw_start;
       tt_dur := old_dur;
       tt_abs_start := host_epoch + clock_delay + tw_start |} in
  let stretched_abs :=
    match stretch_top_level_tween new_dur old_dur tw0 with
    | None => None
    | Some tw1 => Some (tt_abs_start tw1)
    end in
  let '(rem_iter_dur, _) := timeline_remove_entry remove_idx (index_child_durs 0%nat child_durs) in
  (settled_blend, stretched_abs, rem_iter_dur).

(** ** 5. `DragPhysics` + `ScrollObserver` runner *)

Fixpoint run_scroll_positions (start_p finish_p : Z) (positions : list Z) (st : scroll_state) : scroll_state :=
  match positions with
  | [] => st
  | p :: rest => run_scroll_positions start_p finish_p rest (scroll_step start_p finish_p p st)
  end.

Definition observe_drag_scroll
    (lo hi factor v thresh : Z)
    (start_p finish_p : Z) (positions : list Z)
    : Z * Z * Z * (nat * bool) :=
  let safe_lo := Z.min lo hi in
  let safe_hi := Z.max lo hi in
  let safe_fac := Z.max 1 factor in
  let r := resist_exact safe_lo safe_hi safe_fac v in
  let inv := grab_inverse_exact safe_lo safe_hi safe_fac r in
  let edge_zero := edge_scroll_safe safe_lo safe_hi 0 0 thresh 1 v in
  let s_init :=
    {| ss_in_view := false;
       ss_has_entered := false;
       ss_force_enter := false;
       ss_enter_count := 0%nat |} in
  let s_fin := run_scroll_positions start_p finish_p positions s_init in
  (r, inv, edge_zero, (ss_enter_count s_fin, ss_in_view s_fin)).

(** ** 6. `TransformCache` clear-revert + `Motion.animateOn` + `MotionSlot` runner *)

Fixpoint run_slot_ops (ops : list (option nat)) (s : motion_slot) : motion_slot :=
  match ops with
  | [] => s
  | None :: rest => run_slot_ops rest (slot_close s)
  | Some next_id :: rest => run_slot_ops rest (slot_play next_id s)
  end.

Definition observe_transform_and_motion
    (clear_inline : bool)
    (init_style : option Z) (updates : list Z)
    (slot_ops : list (option nat))
    : nat * option Z * (bool * option nat) :=
  let live_inline := if clear_inline then None else Some [(1%nat, 45)] in
  let reverted_cache := revert_transform_components live_inline [(1%nat, 45)] [(0%nat, None)] in
  let '(cur_style, snaps) := run_animate_on_updates init_style updates in
  let restored_style := revert_snapshots_reverse cur_style snaps in
  let s_fin :=
    run_slot_ops slot_ops {| ms_closed := false; ms_running := None; ms_cancelled := [] |} in
  (length reverted_cache, restored_style, (ms_closed s_fin, ms_running s_fin)).

(** ** OCaml Extraction *)

Extraction Language OCaml.
Set Extraction Output Directory ".".

Extraction "engine.ml"
  observe_timing_engine
  observe_stagger_grid
  observe_round_and_color
  observe_composition_timeline
  observe_drag_scroll
  observe_transform_and_motion.
