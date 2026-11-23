---
last_commit_released: 8dadb22b73a065c314bcf38c4cabc498e382e946
updaters:
  - regex:
      file: Ranvier.Anime.fsproj
      pattern: (?<=<Version>)[^<]+(?=</Version>)
---

# Changelog

This changelog is generated from conventional commits by [EasyBuild.ShipIt](https://github.com/easybuild-org/EasyBuild.ShipIt).

## 0.3.0 - 2026-11-23

### 🐞 Bug Fixes

* *(anime)* Resolve pass-2 proof-lifted correctness bugs (#37-#41) and expand benchmarks (#42) ([b7fd6a5](https://github.com/ranvier-org/Ranvier.Anime/commit/b7fd6a5f687a993018f05e767cfd20fe32fd1247))
* *(fable)* Use traced Ranvier.Css.Core dependency and align sibling cohort ([8dadb22](https://github.com/ranvier-org/Ranvier.Anime/commit/8dadb22b73a065c314bcf38c4cabc498e382e946))
* *(proofs)* Add Rocq 9.1 proof suite, Navigator site, differential harness, and resolve #24-#29 (#30) ([890ea40](https://github.com/ranvier-org/Ranvier.Anime/commit/890ea40da51899aafa924a4b6aa5d413ddea0276))

### ⚡ Performance Improvements

* *(anime)* Lazy PlaybackSample, scalar keyframe/animatable SIMD, grid3d SIMD, and zero-alloc Timeline seek (#43) ([1c20e5b](https://github.com/ranvier-org/Ranvier.Anime/commit/1c20e5b06b754fbdfcd34e72be88c26a94cdc69b))
* *(anime)* Zero-alloc color/complex rendering, span parsers & WAAPI linear stops (#44) ([7352f76](https://github.com/ranvier-org/Ranvier.Anime/commit/7352f76557da7c97706f3687b58a71d0fcb2bb12))
* *(anime)* O(1) indexed AutoLayout, fast empty-style snapshots & View/Scroll polish (#45) ([74a181b](https://github.com/ranvier-org/Ranvier.Anime/commit/74a181bdad78261c3f6719369bd63149d4b606a7))
* *(composition)* Coalesce additive blend flush with 3-tier SIMD and zero-alloc timeline ticks (#32) ([ad3efaa](https://github.com/ranvier-org/Ranvier.Anime/commit/ad3efaac0bdd70f99c72f07160cf491633501d73))
* *(layout)* Add zero-alloc FLIP snapshots, char-buffer TextScramble, and 3-tier SIMD snapTo (#36) ([8c39895](https://github.com/ranvier-org/Ranvier.Anime/commit/8c398953f34112fe1c567092058117abc5ff058f))
* *(simd)* Vectorize SVG morph, RGBA/HCL color, 3D stagger, and dyadic LUT easing (#33) ([31ce2e8](https://github.com/ranvier-org/Ranvier.Anime/commit/31ce2e848eeee1bad0ffb2a68723450f5bef96d8))
* *(tracks)* Add SoA scalar SIMD batching, numeric transform sentinels, and SearchValues CSS scanner (#34) ([9fba274](https://github.com/ranvier-org/Ranvier.Anime/commit/9fba2740e8b12195e957e0b72d4766fd1b144d33))

<strong><small>[View changes on Github](https://github.com/ranvier-org/Ranvier.Anime/compare/45c35a8ea4bc4aa0073df21294bc4ccc1a3fe972..8dadb22b73a065c314bcf38c4cabc498e382e946)</small></strong>

## 0.2.0 - 2026-11-18

### 🚀 Features

* *(anime)* Add native motion authoring and playback improvements (#23) ([45c35a8](https://github.com/ranvier-org/Ranvier.Anime/commit/45c35a8ea4bc4aa0073df21294bc4ccc1a3fe972))

### ⚡ Performance Improvements

* *(anime)* Remove tick init guards and ThreadLocal option allocations (#20) ([5e44e0a](https://github.com/ranvier-org/Ranvier.Anime/commit/5e44e0a46ac1cc57d77555ced8a4d2a700042989))

<strong><small>[View changes on Github](https://github.com/ranvier-org/Ranvier.Anime/compare/a067d3dfac4e501d5f1719d6bf8866862268071d..45c35a8ea4bc4aa0073df21294bc4ccc1a3fe972)</small></strong>

## 0.1.0 - 2026-11-14

### 🐞 Bug Fixes

* *(deps)* Build against Ranvier 0.2.0, the stable Dom and Css cohort and the View DSL, and move both packages to the stable channel (#14) ([a067d3d](https://github.com/ranvier-org/Ranvier.Anime/commit/a067d3dfac4e501d5f1719d6bf8866862268071d))

<strong><small>[View changes on Github](https://github.com/ranvier-org/Ranvier.Anime/compare/18fe47a890f2a829db867321cbcb3fc710785bd6..a067d3dfac4e501d5f1719d6bf8866862268071d)</small></strong>

## 0.1.0-rc.2 - 2026-11-12

### 🚀 Features

* *(native)* Expose pure sRGB Lab and HCL color interpolation (#8) ([18fe47a](https://github.com/ranvier-org/Ranvier.Anime/commit/18fe47a890f2a829db867321cbcb3fc710785bd6))

<strong><small>[View changes on Github](https://github.com/ranvier-org/Ranvier.Anime/compare/25b7a02ee23ef59f56d429eedf468dbc3fd411c6..18fe47a890f2a829db867321cbcb3fc710785bd6)</small></strong>

## 0.1.0-rc.1 - 2026-11-11

### 🚀 Features

* *(anime)* The portable surface on the native engine ([74c5ab2](https://github.com/ranvier-org/Ranvier.Anime/commit/74c5ab2dbf384d59ce4c6f3316795aa5eeddf30e))
* *(view-anime)* Motion is one source over the portable surface ([fb05e26](https://github.com/ranvier-org/Ranvier.Anime/commit/fb05e26b1cc778474e3709cf1530f1f380e32e21))

<strong><small>[View changes on Github](https://github.com/ranvier-org/Ranvier.Anime/compare/b42f59bb0bc055cc22b1803bc80c678a84e01f9b..25b7a02ee23ef59f56d429eedf468dbc3fd411c6)</small></strong>

## 0.1.0-rc.0

The port before its first release. See the repository history for its development.
