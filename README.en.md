[日本語](README.md) | English

# Music Tag Auditor

A Windows desktop application (WPF) for auditing and editing tags in a classical music library.

It inspects and corrects tags according to the principles laid out in [docs/TAGGING_POLICY.md](docs/TAGGING_POLICY.md).

**Core principle**: although this is a bulk-processing tool, **a human must always be able to review the diff before it is applied**. The tool never fills in a field it cannot be confident about under the policy.

Main features:

- Recursively scans a folder and reads tags from M4A / FLAC / MP3 / AIFF files
- Detects problems with 24 inspection rules, each producing a proposed fix and the evidence behind it
- **Automatically snapshots tags immediately before applying**, then reads every written field back and verifies it
- Manual cell editing, bulk entry per folder, and dictionary-backed input suggestions
- An editable, validated normalization dictionary (composers / people / ensembles / typos / protected values)
- CSV export of inspection results, and backups that ship with a PowerShell restore script usable without the app

> **Note**: the detailed documentation under `docs/` is written in Japanese. This file covers building, testing, and licensing only.

---

## Tech stack

| Item | Details |
|---|---|
| Runtime | .NET 10 (LTS; GA 2025-11-11, supported until 2028-11-14) |
| UI | WPF (`net10.0-windows`) with MVVM |
| MVVM | CommunityToolkit.Mvvm 8.4.2 |
| DI | Microsoft.Extensions.DependencyInjection 10.0.10 |
| Tag I/O | TagLibSharp 2.3.0 plus a purpose-built MP4 atom reader ([ADR-0001](docs/adr/0001-tag-io-library.md)) |
| Logging | Serilog 4.4.0 with Serilog.Sinks.File 7.0.0 |
| Tests | xUnit 2.9.3 with coverlet.collector 6.0.4 |

