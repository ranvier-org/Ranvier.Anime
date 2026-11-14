# Contributing to Ranvier.Anime

## Commit messages

Use [Conventional Commits](https://www.conventionalcommits.org/en/v1.0.0/) for commit messages and pull request titles:

```text
type(scope): summary
```

The scope is optional and must use lowercase letters, digits, `.`, `_`, `/` or `-`.
Supported contribution types are `build`, `bench`, `chore`, `ci`, `docs`, `feat`,
`fix`, `perf`, `refactor`, `revert`, `style` and `test`.

Mark breaking changes with `!` before the colon, or a `BREAKING CHANGE:` footer.
Explain the migration in the commit body or pull request description.

The **Contribution / Conventional commits** check validates human pull request
titles. Use squash merging and preserve the conventional PR title as the squash
commit subject.

## Before opening a pull request

Restore the pinned tools with `dotnet fsi build.fsx -- tools`, then run `dotnet fsi build.fsx -- test` for every
check, or `test --group native`, `bindings` or `workflows` for one CI shard. `dotnet fsi build.fsx -- ci` runs every CI gate
and packs the libraries. `--quick` skips npm installs, Chromium installation and restores after the first run.

Use .NET 10 (pinned by `global.json`), the .NET 8 runtime, Node.js 26 or later and Python 3.

| Group | Checks |
| --- | --- |
| `native` | `tests/Ranvier.Anime.Tests`: the native port's Expecto suite, including the portable surface contract |
| `bindings` | `tests/Fable.Ranvier.Anime.Tests`: the bindings compiled with Fable under jsdom and a mocked clock, their emitted JavaScript, the CSS grammar type errors, the bundle budget and typed CSS animation in Chromium |
| `workflows` | the contribution check's tests and the version ownership check with its tests |

For changes spanning this repository and Ranvier, use
`dotnet fsi build.fsx -- dev local [--ranvier-root <path>] [--with-css [--ranvier-css-root <path>]]`. This clones a
missing sibling checkout and enables project references through the ignored `Local.Build.props`. `dev packages`
returns to pinned package references. Commit and submit each repository's changes separately. CI uses packages.

Use the build CLI's `format` command to apply Fantomas formatting to tracked F# sources, and `format --dry-format`
to check it.

## Releases

[EasyBuild.ShipIt](https://github.com/easybuild-org/EasyBuild.ShipIt) owns every package version, calculated from
conventional commits. CI's `Version ownership` job, part of the required `CI result`, fails a pull request that
changes a `<Version>` or uses a release title, unless it is ShipIt's release pull request. Do not bump a version or
write a `chore: release` pull request by hand.

Each packaged project has a `regex` entry under `updaters` in a `CHANGELOG.md`, pointing to exactly one literal
`<Version>` in that project, and the latest heading in that changelog matches that version. Use `regex`, not `xml`:
the `xml` updater reformats the whole project file.

| You want | Do this |
| --- | --- |
| A new release | Merge a `feat`, `perf` or `fix` commit touching the project's folder, then merge the release pull request. |
| A release after a dependency pin change | Title the change `fix(deps): ...`. Commits typed `chore`, `build` or `ci` release nothing. |
| A prerelease line | Add `pre_release: <channel>` to the project's changelog; remove it to return to stable versions. |
| A new base version in a prerelease line | Set `force_version` in the changelog for one run. |

| Changelog | Projects | Channel | Published |
| --- | --- | --- | --- |
| `src/Fable.Ranvier.Anime` | `Fable.Ranvier.Anime`, `Fable.Ranvier.Anime.Traced` | stable | yes |
| `src/Ranvier.Anime` | `Ranvier.Anime`, `Ranvier.Anime.Traced` | stable | yes |

After CI succeeds on a push to `main`, the ShipIt workflow opens or updates a release PR and starts CI on its
branch. Merge it using its `chore: release ...` title. CI validates and packs that release commit, then the publish
workflow uploads those exact CI artifacts to NuGet using trusted publishing. The publish workflow pushes exactly
`Fable.Ranvier.Anime`, `Ranvier.Anime` and their `.Traced` packages and fails on any other package; a new published package
needs its name added there. Pull request runs on the release branch stay at `action_required`, because GitHub
holds runs started by `GITHUB_TOKEN`; the started CI run reports the required check on the same commit.

`publish --dry-publish` builds, tests and packs locally without uploading anything.

### Maintainer setup

1. Enable squash merging and allow GitHub Actions to create and approve pull requests in repository Actions
   settings. Require the `CI result` check in branch protection.
2. Set the Actions variable `NUGET_USER` to your NuGet.org username, or rely on the workflow's default. Create a
   NuGet trusted publishing policy for `ranvier-org/Ranvier.Anime` and the workflow filename `publish.yml`.
3. Enable GitHub Pages with GitHub Actions as the source for the docs workflow.

### Imported release baseline

`Fable.Ranvier.Anime` moved here from ranvier-org/Ranvier.View at version `0.1.44`, the latest version on NuGet, where it shared a version with
`Ranvier.View.Anime`. NuGet holds earlier versions published from that repository. Do not reset the version or
attempt to overwrite an existing NuGet version.

When a release requires a newer core, test against a sibling checkout in local-reference mode, publish the core,
update `RanvierVersion` in `Directory.Build.props`, which holds every sibling package version, then run every gate in package mode. `Ranvier.View.Anime` in
ranvier-org/Ranvier.View consumes `Fable.Ranvier.Anime` from this repository: release the bindings before a view
release that needs them.
