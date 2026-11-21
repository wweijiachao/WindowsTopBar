"""Build Windows x64 full/lite release ZIPs for a tag on the current commit.

Requires Windows, Python 3.10+, Git and the .NET 10 SDK. Standard library only.
"""

from __future__ import annotations

import argparse
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tempfile
import zipfile


ROOT = Path(__file__).resolve().parents[1]
NUGET_SOURCE = "https://api.nuget.org/v3/index.json"


def run(command: list[str], root: Path, *, capture: bool = False) -> str:
    print("> " + subprocess.list2cmdline(command), flush=True)
    result = subprocess.run(
        command,
        cwd=root,
        check=True,
        text=True,
        encoding="utf-8" if capture else None,
        stdout=subprocess.PIPE if capture else None,
        creationflags=subprocess.CREATE_NO_WINDOW if sys.platform == "win32" else 0,
    )
    return result.stdout.strip() if capture else ""


def validate_tag(tag: str) -> str:
    # A Git ref may contain slashes and other characters unsafe in Windows paths.
    reserved = {"CON", "PRN", "AUX", "NUL"} | {
        f"{prefix}{number}" for prefix in ("COM", "LPT") for number in range(1, 10)
    }
    if (
        not re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9._-]*", tag)
        or tag.endswith(".")
        or tag.split(".")[0].upper() in reserved
    ):
        raise ValueError("Tag 不能作为 Windows 目录名，请使用类似 v1.0.0 的标签。")
    return tag


def release_tag(root: Path, requested: str | None) -> str:
    changes = run(["git", "status", "--porcelain", "--untracked-files=normal"], root, capture=True)
    if changes:
        raise ValueError("工作区有未提交或未跟踪的文件，请先提交需要发布的修改，再打 tag。")
    tags = run(["git", "tag", "--points-at", "HEAD"], root, capture=True).splitlines()
    if requested:
        if requested not in tags:
            raise ValueError(f"标签 {requested!r} 不在当前提交上；脚本不会自动切换提交。")
        return validate_tag(requested)
    if not tags:
        raise ValueError("当前提交没有 tag。请先提交修改并执行 git tag -a vX.Y.Z -m \"版本说明\"。")
    if len(tags) > 1:
        raise ValueError("当前提交有多个 tag，请用 --tag 指定其中一个：" + ", ".join(tags))
    return validate_tag(tags[0])


def stop_top_bar(root: Path) -> None:
    # Only process management; no focus changes, input simulation, or other app operations.
    command = """
$ErrorActionPreference = 'Stop'
foreach ($bar in @(Get-Process -Name WindowsTopBar -ErrorAction SilentlyContinue)) {
    $requested = $bar.CloseMainWindow()
    if (-not $bar.WaitForExit(5000)) {
        Stop-Process -Id $bar.Id -Force
        $bar.WaitForExit()
    }
    Write-Output "Stopped WindowsTopBar PID $($bar.Id); normal close requested: $requested"
}
if (Get-Process -Name WindowsTopBar -ErrorAction SilentlyContinue) {
    throw 'WindowsTopBar is still running'
}
"""
    run(["powershell.exe", "-NoProfile", "-NonInteractive", "-Command", command], root)


def publish(root: Path, output: Path, build: Path, self_contained: bool) -> None:
    run([
        "dotnet", "publish", "src/WindowsTopBar/WindowsTopBar.csproj",
        "-c", "Release", "-r", "win-x64",
        "--self-contained", str(self_contained).lower(),
        "-p:SatelliteResourceLanguages=zh-Hans",
        f"-p:RestoreSources={NUGET_SOURCE}",
        "--artifacts-path", str(build), "-o", str(output),
    ], root)
    for name in ("WindowsTopBar.exe", "WindowsTopBar.dll", "WindowsTopBar.runtimeconfig.json"):
        if not (output / name).is_file():
            raise ValueError(f"发布结果缺少 {name}，停止打包。")
    if any(path.parent.name != "zh-Hans" for path in output.rglob("*.resources.dll")):
        raise ValueError("发布结果仍含额外卫星语言资源，停止打包。")


def zip_directory(directory: Path, archive: Path) -> None:
    with zipfile.ZipFile(archive, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as package:
        for file in sorted(directory.rglob("*")):
            if file.is_file():
                package.write(file, file.relative_to(directory).as_posix())


def package_release(root: Path, tag: str) -> Path:
    tag = validate_tag(tag)
    releases = root / "artifacts" / "releases"
    if not releases.resolve().is_relative_to(root.resolve()):
        raise ValueError("发布目录解析到仓库之外，停止打包。")
    releases.mkdir(parents=True, exist_ok=True)
    destination = releases / tag
    # Reject links/junctions and resolve the exact target before recursive replacement.
    if destination.resolve() != releases.resolve() / tag or destination.is_symlink():
        raise ValueError("版本目录是链接或解析位置异常，停止打包。")
    if destination.exists() and not destination.is_dir():
        raise ValueError("版本输出位置已被文件占用。")

    # Keep an existing release untouched until BOTH publishes and ZIPs succeed.
    with tempfile.TemporaryDirectory(prefix=f".{tag}-", dir=releases) as temporary:
        staging = Path(temporary)
        for kind, self_contained in (("full", True), ("lite", False)):
            output = staging / kind
            publish(root, output, staging / ".build" / kind, self_contained)
            suffix = "" if self_contained else "-lite"
            zip_directory(output, staging / f"WindowsTopBar-{tag}-win-x64{suffix}.zip")

        if destination.exists():
            shutil.rmtree(destination)
        destination.mkdir()
        for file in staging.iterdir():
            if file.name != ".build":
                file.rename(destination / file.name)
    return destination


def main() -> int:
    parser = argparse.ArgumentParser(description="按当前提交的 tag 打包完整版和精简版（Windows x64）。")
    parser.add_argument("--tag", help="当前提交有多个 tag 时指定使用哪一个；不切换提交")
    args = parser.parse_args()
    try:
        if sys.platform != "win32":
            raise ValueError("请在 Windows 上执行此脚本。")
        for tool in ("git", "dotnet", "powershell.exe"):
            if shutil.which(tool) is None:
                raise ValueError(f"找不到 {tool}，请先安装并加入 PATH。")
        tag = release_tag(ROOT, args.tag)
        stop_top_bar(ROOT)
        destination = package_release(ROOT, tag)
        print(f"\n打包完成：{destination}")
        for archive in sorted(destination.glob("*.zip")):
            print(f"  {archive.name} ({archive.stat().st_size / 1024 / 1024:.2f} MiB)")
        print("完整版自带运行时；精简版需要 .NET 10 Desktop Runtime (x64)。")
        return 0
    except (OSError, ValueError, subprocess.CalledProcessError) as error:
        print(f"\n打包失败：{error}", file=sys.stderr)
        return 1
    except KeyboardInterrupt:
        print("\n已取消打包。", file=sys.stderr)
        return 130


if __name__ == "__main__":
    raise SystemExit(main())
