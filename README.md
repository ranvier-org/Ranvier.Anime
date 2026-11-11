# Ranvier.Anime

Animation for [Ranvier](https://github.com/ranvier-org/Ranvier).

| Package | Holds |
| --- | --- |
| `Fable.Ranvier.Anime` | Typed [anime.js](https://animejs.com) 4.5.0 bindings for Fable, with owners and signals from the Ranvier core. |

Motion parts for `Ranvier.View` templates, `Ranvier.View.Anime`, live in
[ranvier-org/Ranvier.View](https://github.com/ranvier-org/Ranvier.View).

Documentation: <https://ranvier-org.github.io/Ranvier.Anime/>

## Layout

- `src/Fable.Ranvier.Anime`: the bindings, `Reactive` and `Testing`.
- `tests/Fable.Ranvier.Anime.Tests`: the bindings compiled with Fable and run under jsdom and Chromium.
- `docs`: the documentation site.

## Development

```shell
dotnet fsi build.fsx -- tools
dotnet fsi build.fsx -- test                    # every check
dotnet fsi build.fsx -- test --group bindings   # Fable.Ranvier.Anime under jsdom and Chromium
dotnet fsi build.fsx -- ci                      # every CI gate, then pack
```

See [CONTRIBUTOR.md](CONTRIBUTOR.md) for local Ranvier references, formatting and releases.

## License

MIT
