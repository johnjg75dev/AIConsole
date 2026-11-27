#!/usr/bin/env python3
"""AIConsole build driver.

Run with no arguments for the interactive menu, or use a command for CI:

    python compile.py publish --rid win-x64 --out dist/win-x64 --no-bump
    python compile.py installers --artifacts dist --out dist/installers
    python compile.py release --bump patch --no-github
    python compile.py test
"""

from __future__ import annotations

import argparse
import json
import os
import shutil
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent
SOLUTION = ROOT / "AIConsole.slnx"
PROJECT = ROOT / "src" / "AIConsole" / "AIConsole.csproj"
INSTALLER_SOURCE = ROOT / "installer" / "Product.wxs"
VERSION_FILE = ROOT / "version.json"
DIST = ROOT / "dist"

RIDS = ("win-x64", "win-x86", "win-arm64")
INSTALLER_RIDS = ("win-x64", "win-x86")
ARCH = {"win-x64": "x64", "win-x86": "x86", "win-arm64": "arm64"}

DEFAULT_VERSION = {
    "major": 1,
    "minor": 0,
    "patch": 0,
    "revision": 0,
    "stage": "dev",  # dev | rc | stable
}

BUMP_KINDS = ("dev", "rc", "patch", "minor", "major")


# ── version helpers ────────────────────────────────────────────────────────

def load_version() -> dict:
    if VERSION_FILE.exists():
        data = json.loads(VERSION_FILE.read_text(encoding="utf-8"))
        return {**DEFAULT_VERSION, **data}
    save_version(DEFAULT_VERSION.copy())
    return DEFAULT_VERSION.copy()


def save_version(version: dict) -> None:
    VERSION_FILE.write_text(json.dumps(version, indent=2) + "\n", encoding="utf-8")


def numeric_version(version: dict) -> str:
    revision = max(0, min(65535, int(version["revision"])))
    return f'{int(version["major"])}.{int(version["minor"])}.{int(version["patch"])}.{revision}'


def semver(version: dict) -> str:
    base = f'{int(version["major"])}.{int(version["minor"])}.{int(version["patch"])}'
    stage = version.get("stage", "dev")
    if stage == "stable":
        return base
    return f'{base}-{stage}.{int(version["revision"])}'


def tag_name(version: dict) -> str:
    return "v" + semver(version)


def bump(version: dict, kind: str) -> dict:
    result = dict(version)
    if kind == "dev":
        result["revision"] = int(result["revision"]) + 1
        result["stage"] = "dev"
    elif kind == "rc":
        result["revision"] = int(result["revision"]) + 1
        result["stage"] = "rc"
    elif kind == "patch":
        result["patch"] = int(result["patch"]) + 1
        result["revision"] = 0
        result["stage"] = "stable"
    elif kind == "minor":
        result["minor"] = int(result["minor"]) + 1
        result["patch"] = 0
        result["revision"] = 0
        result["stage"] = "stable"
    elif kind == "major":
        result["major"] = int(result["major"]) + 1
        result["minor"] = 0
        result["patch"] = 0
        result["revision"] = 0
        result["stage"] = "stable"
    else:
        raise ValueError(f"Unknown bump kind: {kind}")
    return result


def version_properties(version: dict) -> list[str]:
    return [
        f"/p:Version={semver(version)}",
        f"/p:AssemblyVersion={numeric_version(version)}",
        f"/p:FileVersion={numeric_version(version)}",
        f"/p:InformationalVersion={semver(version)}",
    ]


def msbuild_properties(kind: str = "dev") -> None:
    """Used by Directory.Build.targets: bump and emit one parseable line."""
    version = bump(load_version(), kind)
    save_version(version)
    print(f"{semver(version)}|{numeric_version(version)}")


# ── process helpers ────────────────────────────────────────────────────────

def run(command: list[str], *, check: bool = True) -> subprocess.CompletedProcess:
    print("\n> " + " ".join(str(c) for c in command), flush=True)
    return subprocess.run(command, cwd=ROOT, check=check)


def require_windows() -> None:
    if sys.platform != "win32":
        print(
            "WARNING: WPF builds require Windows. This command will probably fail here.\n",
            file=sys.stderr,
        )


# ── commands ───────────────────────────────────────────────────────────────

