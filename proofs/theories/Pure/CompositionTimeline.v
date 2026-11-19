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

(** ** Batch A1 Refinement Proofs: Coalesced Frame Additive Flush, 3-Tier SIMD Channel Sum & Property-Slice Keyframe Selection *)

Fixpoint update_nth_z (idx : nat) (v : Z) (xs : list Z) : list Z :=
  match xs, idx with
  | [], _ => []
  | _ :: rest, 0%nat => v :: rest
  | x :: rest, S k => x :: update_nth_z k v rest
  end.

Definition apply_blend_update (e : blend_entry) (upd : nat * Z) : blend_entry :=
  let '(idx, v) := upd in
  {| be_base := be_base e; be_deltas := update_nth_z idx v (be_deltas e) |}.

Fixpoint eager_frame_step (out : option Z) (e : blend_entry) (upds : list (nat * Z)) : option Z * blend_entry :=
  match upds with
  | [] => (out, e)
  | u :: rest =>
    let e' := apply_blend_update e u in
    eager_frame_step (Some (flush_additive_scalar e')) e' rest
  end.

Fixpoint stage_blend_updates (dirty : bool) (e : blend_entry) (upds : list (nat * Z)) : bool * blend_entry :=
  match upds with
  | [] => (dirty, e)
  | u :: rest => stage_blend_updates true (apply_blend_update e u) rest
  end.

Definition coalesced_frame_step (out : option Z) (e : blend_entry) (upds : list (nat * Z)) : option Z * blend_entry :=
  let '(dirty, e_final) := stage_blend_updates false e upds in
  if dirty then (Some (flush_additive_scalar e_final), e_final)
  else (out, e_final).

Lemma stage_blend_updates_true_dirty :
  forall upds e,
    fst (stage_blend_updates true e upds) = true.
Proof.
  induction upds as [| u rest IH]; intros e; simpl; [reflexivity | apply IH].
Qed.

Lemma eager_eq_stage_nonempty :
  forall rest u e out,
    eager_frame_step out e (u :: rest) =
      let e_final := snd (stage_blend_updates true (apply_blend_update e u) rest) in
      (Some (flush_additive_scalar e_final), e_final).
Proof.
  induction rest as [| u2 rest' IH]; intros u e out.
  - simpl. reflexivity.
  - simpl. apply (IH u2 (apply_blend_update e u) (Some (flush_additive_scalar (apply_blend_update e u)))).
Qed.

(**md **Coalescing `FlushAdditive` to `AfterUpdate` via `dirtyAdditive` matches eager per-tween flushing and skips clean frames in $O(1)$.**
Across any frame batch `upds` of `Blend` tween delta updates, deferring `FlushAdditive` until `AfterUpdate` produces the exact same target output and `blend_entry` state as flushing after each individual tween, while performing zero flush work when `upds = []`. *)
Theorem coalesced_end_of_frame_flush_eq_eager_per_tween_flush :
  forall out e upds,
    coalesced_frame_step out e upds = eager_frame_step out e upds /\
    coalesced_frame_step out e [] = (out, e).
Proof.
  intros out e upds.
  split; [| reflexivity].
  destruct upds as [| u rest].
  - reflexivity.
  - rewrite eager_eq_stage_nonempty.
    unfold coalesced_frame_step. simpl.
    pose proof (stage_blend_updates_true_dirty rest (apply_blend_update e u)) as Hdirty.
    destruct (stage_blend_updates true (apply_blend_update e u) rest) as [d ef].
    simpl in *. subst d. reflexivity.
Qed.

Fixpoint scalar_channel_add (dst src : list Z) : list Z :=
  match dst, src with
  | [], _ => []
  | d :: drest, [] => d :: drest
  | d :: drest, s :: srest => (d + s) :: scalar_channel_add drest srest
  end.

Fixpoint simd_channel_add_chunks (fuel chunk : nat) (dst src : list Z) : list Z :=
  match fuel with
  | 0%nat => scalar_channel_add dst src
  | S fuel' =>
    if (Nat.leb chunk 0 || Nat.ltb (length dst) chunk || Nat.ltb (length src) chunk)%bool then
      scalar_channel_add dst src
    else
      scalar_channel_add (firstn chunk dst) (firstn chunk src) ++
      simd_channel_add_chunks fuel' chunk (skipn chunk dst) (skipn chunk src)
  end.

Lemma scalar_channel_add_split :
  forall k dst src,
    (k <= length dst)%nat ->
    (k <= length src)%nat ->
    scalar_channel_add (firstn k dst) (firstn k src) ++
    scalar_channel_add (skipn k dst) (skipn k src) =
    scalar_channel_add dst src.
Proof.
  induction k as [| k' IH]; intros dst src Hdst Hsrc.
  - simpl. reflexivity.
  - destruct dst as [| d drest]; [simpl in Hdst; lia |].
    destruct src as [| s srest]; [simpl in Hsrc; lia |].
    simpl in *.
    f_equal.
    apply IH; lia.
Qed.

(**md **3-tier SIMD (`Vector512` / `Vector256` / `Vector128`) channel accumulation equals scalar elementwise addition.**
For every positive SIMD lane width `chunk` (`8`, `4`, `2`) and arbitrary channel lengths `|dst|`, `|src|`, chunked vector addition followed by scalar tail accumulation equals `scalar_channel_add dst src` element-for-element. *)
Theorem simd_channel_add_chunks_eq_scalar :
  forall fuel chunk dst src,
    simd_channel_add_chunks fuel chunk dst src = scalar_channel_add dst src.
Proof.
  induction fuel as [| fuel' IH]; intros chunk dst src.
  - simpl. reflexivity.
  - simpl.
    destruct (Nat.leb_spec chunk 0) as [Hc0 | Hcpos]; [simpl; reflexivity |].
    destruct (Nat.ltb_spec (length dst) chunk) as [Hdl | Hdge]; [simpl; reflexivity |].
    destruct (Nat.ltb_spec (length src) chunk) as [Hsl | Hsge]; [simpl; reflexivity |].
    simpl.
    rewrite IH.
    apply scalar_channel_add_split; lia.
Qed.

Record kframe := {
  kf_id : nat;
  kf_target : nat;
  kf_name : nat;
  kf_start : Z
}.

Definition same_kprop (a b : kframe) : bool :=
  andb (Nat.eqb (kf_target a) (kf_target b)) (Nat.eqb (kf_name a) (kf_name b)).

Definition step_slice_select (time : Z) (selected candidate : kframe) : kframe :=
  if Z.leb (kf_start candidate) time ||
     (Z.ltb time (kf_start selected) && Z.ltb (kf_start candidate) (kf_start selected))
  then candidate
  else selected.

Definition select_kframe_full_scan (time : Z) (anchor : kframe) (frames : list kframe) : kframe :=
  List.fold_left
    (fun sel cand => if same_kprop cand anchor then step_slice_select time sel cand else sel)
    frames anchor.

Definition select_kframe_prop_slice (time : Z) (anchor : kframe) (frames : list kframe) : kframe :=
  List.fold_left
    (step_slice_select time)
    (List.filter (fun cand => same_kprop cand anchor) frames)
    anchor.

Lemma fold_filter_same_kprop :
  forall frames time anchor acc,
    List.fold_left
      (fun sel cand => if same_kprop cand anchor then step_slice_select time sel cand else sel)
      frames acc =
    List.fold_left
      (step_slice_select time)
      (List.filter (fun cand => same_kprop cand anchor) frames)
      acc.
Proof.
  induction frames as [| f rest IH]; intros time anchor acc.
  - reflexivity.
  - simpl.
    destruct (same_kprop f anchor); apply IH.
Qed.

(**md **Pre-grouped `(Target, Name)` property slices select the exact same active keyframe as scanning all compiled frames.**
Restricting `KeyframePlayback` `sampleAt` to the pre-indexed `(Target, Name)` frame slice produces the identical selected `FrameSibling` as scanning the full multi-target, multi-property `frames` array. *)
Theorem property_slice_cursor_search_eq_full_frame_scan :
  forall time anchor frames,
    select_kframe_prop_slice time anchor frames = select_kframe_full_scan time anchor frames.
Proof.
  intros time anchor frames.
  unfold select_kframe_prop_slice, select_kframe_full_scan.
  symmetry.
  apply fold_filter_same_kprop.
Qed.

(** ** Batch A3 Refinement Proof: Multi-Target Homogeneous Scalar SoA 3-Tier SIMD Batch Equivalence *)

Record scalar_tween_aos := {
  sta_from : Z;
  sta_to : Z
}.

Definition eval_scalar_tween_aos (S : Z) (ease_fn : Z -> Z) (norm_t : Z) (tw : scalar_tween_aos) : Z :=
  let p := ease_fn norm_t in
  if Z.eqb p 0 then sta_from tw
  else if Z.eqb p S then sta_to tw
  else sta_from tw + ((sta_to tw - sta_from tw) * p) / S.

Fixpoint eval_aos_list (S : Z) (ease_fn : Z -> Z) (norm_t : Z) (tws : list scalar_tween_aos) : list Z :=
  match tws with
  | [] => []
  | tw :: rest => eval_scalar_tween_aos S ease_fn norm_t tw :: eval_aos_list S ease_fn norm_t rest
  end.

Fixpoint eval_soa_scalar (S p : Z) (froms tos : list Z) : list Z :=
  match froms, tos with
  | f :: frest, t :: trest =>
    let v :=
      if Z.eqb p 0 then f
      else if Z.eqb p S then t
      else f + ((t - f) * p) / S in
    v :: eval_soa_scalar S p frest trest
  | _, _ => []
  end.

Fixpoint eval_soa_simd_chunks (fuel chunk : nat) (S p : Z) (froms tos : list Z) : list Z :=
  match fuel with
  | 0%nat => eval_soa_scalar S p froms tos
  | S f =>
    if (Nat.leb chunk 0 || Nat.ltb (length froms) chunk || Nat.ltb (length tos) chunk)%bool then
      eval_soa_scalar S p froms tos
    else
      eval_soa_scalar S p (firstn chunk froms) (firstn chunk tos) ++
      eval_soa_simd_chunks f chunk S p (skipn chunk froms) (skipn chunk tos)
  end.

Lemma eval_soa_scalar_split :
  forall k S p froms tos,
    (k <= length froms)%nat ->
    (k <= length tos)%nat ->
    eval_soa_scalar S p (firstn k froms) (firstn k tos) ++
    eval_soa_scalar S p (skipn k froms) (skipn k tos) =
    eval_soa_scalar S p froms tos.
Proof.
  induction k as [| k' IH]; intros S p froms tos Hf Ht.
  - reflexivity.
  - destruct froms as [| f frest]; [simpl in Hf; lia |].
    destruct tos as [| t trest]; [simpl in Ht; lia |].
    simpl in *. f_equal. apply IH; lia.
Qed.

Lemma eval_soa_scalar_eq_aos :
  forall tws S ease_fn norm_t,
    eval_soa_scalar S (ease_fn norm_t) (List.map sta_from tws) (List.map sta_to tws) =
    eval_aos_list S ease_fn norm_t tws.
Proof.
  induction tws as [| tw rest IH]; intros S ease_fn norm_t.
  - reflexivity.
  - simpl. unfold eval_scalar_tween_aos. f_equal. apply IH.
Qed.

(**md **Homogeneous multi-target SoA 3-tier SIMD (`Vector512` / `Vector256` / `Vector128`) batch interpolation equals per-tween AoS evaluation.**
Evaluating shared timing and `ease_fn` once per frame and interpolating packed `(froms, tos)` arrays in `chunk`-wide SIMD lanes produces the exact target value sequence as evaluating each `ComposedTween` independently. *)
Theorem soa_multi_target_scalar_simd_eq_per_tween :
  forall fuel chunk S ease_fn norm_t tws,
    eval_soa_simd_chunks fuel chunk S (ease_fn norm_t) (List.map sta_from tws) (List.map sta_to tws) =
    eval_aos_list S ease_fn norm_t tws.
Proof.
  intros fuel chunk S ease_fn norm_t tws.
  rewrite <- eval_soa_scalar_eq_aos.
  set (froms := List.map sta_from tws).
  set (tos := List.map sta_to tws).
  set (p := ease_fn norm_t).
  clearbody froms tos p.
  revert froms tos.
  induction fuel as [| f IH]; intros froms tos.
  - reflexivity.
  - simpl.
    destruct (Nat.leb_spec chunk 0); [simpl; reflexivity |].
    destruct (Nat.ltb_spec (length froms) chunk); [simpl; reflexivity |].
    destruct (Nat.ltb_spec (length tos) chunk); [simpl; reflexivity |].
    simpl. rewrite IH. apply eval_soa_scalar_split; lia.
Qed.

(** ** Pass-2 Correctness Proof (#39): Mid-Flight `Blend` Sibling Cancel Folds Residual Delta Without Discontinuity *)

Definition blend_total_before_cancel (base d_cancelled d_surviving : Z) : Z :=
  base + (d_cancelled + d_surviving).

Definition blend_fold_cancel_base (base d_cancelled : Z) : Z :=
  base + d_cancelled.

Definition blend_total_after_cancel (base' d_surviving : Z) : Z :=
  base' + d_surviving.

(**md **Folding a mid-flight cancelled `Blend` sibling's residual delta into `entry.FromNumber` preserves instantaneous blended output (#39).**
Updating `base' := base + d_cancelled` when detaching an unfinished `Blend` sibling keeps the rendered sum `base' + d_surviving` continuous (`0` jump) at the instant of cancellation and converges to `base + d_cancelled` when surviving siblings finish (`d_surviving = 0`). *)
Theorem blend_midflight_cancel_folds_residual_without_jump :
  forall base d_cancelled d_surviving,
    let base' := blend_fold_cancel_base base d_cancelled in
    blend_total_after_cancel base' d_surviving =
      blend_total_before_cancel base d_cancelled d_surviving /\
    blend_total_after_cancel base' 0 = base + d_cancelled.
Proof.
  intros base d_cancelled d_surviving.
  unfold blend_fold_cancel_base, blend_total_after_cancel, blend_total_before_cancel.
  lia.
Qed.
