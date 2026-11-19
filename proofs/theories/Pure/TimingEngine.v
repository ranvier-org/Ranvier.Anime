From stdpp Require Import prelude.
From Stdlib Require Import Arith Lia List Bool ZArith.
Import ListNotations.
Open Scope Z_scope.

(** * Playback Timing, Clock Generation Fencing & Engine Frame Queue *)

Definition clamp_z (lo hi x : Z) : Z :=
  Z.max lo (Z.min hi x).

Record timing_cfg := {
  tc_iter_dur : Z;
  tc_iters : Z;
  tc_delay : Z;
  tc_loop_delay : Z;
  tc_alternate : bool;
  tc_reversed : bool
}.

Definition total_duration (cfg : timing_cfg) : Z :=
  if Z.leb (tc_iters cfg) 0 then 0
  else
    let d := Z.max 0 (tc_iter_dur cfg) in
    let ld := Z.max 0 (tc_loop_delay cfg) in
    tc_iters cfg * d + (tc_iters cfg - 1) * ld.

Definition sample_bounded_time (cfg : timing_cfg) (raw_t : Z) : Z :=
  clamp_z 0 (total_duration cfg) (raw_t - tc_delay cfg).

Definition sample_direction (reversed alternate : bool) (iter_idx : nat) : bool :=
  xorb reversed (andb alternate (Nat.odd iter_idx)).

Definition safe_zero_dur_toggle_time (dur : Z) (iter_idx : Z) (iter_prog_opt : option Z) (reversed : bool) (scale : Z) : Z :=
  let raw_prog :=
    match iter_prog_opt with
    | None => 0
    | Some p => p
    end in
  let prog := if reversed then scale - raw_prog else raw_prog in
  dur * iter_idx + (dur * prog) / Z.max 1 scale.

(**md **Bounded playhead time stays within `[0, total_duration]`.**
For any non-negative iteration count, iteration duration, and loop delay, `sample_bounded_time` maps any raw timestamp into `[0, total_duration cfg]`. *)
Theorem playback_sample_bounds :
  forall cfg raw_t,
    0 <= total_duration cfg /\
    0 <= sample_bounded_time cfg raw_t <= total_duration cfg.
Proof.
  intros cfg raw_t.
  unfold sample_bounded_time, clamp_z, total_duration.
  destruct (Z.leb_spec (tc_iters cfg) 0) as [Hle | Hgt].
  - lia.
  - assert (Hd : 0 <= Z.max 0 (tc_iter_dur cfg)) by lia.
    assert (Hld : 0 <= Z.max 0 (tc_loop_delay cfg)) by lia.
    assert (Ht1 : 0 <= tc_iters cfg * Z.max 0 (tc_iter_dur cfg)) by nia.
    assert (Ht2 : 0 <= (tc_iters cfg - 1) * Z.max 0 (tc_loop_delay cfg)) by nia.
    lia.
Qed.

(**md **Alternating direction XOR is an involution under base-direction toggle.**
Flipping `reversed` twice restores the exact effective iteration direction `sample_direction` at every repetition parity. *)
Theorem direction_xor_involution :
  forall reversed alternate iter_idx,
    sample_direction (negb (negb reversed)) alternate iter_idx =
    sample_direction reversed alternate iter_idx /\
    sample_direction (negb reversed) alternate iter_idx =
    negb (sample_direction reversed alternate iter_idx).
Proof.
  intros reversed alternate iter_idx.
  unfold sample_direction.
  destruct reversed, alternate, (Nat.odd iter_idx); simpl; auto.
Qed.

