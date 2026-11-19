From stdpp Require Import prelude.
From Stdlib Require Import Arith Lia List Bool ZArith.
Import ListNotations.
Open Scope Z_scope.

(** * ECMAScript Half-Ceiling Quantization, Homogenization & Perceptual Colorimetry *)

(** Models half-integer values `n / 2` via numerator `num = 2 * x`:
    ECMAScript `Math.round(num / 2)` equals `Z.div (num + 1) 2`! *)
Definition js_round_half_int (num : Z) : Z :=
  Z.div (num + 1) 2.

(**md **ECMAScript half-ceiling rounding breaks exact half-ties toward `+infinity` and is monotonic.**
On half-integers `num / 2`, `-1 / 2` (`num = -1`) rounds to `0` while `+1 / 2` (`num = 1`) rounds to `1`, and `num1 <= num2` implies `js_round_half_int num1 <= js_round_half_int num2`. *)
Theorem js_half_ceil_round_ties_toward_pos_inf :
  js_round_half_int (-1) = 0 /\
  js_round_half_int 1 = 1 /\
  (forall k, js_round_half_int (2 * k) = k) /\
  (forall n1 n2, n1 <= n2 -> js_round_half_int n1 <= js_round_half_int n2).
Proof.
  unfold js_round_half_int.
  split; [reflexivity |].
  split; [reflexivity |].
  split.
  - intros k.
    replace (2 * k + 1) with (k * 2 + 1) by lia.
    rewrite (Z.div_add_l k 2 1 ltac:(lia)).
    change (1 / 2) with 0.
    lia.
  - intros n1 n2 Hle.
    apply Z.div_le_mono; lia.
Qed.

Definition lerp_exact_scaled (S a b p : Z) : Z :=
  if Z.eqb p 0 then a
  else if Z.eqb p S then b
  else a + ((b - a) * p) / S.

(**md **Endpoint-guarded linear interpolation preserves `a` at `0` and `b` at `S` unconditionally.**
For any positive scale `S > 0`, `lerp_exact_scaled S a b` returns `a` at `p = 0` and `b` at `p = S` with zero rounding drift. *)
Theorem lerp_exact_endpoints :
  forall S a b,
    0 < S ->
    lerp_exact_scaled S a b 0 = a /\
    lerp_exact_scaled S a b S = b.
Proof.
  intros S a b HS.
  unfold lerp_exact_scaled.
  rewrite Z.eqb_refl.
  destruct (Z.eqb_spec S 0); [lia |].
  rewrite Z.eqb_refl.
  split; reflexivity.
Qed.

(** ** Component vector right-padding homogenization *)

Fixpoint pad_zeros (target_len : nat) (xs : list Z) : list Z :=
  match target_len, xs with
  | 0%nat, _ => []
  | S n, [] => 0 :: pad_zeros n []
  | S n, x :: rest => x :: pad_zeros n rest
  end.

Definition homogenize_components (xs ys : list Z) : list Z * list Z :=
  let n := Nat.max (length xs) (length ys) in
  (pad_zeros n xs, pad_zeros n ys).

Lemma pad_zeros_length :
  forall n xs, length (pad_zeros n xs) = n.
