---
title: Introduction
order: 1
---

## Installation

```bash frame="terminal"
dotnet add package Fable.Ranvier.Anime
npm install animejs@4.5.0
```

`Fable.Ranvier.Anime` holds typed [anime.js](https://animejs.com) 4.5.0 bindings for Fable, with owners and
signals from the [Ranvier core](https://ranvier-org.github.io/Ranvier/). It supports .NET 8, .NET 10 and Fable;
the bindings run under Fable, and .NET builds reference them from portable code. It is a pre-1.0 release; the API
can change before 1.0.

The package depends on `Xantham.Fable.Core.TS`, which restores from the public
[Xantham package feed](https://nuget.cloudsmith.io/shayanhabibi/xantham/v3/index.json). Add it to the application's
`nuget.config`:

```xml
<packageSources>
  <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  <add key="xantham" value="https://nuget.cloudsmith.io/shayanhabibi/xantham/v3/index.json" />
</packageSources>
```

`Fable.Ranvier.Anime.Traced` is the same package built over `Ranvier.Traced`, for development builds that record
the graph's event log.

## Where to go next

- [Animation](../anime/bindings.md): parameters, values, eases, staggers and timelines.
- [Owning and driving animations](../anime/reactive.md): owners, playback memos, signals and reduced motion.
- [Testing and costs](../anime/testing.md): a manual clock under jsdom and Playwright, and the bundle size.

Motion parts for `Ranvier.View` templates are in `Ranvier.View.Anime`, documented with
[Ranvier.View](https://ranvier-org.github.io/Ranvier.View/view/motion-parts/).

## Contributing

Clone [ranvier-org/Ranvier.Anime](https://github.com/ranvier-org/Ranvier.Anime) and install .NET 10, the .NET 8
runtime and Node.js 26 or later. The same Partas.Build workflows run locally and in CI:

```bash frame="terminal"
dotnet fsi build.fsx -- tools
dotnet fsi build.fsx -- test
```

See [CONTRIBUTOR.md](https://github.com/ranvier-org/Ranvier.Anime/blob/main/CONTRIBUTOR.md) for the full workflow
and release requirements.
