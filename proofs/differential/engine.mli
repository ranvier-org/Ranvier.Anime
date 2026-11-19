
type bool =
| True
| False

val xorb : bool -> bool -> bool

val negb : bool -> bool

type nat =
| O
| S of nat

type 'a option =
| Some of 'a
| None

type ('a, 'b) prod =
| Pair of 'a * 'b

val snd : ('a1, 'a2) prod -> 'a2

type 'a list =
| Nil
| Cons of 'a * 'a list

val length : 'a1 list -> nat

val app : 'a1 list -> 'a1 list -> 'a1 list

type comparison =
| Eq
| Lt
| Gt

val compOpp : comparison -> comparison

val sub : nat -> nat -> nat

module Nat :
 sig
  val eqb : nat -> nat -> bool

  val leb : nat -> nat -> bool

  val max : nat -> nat -> nat

  val min : nat -> nat -> nat

  val even : nat -> bool

  val odd : nat -> bool
 end

val map : ('a1 -> 'a2) -> 'a1 list -> 'a2 list

val seq : nat -> nat -> nat list

val filter : ('a1 -> bool) -> 'a1 list -> 'a1 list

type positive =
| XI of positive
| XO of positive
| XH

type z =
| Z0
| Zpos of positive
| Zneg of positive

module Pos :
 sig
  val succ : positive -> positive

  val add : positive -> positive -> positive

  val add_carry : positive -> positive -> positive

  val pred_double : positive -> positive

  val mul : positive -> positive -> positive

  val compare_cont : comparison -> positive -> positive -> comparison

  val compare : positive -> positive -> comparison

  val eqb : positive -> positive -> bool

  val of_succ_nat : nat -> positive
 end

module Z :
 sig
  val double : z -> z

  val succ_double : z -> z

  val pred_double : z -> z

  val pos_sub : positive -> positive -> z

  val add : z -> z -> z

  val opp : z -> z

  val sub : z -> z -> z

  val mul : z -> z -> z

  val compare : z -> z -> comparison

  val leb : z -> z -> bool

  val ltb : z -> z -> bool

  val eqb : z -> z -> bool

  val max : z -> z -> z

  val min : z -> z -> z

  val of_nat : nat -> z

  val pos_div_eucl : positive -> z -> (z, z) prod

  val div_eucl : z -> z -> (z, z) prod

  val div : z -> z -> z

  val modulo : z -> z -> z

  val gtb : z -> z -> bool

  val abs : z -> z
 end

val clamp_z : z -> z -> z -> z

type timing_cfg = { tc_iter_dur : z; tc_iters : z; tc_delay : z;
                    tc_loop_delay : z; tc_alternate : bool; tc_reversed : 
                    bool }

val total_duration : timing_cfg -> z

val sample_bounded_time : timing_cfg -> z -> z

val sample_direction : bool -> bool -> nat -> bool

val advance_clock_anchor : z -> z -> z -> z

val insert_priority : z -> nat -> (z, nat) prod list -> (z, nat) prod list

val grid3_decode : z -> z -> z -> ((z, z) prod, z) prod

val flat_step_scalar : z -> z -> z -> z -> z

val flat_step_simd_chunks : nat -> nat -> z -> nat -> z -> z -> z -> z list

val js_round_half_int : z -> z

val pad_zeros : nat -> z list -> z list

val homogenize_components : z list -> z list -> (z list, z list) prod

val hcl_shortest_delta : z -> z -> z

val max_child_end : ((nat, z) prod, z) prod list -> z

val timeline_remove_entry :
  nat -> ((nat, z) prod, z) prod list -> (z, ((nat, z) prod, z) prod list)
  prod

type tween_timing = { tt_host_epoch : z; tt_clock_delay : z; tt_start : 
                      z; tt_dur : z; tt_abs_start : z }

val stretch_top_level_tween : z -> z -> tween_timing -> tween_timing option

val assoc_set : nat -> z -> (nat, z) prod list -> (nat, z) prod list

val assoc_remove : nat -> (nat, z) prod list -> (nat, z) prod list

val apply_originals :
  (nat, z option) prod list -> (nat, z) prod list -> (nat, z) prod list

val revert_transform_components :
  (nat, z) prod list option -> (nat, z) prod list -> (nat, z option) prod
  list -> (nat, z) prod list

val resist_exact : z -> z -> z -> z -> z

val grab_inverse_exact : z -> z -> z -> z -> z

val edge_scroll_safe : z -> z -> z -> z -> z -> z -> z -> z

type scroll_state = { ss_in_view : bool; ss_has_entered : bool;
                      ss_force_enter : bool; ss_enter_count : nat }

val scroll_step : z -> z -> z -> scroll_state -> scroll_state

val run_animate_on_updates :
  z option -> z list -> (z option, z option list) prod

val revert_snapshots_reverse : z option -> z option list -> z option

type motion_slot = { ms_closed : bool; ms_running : nat option;
                     ms_cancelled : nat list }

val slot_play : nat -> motion_slot -> motion_slot

val slot_close : motion_slot -> motion_slot

val insert_prio_seq :
  (z, nat) prod list -> (z, nat) prod list -> (z, nat) prod list

val observe_timing_engine :
  z -> z -> z -> z -> bool -> bool -> z -> nat -> z -> z -> z -> (z, nat)
  prod list -> ((((z, z) prod, bool) prod, z) prod, nat list) prod

val seq_z : z -> nat -> z list

val observe_stagger_grid :
  z -> z -> z -> z -> z -> nat -> (z list, ((z, z) prod, z) prod list) prod

val observe_round_and_color :
  z list -> z list -> z list -> bool -> z -> z -> z -> (((z list, (z list, z
  list) prod) prod, z) prod, z) prod

val apply_blend_targets : z -> (bool, z) prod list -> z

val index_child_durs : nat -> z list -> ((nat, z) prod, z) prod list

val observe_composition_timeline :
  z -> (bool, z) prod list -> z -> z -> z -> z -> z -> z list -> nat -> ((z,
  z option) prod, z) prod

val run_scroll_positions : z -> z -> z list -> scroll_state -> scroll_state

val observe_drag_scroll :
  z -> z -> z -> z -> z -> z -> z -> z list -> (((z, z) prod, z) prod, (nat,
  bool) prod) prod

val run_slot_ops : nat option list -> motion_slot -> motion_slot

val observe_transform_and_motion :
  bool -> z option -> z list -> nat option list -> ((nat, z option) prod,
  (bool, nat option) prod) prod