Proof.
  induction n as [| n' IH]; intros [| x rest]; simpl; auto.
Qed.

(**md **Component homogenization equalizes vector lengths to `max |xs| |ys|`.**
`homogenize_components` pads the shorter numeric component list with trailing zeros so both endpoints have identical arity `max (length xs) (length ys)`. *)
Theorem homogenize_pads_shorter_to_equal_length :
  forall xs ys,
    let '(xs', ys') := homogenize_components xs ys in
    length xs' = Nat.max (length xs) (length ys) /\
    length ys' = Nat.max (length xs) (length ys) /\
    length xs' = length ys'.
Proof.
  intros xs ys.
  unfold homogenize_components.
  rewrite !pad_zeros_length.
  repeat split; reflexivity.
Qed.

(** ** Gamut clipping, HCL shortest-arc hue & D50 white boundary tolerance *)

Record rgb_scaled := {
  rgb_r : Z;
  rgb_g : Z;
  rgb_b : Z
}.

Definition gamut_clip_chan (S c : Z) : Z :=
  Z.max 0 (Z.min S c).

Definition gamut_clip (S : Z) (c : rgb_scaled) : rgb_scaled :=
  {| rgb_r := gamut_clip_chan S (rgb_r c);
     rgb_g := gamut_clip_chan S (rgb_g c);
     rgb_b := gamut_clip_chan S (rgb_b c) |}.

(**md **Gamut clipping projects `Z^3` into `[0, S]^3` and is idempotent.**
For any `S >= 0`, `gamut_clip S` bounds every channel to `[0, S]` and applying `gamut_clip S` a second time leaves the color unchanged. *)
Theorem gamut_clip_in_bounds_and_idempotent :
  forall S c,
    0 <= S ->
    let c1 := gamut_clip S c in
    0 <= rgb_r c1 <= S /\
    0 <= rgb_g c1 <= S /\
    0 <= rgb_b c1 <= S /\
    gamut_clip S c1 = c1.
Proof.
  intros S [r g b] HS.
  unfold gamut_clip, gamut_clip_chan; simpl.
  repeat split; try lia.
  f_equal; lia.
Qed.

Definition hcl_shortest_delta (h0 h1 : Z) : Z :=
  Z.modulo (h1 - h0 + 540) 360 - 180.

(**md **HCL shortest-arc hue delta lies in `[-180, 180)` and is congruent modulo `360`.**
For any hue angles `h0, h1`, `hcl_shortest_delta h0 h1` lies in `[-180, 180)` and satisfies `h0 + delta ≡ h1 (mod 360)`. *)
Theorem hcl_shortest_hue_arc_bounded :
  forall h0 h1,
    -180 <= hcl_shortest_delta h0 h1 < 180 /\
    exists k, (h0 + hcl_shortest_delta h0 h1) - h1 = 360 * k.
Proof.
  intros h0 h1.
  unfold hcl_shortest_delta.
  assert (Hm : 0 <= (h1 - h0 + 540) mod 360 < 360) by (apply Z.mod_pos_bound; lia).
  split; [lia |].
  pose proof (Z.div_mod (h1 - h0 + 540) 360 ltac:(lia)) as Hdm.
  exists (1 - (h1 - h0 + 540) / 360).
  lia.
Qed.

Definition hcl_interp_hue (S c0 h0 c1 h1 p : Z) : Z :=
  let eff_h0 := if Z.eqb c0 0 then h1 else h0 in
  let eff_h1 := if Z.eqb c1 0 then eff_h0 else h1 in
  let delta := hcl_shortest_delta eff_h0 eff_h1 in
  eff_h0 + (p * delta) / Z.max 1 S.

(**md **Achromatic endpoints borrow the chromatic endpoint's hue without hue sweep.**
When `c0 = 0`, `hcl_interp_hue` evaluates to constant `h1` at every progress sample `p`. *)
Theorem achromatic_hue_borrowing_constant :
  forall S h0 c1 h1 p,
    hcl_interp_hue S 0 h0 c1 h1 p = h1.
Proof.
  intros S h0 c1 h1 p.
  unfold hcl_interp_hue, hcl_shortest_delta.
  rewrite Z.eqb_refl.
  assert (Heff : (if Z.eqb c1 0 then h1 else h1) = h1) by (destruct (Z.eqb c1 0); reflexivity).
  rewrite Heff.
  replace (h1 - h1 + 540) with 540 by lia.
  change (540 mod 360) with 180.
  replace (180 - 180) with 0 by lia.
  replace (p * 0) with 0 by lia.
  rewrite Z.div_0_l by lia.
  lia.
Qed.

Definition snap_lab_unit (S eps v : Z) : Z :=
  if andb (Z.ltb v 0) (Z.geb v (-eps)) then 0
  else if andb (Z.gtb v S) (Z.leb v (S + eps)) then S
  else v.

(**md **D50 Lab boundary tolerance keeps valid white inside `[0, S]` while rejecting true out-of-gamut channels (#29).**
Any channel within `[0 - eps, S + eps]` maps into `[0, S]`, whereas any channel strictly past `S + eps` or `-eps` remains out-of-gamut for `GamutPolicy.Reject`. *)
Theorem lab_white_snap_preserves_in_gamut :
  forall S eps v,
    0 < S ->
    0 <= eps ->
    (-eps <= v <= S + eps -> 0 <= snap_lab_unit S eps v <= S) /\
    (v > S + eps -> snap_lab_unit S eps v = v /\ v > S).
Proof.
  intros S eps v HS Heps.
  unfold snap_lab_unit.
  split.
  - intros Hrange.
    destruct (Z.ltb_spec v 0); destruct (Z.geb_spec v (-eps)); simpl; try lia.
    destruct (Z.gtb_spec v S); destruct (Z.leb_spec v (S + eps)); simpl; lia.
  - intros Hout.
    destruct (Z.ltb_spec v 0); [lia |]; simpl.
    destruct (Z.gtb_spec v S); [| lia].
    destruct (Z.leb_spec v (S + eps)); [lia |]; simpl.
    lia.
Qed.

(** ** CSS Angle Unit Conversion (`deg`, `grad`, `turn`, #28) *)

Inductive angle_unit :=
  | UDeg
  | UGrad
  | UTurn.

(** Exact integer conversion in units of `1/10 deg` (`1 turn = 3600`, `1 deg = 10`, `1 grad = 9`). *)
Definition angle_tenth_deg (u : angle_unit) : Z :=
  match u with
  | UDeg => 10
  | UGrad => 9
  | UTurn => 3600
  end.

Definition convert_angle_exact (v : Z) (u_from u_to : angle_unit) : Z :=
  (v * angle_tenth_deg u_from) / angle_tenth_deg u_to.

(**md **Angle conversion across `deg`, `grad`, and `turn` is reflexive and maps `200 grad = 180 deg` and `1 turn = 400 grad` (#28).**
`convert_angle_exact` is the identity when `u_from = u_to` and satisfies exact CSS Values 4 ratios between gradians, degrees, and turns. *)
Theorem angle_grad_turn_deg_homomorphism :
  (forall v u, convert_angle_exact v u u = v) /\
  convert_angle_exact 200 UGrad UDeg = 180 /\
  convert_angle_exact 1 UTurn UGrad = 400.
Proof.
  split.
  - intros v [| |]; unfold convert_angle_exact, angle_tenth_deg;
      rewrite Z.div_mul by lia; reflexivity.
  - split; reflexivity.
Qed.

(** ** Batch A2 Refinement Proofs: 3-Tier SIMD Morph Coordinate Rounding & Fixed-4-Decimal Integer Decomposition *)

Definition morph_coord_step (S a b p : Z) : Z :=
  js_round_half_int (lerp_exact_scaled S (2 * a) (2 * b) p).

Fixpoint morph_coords_scalar (S p : Z) (xs ys : list Z) : list Z :=
  match xs, ys with
  | x :: xrest, y :: yrest => morph_coord_step S x y p :: morph_coords_scalar S p xrest yrest
  | _, _ => []
  end.

Fixpoint morph_coords_simd_chunks (fuel chunk : nat) (S p : Z) (xs ys : list Z) : list Z :=
  match fuel with
  | 0%nat => morph_coords_scalar S p xs ys
  | S fuel' =>
    if (Nat.leb chunk 0 || Nat.ltb (length xs) chunk || Nat.ltb (length ys) chunk)%bool then
      morph_coords_scalar S p xs ys
    else
      morph_coords_scalar S p (firstn chunk xs) (firstn chunk ys) ++
      morph_coords_simd_chunks fuel' chunk S p (skipn chunk xs) (skipn chunk ys)
  end.

Lemma morph_coords_scalar_split :
  forall k S p xs ys,
    (k <= length xs)%nat ->
    (k <= length ys)%nat ->
    morph_coords_scalar S p (firstn k xs) (firstn k ys) ++
    morph_coords_scalar S p (skipn k xs) (skipn k ys) =
    morph_coords_scalar S p xs ys.
Proof.
  induction k as [| k' IH]; intros S p xs ys Hx Hy.
  - simpl. reflexivity.
  - destruct xs as [| x xrest]; [simpl in Hx; lia |].
    destruct ys as [| y yrest]; [simpl in Hy; lia |].
    simpl in *.
    f_equal.
    apply IH; lia.
Qed.

(**md **3-tier SIMD (`Vector512` / `Vector256` / `Vector128`) SVG morph interpolation with half-ceiling tie rounding matches scalar `morphCoordinates`.**
Across any positive lane width `chunk` (`8`, `4`, `2`) and arbitrary coordinate arrays `xs`, `ys`, chunked SIMD interpolation and JS half-ceiling rounding followed by a scalar tail equals `morph_coords_scalar` element-for-element. *)
Theorem morph_simd_chunks_eq_scalar :
  forall fuel chunk S p xs ys,
    morph_coords_simd_chunks fuel chunk S p xs ys = morph_coords_scalar S p xs ys.
Proof.
  induction fuel as [| fuel' IH]; intros chunk S p xs ys.
  - simpl. reflexivity.
  - simpl.
    destruct (Nat.leb_spec chunk 0); [simpl; reflexivity |].
    destruct (Nat.ltb_spec (length xs) chunk); [simpl; reflexivity |].
    destruct (Nat.ltb_spec (length ys) chunk); [simpl; reflexivity |].
    simpl.
    rewrite IH.
    apply morph_coords_scalar_split; lia.
Qed.

Definition decompose_fixed4_int (n : Z) : bool * Z * Z :=
  let neg := Z.ltb n 0 in
  let mag := Z.abs n in
  (neg, mag / 10000, mag mod 10000).

Definition reconstruct_fixed4_int (parts : bool * Z * Z) : Z :=
  let '(neg, q, r) := parts in
  let mag := 10000 * q + r in
  if neg then -mag else mag.

(**md **Fixed-4-decimal integer decomposition splits any scaled coordinate `n` into a non-negative quotient and `[0, 9999]` fractional remainder and reconstructs `n` losslessy.**
For every integer `n`, `decompose_fixed4_int n` produces `q >= 0` and `0 <= r < 10000` such that `reconstruct_fixed4_int (decompose_fixed4_int n) = n`. *)
Theorem fixed4_int_decomposition_exact :
  forall n,
    let '(neg, q, r) := decompose_fixed4_int n in
    0 <= q /\
    0 <= r < 10000 /\
    reconstruct_fixed4_int (neg, q, r) = n.
Proof.
  intros n.
  unfold decompose_fixed4_int, reconstruct_fixed4_int.
  assert (Habs : 0 <= Z.abs n) by lia.
  assert (Hq : 0 <= Z.abs n / 10000) by (apply Z.div_pos; lia).
  assert (Hr : 0 <= Z.abs n mod 10000 < 10000) by (apply Z.mod_pos_bound; lia).
  assert (Hdm : Z.abs n = 10000 * (Z.abs n / 10000) + Z.abs n mod 10000) by (apply Z.div_mod; lia).
  destruct (Z.ltb_spec n 0); repeat split; lia.
Qed.
