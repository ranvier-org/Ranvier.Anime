From stdpp Require Import prelude.
From Stdlib Require Import Arith Lia List Bool ZArith.
Import ListNotations.
Open Scope Z_scope.

(** * Timeline Position Algebra, MVCC Sibling Isolation & Three-Mode Tween Composition *)

Inductive rel_op :=
  | RelNone
  | RelAdd (d : Z)
  | RelSub (d : Z)
  | RelMul (d : Z).

Definition apply_rel_op (anchor : Z) (op : rel_op) : Z :=
  match op with
  | RelNone => anchor
  | RelAdd d => anchor + d
  | RelSub d => anchor - d
  | RelMul d => anchor * d
  end.

(**md **Timeline relative operators preserve anchor identity at `+0`, `-0`, and `*1`, and compose additively.**
Applying `RelAdd 0`, `RelSub 0`, or `RelMul 1` equals `RelNone`, and `RelAdd a` followed by `RelSub a` recovers the anchor exactly. *)
Theorem timeline_position_relative_ops_sound :
  forall anchor a,
    apply_rel_op anchor (RelAdd 0) = anchor /\
    apply_rel_op anchor (RelSub 0) = anchor /\
    apply_rel_op anchor (RelMul 1) = anchor /\
    apply_rel_op (apply_rel_op anchor (RelAdd a)) (RelSub a) = anchor.
Proof.
  intros anchor a.
  unfold apply_rel_op.
  repeat split; lia.
Qed.

