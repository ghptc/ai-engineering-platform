#!/usr/bin/env python3
"""Copy explicit owner-approved dependency caches into a sandbox image, not host mounts."""
import argparse
from pathlib import Path
import shutil
import subprocess
import tempfile

parser = argparse.ArgumentParser()
parser.add_argument('--runtime-image', default='aep-executor-runtime:local')
parser.add_argument('--nuget-cache', type=Path)
parser.add_argument('--npm-cache', type=Path, help='Only the npm _cacache directory, never .npmrc or credential files.')
parser.add_argument('--tag', default='aep-executor-cached:local')
args = parser.parse_args()
if not args.nuget_cache and not args.npm_cache:
    raise SystemExit('Specify explicitly approved dependency cache directories; no home directory is inferred.')
root = Path(__file__).resolve().parents[2]
image = subprocess.check_output(['docker', 'image', 'inspect', args.runtime_image, '--format', '{{.Id}}'], text=True).strip()
# BuildKit resolves FROM references by local image name, not a bare config ID.
# Bind a content-derived local tag to the already-inspected image; workloads
# still use the immutable ID of the resulting cache image. No registry pull.
base_tag = 'aep-executor-cache-base:' + image.removeprefix('sha256:')
subprocess.run(['docker', 'tag', image, base_tag], check=True)
with tempfile.TemporaryDirectory(prefix='aep-offline-cache-') as directory:
    target = Path(directory)
    size = 0
    for label, source in [('nuget', args.nuget_cache), ('npm', args.npm_cache)]:
        (target / label).mkdir()
        if not source:
            continue
        source = source.resolve(strict=True)
        if label == 'npm' and source.name != '_cacache':
            raise SystemExit('Select only the npm _cacache directory, not the npm configuration directory.')
        for item in source.rglob('*'):
            if item.is_symlink():
                raise SystemExit('Cache symlinks are not permitted: ' + str(item.relative_to(source)))
            if item.is_dir():
                continue
            if not item.is_file():
                raise SystemExit('Cache special files are not permitted.')
            if item.name in ('.npmrc', 'NuGet.Config', 'nuget.config') or item.suffix in ('.pem', '.key', '.pfx', '.p12') or item.name.startswith('.env'):
                continue
            size += item.stat().st_size
            if size > 2 * 1024 ** 3:
                raise SystemExit('Approved cache data exceeds the 2 GiB image preparation bound.')
            output = target / label / item.relative_to(source)
            output.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(item, output)
            output.chmod(0o644)
    shutil.copyfile(root / 'backend/executor/runtime/cache.Dockerfile', target / 'Dockerfile')
    subprocess.run(['docker', 'build', '--build-arg', 'RUNTIME_IMAGE=' + base_tag, '-t', args.tag, str(target)], check=True)
print('Offline image prepared. Re-pin its image ID in the owner-managed executor profiles; no dependency network access was enabled.')