def build(kind: str = "dev") -> None:
    require_windows()
    version = bump(load_version(), kind)
    save_version(version)
    configuration = "Debug" if kind == "dev" else "Release"
    run([
        "dotnet", "build", str(SOLUTION), "--nologo",
        f"-c", configuration, *version_properties(version), "-p:AutoVersion=0",
    ])
    print(f"\nBuilt {configuration} {semver(version)} ({numeric_version(version)})")


def publish(rid: str | None = None, out: Path | None = None, *, do_bump: bool = False) -> None:
    require_windows()
    version = bump(load_version(), "dev") if do_bump else load_version()
    if do_bump:
        save_version(version)

    destination = (out or DIST).resolve()
    destination.mkdir(parents=True, exist_ok=True)

    command = [
        "dotnet", "publish", str(PROJECT),
        "-p:PublishProfile=SingleFile", "--nologo",
        *version_properties(version), "-p:AutoVersion=0",
    ]
    if rid:
        command.append(f"-p:RuntimeIdentifier={rid}")
    command.append(f"-p:PublishDir={destination}{os.sep}")
    run(command)

    exe = destination / "AIConsole.exe"
    if rid and exe.exists():
        named = destination / f"AIConsole-{semver(version)}-{rid}.exe"
        exe.replace(named)
        print(f"Standalone: {named}")
    else:
        print(f"Standalone: {exe}")


def test() -> None:
    run(["dotnet", "test", str(SOLUTION), "--nologo", "-p:AutoVersion=0"])


def debug() -> None:
    build("dev")
    version = load_version()
    run([
        "dotnet", "run", "--project", str(PROJECT), "-c", "Debug",
        "--no-build", *version_properties(version), "-p:AutoVersion=0",
    ])


def wix_executable() -> str | None:
    local = ROOT / ".tools" / ("wix.exe" if sys.platform == "win32" else "wix")
    if local.exists():
        return str(local)
    return shutil.which("wix")


def infer_rid(path: Path) -> str | None:
    text = str(path).lower()
    for rid in RIDS:
        if rid in text:
            return rid
    return None


def build_installers(artifacts: Path, out: Path) -> list[Path]:
    version = load_version()
    wix = wix_executable()
    if wix is None:
        print("WiX was not found. Install it with: dotnet tool install wix --tool-path .tools")
        return []

    out = out.resolve()
    out.mkdir(parents=True, exist_ok=True)
    created: list[Path] = []

    for exe in sorted(artifacts.rglob("AIConsole*.exe")):
        rid = infer_rid(exe)
        if rid not in INSTALLER_RIDS:
            print(f"Skipping installer for {exe} (unsupported RID: {rid})")
            continue
        target = out / f"AIConsole-{semver(version)}-{rid}.msi"
        run([
            wix, "build",
            "-arch", ARCH[rid],
            "-d", f"Version={numeric_version(version)}",
            "-d", f"Source={exe}",
            str(INSTALLER_SOURCE),
            "-o", str(target),
            "-pdbtype", "none",
        ])
        created.append(target)
        print(f"Installer: {target}")

    return created


def release(bump_kind: str = "patch", *, github: bool) -> None:
    version = load_version()
    if bump_kind != "none":
        version = bump(version, bump_kind)
        save_version(version)
    print(f"\n=== Release {tag_name(version)} ===")

    test()
    for rid in RIDS:
        publish(rid, DIST / rid)
    build_installers(DIST, DIST / "installers")

    if github:
        push_release_tag(version)
    else:
        print("\nLocal release artifacts are in dist/. No GitHub release was created.")


def push_release_tag(version: dict) -> None:
    tag = tag_name(version)

    status = subprocess.run(
        ["git", "status", "--porcelain", "--", ".", ":(exclude)version.json"],
        cwd=ROOT, capture_output=True, text=True,
    )
    if status.returncode != 0:
        raise RuntimeError("Could not read git status.")
    if status.stdout.strip():
        raise RuntimeError(
            "Commit or stash other changes before pushing a release tag:\n"
            + status.stdout
        )

    run(["git", "add", "version.json"])
    run(["git", "commit", "-m", f"chore: release {tag}"])
    run(["git", "tag", "-a", tag, "-m", f"AIConsole {tag}"])
    run(["git", "push", "origin", "HEAD"])
    run(["git", "push", "origin", tag])
    print(
        f"\nPushed {tag}. GitHub Actions will compile fresh standalone EXEs and "
        "installers and attach them to the new release."
    )


