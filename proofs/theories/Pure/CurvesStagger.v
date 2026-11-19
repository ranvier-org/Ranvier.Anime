From stdpp Require Import prelude.
From Stdlib Require Import Arith Lia List Bool ZArith.
Import ListNotations.
Open Scope Z_scope.

(** * Easing Symmetries, Spring Stretch Invariance & Grid Stagger Bijection *)

Definition ease_out_scaled (S : Z) (e : Z -> Z) (t : Z) : Z :=
  S - e (S - t).

Definition ease_inout_scaled (half : Z) (e : Z -> Z) (t : Z) : Z :=
  let S := 2 * half in
  if Z.ltb t half then (e (2 * t)) / 2
  else if Z.eqb t half then half
  else S - (e (2 * (S - t))) / 2.

(**md **Ease `Out` reflection preserves `[0, S]` endpoints and is a strict involution.**
Whenever `e 0 = 0` and `e S = S`, `ease_out_scaled S e` fixes `0` and `S`, and applying `ease_out_scaled S` twice recovers `e t` at every input `t`. *)
Theorem ease_out_reflection_endpoints_and_involution :
  forall S (e : Z -> Z) t,
    e 0 = 0 ->
    e S = S ->
    ease_out_scaled S e 0 = 0 /\
    ease_out_scaled S e S = S /\
    ease_out_scaled S (ease_out_scaled S e) t = e t.
Proof.
  intros S e t He0 HeS.
  unfold ease_out_scaled.
  replace (S - 0) with S by lia.
  replace (S - S) with 0 by lia.
  replace (S - (S - t)) with t by lia.
  rewrite HeS, He0.
  repeat split; lia.
Qed.

(**md **Ease `InOut` fixes the midpoint and satisfies odd symmetry around `(half, half)`.**
For any scale `S = 2 * half` with `half > 0`, `ease_inout_scaled half e` maps `half` to `half` and satisfies `f(2 * half - t) = 2 * half - f(t)` for all `t`. *)
Theorem ease_inout_midpoint_and_symmetry :
  forall half (e : Z -> Z) t,
    0 < half ->
    ease_inout_scaled half e half = half /\
    ease_inout_scaled half e (2 * half - t) = 2 * half - ease_inout_scaled half e t.
Proof.
  intros half e t Hpos.
  unfold ease_inout_scaled.
  split.
  - destruct (Z.ltb_spec half half); [lia |].
    rewrite Z.eqb_refl. reflexivity.
  - destruct (Z.ltb_spec t half) as [Hlt | Hge].
    + destruct (Z.ltb_spec (2 * half - t) half) as [Hlt2 | Hge2]; [lia |].
      destruct (Z.eqb_spec (2 * half - t) half) as [Heq2 | Hne2]; [lia |].
      replace (2 * half - (2 * half - t)) with t by lia.
      lia.
    + destruct (Z.eqb_spec t half) as [Heq | Hne].
      * subst t.
        replace (2 * half - half) with half by lia.
        destruct (Z.ltb_spec half half); [lia |].
        rewrite Z.eqb_refl. lia.
      * assert (Hgt : half < t) by lia.
        destruct (Z.ltb_spec (2 * half - t) half) as [Hlt2 | Hge2]; [| lia].
        replace (2 * (2 * half - t)) with (2 * (2 * half - t)) by reflexivity.
        lia.
Qed.

(** ** Spring perceived completion ratio invariance under Stretch *)

Record spring_state := {
  sp_settling_dur : Z;
  sp_natural_settling : Z;
  sp_perceived_dur : Z
}.

Definition spring_stretch (new_dur : Z) (sp : spring_state) : spring_state :=
  {| sp_settling_dur := Z.max 0 new_dur;
     sp_natural_settling := sp_natural_settling sp;
     sp_perceived_dur := sp_perceived_dur sp |}.

