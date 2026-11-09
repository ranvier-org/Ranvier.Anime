# Fable.Ranvier.Anime

Typed [anime.js](https://animejs.com) 4.5 bindings for Fable, with Ranvier integration. Install the
JavaScript package beside it:

```shell
npm install animejs@4.5.0
```

Parameters are constructors with named optional arguments. Each emits an object literal holding only
the arguments given, and every function is imported from `animejs` on its own, so a bundle carries
only the anime features it calls.

```fsharp
open Ranvier.Anime

let card (el: Fable.Core.TS.Dom.Element) =
    Anime.animate (el, Anim(x = 100, opacity = Value.fromTo (0, 1), duration = 400, ease = Ease.outQuart))
// animate(el, { x: 100, opacity: [0, 1], duration: 400, ease: "outQuart" })
```

`Ranvier.Anime.Reactive` ties anime objects to Ranvier's ownership tree and signals:

- `own` reverts an animation, timeline, draggable or layout when the current owner is disposed.
- `track` publishes a tickable's progress, running and completed state as memos.
- `scrub` seeks a tickable to a signal's progress; `drive` animates an animatable property to a signal's value.
- `policy` is a signal of the reduced-motion preference.

`Ranvier.Anime.Testing` steps anime's engine by hand: mock `Date` before `animejs` is imported, call
`Testing.manual ()`, then advance the mocked time and call `Testing.step ()`.

The assembly also compiles on .NET, so portable code can reference it; the bindings run only under Fable.

## Typed CSS custom properties

The typed bridge is available in `Fable.Ranvier.Anime` and `Ranvier.View.Anime` `0.1.1-alpha.1`;
their runtime dependency supplies `Ranvier.Css` `0.1.1-alpha.1`. Generated sugars remain in
`Ranvier.Css.Bindings` `0.1.1-alpha.1`.

Add `Ranvier.Css.Bindings` for the generated `Css` sugars. Custom-property handles and numeric
values retain the same grammar marker through Anime parameters:

```fsharp
open Ranvier.Css
open Ranvier.Anime

let gap = Css.lengthVariable "--gap"

let animateGap (el: Fable.Core.TS.Dom.Element) =
    Anime.animate(el, Anim(duration = 300).With(gap, Css.px 0., Css.px 100.))

let pulseGap (el: Fable.Core.TS.Dom.Element) =
    Anime.animate(el, Anim(duration = 600).With(gap, [| Css.px 0.; Css.px 100.; Css.px 20. |]))
```

`Anim.With(variable, value)` supplies a target; its existing CSS value must use the target's
unit. Explicit endpoints and keyframes must have matching unit suffixes. A length handle rejects
angles at compile time; mixed units and compound endpoints raise `ArgumentException` before
changing the parameters. Use numeric literals as endpoints and consume the animated variable in
CSS expressions such as `Css.width (Css.variable gap * Css.number 2. + Css.px 10.)`.

`Reactive.driveCss read element variable (AnimatableProp(duration = 300.))` sets the first reader
value immediately and animates later changes through Anime's animatable object. The reader must
keep its unit suffix. Disposing its owner stops animation and restores the original inline value
and priority. Frames update the CSS variable directly; they do not write signals.

In views, use `Motion.followCss variable parameters read` as an element attribute, alongside
`styles` and user classes. It uses the existing Motion exit lifecycle. The
[complete component example](../../docs/samples/css-anime/Example.fs) combines a reactive signal,
typed expressions, an automatic style class and a user class. Extract and serve its CSS with the
normal CSS build pipeline.

The example project is `docs/samples/css-anime/Ranvier.Docs.CssAnime.fsproj`; pass it to the CSS
compiler for either CSS modules or a single stylesheet. Its static width remains
`calc(var(--card-gap, 0px) * 2 + 10px)` while each component instance owns its variable animation.

These helpers use Anime's JavaScript engine. They accept finite numeric tokens with generated
metadata. CSS keywords, colors, `var()` and `calc()` endpoints remain separate interpolation
concerns; arbitrary valid CSS does not imply numeric interpolation. Animate a numeric variable
used by the expression instead. WAAPI's custom-property registration is not provided by this bridge.