def configure_version() -> None:
    version = load_version()
    print(f"\nCurrent version: {semver(version)} ({numeric_version(version)})")
    for key in DEFAULT_VERSION:
        answer = input(f"{key} [{version[key]}]: ").strip()
        if not answer:
            continue
        version[key] = answer if key == "stage" else int(answer)
    save_version(version)
    print(f"Version set to {semver(version)}")


# ── interactive menu ───────────────────────────────────────────────────────

MENU = """
AIConsole compile
=================
  1  Dev build            (new build number)
  2  RC build             (new release candidate)
  3  Patch release build  (new patch version)
  4  Minor release build
  5  Major release build
  6  Publish single-file  (current version)
  7  Run tests
  8  Debug                (build + run)
  9  Release locally      (no GitHub release)
 10  Release + GitHub     (push tag; Actions creates the release)
 11  Show / set version
  0  Exit
"""


def interactive() -> None:
    while True:
        version = load_version()
        print(MENU)
        print(f"  version: {semver(version)}  ({numeric_version(version)})\n")
        choice = input("Select: ").strip()
        try:
            if choice == "1":
                build("dev")
            elif choice == "2":
                build("rc")
            elif choice == "3":
                build("patch")
            elif choice == "4":
                build("minor")
            elif choice == "5":
                build("major")
            elif choice == "6":
                publish(do_bump=True)
            elif choice == "7":
                test()
            elif choice == "8":
                debug()
            elif choice == "9":
                answer = input("Bump (patch/minor/major/none) [patch]: ").strip().lower()
                release("none" if answer == "none" else answer or "patch", github=False)
            elif choice == "10":
                answer = input("Bump (patch/minor/major) [patch]: ").strip().lower() or "patch"
                release(answer, github=True)
            elif choice == "11":
                configure_version()
            elif choice == "0":
                return
            else:
                print("Unknown option.")
        except Exception as exc:  # keep the menu alive after a failed command
            print(f"\nERROR: {exc}", file=sys.stderr)
        input("\nPress Enter to continue...")


# ── CLI ────────────────────────────────────────────────────────────────────

def main() -> None:
    parser = argparse.ArgumentParser(description="AIConsole build driver")
    commands = parser.add_subparsers(dest="command")

    p_build = commands.add_parser("build", help="compile with an automatic version bump")
    p_build.add_argument("--kind", choices=BUMP_KINDS, default="dev")

    p_publish = commands.add_parser("publish", help="publish single-file EXE(s)")
    p_publish.add_argument("--rid", choices=RIDS)
    p_publish.add_argument("--out", type=Path)
    p_publish.add_argument("--no-bump", action="store_true")

    commands.add_parser("test", help="run the test suite")
    commands.add_parser("debug", help="build and launch the debug app")

    p_release = commands.add_parser("release", help="build all release artifacts")
    p_release.add_argument("--bump", choices=("none", *BUMP_KINDS), default="patch")
    p_release.add_argument("--no-github", action="store_true")

    commands.add_parser("version", help="print the current version")

    p_msbuild = commands.add_parser(
        "msbuild-props", help="bump the version and emit MSBuild properties"
    )
    p_msbuild.add_argument("--kind", choices=BUMP_KINDS, default="dev")

    p_installers = commands.add_parser("installers", help="build MSI installers")
    p_installers.add_argument("--artifacts", type=Path, default=DIST)
    p_installers.add_argument("--out", type=Path, default=DIST / "installers")

    args = parser.parse_args()

    if args.command is None:
        interactive()
        return

    version = load_version()
    if args.command == "build":
        build(args.kind)
    elif args.command == "publish":
        publish(args.rid, args.out, do_bump=not args.no_bump)
    elif args.command == "test":
        test()
    elif args.command == "debug":
        debug()
    elif args.command == "release":
        release(args.bump, github=not args.no_github)
    elif args.command == "version":
        print(semver(version))
        print(numeric_version(version))
        print(tag_name(version))
    elif args.command == "msbuild-props":
        msbuild_properties(args.kind)
    elif args.command == "installers":
        build_installers(args.artifacts, args.out)


if __name__ == "__main__":
    main()
