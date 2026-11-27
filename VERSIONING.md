# AIConsole versioning and release process

This document defines the **required** workflow for every developer working on this repository.
The build scripts and CI workflows are the authority; do not invent parallel versioning rules.

---

## 0. Mandatory entry point

**Always start from the interactive script.** Double-click `compile.bat` (Windows) or run
`./compile.ps1` / `python compile.py`. Use its menu to select the correct version bump before any
local compile, publish, or release:

- **1 / 2 / 3 / 4 / 5** — dev, RC, patch, minor, or major build. This is how you “properly version”
  a change. The script increments `version.json` and stamps the binaries automatically.
- **6** — publish a single-file EXE with a new build number.
- **9** — local release (no GitHub release).
- **10** — release with a pushed tag; GitHub Actions creates the release.
- **11** — review or correct `version.json` only when fixing a mistake. Do not hand-edit the file.

The CLI examples later in this document exist for CI and automation. **Do not substitute bare
`dotnet build` / `dotnet publish` commands for the menu during local development**; the menu is the
supported way to guarantee every compilation receives the right version.

---

## 1. Source of truth

`version.json` at the repository root is the **only** place where the version is stored.

```json
{
  "major": 1,
  "minor": 0,
  "patch": 0,
  "revision": 0,
  "stage": "dev"        // dev | rc | stable
}
```

Never hand-edit version numbers in `.csproj`, generated binaries, or MSI metadata. In normal
development, change them through the **interactive menu (section 0)**. The MSBuild hook exists for
people who insist on a plain `dotnet build`; it is not the preferred workflow.

Generated values:

| Value | Format | Example | Used for |
|---|---|---|---|
| Numeric version | `major.minor.patch.revision` | `1.0.0.5` | `AssemblyVersion`, `FileVersion`, MSI `ProductVersion` |
| SemVer | `major.minor.patch` or `major.minor.patch-<stage>.<revision>` | `1.0.0-rc.5` | `Version`, `InformationalVersion`, EXE filenames, Git tag |
| Git tag | `v<semver>` | `v1.0.0-rc.5` | GitHub release trigger |

`revision` is clamped to `0..65535` because MSI/assembly versions are 16-bit per field.

---

## 2. Bump policy

Choose the bump that matches the change. **Every compile must produce a new version.**

| Situation | Required bump | Menu option | CLI equivalent (CI only) | Result |
|---|---|---|---|---|
| Normal development / daily builds | `dev` | **1** | `python compile.py build --kind dev` | `1.0.0-dev.6` |
| Preview / candidate for an upcoming release | `rc` | **2** | `python compile.py build --kind rc` | `1.0.0-rc.7` |
| Bug-fix release | `patch` | **3** or **9** | `python compile.py release --bump patch` | `v1.0.1` |
| New backward-compatible feature | `minor` | **4** or **9** | `python compile.py release --bump minor` | `v1.1.0` |
| Breaking change | `major` | **5** or **9** | `python compile.py release --bump major` | `v2.0.0` |

Rules:

- `dev` and `rc` increment `revision` and set `stage` accordingly.
- `patch`, `minor`, and `major` reset `revision` to `0` and set `stage` to `stable`.
- Do not publish an RC or stable version from a branch that has failing lint/tests.
- Do not reuse a tag. Tags are immutable once pushed.

---

## 3. Developer workflow

### 3.1 Interactive driver (required locally)

Double-click **`compile.bat`**, or run `./compile.ps1` / `python compile.py`. Pick the menu item
that matches your change — that selection is the versioning step. The menu covers:

```text
 1  Dev build            (new build number)
 2  RC build             (new release candidate)
 3  Patch release build  (new patch version)
 4  Minor release build
 5  Major release build
 6  Publish single-file  (new build number)
 7  Run tests
 8  Debug                (build + run)
 9  Release locally      (no GitHub release)
10  Release + GitHub     (push tag; Actions creates the release)
11  Show / set version
 0  Exit
```

Use option **1** for ordinary work. Use option **11** only for corrections; prefer the bump
commands so the revision history stays sequential.

### 3.2 Command line / CI

```powershell
python compile.py build --kind dev
python compile.py build --kind rc
python compile.py publish --no-bump
python compile.py test
python compile.py debug
python compile.py release --bump patch --no-github
python compile.py release --bump patch
python compile.py version
python compile.py msbuild-props --kind dev
```

- `publish` bumps `dev` by default; pass `--no-bump` in CI where the tag already carries the version.
- `release --bump none` rebuilds everything at the current version without changing it.
- `release` without `--no-github` pushes the tag; GitHub Actions creates the release.

---

## 4. Plain `dotnet build`

`Directory.Build.targets` hooks `BeforeBuild` for the main `AIConsole` project:

```powershell
dotnet build AIConsole.slnx                       # dev bump, then compile
dotnet build AIConsole.slnx -p:VersionKind=rc     # rc bump, then compile
dotnet build AIConsole.slnx -p:AutoVersion=0      # no bump
```