The UI shares its design tokens with the sibling project [MusicFolderTimeFitter](https://github.com/tkinugaw-prv/MusicFolderTimeFitter).

---

## Build and run

Requirements: .NET 10 SDK on Windows 11. The solution contains a WPF project, so it can only be built on Windows.

```bash
dotnet build
```

```bash
dotnet run --project src/MusicTagAuditor.App/MusicTagAuditor.App.csproj
```

Pass a library path as the first argument to open and scan that folder on startup, overriding the remembered library:

```bash
dotnet run --project src/MusicTagAuditor.App/MusicTagAuditor.App.csproj -- "D:\Music\Classic"
```

Logs are written daily to `%LOCALAPPDATA%\MusicTagAuditor\logs\`. The dictionary lives at `%APPDATA%\MusicTagAuditor\dictionary.json` and settings at `settings.json` in the same folder.

---

## Downloads

Single-file Windows x64 executables are published on the [Releases](https://github.com/tkinugaw-prv/MusicTagAuditor/releases) page.

| File | Kind | Requirements |
|---|---|---|
| `MusicTagAuditor-<tag>-win-x64.exe` | Self-contained (runtime bundled) | None (Windows x64) |
| `MusicTagAuditor-<tag>-win-x64-fdd.exe` | Framework-dependent | .NET 10 Desktop Runtime |

### Publishing locally

```powershell
dotnet publish src/MusicTagAuditor.App -p:PublishProfile=win-x64-self-contained
```

Output goes to `src/MusicTagAuditor.App/bin/publish/win-x64-self-contained/`. The profiles live in `src/MusicTagAuditor.App/Properties/PublishProfiles/`.

Both profiles enable `IncludeAllContentForSelfExtract`. Without it, `Assembly.Location` returns an empty string for assemblies embedded in the single-file bundle, which means **backups can no longer bundle the `TagLibSharp.dll` that the standalone restore script needs** — restoring without the app would stop working.

### Cutting a release

Pushing a tag that starts with `v` triggers the [release workflow](.github/workflows/release.yml): it runs the tests, publishes both configurations, and creates a GitHub Release with the executables attached. The version is taken from the tag name (e.g. `v1.2.3` becomes `1.2.3`).

```powershell
git tag v1.0.0
git push origin v1.0.0
```

---

## Tests and coverage reports

Test results and coverage reports are generated on every run by GitHub Actions ([CI workflow](.github/workflows/ci.yml)).

| Where | What |
|---|---|
| Run **summary page** | Coverage summary table (no login required, never expires) |
| Run **artifact** `test-results` | Raw test results (TRX) |
| Run **artifact** `coverage-report` | HTML report generated from the raw coverage data (Cobertura XML) |

Anyone can verify the pass/fail status and per-class coverage from there.

### Reproducing locally

```powershell
# 1. Run tests with a TRX log and coverage collection (.NET 10 SDK required)
dotnet test --logger "trx" --collect:"XPlat Code Coverage" --results-directory "reports/raw"
```

```powershell
# 2. Install ReportGenerator (first time only)
dotnet tool install --global dotnet-reportgenerator-globaltool
```

```powershell
# 3. Generate the HTML report (reports/ is not tracked by git)
reportgenerator "-reports:reports/raw/*/coverage.cobertura.xml" "-targetdir:reports/coverage/html" "-reporttypes:Html;TextSummary"
```

Do not widen the glob in step 3 to `**/`. The TRX logger also copies `coverage.cobertura.xml` into its attachment directory, so `**/` would read the same report twice.

`reports/` is deliberately kept out of git: TRX files embed the local user and machine name, and Cobertura XML embeds absolute source paths.

### Scope of the tests

| Project | Covers |
|---|---|
| `MusicTagAuditor.Core.Tests` | The whole domain: normalization, dictionary, inspection rules, backup, apply, CSV export, settings |
| `MusicTagAuditor.TagIo.Tests` | Tag read/write round-trips and the MP4 atom reader |
| `MusicTagAuditor.App.Tests` | ViewModels, exercising a real WPF `ListCollectionView` |

Views, XAML, and themes are **out of scope** for unit testing and are verified manually. Since the overall line-coverage figure still counts those layers in its denominator, read the per-layer numbers from the run summary page rather than the total.

The **11 integration tests marked `[RealLibraryFact]` require an actual music library** and are skipped unless `MUSICTAGAUDITOR_LIBRARY_ROOT` is set. CI always skips them, so the test count visible in a run is 11 lower than a local run configured with a real library.

---

## Environment variables

| Name | Purpose | Default |
|---|---|---|
| `MUSICTAGAUDITOR_LIBRARY_ROOT` | Path to the real library used by integration tests. **Test-only**; the application itself never reads it. If unset, or if the folder does not exist, the affected tests are skipped | None (there is no default) |

---

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md). `develop` is the default branch; pushing directly to `main` or `develop` is not allowed. Branch from `develop` using a `feature/` or `fix/` prefix and open a pull request.

---

## License

MIT License — see [LICENSE](LICENSE).

Copyright notices and full license texts for every third-party component shipped in the binaries are collected in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md), which is also attached to each Release.

The [TagLibSharp](https://github.com/mono/taglib-sharp) dependency is LGPL-2.1-only. **The LGPL places no conditions on the license of the consuming work, so this project's own source stays MIT**; TagLibSharp is used unmodified, exactly as published on NuGet.

Note, however, that the released executables bundle `TagLibSharp.dll` inside the binary via `PublishSingleFile`. To substitute a modified build of TagLibSharp, publish without single-file packaging — `TagLibSharp.dll` is then emitted as a standalone file and can be replaced without rebuilding this application.

```powershell
dotnet publish src/MusicTagAuditor.App -c Release -r win-x64 --self-contained false -p:PublishSingleFile=false
```

Serilog is Apache-2.0; CommunityToolkit.Mvvm and Microsoft.Extensions.DependencyInjection are MIT. See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) for details.