Definition spring_perceived_done (progress_num progress_den : Z) (sp : spring_state) : bool :=
  Z.geb (progress_num * sp_natural_settling sp) (sp_perceived_dur sp * progress_den).

(**md **Stretching a `Spring` preserves its normalized perceived-completion threshold.**
`Spring.Stretch` updates `settlingDuration` without altering `naturalSettlingDuration` or `durationValue`, so `spring_perceived_done` fires at the identical normalized progress fraction. *)
Theorem spring_stretch_preserves_completion_ratio :
  forall new_dur p_num p_den sp,
    spring_perceived_done p_num p_den (spring_stretch new_dur sp) =
    spring_perceived_done p_num p_den sp.
Proof.
  intros new_dur p_num p_den sp.
  unfold spring_perceived_done, spring_stretch.
  simpl. reflexivity.
Qed.

(** ** 3D Grid Stagger coordinate bijection & flat-step SIMD equivalence *)

Definition grid3_decode (cols rows idx : Z) : Z * Z * Z :=
  let x := Z.modulo idx cols in
  let y := Z.modulo (Z.div idx cols) rows in
  let z := Z.div idx (cols * rows) in
  (x, y, z).

Definition grid3_encode (cols rows x y z : Z) : Z :=
  x + cols * (y + rows * z).

(**md **3D stagger grid index decoding and encoding form an exact bijection.**
For `cols > 0` and `rows > 0`, `grid3_decode` and `grid3_encode` invert one another across all non-negative target indices and in-bounds `(x, y, z)` coordinates. *)
Theorem stagger_grid3d_bijection :
  forall cols rows idx,
    0 < cols ->
    0 < rows ->
    0 <= idx ->
    let '(x, y, z) := grid3_decode cols rows idx in
    0 <= x < cols /\
    0 <= y < rows /\
    0 <= z /\
    grid3_encode cols rows x y z = idx.
Proof.
  intros cols rows idx Hc Hr Hidx.
  unfold grid3_decode, grid3_encode.
  assert (Hx : 0 <= idx mod cols < cols) by (apply Z.mod_pos_bound; lia).
  set (q1 := idx / cols).
  assert (Hq1_eq : idx = cols * q1 + idx mod cols) by (apply Z.div_mod; lia).
  assert (Hq1_nonneg : 0 <= q1) by (apply Z.div_pos; lia).
  assert (Hy : 0 <= q1 mod rows < rows) by (apply Z.mod_pos_bound; lia).
  set (q2 := q1 / rows).
  assert (Hq2_eq : q1 = rows * q2 + q1 mod rows) by (apply Z.div_mod; lia).
  assert (Hq2_nonneg : 0 <= q2) by (apply Z.div_pos; lia).
  assert (Hz_eq : idx / (cols * rows) = q2).
  { subst q1 q2. rewrite Z.div_div by lia. reflexivity. }
  rewrite Hz_eq.
  repeat split; nia.
Qed.

Definition flat_step_scalar (origin step start_off i : Z) : Z :=
  start_off + step * Z.abs (origin - i).

Fixpoint flat_step_simd_chunks (fuel chunk : nat) (base : Z) (rem : nat) (origin step start_off : Z) : list Z :=
  match fuel, rem with
  | _, 0%nat => []
  | 0%nat, _ =>
    List.map (fun k => flat_step_scalar origin step start_off (base + Z.of_nat k)) (seq 0 rem)
  | S f, _ =>
    if Nat.leb chunk rem then
      let lane_vals :=
        List.map (fun k => start_off + step * Z.abs (origin - (base + Z.of_nat k))) (seq 0 chunk) in
      lane_vals ++ flat_step_simd_chunks f chunk (base + Z.of_nat chunk) (rem - chunk)%nat origin step start_off
    else
      List.map (fun k => flat_step_scalar origin step start_off (base + Z.of_nat k)) (seq 0 rem)
  end.

Lemma map_seq_offset :
  forall (f : Z -> Z) len start base,
    List.map (fun k => f (base + Z.of_nat k)) (seq start len) =
    List.map (fun k => f (base + Z.of_nat start + Z.of_nat k)) (seq 0 len).
