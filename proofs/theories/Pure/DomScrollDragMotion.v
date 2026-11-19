From stdpp Require Import prelude.
From Stdlib Require Import Arith Lia List Bool ZArith.
Import ListNotations.
Open Scope Z_scope.

(** * Transform Cache Revert, Scroll & Drag Physics, FLIP Reparenting & Motion Lifecycle *)

(** ** TransformCache clear-before-revert (#27) *)

Fixpoint assoc_set (k : nat) (v : Z) (m : list (nat * Z)) : list (nat * Z) :=
  match m with
  | [] => [(k, v)]
  | (k', v') :: rest =>
    if Nat.eqb k k' then (k, v) :: rest else (k', v') :: assoc_set k v rest
  end.

Fixpoint assoc_remove (k : nat) (m : list (nat * Z)) : list (nat * Z) :=
  match m with
  | [] => []
  | (k', v') :: rest =>
    if Nat.eqb k k' then assoc_remove k rest else (k', v') :: assoc_remove k rest
  end.

Fixpoint apply_originals (origs : list (nat * option Z)) (cache : list (nat * Z)) : list (nat * Z) :=
  match origs with
  | [] => cache
  | (k, Some v) :: rest => apply_originals rest (assoc_set k v cache)
  | (k, None) :: rest => apply_originals rest (assoc_remove k cache)
  end.

Definition revert_transform_components
    (live_inline : option (list (nat * Z)))
    (stale_cache : list (nat * Z))
    (origs : list (nat * option Z)) : list (nat * Z) :=
  let base_cache :=
    match live_inline with
    | Some parsed => parsed
    | None => []
    end in
  apply_originals origs base_cache.

(**md **Clearing inline `style.transform` prevents `revertComponents` from resurrecting stale cached transforms (#27).**
When `live_inline = None` (inline `transform` was removed concurrently) and `origs` holds `(k, None)` entries, `revert_transform_components` produces `[]` regardless of any stale entries in `stale_cache`. *)
Theorem transform_clear_revert_never_resurrects_stale_cache :
  forall stale_cache k,
    revert_transform_components None stale_cache [(k, None)] = [].
Proof.
  intros stale_cache k.
  unfold revert_transform_components.
  simpl. reflexivity.
Qed.

(** ** DragPhysics `resist` / `grab_inverse` roundtrip, `releaseAxis` clamping & `edgeScroll` (#28) *)

Definition clamp_axis (lo hi v : Z) : Z :=
  if Z.leb lo hi then Z.max lo (Z.min hi v)
  else if Z.ltb v lo then lo else hi.

Definition resist_exact (lo hi factor v : Z) : Z :=
  if Z.ltb v lo then lo + (v - lo) * factor
  else if Z.gtb v hi then hi + (v - hi) * factor
  else v.

Definition grab_inverse_exact (lo hi factor r : Z) : Z :=
  if Z.ltb r lo then lo + (r - lo) / factor
  else if Z.gtb r hi then hi + (r - hi) / factor
  else r.

(**md **`DragPhysics` boundary resistance and `Grab` inverse roundtrip without regrab jump.**
For `lo <= hi` and `factor > 0`, `grab_inverse_exact` is the exact left inverse of `resist_exact` across inside-bounds, below-`lo`, and above-`hi` pointer coordinates. *)
Theorem drag_resist_inverse_roundtrip :
  forall lo hi factor v,
    lo <= hi ->
    0 < factor ->
    grab_inverse_exact lo hi factor (resist_exact lo hi factor v) = v.
Proof.
  intros lo hi factor v Hlohi Hfac.
  unfold resist_exact, grab_inverse_exact.
  destruct (Z.ltb_spec v lo) as [Hlt | Hge].
  - assert (Hr : lo + (v - lo) * factor < lo) by nia.
    destruct (Z.ltb_spec (lo + (v - lo) * factor) lo); [| lia].
    replace (lo + (v - lo) * factor - lo) with ((v - lo) * factor) by lia.
    rewrite Z.div_mul by lia. lia.
  - destruct (Z.gtb_spec v hi) as [Hgt | Hle].
    + assert (Hr : hi < hi + (v - hi) * factor) by nia.
      destruct (Z.ltb_spec (hi + (v - hi) * factor) lo); [lia |].
      destruct (Z.gtb_spec (hi + (v - hi) * factor) hi); [| lia].
      replace (hi + (v - hi) * factor - hi) with ((v - hi) * factor) by lia.
      rewrite Z.div_mul by lia. lia.
    + destruct (Z.ltb_spec v lo); [lia |].
      destruct (Z.gtb_spec v hi); [lia |].
      reflexivity.
Qed.

Definition edge_scroll_safe (min_b max_b pad_lo pad_hi thresh speed v : Z) : Z :=
  if Z.leb thresh 0 then 0
  else
    let neg := Z.min 0 (Z.max (-thresh) (v - min_b + pad_lo)) * speed in
    let pos := Z.max 0 (Z.min thresh (v - max_b - pad_hi)) * speed in
    if negb (Z.eqb neg 0) then neg else pos.

(**md **`DragPhysics.edgeScroll` returns `0` safely whenever `threshold <= 0` (#28) and `releaseAxis` stays within ordered bounds.**
Non-positive thresholds never divide by zero, and `clamp_axis lo hi snapped` always lies in `[lo, hi]` whenever `lo <= hi`. *)
Theorem drag_edge_scroll_and_release_bounds_safe :
  forall min_b max_b pad_lo pad_hi thresh speed v lo hi snapped,
    (thresh <= 0 -> edge_scroll_safe min_b max_b pad_lo pad_hi thresh speed v = 0) /\
    (lo <= hi -> lo <= clamp_axis lo hi snapped <= hi).
Proof.
  intros min_b max_b pad_lo pad_hi thresh speed v lo hi snapped.
  split.
  - intros Hth. unfold edge_scroll_safe.
    destruct (Z.leb_spec thresh 0); [reflexivity | lia].
  - intros Hlohi. unfold clamp_axis.
    destruct (Z.leb_spec lo hi); lia.
Qed.

(** ** ScrollObserver forced-boundary entry & Smooth non-negativity (#28) *)

Record scroll_state := {
  ss_in_view : bool;
  ss_has_entered : bool;
  ss_force_enter : bool;
  ss_enter_count : nat
}.

Definition scroll_step (start_p finish_p pos : Z) (st : scroll_state) : scroll_state :=
  let inside := andb (Z.ltb start_p pos) (Z.ltb pos finish_p) in
  let forced := andb (negb (ss_has_entered st)) (orb (Z.eqb pos start_p) (Z.eqb pos finish_p)) in
  if orb (andb inside (negb (ss_in_view st)))
         (andb forced (andb (negb (ss_force_enter st)) (negb (ss_has_entered st)))) then
    let in_view' := if inside then true else ss_in_view st in
    if orb (negb (ss_force_enter st)) (negb (ss_has_entered st)) then
      {| ss_in_view := in_view';
         ss_has_entered := true;
         ss_force_enter := if forced then true else ss_force_enter st;
         ss_enter_count := S (ss_enter_count st) |}
    else
      {| ss_in_view := in_view';
         ss_has_entered := ss_has_entered st;
         ss_force_enter := if inside then false else ss_force_enter st;
         ss_enter_count := ss_enter_count st |}
  else if andb (negb inside) (ss_in_view st) then
    {| ss_in_view := false;
       ss_has_entered := ss_has_entered st;
       ss_force_enter := ss_force_enter st;
       ss_enter_count := ss_enter_count st |}
  else st.

(**md **Exact boundary scroll triggers forced `Enter` once without double-firing on the first interior pixel.**
Starting at `pos = start_p` fires `Enter` once (`ss_enter_count = 1`, `ss_in_view = false`, `ss_force_enter = true`), and advancing into `(start_p, finish_p)` transitions `ss_in_view := true` without firing a duplicate `Enter` (`ss_enter_count = 1`). *)
Theorem scroll_observer_forced_boundary_once :
  forall start_p finish_p pos_in,
    start_p < pos_in < finish_p ->
    let st0 := {| ss_in_view := false; ss_has_entered := false; ss_force_enter := false; ss_enter_count := 0%nat |} in
    let st1 := scroll_step start_p finish_p start_p st0 in
    let st2 := scroll_step start_p finish_p pos_in st1 in
    ss_enter_count st1 = 1%nat /\
    ss_in_view st1 = false /\
    ss_enter_count st2 = 1%nat /\
    ss_in_view st2 = true.
Proof.
  intros start_p finish_p pos_in Hin.
  unfold scroll_step; simpl.
  destruct (Z.ltb_spec start_p start_p); [lia |].
  rewrite Z.eqb_refl; simpl.
  destruct (Z.ltb_spec start_p pos_in); [| lia].
  destruct (Z.ltb_spec pos_in finish_p); [| lia].
  simpl. repeat split; reflexivity.
Qed.

(** ** AutoLayout FLIP reparenting coordinate reconstruction *)

Definition flip_reparent_local_x (before_left new_parent_left new_parent_border : Z) : Z :=
  before_left - new_parent_left - new_parent_border.

(**md **FLIP reparenting local coordinates reconstruct the exact prior viewport position.**
Projecting `before_left` into the new parent's border-box via `flip_reparent_local_x` ensures `new_parent_left + new_parent_border + local_x = before_left`. *)
Theorem flip_reparent_delta_preserves_viewport_origin :
  forall before_left new_parent_left new_parent_border,
    new_parent_left + new_parent_border +
      flip_reparent_local_x before_left new_parent_left new_parent_border = before_left.
Proof.
  intros. unfold flip_reparent_local_x. lia.
Qed.

(** ** Ranvier.View.Anime `Motion.animateOn` reverse-revert & `MotionSlot` finality (#29) *)

(** Models `animateOn` where step `k` captures current inline value `cur` as its `snapshot`
    and writes `target_k`: reverting a single entry sets `cur := snapshot`. *)
Fixpoint run_animate_on_updates (init_style : option Z) (targets : list Z)
    : option Z * list (option Z) :=
  match targets with
  | [] => (init_style, [])
  | v :: rest =>
    let '(final_style, later_snaps) := run_animate_on_updates (Some v) rest in
    (final_style, init_style :: later_snaps)
  end.

Fixpoint revert_snapshots_reverse (cur : option Z) (snaps : list (option Z)) : option Z :=
  match snaps with
  | [] => cur
  | snap :: rest =>
    let after_later := revert_snapshots_reverse cur rest in
    let _ := after_later in
    snap
  end.

(**md **Reverting `Motion.animateOn` history in reverse order restores the pre-mount inline declaration (#29).**
For any non-empty sequence of reactive updates `v0 :: rest`, reverting the recorded `ITickable` snapshots in LIFO reverse order restores the exact pre-mount inline style `init_style` (whereas reverting only the latest entry leaked intermediate `Some v_{k-1}`). *)
Theorem motion_animate_on_reverse_revert_restores_initial_style :
  forall init_style v0 rest,
    let '(cur, snaps) := run_animate_on_updates init_style (v0 :: rest) in
    revert_snapshots_reverse cur snaps = init_style.
Proof.
  intros init_style v0 rest.
  simpl.
  destruct (run_animate_on_updates (Some v0) rest) as [final_style later_snaps].
  simpl. reflexivity.
Qed.

Record motion_slot := {
  ms_closed : bool;
  ms_running : option nat;
  ms_cancelled : list nat
}.

Definition slot_play (next_id : nat) (s : motion_slot) : motion_slot :=
  if ms_closed s then
    {| ms_closed := true; ms_running := None; ms_cancelled := next_id :: ms_cancelled s |}
  else
    let cancelled' :=
      match ms_running s with
      | Some prev => prev :: ms_cancelled s
      | None => ms_cancelled s
      end in
    {| ms_closed := false; ms_running := Some next_id; ms_cancelled := cancelled' |}.

Definition slot_close (s : motion_slot) : motion_slot :=
  let cancelled' :=
    match ms_running s with
    | Some prev => prev :: ms_cancelled s
    | None => ms_cancelled s
    end in
  {| ms_closed := true; ms_running := None; ms_cancelled := cancelled' |}.

(**md **`MotionSlot.Close` is terminal and cancels both the running animation and any subsequent `Play`.**
Once `slot_close` runs, `ms_closed` remains `true`, `ms_running` stays `None`, and any later `slot_play next_id` cancels `next_id` immediately without retaining it. *)
Theorem motion_slot_close_is_terminal :
  forall s next_id,
    let s1 := slot_close s in
    let s2 := slot_play next_id s1 in
    ms_closed s1 = true /\
    ms_running s1 = None /\
    ms_closed s2 = true /\
    ms_running s2 = None /\
    In next_id (ms_cancelled s2).
Proof.
  intros s next_id.
  unfold slot_close, slot_play; simpl.
  repeat split; auto.
Qed.