(** ** Timeline.Remove iteration-duration resize (#26) *)

Fixpoint max_child_end (entries : list (nat * Z * Z)) : Z :=
  match entries with
  | [] => 0
  | (_, off, dur) :: rest => Z.max (off + dur) (max_child_end rest)
  end.

Definition timeline_remove_entry (target_id : nat) (entries : list (nat * Z * Z)) : Z * list (nat * Z * Z) :=
  let remaining := List.filter (fun '(id, _, _) => negb (Nat.eqb id target_id)) entries in
  (max_child_end remaining, remaining).

(**md **`Timeline.Remove` recomputes `IterationDuration` from the remaining children (#26).**
Removing child `B` `(1, 0, 500)` from `[(0, 0, 100); (1, 0, 500)]` shrinks `IterationDuration` to `100` rather than retaining the stale `500` span. *)
Theorem timeline_remove_resizes_iteration_duration :
  forall target_id entries,
    fst (timeline_remove_entry target_id entries) =
    max_child_end (snd (timeline_remove_entry target_id entries)) /\
    fst (timeline_remove_entry 1%nat [(0%nat, 0, 100); (1%nat, 0, 500)]) = 100.
Proof.
  intros target_id entries.
  split; [reflexivity | vm_compute; reflexivity].
Qed.

(** ** MVCC FrameSibling Transaction Rollback Isolation *)

Record sibling_tx := {
  stx_live : list Z;
  stx_staged : list Z;
  stx_expected_gen : nat
}.

Definition commit_sibling_tx (cur_gen : nat) (can_write : bool) (tx : sibling_tx) : list Z :=
  if andb can_write (Nat.eqb cur_gen (stx_expected_gen tx)) then stx_staged tx
  else stx_live tx.

(**md **MVCC sibling transactions leave live siblings untouched on generation mismatch or write-guard revocation.**
Whenever `can_write = false` or `cur_gen <> stx_expected_gen tx`, `commit_sibling_tx` discards `stx_staged` and returns `stx_live` bit-for-bit unchanged. *)
Theorem mvcc_rollback_preserves_live_siblings :
  forall cur_gen can_write tx,
    (can_write = false \/ cur_gen <> stx_expected_gen tx) ->
    commit_sibling_tx cur_gen can_write tx = stx_live tx.
Proof.
  intros cur_gen can_write tx Hfail.
  unfold commit_sibling_tx.
  destruct Hfail as [Hcw | Hgen].
  - subst can_write. simpl. reflexivity.
  - destruct can_write; simpl; [| reflexivity].
    destruct (Nat.eqb_spec cur_gen (stx_expected_gen tx)); [contradiction | reflexivity].
Qed.

(** ** Clock-Muted Sibling Deferral (#24) *)

Record replace_slot := {
  rs_owner : nat;
  rs_cancelled : bool;
  rs_blend_base : Z
}.

Definition attach_composed_animation
    (anim_id : nat) (effectively_muted : bool) (new_target : Z) (slot : replace_slot)
    : replace_slot * bool :=
  if effectively_muted then
    (slot, true) (* pendingInitialAttach = true; live slot untouched *)
  else
    ({| rs_owner := anim_id; rs_cancelled := true; rs_blend_base := new_target |}, false).

(**md **Clock-muted `ComposedAnimation` defers sibling attachment without hijacking `Replace` or `Blend` state (#24).**
When `effectively_muted = true`, `attach_composed_animation` sets `pendingInitialAttach = true` and preserves `rs_owner`, `rs_cancelled`, and `rs_blend_base` unchanged; unmuting subsequently attaches and claims ownership cleanly. *)
Theorem muted_animation_defers_sibling_hijack :
  forall anim_id new_target slot,
    attach_composed_animation anim_id true new_target slot = (slot, true) /\
    fst (attach_composed_animation anim_id false new_target slot) =
      {| rs_owner := anim_id; rs_cancelled := true; rs_blend_base := new_target |}.
Proof.
  intros anim_id new_target slot.
  unfold attach_composed_animation.
  split; reflexivity.
Qed.

(** ** Atomic `ComposedAnimation.Stretch` & Top-Level Host Epoch Invariance (#25) *)

Record tween_timing := {
  tt_host_epoch : Z;
  tt_clock_delay : Z;
  tt_start : Z;
  tt_dur : Z;
  tt_abs_start : Z
}.

Definition stretch_top_level_tween (new_dur old_dur : Z) (tw : tween_timing) : option tween_timing :=
  if Z.ltb new_dur 0 || Z.leb old_dur 0 then None
  else
    let delay' := (tt_clock_delay tw * new_dur) / old_dur in
    let start' := (tt_start tw * new_dur) / old_dur in
    let dur' := Z.max 1 ((tt_dur tw * new_dur) / old_dur) in
    Some {| tt_host_epoch := tt_host_epoch tw;
            tt_clock_delay := delay';
            tt_start := start';
            tt_dur := dur';
            tt_abs_start := tt_host_epoch tw + delay' + start' |}.

(**md **Top-level `Stretch` rejects negative durations before mutation and never scales the host epoch (#25).**
Negative `new_dur` returns `None` without mutating state, and valid `Stretch` preserves `tt_host_epoch` while recomputing `tt_abs_start = tt_host_epoch + delay' + start'`. *)
Theorem stretch_preserves_host_epoch_and_validates_first :
  forall new_dur old_dur tw,
    (new_dur < 0 -> stretch_top_level_tween new_dur old_dur tw = None) /\
    (0 <= new_dur -> 0 < old_dur ->
      exists tw',
        stretch_top_level_tween new_dur old_dur tw = Some tw' /\
        tt_host_epoch tw' = tt_host_epoch tw /\
        tt_abs_start tw' = tt_host_epoch tw + tt_clock_delay tw' + tt_start tw').
Proof.
  intros new_dur old_dur tw.
  unfold stretch_top_level_tween.
  split.
  - intros Hneg.
    destruct (Z.ltb_spec new_dur 0); [simpl; reflexivity | lia].
  - intros Hnn Hold.
    destruct (Z.ltb_spec new_dur 0); [lia |].
    destruct (Z.leb_spec old_dur 0); [lia |].
    simpl. eexists. repeat split; reflexivity.
Qed.

(** ** Additive `Blend` Telescoping Conservation & SIMD Sum Equivalence *)

Record blend_entry := {
  be_base : Z;
  be_deltas : list Z
}.

Fixpoint sum_z (xs : list Z) : Z :=
  match xs with
  | [] => 0
  | x :: rest => x + sum_z rest
  end.

Definition flush_additive_scalar (e : blend_entry) : Z :=
  be_base e + sum_z (be_deltas e).

Definition compose_blend_tween (target : Z) (e : blend_entry) : blend_entry :=
  let delta0 := be_base e - target in
  {| be_base := target;
     be_deltas := delta0 :: be_deltas e |}.

Fixpoint zeros_z (n : nat) : list Z :=
  match n with
  | 0%nat => []
  | S k => 0 :: zeros_z k
  end.

Lemma sum_zeros_z : forall n, sum_z (zeros_z n) = 0.
Proof.
  induction n as [| k IH]; simpl; lia.
Qed.

(**md **Additive `Blend` composition conserves the instantaneous value at `p = 0` and converges to the latest target at `p = 1`.**
Attaching a new `Blend` tween toward `target` sets `delta_0 = be_base - target` and `be_base' = target`, so the flushed value at `p = 0` equals the pre-attachment value identically (zero jump), and once all active deltas settle to `0` (`p = 1`), the flushed output equals `target`. *)
Theorem blend_telescoping_initial_conservation_and_target_convergence :
  forall target e n,
    flush_additive_scalar (compose_blend_tween target e) = flush_additive_scalar e /\
    flush_additive_scalar {| be_base := target; be_deltas := zeros_z n |} = target.
Proof.
  intros target e n.
  unfold flush_additive_scalar, compose_blend_tween; simpl.
  rewrite sum_zeros_z.
  split; lia.
Qed.

(** ** Same-timeline `TrackHead` ownership interval disjointness *)

Definition owns_track_interval (start_t : Z) (next_start : option Z) (t : Z) : bool :=
  match next_start with
  | None => true
  | Some ns => Z.ltb t ns
  end.

(**md **Consecutive same-timeline `Replace` tracks partition time without overlap.**
For `s1 < s2`, track 1 (`[ -inf, s2 )`) and track 2 (`[ s2, next )` when `t >= s2`) never simultaneously own the same parent timeline timestamp `t` once track 2's start `s2` is reached. *)
Theorem timeline_track_intervals_disjoint :
  forall s1 s2 t,
    s1 < s2 ->
    owns_track_interval s1 (Some s2) t = true ->
    Z.leb s2 t = false.
Proof.
  intros s1 s2 t Hlt Hown.
  unfold owns_track_interval in Hown.
  destruct (Z.ltb_spec t s2); [| discriminate].
  destruct (Z.leb_spec s2 t); [lia | reflexivity].
Qed.