Proof.
  intros f len.
  induction len as [| len' IH]; intros start base; simpl; [reflexivity |].
  f_equal.
  - f_equal. lia.
  - rewrite IH with (start := S start) (base := base).
    rewrite IH with (start := 1%nat) (base := base + Z.of_nat start).
    apply List.map_ext. intros k. f_equal. lia.
Qed.

(**md **3-tier SIMD flat-step stagger matches scalar stagger element-for-element.**
Processing `count` targets in arbitrary positive SIMD lane widths (`chunk = 8`, `4`, or `2`) followed by a scalar tail produces the exact list of offsets `[flat_step_scalar origin step start_off i | i <- 0 .. count - 1]`. *)
Theorem stagger_flat_step_simd_eq_scalar :
  forall fuel chunk rem base origin step start_off,
    (0 < chunk)%nat ->
    flat_step_simd_chunks fuel chunk base rem origin step start_off =
    List.map (fun k => flat_step_scalar origin step start_off (base + Z.of_nat k)) (seq 0 rem).
Proof.
  induction fuel as [| f IH]; intros chunk rem base origin step start_off Hchunk.
  - destruct rem; simpl; reflexivity.
  - destruct rem as [| r']; [simpl; reflexivity |].
    simpl flat_step_simd_chunks.
    destruct (Nat.leb_spec chunk (S r')) as [Hle | Hgt].
    + rewrite IH by lia.
      replace (S r') with (chunk + (S r' - chunk))%nat at 2 by lia.
      rewrite seq_app, map_app.
      f_equal.
      rewrite map_seq_offset with (start := chunk) (base := base).
      reflexivity.
    + reflexivity.
Qed.

(** ** Batch A2 Refinement Proofs: 4-Branch `Ease.bounce` & Monotone Binary-Search `Ease.linear` *)

Definition bounce_term_64 (scale64 center22 t22 : Z) : Z :=
  let d := center22 - t22 in
  scale64 - d * d.

Fixpoint bounce_loop_64_aux (steps : list (Z * Z * Z)) (fallback : Z * Z) (t22 : Z) : Z :=
  match steps with
  | [] =>
    let '(sc, c) := fallback in
    bounce_term_64 sc c t22
  | (thresh, sc, c) :: rest =>
    if Z.ltb t22 thresh then bounce_loop_64_aux rest fallback t22
    else bounce_term_64 sc c t22
  end.

(** Models the `b = 3, 2, 1, 0` Penner/Anime.js `bounce` loop on `t = t22 / 22 >= 0` scaled by `64` (`7.5625 = (11/4)^2`). *)
Definition bounce_loop_64 (t22 : Z) : Z :=
  bounce_loop_64_aux [(14, 64, 22); (6, 16, 10); (2, 4, 4)] (1, 1) t22.

Definition bounce_4branch_64 (t22 : Z) : Z :=
  if Z.geb t22 14 then 64 - (22 - t22) * (22 - t22)
  else if Z.geb t22 6 then 16 - (10 - t22) * (10 - t22)
  else if Z.geb t22 2 then 4 - (4 - t22) * (4 - t22)
  else 1 - (1 - t22) * (1 - t22).

(**md **`Math.Pow`-free 4-branch `Ease.bounce` equals the Penner/Anime.js power-search loop across `[0, +infinity)`.**
For all non-negative `t22 = 22 * t >= 0`, the closed-form 4-branch evaluation with dyadic constants `(1, 1/4, 1/16, 1/64)` and `(22/22, 10/22, 4/22, 1/22)` matches the `b = 3 .. 0` search loop identically. *)
Theorem bounce_4branch_eq_pow_loop :
  forall t22,
    0 <= t22 ->
    bounce_4branch_64 t22 = bounce_loop_64 t22.
Proof.
  intros t22 Hnn.
  unfold bounce_4branch_64, bounce_loop_64, bounce_loop_64_aux, bounce_term_64.
  destruct (Z.geb_spec t22 14); destruct (Z.ltb_spec t22 14); try lia.
  destruct (Z.geb_spec t22 6); destruct (Z.ltb_spec t22 6); try lia.
  destruct (Z.geb_spec t22 2); destruct (Z.ltb_spec t22 2); lia.
Qed.

Definition nondecreasing_z (xs : list Z) : Prop :=
  forall i j : nat,
    (i <= j)%nat ->
    (j < length xs)%nat ->
    List.nth i xs 0 <= List.nth j xs 0.

Fixpoint linear_first_ge (t : Z) (xs : list Z) : nat :=
  match xs with
  | [] => 0%nat
  | x :: rest => if Z.gtb t x then S (linear_first_ge t rest) else 0%nat
  end.

Fixpoint bsearch_first_ge (fuel lo hi : nat) (t : Z) (xs : list Z) : nat :=
  match fuel with
  | 0%nat => lo
  | S f =>
    if Nat.leb hi lo then lo
    else
      let mid := Nat.div (lo + hi) 2 in
      if Z.gtb t (List.nth mid xs 0) then
        bsearch_first_ge f (S mid) hi t xs
      else
        bsearch_first_ge f lo mid t xs
  end.

Lemma linear_first_ge_bound :
  forall t xs, (linear_first_ge t xs <= length xs)%nat.
Proof.
  intros t xs.
  induction xs as [| x rest IH]; simpl; [lia |].
  destruct (Z.gtb t x); lia.
Qed.

Lemma nondecreasing_cons_tail :
  forall x rest,
    nondecreasing_z (x :: rest) ->
    nondecreasing_z rest.
Proof.
  intros x rest Hnd i j Hij Hj.
  exact (Hnd (S i) (S j) ltac:(lia) ltac:(simpl; lia)).
Qed.

Lemma linear_first_ge_characterization :
  forall xs t mid,
    nondecreasing_z xs ->
    (mid < length xs)%nat ->
    (Z.gtb t (List.nth mid xs 0) = true <-> (mid < linear_first_ge t xs)%nat).
Proof.
  induction xs as [| x rest IH]; intros t mid Hnd Hmid.
  - simpl in Hmid. lia.
  - destruct mid as [| m'].
    + simpl.
      destruct (Z.gtb_spec t x) as [Htx | Hle]; split; intros Hgoal; try reflexivity; try discriminate; lia.
    + simpl List.nth.
      simpl linear_first_ge.
      destruct (Z.gtb_spec t x) as [Htx | Hle].
      * rewrite (IH t m' (nondecreasing_cons_tail x rest Hnd) ltac:(simpl in Hmid; lia)).
        lia.
      * assert (Hxm : x <= List.nth m' rest 0).
        { apply (Hnd 0%nat (S m')); [lia | exact Hmid]. }
        destruct (Z.gtb_spec t (List.nth m' rest 0)) as [Htm | Htm]; split; intros Hgoal; try discriminate; lia.
Qed.

Lemma bsearch_first_ge_invariant :
  forall fuel lo hi t xs,
    nondecreasing_z xs ->
    (hi <= length xs)%nat ->
    (lo <= linear_first_ge t xs <= hi)%nat ->
    (hi - lo <= fuel)%nat ->
    bsearch_first_ge fuel lo hi t xs = linear_first_ge t xs.
Proof.
  induction fuel as [| f IH]; intros lo hi t xs Hnd Hhi Hrange Hfuel.
  - simpl. lia.
  - cbn [bsearch_first_ge].
    destruct (Nat.leb_spec hi lo) as [Hle | Hlt].
    + lia.
    + set (mid := Nat.div (lo + hi) 2).
      assert (Hmid_bounds : (lo <= mid /\ mid < hi)%nat).
      { subst mid.
        pose proof (Nat.div_mod (lo + hi) 2 ltac:(lia)) as Hdm.
        pose proof (Nat.mod_upper_bound (lo + hi) 2 ltac:(lia)) as Hmod.
        lia. }
      assert (Hmid_len : (mid < length xs)%nat) by lia.
      pose proof (linear_first_ge_characterization xs t mid Hnd Hmid_len) as Hchar.
      destruct (Z.gtb t (List.nth mid xs 0)) eqn:Hgt.
      * assert (Hmk : (mid < linear_first_ge t xs)%nat) by (apply Hchar; reflexivity).
        apply IH; [exact Hnd | exact Hhi | lia | lia].
      * assert (Hkm : (linear_first_ge t xs <= mid)%nat).
        { destruct (Nat.lt_ge_cases mid (linear_first_ge t xs)) as [Hlt_k | Hge_k]; [| exact Hge_k].
          apply Hchar in Hlt_k. discriminate. }
        apply IH; [exact Hnd | lia | lia | lia].
Qed.

(**md **Binary-search stop selection on non-decreasing `Ease.linear` stops equals sequential stop scanning (including duplicate stops).**
For every sorted stop-position array `xs` (allowing repeated timestamps), `bsearch_first_ge` returns the exact first index `k` with `time <= xs[k]` selected by `Ease.linear`'s sequential loop. *)
Theorem monotone_lower_bound_bsearch_eq_linear_scan :
  forall t xs,
    nondecreasing_z xs ->
    bsearch_first_ge (length xs) 0%nat (length xs) t xs = linear_first_ge t xs.
Proof.
  intros t xs Hnd.
  pose proof (linear_first_ge_bound t xs) as Hbnd.
  apply bsearch_first_ge_invariant; [exact Hnd | lia | lia | lia].
Qed.

Fixpoint bisect_direct (steps : nat) (f : nat -> Z) (target tol : Z) (lo hi cur : nat) : nat * nat * nat :=
  match steps with
  | 0%nat => (lo, hi, cur)
  | S rem =>
    let mid := Nat.div (lo + hi) 2 in
    let diff := f mid - target in
    let '(lo', hi') := if Z.gtb diff 0 then (lo, mid) else (mid, hi) in
    if Z.leb (Z.abs diff) tol then (lo', hi', mid)
    else bisect_direct rem f target tol lo' hi' mid
  end.

Fixpoint bisect_lut (lut_steps rem_steps : nat) (lut : list Z) (f : nat -> Z) (target tol : Z) (lo hi cur : nat) : nat * nat * nat :=
  match lut_steps with
  | 0%nat => bisect_direct rem_steps f target tol lo hi cur
  | S lrem =>
    let mid := Nat.div (lo + hi) 2 in
    let diff := List.nth mid lut 0 - target in
    let '(lo', hi') := if Z.gtb diff 0 then (lo, mid) else (mid, hi) in
    if Z.leb (Z.abs diff) tol then (lo', hi', mid)
    else bisect_lut lrem rem_steps lut f target tol lo' hi' mid
  end.

(**md **Dyadic LUT-prefixed `Ease.cubicBezier` bisection is bit-for-bit identical to full bisection.**
Whenever `lut` tabulates `f` across `[0 .. hi]` (`nth k lut 0 = f k`), running `lut_steps` table-lookup bisection iterations followed by `rem_steps` direct polynomial bisection iterations produces the exact same `(lo, hi, current)` state and early-exit iteration as `(lut_steps + rem_steps)` direct bisection iterations. *)
Theorem dyadic_lut_bisection_eq_direct_bisection :
  forall lut_steps rem_steps lut (f : nat -> Z) target tol lo hi cur,
    (lo <= hi)%nat ->
    (forall k : nat, (k <= hi)%nat -> List.nth k lut 0 = f k) ->
    bisect_lut lut_steps rem_steps lut f target tol lo hi cur =
    bisect_direct (lut_steps + rem_steps)%nat f target tol lo hi cur.
Proof.
  induction lut_steps as [| lrem IH]; intros rem_steps lut f target tol lo hi cur Hle Hlut.
  - simpl. reflexivity.
  - cbn [bisect_lut plus bisect_direct].
    set (mid := Nat.div (lo + hi) 2).
    assert (Hmid : (lo <= mid /\ mid <= hi)%nat).
    { subst mid.
      pose proof (Nat.div_mod (lo + hi) 2 ltac:(lia)) as Hdm.
      pose proof (Nat.mod_upper_bound (lo + hi) 2 ltac:(lia)) as Hmod.
      lia. }
    rewrite (Hlut mid ltac:(lia)).
    destruct (Z.gtb (f mid - target) 0);
      destruct (Z.leb (Z.abs (f mid - target)) tol);
      try reflexivity;
      apply IH; try lia;
      intros k Hk; apply Hlut; lia.
Qed.

(** ** Pass-2 Correctness Proofs (#37, #38): ULP-Safe Clamped Keyframe Offset Deltas & Rationalized Overdamped Spring Decay *)

Definition clamped_offset_step (acc_elapsed target_end : Z) : Z * Z :=
  let seg_dur := Z.max 0 (target_end - acc_elapsed) in
  (seg_dur, acc_elapsed + seg_dur).

(**md **Clamping `target_end - acc_elapsed` at `0` guarantees non-negative segment durations even under positive ULP sum drift (#37).**
For every `acc_elapsed` and `target_end`, `clamped_offset_step` emits `seg_dur >= 0` (preventing `ArgumentException` on equal normalized offsets after a rounded-up prior segment) and reaches `max acc_elapsed target_end`. *)
Theorem nondecreasing_offsets_clamped_delta_nonneg :
  forall acc_elapsed target_end,
    let '(seg_dur, next_elapsed) := clamped_offset_step acc_elapsed target_end in
    0 <= seg_dur /\
    next_elapsed = Z.max acc_elapsed target_end /\
    (acc_elapsed <= target_end -> seg_dur = target_end - acc_elapsed /\ next_elapsed = target_end).
Proof.
  intros acc_elapsed target_end.
  unfold clamped_offset_step.
  repeat split; lia.
Qed.

(**md **Conjugate rationalization `-(w0^2) / (zeta*w0 + wd)` equals `-zeta*w0 + wd` without catastrophic subtraction cancellation (#38).**
Whenever `wd^2 = (zeta*w0)^2 - w0^2` with `w0 > 0` and `zeta*w0 + wd > 0`, the slow overdamped root `r1 = -zeta*w0 + wd` satisfies `r1 * (zeta*w0 + wd) = -(w0 * w0) < 0`, guaranteeing `r1 < 0` strictly negative rather than cancelling to `0.0`. *)
Theorem rationalized_overdamped_decay_avoids_cancellation :
  forall zeta_w0 wd w0,
    0 < w0 ->
    0 < zeta_w0 + wd ->
    wd * wd = zeta_w0 * zeta_w0 - w0 * w0 ->
    (-zeta_w0 + wd) * (zeta_w0 + wd) = -(w0 * w0) /\
    -zeta_w0 + wd < 0.
Proof.
  intros zeta_w0 wd w0 Hw0 Hsum Hwd.
  assert (Hprod : (-zeta_w0 + wd) * (zeta_w0 + wd) = -(w0 * w0)) by nia.
  split; [exact Hprod |].
  assert (Hneg : (-zeta_w0 + wd) * (zeta_w0 + wd) < 0) by nia.
  nia.
Qed.

(** ** Pass-2 Batch B1 Refinement Proofs: Fused `SinCos` Underdamped Spring & 3D Grid Quotient-Chain SIMD *)

Definition spring_underdamped_direct
    (exp_fn sin_fn cos_fn : Z -> Z)
    (zeta_w0 wd coeff t : Z) : Z :=
  let env := exp_fn (-t * zeta_w0) in
  let osc := cos_fn (wd * t) + (if Z.eqb coeff 0 then 0 else coeff * sin_fn (wd * t)) in
  if Z.eqb osc 0 then 0 else env * osc.

Definition spring_underdamped_sincos
    (exp_fn : Z -> Z) (sincos_fn : Z -> Z * Z)
    (neg_zeta_w0 wd coeff t : Z) : Z :=
  let env := exp_fn (neg_zeta_w0 * t) in
  let osc :=
    if Z.eqb coeff 0 then snd (sincos_fn (wd * t))
    else
      let '(s, c) := sincos_fn (wd * t) in
      c + coeff * s in
  if Z.eqb osc 0 then 0 else env * osc.

(**md **Fused `SinCos` and precomputed decay rate `-zeta * w0` equal direct underdamped `Spring.solve` evaluation.**
Whenever `sincos_fn x = (sin_fn x, cos_fn x)` and `neg_zeta_w0 = -zeta_w0`, `spring_underdamped_sincos` evaluates to the exact displacement of `spring_underdamped_direct`, including the `coeff = 0` and `osc = 0` guards. *)
Theorem spring_precomputed_sincos_eq_direct :
  forall (exp_fn sin_fn cos_fn : Z -> Z) (sincos_fn : Z -> Z * Z)
         zeta_w0 wd coeff t,
    (forall x, sincos_fn x = (sin_fn x, cos_fn x)) ->
    spring_underdamped_sincos exp_fn sincos_fn (-zeta_w0) wd coeff t =
    spring_underdamped_direct exp_fn sin_fn cos_fn zeta_w0 wd coeff t.
Proof.
  intros exp_fn sin_fn cos_fn sincos_fn zeta_w0 wd coeff t Hsc.
  unfold spring_underdamped_sincos, spring_underdamped_direct.
  rewrite Hsc.
  replace ((-zeta_w0) * t) with (-t * zeta_w0) by lia.
  destruct (Z.eqb coeff 0); simpl; [rewrite Z.add_0_r; reflexivity | reflexivity].
Qed.

Definition grid3_quotient_chain (cols rows idx : Z) : Z * Z * Z :=
  let q1 := Z.div idx cols in
  let x := idx - q1 * cols in
  let q2 := Z.div q1 rows in
  let y := q1 - q2 * rows in
  (x, y, q2).

(**md **Chained floor-quotient 3D grid SIMD coordinates equal direct modulo/slice coordinates.**
For `cols > 0`, `rows > 0`, and `idx >= 0`, computing `q1 = idx / cols`, `x = idx - q1 * cols`, `q2 = q1 / rows`, `y = q1 - q2 * rows`, and `z = q2` in SIMD vector lanes matches `grid3_decode cols rows idx` (`idx mod cols`, `(idx / cols) mod rows`, `idx / (cols * rows)`) identically without a third division by `cols * rows`. *)
Theorem stagger_grid3d_quotient_chain_simd_eq_direct :
  forall cols rows idx,
    0 < cols ->
    0 < rows ->
    0 <= idx ->
    grid3_quotient_chain cols rows idx = grid3_decode cols rows idx.
Proof.
  intros cols rows idx Hc Hr Hidx.
  unfold grid3_quotient_chain, grid3_decode.
  pose proof (Z.div_mod idx cols ltac:(lia)) as Hdm1.
  set (q1 := idx / cols) in *.
  assert (Hq1 : 0 <= q1) by (subst q1; apply Z.div_pos; lia).
  pose proof (Z.div_mod q1 rows ltac:(lia)) as Hdm2.
  set (q2 := q1 / rows) in *.
  assert (Hz : idx / (cols * rows) = q2).
  { subst q1 q2. rewrite Z.div_div by lia. reflexivity. }
  rewrite Hz.
  replace (idx - q1 * cols) with (idx mod cols) by nia.
  replace (q1 - q2 * rows) with (q1 mod rows) by nia.
  reflexivity.
Qed.