The hook runs `compile.py msbuild-props`, which writes `version.json` and emits one line:

```text
1.0.0-dev.6|1.0.0.6
```

MSBuild parses that into `Version`, `AssemblyVersion`, `FileVersion`, and `InformationalVersion`.

`compile.py` always passes `-p:AutoVersion=0` to its own `dotnet` invocations. Do **not** remove
that flag, or a single compile will bump twice.

> A plain `dotnet build` intentionally dirties `version.json`. Commit that change with your work or
> as a small `chore: bump version to ...` commit. Never leave a release tag pointing at an uncommitted
> `version.json`.

---

## 5. Testing and lint

CI treats compiler warnings as errors. Before pushing:

```powershell
python compile.py test
dotnet build AIConsole.slnx --nologo -warnaserror
```

Required local prerequisites:

- Windows + .NET 10 SDK (WPF is Windows-only)
- Python 3
- WiX 4.0.6 only when building installers locally

---

## 6. Publishing

```powershell
python compile.py publish                              # dist\AIConsole.exe, dev bump
python compile.py publish --rid win-x64 --out dist/win-x64 --no-bump
```

The single-file profile embeds the .NET runtime and native SQLite library. `dist/` is gitignored and
must never be committed.

Supported RIDs: `win-x64`, `win-x86`, `win-arm64`.

---

## 7. Releases

### 7.1 Local release (no GitHub release)

```powershell
python compile.py release --bump patch --no-github
```

This will:

1. Bump `version.json`.
2. Run the full test suite.
3. Publish standalones for `win-x64`, `win-x86`, `win-arm64` into `dist\<rid>\`.
4. Build per-user MSI installers for x64 and x86 into `dist\installers\`.
5. Leave the tag and remote untouched.

Use this to verify release artifacts before tagging.

### 7.2 GitHub release

```powershell
python compile.py release --bump patch
```

This will:

1. Perform steps 1–4 above.
2. Require a clean working tree **except** `version.json`.
3. Commit `version.json` as `chore: release v<semver>`.
4. Create an annotated tag `v<semver>`.
5. Push `HEAD` and the tag.

Then **GitHub Actions**, not your machine, creates the release:

- `.github/workflows/build.yml` rebuilds all three standalones and both MSIs from the tagged commit.
- `softprops/action-gh-release` attaches freshly compiled EXEs and MSIs and generates release notes.

Do not manually upload artifacts or create the release on GitHub. Do not tag by hand without using
the script, because CI derives artifact names and MSI versions from `version.json`.

---

## 8. CI behavior every developer must respect

| Workflow | Trigger | What it enforces |
|---|---|---|
| `lint.yml` | Every push, PR, manual | Python syntax, version command, restore, `-warnaserror` build, tests |
| `build.yml` | Push to `main`, PR, `v*` tag, manual | Cross-RID standalone publish, MSI creation, artifacts; release on `v*` tags |

Notes:

- CI lint builds bump `version.json` **in the runner only**; those bumps are not pushed.
- CI release builds always use `--no-bump`; the tag is the version authority.
- WPF cannot cross-compile to Linux/macOS. “Cross-compile” here means all supported Windows RIDs.

---

## 9. Installers

`installer/Product.wxs` is a WiX v4 per-user MSI. It installs `AIConsole.exe` to
`%LOCALAPPDATA%\AIConsole`, adds a Start Menu shortcut, and writes a registry marker.

- MSI version uses the numeric form (`1.0.0.5`).
- WiX is installed to `.tools/` (gitignored) by CI and by local release builds.
- Arm64 gets a standalone EXE but no MSI; only x64/x86 MSIs are produced.

---

## 10. Prohibited actions

- Editing `version.json` directly to “pick” a release number without using a bump command.
- Running `dotnet publish`/`dotnet build` with hand-written `Version` properties to fake a release.
- Removing `-p:AutoVersion=0` from `compile.py`.
- Tagging `v*` manually or re-tagging an existing version.
- Committing `dist/`, `.tools/`, `bin/`, `obj/`, or `__pycache__/`.
- Creating the GitHub release manually or uploading locally built artifacts to it.
- Merging a PR whose lint workflow failed.

---

## 11. Troubleshooting

**`AutoVersionBuild` did not change the version.**  
Confirm the project is `AIConsole`, `AutoVersion` is not `0`, and this is not a design-time build.
Check that Python 3 is on `PATH`.

**Version jumped twice.**  
You combined `compile.py` with a raw `dotnet` command that still had the hook active. Use one path:
either `compile.py` or plain `dotnet` with `-p:AutoVersion=0`.

**MSI build failed.**  
Install WiX locally:

```powershell
dotnet tool install wix --tool-path .tools --version 4.0.6
```

**Release tag rejected by `push_release_tag`.**  
Commit or stash everything except `version.json`, then rerun the release command.

**Assembly version differs from displayed version.**  
Expected: assemblies use `major.minor.patch.revision`; user-facing tags/SemVer use
`major.minor.patch[-rc.N]`.
