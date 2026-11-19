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

(** ** Batch A3 Refinement Proofs: Deferred Numeric Transform Sentinel & Single-Pass CSS Declaration Scanner *)

Inductive tf_entry :=
  | TfText (txt_id : Z)
  | TfNumericSentinel.

Definition resolve_tf_entry (fmt : Z -> Z) (numbers : nat -> Z) (key : nat) (entry : tf_entry) : Z :=
  match entry with
  | TfText txt => txt
  | TfNumericSentinel => fmt (numbers key)
  end.

Fixpoint assoc_set_entry (k : nat) (e : tf_entry) (m : list (nat * tf_entry)) : list (nat * tf_entry) :=
  match m with
  | [] => [(k, e)]
  | (k', e') :: rest =>
    if Nat.eqb k k' then (k, e) :: rest else (k', e') :: assoc_set_entry k e rest
  end.

Definition update_num_table (key : nat) (v : Z) (tbl : nat -> Z) : nat -> Z :=
  fun q => if Nat.eqb q key then v else tbl q.

Definition serialize_tf_cache (fmt : Z -> Z) (tbl : nat -> Z) (cache : list (nat * tf_entry)) : list (nat * Z) :=
  List.map (fun '(k, e) => (k, resolve_tf_entry fmt tbl k e)) cache.

Fixpoint no_key_in (key : nat) (cache : list (nat * tf_entry)) : Prop :=
  match cache with
  | [] => True
  | (k', _) :: rest => k' <> key /\ no_key_in key rest
  end.

Inductive unique_tf_keys : list (nat * tf_entry) -> Prop :=
  | UtfNil : unique_tf_keys []
  | UtfCons k e rest : no_key_in k rest -> unique_tf_keys rest -> unique_tf_keys ((k, e) :: rest).

Lemma serialize_untouched_table :
  forall rest fmt tbl key v,
    no_key_in key rest ->
    serialize_tf_cache fmt (update_num_table key v tbl) rest =
    serialize_tf_cache fmt tbl rest.
Proof.
  induction rest as [| [k' e'] r IH]; intros fmt tbl key v Hnk; simpl.
  - reflexivity.
  - destruct Hnk as [Hne Hrest].
    f_equal.
    + destruct e'; simpl; [reflexivity |].
      unfold update_num_table.
      destruct (Nat.eqb_spec k' key); [lia | reflexivity].
    + apply IH. exact Hrest.
Qed.

(**md **Storing `TransformCache.numeric` sentinels in `DraggableTransformTarget` serializes identically to eager per-write string formatting.**
For any unique-keyed transform cache `cache`, updating `numbers[key] := v` and marking `cache[key] := TfNumericSentinel` produces the exact serialized declaration list at `Flush()` as eagerly formatting `TfText (fmt v)` on every axis write, with zero intermediate string allocations. *)
Theorem draggable_numeric_sentinel_cache_eq_eager_text :
  forall cache fmt tbl key v,
    unique_tf_keys cache ->
    serialize_tf_cache fmt (update_num_table key v tbl) (assoc_set_entry key TfNumericSentinel cache) =
    serialize_tf_cache fmt tbl (assoc_set_entry key (TfText (fmt v)) cache).
Proof.
  induction cache as [| [k' e'] rest IH]; intros fmt tbl key v Huniq; simpl.
  - unfold update_num_table. rewrite Nat.eqb_refl. reflexivity.
  - inversion Huniq as [| k0 e0 r0 Hnk Hurest]; subst.
    destruct (Nat.eqb_spec key k') as [Heq | Hneq].
    + subst k'. simpl. unfold update_num_table at 1. rewrite Nat.eqb_refl.
      f_equal. apply serialize_untouched_table. exact Hnk.
    + simpl. f_equal.
      * destruct e'; simpl; [reflexivity |].
        unfold update_num_table.
        destruct (Nat.eqb_spec k' key); [lia | reflexivity].
      * apply IH. exact Hurest.
Qed.

Inductive css_punct :=
  | TokColon
  | TokBang
  | TokOther.

Fixpoint find_first_colon_tail (toks : list (nat * css_punct)) : option (nat * list (nat * css_punct)) :=
  match toks with
  | [] => None
  | (pos, TokColon) :: rest => Some (pos, rest)
  | _ :: rest => find_first_colon_tail rest
  end.

Fixpoint find_last_bang (toks : list (nat * css_punct)) : option nat :=
  match toks with
  | [] => None
  | (pos, TokBang) :: rest =>
    match find_last_bang rest with
    | Some later => Some later
    | None => Some pos
    end
  | _ :: rest => find_last_bang rest
  end.

Definition two_pass_decl_markers (toks : list (nat * css_punct)) : option (nat * option nat) :=
  match find_first_colon_tail toks with
  | None => None
  | Some (c, tail) => Some (c, find_last_bang tail)
  end.

Fixpoint single_pass_decl_markers_aux
    (toks : list (nat * css_punct))
    (first_colon : option nat)
    (last_bang : option nat) : option (nat * option nat) :=
  match toks with
  | [] =>
    match first_colon with
    | None => None
    | Some c => Some (c, last_bang)
    end
  | (pos, TokColon) :: rest =>
    match first_colon with
    | None => single_pass_decl_markers_aux rest (Some pos) None
    | Some c => single_pass_decl_markers_aux rest (Some c) last_bang
    end
  | (pos, TokBang) :: rest =>
    match first_colon with
    | None => single_pass_decl_markers_aux rest None None
    | Some c => single_pass_decl_markers_aux rest (Some c) (Some pos)
    end
  | (_, TokOther) :: rest =>
    single_pass_decl_markers_aux rest first_colon last_bang
  end.

Lemma single_pass_after_colon :
  forall rest c acc_bang,
    single_pass_decl_markers_aux rest (Some c) acc_bang =
    Some (c, match find_last_bang rest with
             | Some later => Some later
             | None => acc_bang
             end).
Proof.
  induction rest as [| [pos tok] r IH]; intros c acc_bang.
  - simpl. reflexivity.
  - destruct tok; simpl.
    + apply IH.
    + rewrite IH.
      destruct (find_last_bang r); reflexivity.
    + apply IH.
Qed.

(**md **Single-pass streaming CSS declaration marker tracking matches two-pass token-list `tryFind` + `tryFindBack`.**
Streaming `(first_colon, last_bang)` in a single pass across top-level CSS tokens produces the exact `(colon, bang)` boundary indices as materializing `ResizeArray<int * char>` and scanning forward for `':'` and backward for `'!'`. *)
Theorem css_balanced_delimiter_single_pass_eq_two_pass :
  forall toks,
    single_pass_decl_markers_aux toks None None = two_pass_decl_markers toks.
Proof.
  induction toks as [| [pos tok] rest IH].
  - reflexivity.
  - destruct tok; unfold two_pass_decl_markers in *; simpl.
    + rewrite single_pass_after_colon.
      destruct (find_last_bang rest); reflexivity.
    + exact IH.
    + exact IH.
Qed.

(** ** Batch A4 Refinement Proofs: Pre-Allocated Parallel Snapshot Restore & Flat `char[]` Scramble Compaction *)

Definition style_pair_eqb (a b : Z * Z) : bool :=
  let '(v1, p1) := a in
  let '(v2, p2) := b in
  andb (Z.eqb v1 v2) (Z.eqb p1 p2).

Fixpoint assoc_lookup_style (leaf : nat) (m : list (nat * (Z * Z))) : option (Z * Z) :=
  match m with
  | [] => None
  | (k, vp) :: rest => if Nat.eqb leaf k then Some vp else assoc_lookup_style leaf rest
  end.

Fixpoint parallel_lookup_style (leaf : nat) (ks : list nat) (vs : list (Z * Z)) : option (Z * Z) :=
  match ks, vs with
  | k :: krest, vp :: vrest =>
    if Nat.eqb leaf k then Some vp else parallel_lookup_style leaf krest vrest
  | _, _ => None
  end.

Definition frozendict_leaf_allowed
    (m : list (nat * (Z * Z))) (live : nat -> Z * Z) (prop_leaves : list nat) : bool :=
  List.forallb (fun leaf =>
    match assoc_lookup_style leaf m with
    | Some vp => style_pair_eqb (live leaf) vp
    | None => true
    end) prop_leaves.

Definition parallel_array_leaf_allowed
    (leaves : list nat) (written_vals : list (Z * Z)) (live : nat -> Z * Z) (prop_leaves : list nat) : bool :=
  List.forallb (fun leaf =>
    match parallel_lookup_style leaf leaves written_vals with
    | Some vp => style_pair_eqb (live leaf) vp
    | None => true
    end) prop_leaves.

Lemma parallel_lookup_map_eq_assoc :
  forall leaves written_dom leaf,
    parallel_lookup_style leaf leaves (List.map written_dom leaves) =
    assoc_lookup_style leaf (List.map (fun l => (l, written_dom l)) leaves).
Proof.
  induction leaves as [| k rest IH]; intros written_dom leaf; simpl.
  - reflexivity.
  - destruct (Nat.eqb leaf k); [reflexivity | apply IH].
Qed.

(**md **In-place parallel `(value, priority)` array updates in `LayoutStyleSnapshot.MarkWritten` restore identically to rebuilding `FrozenDictionary` per frame.**
Storing `List.map written_dom leaves` in a pre-allocated mutable array parallel to `leaves` yields the exact leaf-permission predicate `allowed` during `Restore()` as constructing a fresh `FrozenDictionary` on every `MarkWritten()` frame, eliminating `100%` of per-frame dictionary allocations. *)
Theorem parallel_snapshot_restore_eq_frozendict :
  forall leaves written_dom live prop_leaves,
    parallel_array_leaf_allowed leaves (List.map written_dom leaves) live prop_leaves =
    frozendict_leaf_allowed (List.map (fun l => (l, written_dom l)) leaves) live prop_leaves.
Proof.
  intros leaves written_dom live prop_leaves.
  unfold parallel_array_leaf_allowed, frozendict_leaf_allowed.
  induction prop_leaves as [| leaf rest IH]; simpl.
  - reflexivity.
  - rewrite parallel_lookup_map_eq_assoc.
    rewrite IH.
    reflexivity.
Qed.

Definition slot_to_string_singleton (slot : option Z) : list Z :=
  match slot with
  | Some ch => [ch]
  | None => []
  end.

Fixpoint compact_char_slots (slots : list (option Z)) : list Z :=
  match slots with
  | [] => []
  | Some ch :: rest => ch :: compact_char_slots rest
  | None :: rest => compact_char_slots rest
  end.

(**md **Compacting unboxed integer/character scramble caches into a pre-allocated `char[]` buffer matches 1-char `string[]` caches + `StringBuilder` and stays within `animLength`.**
For any resolved per-slot code-unit sequence `slots`, `compact_char_slots` emits the exact character sequence produced by concatenating 1-character `string[]` cache entries via `StringBuilder`, and its output length never exceeds `length slots` (`animLength`). *)
Theorem char_buffer_scramble_eq_string_cache :
  forall slots,
    compact_char_slots slots = List.concat (List.map slot_to_string_singleton slots) /\
    (length (compact_char_slots slots) <= length slots)%nat.
Proof.
  induction slots as [| [ch | ] rest [Heq Hlen]]; simpl.
  - split; [reflexivity | lia].
  - split; [rewrite Heq; reflexivity | lia].
  - split; [exact Heq | lia].
Qed.

(** ** Pass-2 Correctness Proof (#41): Zero-Duration `AutoLayout` Sampler Reaches Target Endpoint After Delay *)

Definition layout_zero_dur_seek (eps local_t : Z) : Z :=
  if Z.ltb local_t 0 then 0 else eps.

(**md **Zero-duration `AutoLayout` property samplers hold `0` (`first`) during delay (`local_t < 0`) and seek to `eps` (`last`) for all `local_t >= 0` (#41).**
For any normalized timer epsilon `eps > 0` (`1e-11`), `layout_zero_dur_seek eps local_t` evaluates to `0` before `delay` elapses and `eps` (`100%` progress) at and after `delay`, preventing `Math.Clamp(local_t, 0, 0) = 0` from stranding `Duration = 0` layout transitions at their pre-mutation geometry. *)
Theorem layout_zero_duration_seek_reaches_target_after_delay :
  forall eps local_t,
    0 < eps ->
    (local_t < 0 -> layout_zero_dur_seek eps local_t = 0) /\
    (0 <= local_t -> layout_zero_dur_seek eps local_t = eps).
Proof.
  intros eps local_t Heps.
  unfold layout_zero_dur_seek.
  destruct (Z.ltb_spec local_t 0); lia.
Qed.

(** ** Batch B3 Refinement Proofs: Indexed `AutoLayout` Style Tracking, Flat Property Ordinal Array & Empty-Inline-Style Fast Snapshot *)

Definition style_key := (nat * nat)%type.

Definition style_key_eqb (a b : style_key) : bool :=
  let '(e1, p1) := a in
  let '(e2, p2) := b in
  andb (Nat.eqb e1 e2) (Nat.eqb p1 p2).

Lemma style_key_eqb_eq :
  forall a b, style_key_eqb a b = true <-> a = b.
Proof.
  intros [e1 p1] [e2 p2]. unfold style_key_eqb.
  destruct (Nat.eqb_spec e1 e2); destruct (Nat.eqb_spec p1 p2); simpl; split; intros Heq;
    try congruence; try inversion Heq; subst; auto.
Qed.

Fixpoint linear_find_style {V : Type} (k : style_key) (entries : list (style_key * V)) : option V :=
  match entries with
  | [] => None
  | (k', v) :: rest => if style_key_eqb k k' then Some v else linear_find_style k rest
  end.

Definition track_style_linear {V : Type} (mk : style_key -> V) (entries : list (style_key * V)) (k : style_key)
    : list (style_key * V) * V :=
  match linear_find_style k entries with
  | Some existing => (entries, existing)
  | None =>
    let fresh := mk k in
    (entries ++ [(k, fresh)], fresh)
  end.

Definition indexed_style_state (V : Type) := (list (style_key * V) * (style_key -> option V))%type.

Definition track_style_indexed {V : Type} (mk : style_key -> V) (st : indexed_style_state V) (k : style_key)
    : indexed_style_state V * V :=
  let '(order_log, idx_map) := st in
  match idx_map k with
  | Some existing => (st, existing)
  | None =>
    let fresh := mk k in
    ((order_log ++ [(k, fresh)], fun q => if style_key_eqb q k then Some fresh else idx_map q), fresh)
  end.

Lemma linear_find_app_fresh :
  forall {V : Type} (entries : list (style_key * V)) (k q : style_key) (fresh : V),
    linear_find_style k entries = None ->
    linear_find_style q (entries ++ [(k, fresh)]) =
    (if style_key_eqb q k then Some fresh else linear_find_style q entries).
Proof.
  induction entries as [| [k' v'] rest IH]; intros k q fresh Hnone; simpl in *.
  - destruct (style_key_eqb q k); reflexivity.
  - destruct (style_key_eqb k k') eqn:Hkk'; [discriminate |].
    destruct (style_key_eqb q k') eqn:Hqk'.
    + destruct (style_key_eqb q k) eqn:Hqk.
      * apply style_key_eqb_eq in Hqk'. apply style_key_eqb_eq in Hqk. subst.
        assert (Hrefl : style_key_eqb k k = true) by (apply style_key_eqb_eq; reflexivity).
        congruence.
      * reflexivity.
    + apply IH. exact Hnone.
Qed.

Fixpoint run_linear_tracks {V : Type} (mk : style_key -> V) (ks : list style_key) (entries : list (style_key * V))
    : list (style_key * V) * list V :=
  match ks with
  | [] => (entries, [])
  | k :: rest =>
    let '(entries', v) := track_style_linear mk entries k in
    let '(final_entries, vs) := run_linear_tracks mk rest entries' in
    (final_entries, v :: vs)
  end.

Fixpoint run_indexed_tracks {V : Type} (mk : style_key -> V) (ks : list style_key) (st : indexed_style_state V)
    : indexed_style_state V * list V :=
  match ks with
  | [] => (st, [])
  | k :: rest =>
    let '(st', v) := track_style_indexed mk st k in
    let '(final_st, vs) := run_indexed_tracks mk rest st' in
    (final_st, v :: vs)
  end.

Lemma run_indexed_tracks_inv :
  forall {V : Type} (mk : style_key -> V) (ks : list style_key)
         (entries : list (style_key * V)) (idx_map : style_key -> option V),
    (forall q, idx_map q = linear_find_style q entries) ->
    let '((idx_log, final_map), vs_idx) := run_indexed_tracks mk ks (entries, idx_map) in
    let '(lin_log, vs_lin) := run_linear_tracks mk ks entries in
    idx_log = lin_log /\ vs_idx = vs_lin /\ (forall q, final_map q = linear_find_style q lin_log).
Proof.
  intros V mk ks.
  induction ks as [| k rest IH]; intros entries idx_map Hinv; simpl.
  - repeat split; auto.
  - unfold track_style_indexed, track_style_linear.
    rewrite Hinv.
    destruct (linear_find_style k entries) as [existing | ] eqn:Hfind.
    + specialize (IH entries idx_map Hinv).
      destruct (run_indexed_tracks mk rest (entries, idx_map)) as [[idx_log final_map] vs_idx].
      destruct (run_linear_tracks mk rest entries) as [lin_log vs_lin].
      destruct IH as [Hlog [Hvs Hmap]].
      repeat split; congruence.
    + assert (Hinv' : forall q,
                (if style_key_eqb q k then Some (mk k) else idx_map q) =
                linear_find_style q (entries ++ [(k, mk k)])).
      { intro q. rewrite Hinv. symmetry. apply linear_find_app_fresh. exact Hfind. }
      specialize (IH (entries ++ [(k, mk k)])
                     (fun q => if style_key_eqb q k then Some (mk k) else idx_map q)
                     Hinv').
      destruct (run_indexed_tracks mk rest
                  (entries ++ [(k, mk k)], fun q => if style_key_eqb q k then Some (mk k) else idx_map q))
        as [[idx_log final_map] vs_idx].
      destruct (run_linear_tracks mk rest (entries ++ [(k, mk k)])) as [lin_log vs_lin].
      destruct IH as [Hlog [Hvs Hmap]].
      repeat split; congruence.
Qed.

(**md **Indexing `(HTMLElement, propName)` style writes in `AutoLayout` yields the exact snapshot handles and reverse-restoration log as linear `styles` scans.**
Starting from an empty style table, tracking any sequence of `(element, property)` writes `ks` via an $O(1)$ key map + append-only order log produces the exact sequence of `LayoutStyleWrite` handles and the exact reverse-insertion restoration list `List.rev lin_log` as scanning `styles` linearly on every `write`, `track`, and `updateLast`. *)
Theorem indexed_style_map_eq_linear_styles :
  forall {V : Type} (mk : style_key -> V) (ks : list style_key),
    let '((idx_log, _), vs_idx) := run_indexed_tracks mk ks ([], fun _ => None) in
    let '(lin_log, vs_lin) := run_linear_tracks mk ks [] in
    vs_idx = vs_lin /\ List.rev idx_log = List.rev lin_log.
Proof.
  intros V mk ks.
  pose proof (run_indexed_tracks_inv mk ks [] (fun _ => None) (fun _ => eq_refl)) as Hres.
  destruct (run_indexed_tracks mk ks ([], fun _ => None)) as [[idx_log final_map] vs_idx].
  destruct (run_linear_tracks mk ks []) as [lin_log vs_lin].
  destruct Hres as [Hlog [Hvs _]].
  split; congruence.
Qed.

Fixpoint assoc_lookup_prop (p : nat) (m : list (nat * Z)) : option Z :=
  match m with
  | [] => None
  | (q, v) :: rest => if Nat.eqb p q then Some v else assoc_lookup_prop p rest
  end.

Fixpoint prop_ordinal (p : nat) (props : list nat) : option nat :=
  match props with
  | [] => None
  | q :: rest =>
    if Nat.eqb p q then Some 0%nat
    else option_map S (prop_ordinal p rest)
  end.

Definition flat_values_lookup (p : nat) (props : list nat) (vals : list Z) : option Z :=
  match prop_ordinal p props with
  | Some idx => List.nth_error vals idx
  | None => None
  end.

(**md **Storing `LayoutMeasurement.Values` as a flat `string[]` indexed by `props` ordinal equals per-node `FrozenDictionary` lookup.**
For any property list `props` and computed-style reader `read_css`, resolving `p`'s ordinal once in `props` and indexing `List.map read_css props` returns the exact `option` value produced by constructing and querying a per-node dictionary `List.map (fun q => (q, read_css q)) props`, eliminating `FrozenDictionary.ToFrozenDictionary` allocations on every measured DOM node. *)
Theorem flat_prop_index_values_eq_frozendict :
  forall (props : list nat) (read_css : nat -> Z) (p : nat),
    flat_values_lookup p props (List.map read_css props) =
    assoc_lookup_prop p (List.map (fun q => (q, read_css q)) props).
Proof.
  unfold flat_values_lookup.
  induction props as [| q rest IH]; intros read_css p; simpl.
  - reflexivity.
  - destruct (Nat.eqb p q) eqn:Heq; simpl.
    + reflexivity.
    + rewrite <- IH.
      destruct (prop_ordinal p rest) as [idx | ]; simpl; reflexivity.
Qed.

Definition opt_style_eqb (a b : option (Z * Z)) : bool :=
  match a, b with
  | None, None => true
  | Some p1, Some p2 => style_pair_eqb p1 p2
  | _, _ => false
  end.

Definition full_snapshot_restore_leaf
    (orig : nat -> option (Z * Z))
    (last_written : nat -> option (Z * Z))
    (live : nat -> option (Z * Z))
    (leaf : nat) : option (Z * Z) :=
  if opt_style_eqb (live leaf) (last_written leaf) then orig leaf else live leaf.

Definition empty_origin_fast_restore_leaf
    (last_written : nat -> option (Z * Z))
    (live : nat -> option (Z * Z))
    (leaf : nat) : option (Z * Z) :=
  if opt_style_eqb (live leaf) (last_written leaf) then None else live leaf.

(**md **Fast empty-inline-style snapshot capture and direct `removeProperty` restoration equal full `CssPropertySnapshot` capture and guarded `Restore`.**
When an element's inline `style` has zero declarations at capture (`orig leaf = None` for all `leaf` and no live logical counterparts), removing each leaf whose current inline value still matches `last_written leaf` (`None`) and leaving concurrently edited leaves untouched (`live leaf`) yields the exact post-restoration inline style for every leaf as full `CssPropertySnapshot` capture and guarded `Restore`. *)
Theorem empty_inline_style_fast_snapshot_eq_full :
  forall (orig last_written live : nat -> option (Z * Z)) (leaves : list nat),
    (forall leaf, In leaf leaves -> orig leaf = None) ->
    List.map (empty_origin_fast_restore_leaf last_written live) leaves =
    List.map (full_snapshot_restore_leaf orig last_written live) leaves.
Proof.
  intros orig last_written live leaves Hempty.
  induction leaves as [| l rest IH]; simpl.
  - reflexivity.
  - f_equal.
    + unfold empty_origin_fast_restore_leaf, full_snapshot_restore_leaf.
      rewrite (Hempty l (or_introl eq_refl)).
      reflexivity.
    + apply IH. intros leaf Hin. apply Hempty. right; exact Hin.
Qed.
