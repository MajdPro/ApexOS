#!/usr/bin/env python3
"""Portable Apex configuration and path checks."""
import json
import hashlib
import pathlib
import re
import shutil
import sys

root = pathlib.Path(__file__).resolve().parents[1]
errors = []
if not shutil.which("dotnet"):
    errors.append("Missing dependency: .NET SDK (dotnet)")
for name in ("README.md", "BUILD.md", "CONTRIBUTING.md", "LICENSE", "docs/WINDOWS-VM-TESTING.md", "config/toolbox.json", "config/protected-components.json", "src/ApexToolbox/ApexToolbox.csproj", "scripts/Modules/Apex.Common.psm1", "scripts/TestApexWindows.ps1"):
    if not (root / name).is_file():
        errors.append(f"Missing required file: {name}")
try:
    config = json.loads((root / "config/toolbox.json").read_text(encoding="utf-8"))
except (OSError, json.JSONDecodeError) as exc:
    errors.append(f"Invalid toolbox JSON: {exc}")
    config = {"actions": []}
for config_file in (root / "config").glob("*.json"):
    try:
        json.loads(config_file.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        errors.append(f"Invalid JSON in {config_file.relative_to(root)}: {exc}")
playbook_validator = root / "scripts/validate_playbook.py"
if playbook_validator.is_file():
    import subprocess
    result = subprocess.run([sys.executable, str(playbook_validator)], capture_output=True, text=True)
    if result.returncode:
        errors.extend(line.removeprefix("ERROR: ").strip() for line in result.stderr.splitlines() if line.strip())
seen = set()
for action in config.get("actions", []):
    key = action.get("id", "")
    if key in seen:
        errors.append(f"Duplicate action id: {key}")
    seen.add(key)
    for field in ("category", "title", "description", "script", "statusArgs", "applyArgs", "restoreArgs", "requiresAdmin"):
        if field not in action:
            errors.append(f"Action {key} missing {field}")
    risk_tier = action.get("riskTier", "").upper()
    if risk_tier and risk_tier not in {"SAFE", "ADVANCED", "EXPERIMENTAL"}:
        errors.append(f"Action {key} has invalid risk tier: {risk_tier}")
    if risk_tier == "EXPERIMENTAL" and not action.get("requiresConfirmation", False):
        errors.append(f"Experimental action {key} must require confirmation")
    script = pathlib.PurePosixPath(action.get("script", ""))
    if script.is_absolute() or ".." in script.parts:
        errors.append(f"Unsafe script path on {key}: {script}")
    elif not (root / pathlib.Path(*script.parts)).is_file():
        errors.append(f"Missing configured script: {script}")
if not config.get("actions"):
    errors.append("Toolbox configuration has no actions")
for file in root.rglob("*.reg"):
    data = file.read_bytes()
    encoding = "utf-16" if data.startswith((b"\xff\xfe", b"\xfe\xff")) else "utf-8-sig"
    if not data.decode(encoding).startswith("Windows Registry Editor Version 5.00"):
        errors.append(f"Invalid registry header: {file.relative_to(root)}")
script_hashes = {}
for file in (root / "scripts").rglob("*"):
    if not file.is_file() or file.suffix not in (".ps1", ".psm1"):
        continue
    digest = hashlib.sha256(file.read_bytes()).hexdigest()
    if digest in script_hashes:
        errors.append(f"Duplicate script content: {file.relative_to(root)} and {script_hashes[digest]}")
    else:
        script_hashes[digest] = file.relative_to(root)
    if file.suffix == ".ps1":
        source = file.read_text(encoding="utf-8")
        for relative in re.findall(r"Import-Module\s+\(Join-Path\s+\$PSScriptRoot\s+'([^']+)'", source, re.IGNORECASE):
            target = file.parent.joinpath(*pathlib.PureWindowsPath(relative).parts)
            if not target.is_file():
                errors.append(f"Missing imported module in {file.relative_to(root)}: {relative}")
if errors:
    print("\n".join(f"ERROR: {item}" for item in errors), file=sys.stderr)
    sys.exit(1)
print(f"Apex project validation passed ({len(config.get('actions', []))} actions).")