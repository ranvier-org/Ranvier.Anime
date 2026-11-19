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