(**md **Zero-duration repeating toggle never produces non-finite time (#26).**
When `dur = 0` and iteration progress is undefined (`None`, modelling `0. / 0.`), `safe_zero_dur_toggle_time` evaluates to exact `0`. *)
Theorem zero_duration_repeat_toggle_safe :
  forall iter_idx prog_opt reversed scale,
    safe_zero_dur_toggle_time 0 iter_idx prog_opt reversed scale = 0.
Proof.
  intros iter_idx prog_opt reversed scale.
  unfold safe_zero_dur_toggle_time.
  simpl.
  rewrite Z.div_0_l by lia.
  lia.
Qed.

(** ** Generation-fenced callback phase execution *)

Inductive tick_phase :=
  | PhBegin
  | PhLoop
  | PhBeforeUpdate
  | PhRender
  | PhUpdate
  | PhComplete.

Fixpoint run_fenced_phases
    (expected_gen : nat)
    (step_fn : tick_phase -> nat -> nat)
    (phases : list tick_phase)
    (cur_gen : nat) : nat * list tick_phase :=
  match phases with
  | [] => (cur_gen, [])
  | ph :: rest =>
    if Nat.eqb cur_gen expected_gen then
      let next_gen := step_fn ph cur_gen in
      let '(final_gen, ran) := run_fenced_phases expected_gen step_fn rest next_gen in
      (final_gen, ph :: ran)
    else
      (cur_gen, [])
  end.

(**md **Generation fencing aborts remaining tick phases immediately on reentrant control.**
Once a phase callback increments `cur_gen` away from `expected_gen`, zero subsequent phases in `rest` execute. *)
Theorem playback_generation_fences_reentrant_seek :
  forall expected_gen step_fn ph rest cur_gen,
    cur_gen = expected_gen ->
    step_fn ph cur_gen <> expected_gen ->
    snd (run_fenced_phases expected_gen step_fn (ph :: rest) cur_gen) = [ph].
Proof.
  intros expected_gen step_fn ph rest cur_gen Heq Hneq.
  subst cur_gen.
  simpl.
  rewrite Nat.eqb_refl.
  destruct rest as [| ph2 rest'].
  - simpl. reflexivity.
  - simpl.
    destruct (Nat.eqb_spec (step_fn ph expected_gen) expected_gen) as [Hc | _].
    + contradiction.
    + simpl. reflexivity.
Qed.

(** ** Engine clock remainder & priority queue ordering *)

Definition advance_clock_anchor (last_tick now frame_dur : Z) : Z :=
  let elapsed := now - last_tick in
  if Z.ltb elapsed frame_dur then last_tick
  else now - Z.modulo elapsed frame_dur.

(**md **Engine clock phase advance keeps sub-frame remainder bounded in `[0, frame_dur)`.**
When `now - last_tick >= frame_dur > 0`, `advance_clock_anchor` advances `last_tick` to `now - ((now - last_tick) mod frame_dur)`, bounding the residual phase within `[0, frame_dur)`. *)
Theorem engine_clock_phase_drift_bounded :
  forall last_tick now frame_dur,
    0 < frame_dur ->
    frame_dur <= now - last_tick ->
    0 <= now - advance_clock_anchor last_tick now frame_dur < frame_dur.
Proof.
  intros last_tick now frame_dur Hfd Hel.
  unfold advance_clock_anchor.
  destruct (Z.ltb_spec (now - last_tick) frame_dur) as [Hlt | Hge].
  - lia.
  - replace (now - (now - (now - last_tick) mod frame_dur))
      with ((now - last_tick) mod frame_dur) by lia.
    apply Z.mod_pos_bound. lia.
Qed.

Fixpoint insert_priority (p : Z) (id : nat) (q : list (Z * nat)) : list (Z * nat) :=
  match q with
  | [] => [(p, id)]
  | (qp, qid) :: rest =>
    if Z.leb qp p then (qp, qid) :: insert_priority p id rest
    else (p, id) :: q
  end.

Inductive sorted_priorities : list (Z * nat) -> Prop :=
  | SPNil : sorted_priorities []
  | SPSingle x : sorted_priorities [x]
  | SPCons p1 id1 p2 id2 rest :
      p1 <= p2 ->
      sorted_priorities ((p2, id2) :: rest) ->
      sorted_priorities ((p1, id1) :: (p2, id2) :: rest).

(**md **Engine priority insertion preserves non-decreasing priority order.**
Inserting `(p, id)` after all existing entries with `priority <= p` maintains `sorted_priorities` and places equal-priority roots in FIFO order. *)
Theorem priority_insert_preserves_sorted :
  forall q p id,
    sorted_priorities q ->
    sorted_priorities (insert_priority p id q).
Proof.
  induction q as [| [qp qid] rest IH]; intros p id Hsort.
  - simpl. constructor.
  - simpl.
    destruct (Z.leb_spec qp p) as [Hle | Hgt].
    + inversion Hsort as [| | p1 i1 p2 i2 r Hp12 Hrest]; subst.
      * simpl. constructor; [lia | constructor].
      * specialize (IH p id Hrest).
        simpl in *.
        destruct (Z.leb_spec p2 p) as [Hle2 | Hgt2].
        -- constructor; [exact Hp12 | exact IH].
        -- constructor; [exact Hle | exact IH].
    + constructor; [lia | exact Hsort].
Qed.

(** ** Outermost-frame deferred write depth *)

Record deferred_state := {
  ds_depth : nat;
  ds_pending : nat;
  ds_flushed : nat
}.

Definition deferred_enter (s : deferred_state) : deferred_state :=
  {| ds_depth := S (ds_depth s);
     ds_pending := ds_pending s;
     ds_flushed := ds_flushed s |}.

Definition deferred_queue_write (k : nat) (s : deferred_state) : deferred_state :=
  if Nat.ltb 0 (ds_depth s) then
    {| ds_depth := ds_depth s;
       ds_pending := (ds_pending s + k)%nat;
       ds_flushed := ds_flushed s |}
  else
    {| ds_depth := 0%nat;
       ds_pending := ds_pending s;
       ds_flushed := (ds_flushed s + k)%nat |}.

Definition deferred_exit (s : deferred_state) : deferred_state :=
  match ds_depth s with
  | 0%nat => s
  | 1%nat =>
    {| ds_depth := 0%nat;
       ds_pending := 0%nat;
       ds_flushed := (ds_flushed s + ds_pending s)%nat |}
  | S (S d) =>
    {| ds_depth := S d;
       ds_pending := ds_pending s;
       ds_flushed := ds_flushed s |}
  end.

(**md **Nested deferred-write scopes flush pending writes only when the outermost frame exits.**
A nested `Enter -> Queue k1 -> Enter -> Queue k2 -> Exit -> Exit` sequence holds `k1 + k2` writes un-flushed on the inner exit and flushes all `k1 + k2` writes atomically when depth returns to `0`. *)
Theorem deferred_writes_depth_balanced :
  forall k1 k2,
    let s0 := {| ds_depth := 0%nat; ds_pending := 0%nat; ds_flushed := 0%nat |} in
    let s_inner :=
      deferred_exit
        (deferred_queue_write k2
          (deferred_enter
            (deferred_queue_write k1 (deferred_enter s0)))) in
    let s_outer := deferred_exit s_inner in
    ds_depth s_inner = 1%nat /\
    ds_flushed s_inner = 0%nat /\
    ds_pending s_inner = (k1 + k2)%nat /\
    ds_depth s_outer = 0%nat /\
    ds_pending s_outer = 0%nat /\
    ds_flushed s_outer = (k1 + k2)%nat.
Proof.
  intros k1 k2.
  simpl.
  lia.
Qed.

(** ** Batch A1 Refinement Proof: Cached Snapshot Indexed Traversal Without `Array.rev` Allocation *)

Fixpoint collect_forward {A : Type} (xs : list A) (idx count : nat) (default : A) : list A :=
  match count with
  | 0%nat => []
  | S rem => List.nth idx xs default :: collect_forward xs (S idx) rem default
  end.

Fixpoint collect_backward {A : Type} (xs : list A) (rem : nat) (default : A) : list A :=
  match rem with
  | 0%nat => []
  | S idx => List.nth idx xs default :: collect_backward xs idx default
  end.

Definition traverse_snapshot_indexed {A : Type} (backwards : bool) (xs : list A) (default : A) : list A :=
  if backwards then collect_backward xs (length xs) default
  else collect_forward xs 0%nat (length xs) default.

Lemma collect_forward_cons :
  forall (A : Type) (x : A) (xs : list A) (idx count : nat) (default : A),
    collect_forward (x :: xs) (S idx) count default = collect_forward xs idx count default.
Proof.
  intros A x xs idx count default.
  revert idx.
  induction count as [| rem IH]; intros idx.
  - reflexivity.
  - simpl. f_equal. apply IH.
Qed.

Lemma collect_forward_exact :
  forall (A : Type) (xs : list A) (default : A),
    collect_forward xs 0%nat (length xs) default = xs.
Proof.
  intros A xs default.
  induction xs as [| x rest IH].
  - reflexivity.
  - simpl. rewrite collect_forward_cons. f_equal. exact IH.
Qed.

Lemma collect_backward_app_prefix :
  forall (A : Type) (xs suffix : list A) (rem : nat) (default : A),
    (rem <= length xs)%nat ->
    collect_backward (xs ++ suffix) rem default = collect_backward xs rem default.
Proof.
  intros A xs suffix rem default Hle.
  induction rem as [| idx IH].
  - reflexivity.
  - simpl.
    rewrite app_nth1 by lia.
    f_equal.
    apply IH. lia.
Qed.

Lemma collect_backward_exact :
  forall (A : Type) (xs : list A) (default : A),
    collect_backward xs (length xs) default = List.rev xs.
Proof.
  intros A xs default.
  induction xs as [| x l IH] using rev_ind.
  - reflexivity.
  - rewrite length_app; simpl.
    replace (length l + 1)%nat with (S (length l)) by lia.
    simpl.
    rewrite nth_middle.
    rewrite collect_backward_app_prefix by lia.
    rewrite IH.
    rewrite rev_unit.
    reflexivity.
Qed.

(**md **Indexed forward (`0 .. n-1`) and backward (`n-1 downto 0`) traversal over a cached snapshot equals `ToArray()` and `Array.rev (ToArray())`.**
Traversing a cached child snapshot by index in `Timeline` and `ComposedTimeline` visits the exact sequence `xs` forward and `List.rev xs` backward with zero per-tick array copies or reversals. *)
Theorem snapshot_index_traversal_eq_reversed_copy :
  forall (A : Type) (backwards : bool) (xs : list A) (default : A),
    traverse_snapshot_indexed backwards xs default =
      (if backwards then List.rev xs else xs).
Proof.
  intros A backwards xs default.
  unfold traverse_snapshot_indexed.
  destruct backwards.
  - apply collect_backward_exact.
  - apply collect_forward_exact.
Qed.

(** ** Pass-2 Correctness Proofs (#37, #39): Forever Re-Anchor Scaling, Signed Odd-Bit XOR & Resize Rebound *)

Definition reanchor_forever_host (anchor duration eff_speed : Z) : Z :=
  anchor + duration / eff_speed.

Definition timer_elapsed_from_anchor (now anchor eff_speed : Z) : Z :=
  (now - anchor) * eff_speed.

(**md **Scaling `PlaybackRepeat.Forever` re-anchor by `1 / (Speed * FrameSpeed)` resets timer-unit elapsed time by `-Duration` (#37).**
Whenever `duration = host_span * eff_speed` (`eff_speed > 0`), advancing `anchor` by `duration / eff_speed` reduces `(now - anchor) * eff_speed` by exact `duration` (`0` at `now = anchor + host_span`), preventing negative-time jumps when `eff_speed <> 1`. *)
Theorem forever_reanchor_divides_effective_speed :
  forall now anchor host_span eff_speed,
    0 < eff_speed ->
    let duration := host_span * eff_speed in
    let anchor' := reanchor_forever_host anchor duration eff_speed in
    timer_elapsed_from_anchor now anchor' eff_speed =
      timer_elapsed_from_anchor now anchor eff_speed - duration.
Proof.
  intros now anchor host_span eff_speed Hpos duration anchor'.
  unfold anchor', reanchor_forever_host, timer_elapsed_from_anchor, duration.
  rewrite Z.div_mul by lia.
  nia.
Qed.

Definition sample_dir_signed (reversed alternate : bool) (iter_idx : Z) : bool :=
  xorb reversed (andb alternate (Z.odd iter_idx)).

(**md **Boolean `xorb` with `Z.odd` (`index &&& 1 <> 0`) satisfies direction involution across all signed iteration indices including `-1` (#37).**
For any signed integer `iter_idx : Z` (including `iter_idx = -1` under 32-bit wraparound), `sample_dir_signed` flips cleanly when `reversed` is negated, whereas `(1 ^^^ (-1 rem 2)) <> 0` fails (`Z.lxor 1 (-1) = -2 <> 0`). *)
Theorem signed_odd_bit_xor_involution :
  (forall reversed alternate iter_idx,
    sample_dir_signed (negb reversed) alternate iter_idx =
    negb (sample_dir_signed reversed alternate iter_idx)) /\
  Z.eqb (Z.lxor 1 (Z.rem (-1) 2)) 0 = false.
Proof.
  split.
  - intros [|] [|] iter_idx; unfold sample_dir_signed; destruct (Z.odd iter_idx); reflexivity.
  - reflexivity.
Qed.

Definition rebound_resized_sample (delay new_dur cur_time : Z) : Z :=
  Z.max (-delay) (Z.min new_dur cur_time).

(**md **Rebounding `PlaybackState` on `ResizeWithoutReset` preserves in-range playheads and bounds out-of-range playheads to `[-delay, new_dur]` (#37, #39).**
When `cur_time` already lies in `[-delay, new_dur]`, `rebound_resized_sample` leaves `cur_time` untouched (preserving active playback on `ComposedTimeline.RemoveTargets`), and when `new_dur < cur_time`, it clamps `cur_time` to `new_dur`. *)
Theorem resize_rebounds_sample_within_new_duration :
  forall delay new_dur cur_time,
    0 <= delay ->
    0 <= new_dur ->
    (-delay <= rebound_resized_sample delay new_dur cur_time <= new_dur) /\
    (-delay <= cur_time <= new_dur -> rebound_resized_sample delay new_dur cur_time = cur_time).
Proof.
  intros delay new_dur cur_time Hdel Hdur.
  unfold rebound_resized_sample.
  split; lia.
Qed.
