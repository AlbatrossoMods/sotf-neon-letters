#!/usr/bin/env python3

import os
from pathlib import Path
import shutil
import stat
import sys
import tempfile
import zipfile


ROOT_FILES = (
    "Assets/Generated.meta",
    "Assets/GeneratedSource.meta",
)
ROOT_DIRECTORIES = (
    "Assets/Generated",
    "Assets/GeneratedSource",
)
ALLOWED_PREFIXES = tuple(f"{path}/" for path in ROOT_DIRECTORIES)


def fail(message: str) -> None:
    raise ValueError(message)


def validate_entries(archive: zipfile.ZipFile) -> list[zipfile.ZipInfo]:
    entries = archive.infolist()
    if not entries:
        fail("Canonical snapshot is empty.")

    seen: set[str] = set()
    for entry in entries:
        name = entry.filename
        if name in seen:
            fail(f"Canonical snapshot contains a duplicate entry: {name}")
        seen.add(name)

        components = name.split("/")
        if (
            name.startswith("/")
            or "\\" in name
            or any(component in ("", ".", "..") for component in components)
        ):
            fail(f"Canonical snapshot contains an unsafe path: {name}")

        if name not in ROOT_FILES and not name.startswith(ALLOWED_PREFIXES):
            fail(f"Canonical snapshot entry is outside generated roots: {name}")

        unix_mode = entry.external_attr >> 16
        if entry.create_system != 3 or not stat.S_ISREG(unix_mode):
            fail(f"Canonical snapshot entry is not a regular file: {name}")

    return entries


def require_within_destination(resolved_root: Path, candidate: Path) -> None:
    try:
        candidate.relative_to(resolved_root)
    except ValueError:
        fail(f"Destination path resolves outside destination root: {candidate}")


def validate_destination(destination_root: Path) -> tuple[Path, dict[str, Path]]:
    if destination_root.is_symlink():
        fail(f"Destination root cannot be a symbolic link: {destination_root}")
    if not destination_root.exists():
        fail(f"Destination root does not exist: {destination_root}")
    if not destination_root.is_dir():
        fail(f"Destination root is not a directory: {destination_root}")

    resolved_root = destination_root.resolve(strict=True)
    destination_assets = resolved_root / "Assets"
    if destination_assets.is_symlink():
        fail(f"Destination Assets path cannot be a symbolic link: {destination_assets}")
    if destination_assets.exists() and not destination_assets.is_dir():
        fail(f"Destination Assets path is not a directory: {destination_assets}")

    resolved_assets = destination_assets.resolve(strict=False)
    require_within_destination(resolved_root, resolved_assets)

    destinations: dict[str, Path] = {}
    for name in ("Generated.meta", "GeneratedSource.meta", "Generated", "GeneratedSource"):
        destination = resolved_assets / name
        if destination.is_symlink():
            fail(f"Final destination cannot be a symbolic link: {destination}")
        resolved_destination = destination.resolve(strict=False)
        require_within_destination(resolved_root, resolved_destination)
        destinations[name] = resolved_destination

    return resolved_assets, destinations


def extract_snapshot(archive_path: Path, destination_root: Path) -> None:
    if not archive_path.is_file():
        fail(f"Canonical snapshot is missing or not a file: {archive_path}")

    staging_root = Path(tempfile.mkdtemp(prefix="sotf-neon-generated-assets."))
    try:
        with zipfile.ZipFile(archive_path, "r") as archive:
            entries = validate_entries(archive)
            for entry in entries:
                destination = staging_root.joinpath(*entry.filename.split("/"))
                destination.parent.mkdir(parents=True, exist_ok=True)
                with archive.open(entry, "r") as source, destination.open("xb") as output:
                    shutil.copyfileobj(source, output)

        staged_assets = staging_root / "Assets"
        for relative_path in ROOT_FILES:
            if not (staging_root / relative_path).is_file():
                fail(f"Canonical snapshot is missing required file: {relative_path}")
        for relative_path in ROOT_DIRECTORIES:
            if not (staging_root / relative_path).is_dir():
                fail(f"Canonical snapshot is missing required directory: {relative_path}")

        destination_assets, destinations = validate_destination(destination_root)
        destination_assets.mkdir(parents=True, exist_ok=True)
        for destination in destinations.values():
            if destination.is_symlink() or destination.is_file():
                destination.unlink()
            elif destination.is_dir():
                shutil.rmtree(destination)

        for name in ("Generated.meta", "GeneratedSource.meta", "Generated", "GeneratedSource"):
            shutil.move(str(staged_assets / name), str(destinations[name]))
    finally:
        shutil.rmtree(staging_root, ignore_errors=True)


def main() -> int:
    if len(sys.argv) != 3:
        print(
            "Usage: extract-canonical-unity-assets.py <snapshot-zip> <destination-root>",
            file=sys.stderr,
        )
        return 2

    try:
        destination_root = Path(os.path.abspath(sys.argv[2]))
        extract_snapshot(Path(sys.argv[1]).resolve(), destination_root)
    except (OSError, RuntimeError, ValueError, zipfile.BadZipFile) as error:
        print(f"Canonical snapshot extraction failed: {error}", file=sys.stderr)
        return 1

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
