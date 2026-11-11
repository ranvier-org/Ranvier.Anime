# Ranvier.Anime

Animation for [Ranvier](https://github.com/ranvier-org/Ranvier).

| Package | Holds |
| --- | --- |
| `Ranvier.Anime` | A .NET port of [anime.js](https://animejs.com) 4.5.0 for Ranvier hosts, reflection-free and NativeAOT-compatible. |
| `Fable.Ranvier.Anime` | The same API in the browser over the anime.js npm package, plus the typed anime.js bindings in namespace `Fable.Ranvier.Anime`. |

Both packages share one portable surface in namespace `Ranvier.Anime`: code written against it compiles unchanged for
either engine. [`tests/Portable/Surface.fs`](tests/Portable/Surface.fs) is that surface as a compiled contract, and
[the specification](docs/superpowers/specs/2026-11-11-portable-surface.md) describes it.

Motion parts for `Ranvier.View` templates, `Ranvier.View.Anime`, live in
[ranvier-org/Ranvier.View](https://github.com/ranvier-org/Ranvier.View).

Documentation: <https://ranvier-org.github.io/Ranvier.Anime/>

## Layout

- `src/Ranvier.Anime`: the native port.
- `src/Fable.Ranvier.Anime`: the bindings, `Reactive` and `Testing`, and the portable surface over them (`Portable/`).
- `tests/Ranvier.Anime.Tests`: the port's Expecto suite, checked against reference traces from the anime.js source.
- `tests/Fable.Ranvier.Anime.Tests`: the Fable package compiled with Fable and run under jsdom and Chromium.
- `tests/Portable`: the contract both suites compile.
- `tests/Ranvier.Anime.NativeSmoke`, `tests/Ranvier.Anime.CssAotProbe`: win-x64 host and NativeAOT probes, run with
  `build.native.fsx` (see [native integration](docs/native-integration.md)).
- `docs`: the documentation site.

## Development

```shell
dotnet fsi build.fsx -- tools
dotnet fsi build.fsx -- test                    # every check
dotnet fsi build.fsx -- test --group native     # Ranvier.Anime on .NET
dotnet fsi build.fsx -- test --group bindings   # Fable.Ranvier.Anime under jsdom and Chromium
dotnet fsi build.fsx -- ci                      # every CI gate, then pack
```

See [CONTRIBUTOR.md](CONTRIBUTOR.md) for local Ranvier references, formatting and releases.

## License

MIT
