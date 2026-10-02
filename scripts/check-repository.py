"""Scan tracked sources without echoing potentially sensitive values."""
import re
import subprocess
from pathlib import Path

patterns = [
    re.compile(rb'https?://[a-f0-9]{32}\.r2\.cloudflarestorage\.com', re.I),
    re.compile(rb'(?:ghp_|github_pat_|AKIA)[A-Za-z0-9_]{16,}'),
    re.compile(rb'_keyBox\.Text\s*=\s*"[^"\r\n]{16,}"'),
]
files = subprocess.check_output(['git', 'ls-files', '-z']).decode().split('\0')
errors = []
for name in filter(None, files):
    p = Path(name)
    if not p.is_file():
        continue
    if p.suffix.lower() in ('.dll', '.exe', '.nupkg', '.pfx', '.pem') or p.name.startswith('.env'):
        errors.append(f'{name}: forbidden tracked local/binary file')
        continue
    data = p.read_bytes()
    if any(pattern.search(data) for pattern in patterns):
        errors.append(f'{name}: sensitive literal detected')
if errors:
    print('\n'.join(errors))
    raise SystemExit(1)
print('Tracked source security check passed.')
