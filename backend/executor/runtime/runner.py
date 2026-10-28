#!/usr/bin/python3
"""Trusted archive receiver. Only this fixed image entrypoint precedes untrusted code."""
import os
import pathlib
import sys
import shutil
import tarfile

root = pathlib.Path('/workspace')
size = 0
count = 0
try:
    with tarfile.open(fileobj=sys.stdin.buffer, mode='r|') as archive:
        for item in archive:
            path = pathlib.PurePosixPath(item.name)
            count += 1
            size += item.size
            if not item.isfile() or path.is_absolute() or '..' in path.parts or '.git' in path.parts or count > 10000 or size > 256 * 1024 * 1024:
                raise ValueError('Unsafe archive or snapshot limit exceeded')
            target = root.joinpath(*path.parts)
            target.parent.mkdir(parents=True, exist_ok=True)
            with os.fdopen(os.open(target, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o600), 'wb') as output:
                source = archive.extractfile(item)
                while chunk := source.read(65536):
                    output.write(chunk)
    if len(sys.argv) < 2:
        raise ValueError('An administrator-defined command is required')
    # Only a pre-approved image may provide cache data. Copy npm's mutable
    # index into bounded tmpfs; never mount the owner's cache or credentials.
    if pathlib.Path('/opt/npm-cache').is_dir():
        shutil.copytree('/opt/npm-cache', '/tmp/npm/_cacache', dirs_exist_ok=True)
    os.umask(0o077)
    os.execvpe(sys.argv[1], sys.argv[1:], os.environ)
except Exception as error:
    print('Executor archive/command rejected: ' + str(error), file=sys.stderr)
    sys.exit(125)
