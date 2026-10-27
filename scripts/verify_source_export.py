"""Audit source-only files and, optionally, every reachable Git tree/blob. No network."""
from pathlib import Path
import argparse
import hashlib
import re
import subprocess
import sys

IGNORED_WORKING = {'.git', 'bin', 'obj', '__pycache__'}
FORBIDDEN_PARTS = {'bin', 'obj', 'publish', 'logs', 'evidence', 'private-local-backup', '.vs', '.env', '__pycache__'}
FORBIDDEN_SUFFIXES = {'.dll', '.exe', '.pdb', '.zip', '.7z', '.nupkg', '.snupkg', '.msi', '.msix', '.appx',
    '.ico', '.png', '.jpg', '.jpeg', '.gif', '.webp', '.svg', '.docx', '.pdf', '.mp3', '.wav', '.mp4',
    '.protected', '.pem', '.pfx', '.key', '.log', '.jsonl', '.woff', '.woff2', '.ttf', '.otf', '.pyc', '.pyo'}
KEY_PATTERNS = [re.compile(rb'\bsk-(?:proj-|svcacct-)?[A-Za-z0-9_-]{32,}'),
    re.compile(rb'\bgh[pousr]_[A-Za-z0-9]{20,}'), re.compile(rb'\bgithub_pat_[A-Za-z0-9_]{30,}'),
    re.compile(rb'-----BEGIN [A-Z ]*PRIVATE KEY-----'),
    re.compile(rb'https?://[^\s/:]+:[^\s/@]+@')]

def audit_file(name: str, data: bytes) -> list[str]:
    path = Path(name); parts = {p.lower() for p in path.parts}; base = path.name.lower()
    problems = []
    if parts & FORBIDDEN_PARTS or path.suffix.lower() in FORBIDDEN_SUFFIXES or base.startswith('.env') or base in {'settings.json', 'credentials.json', 'service-account.json'} or base.endswith(('.transcript.txt', '.transcript.md')):
        problems.append(f'Forbidden source payload: {name}')
    if any(pattern.search(data) for pattern in KEY_PATTERNS):
        problems.append(f'Credential-pattern finding (value omitted): {name}')
    if b'\x00' in data:
        problems.append(f'Unexpected binary/NUL content: {name}')
    try: data.decode('utf-8-sig')
    except UnicodeDecodeError: problems.append(f'Unexpected non-UTF8 content: {name}')
    if path.suffix.lower() == '.rtf' and (name.replace('\\', '/') != 'licenses/Microsoft-Windows-SDK-original.rtf' or hashlib.sha256(data).hexdigest() != 'dd07eb178e00c6bba4148457fc00ff77cd4887eb521d504186fe59c9ec8bbe62'):
        problems.append(f'Unreviewed RTF payload: {name}')
    if re.search(rb'data:image/[a-z0-9.+-]+;base64,', data, re.I):
        problems.append(f'Embedded image bytes require asset review: {name}')
    return problems

def git(root: Path, *args: str) -> bytes:
    result = subprocess.run(['git', '-C', str(root), *args], capture_output=True, check=False)
    if result.returncode: raise RuntimeError(result.stderr.decode('utf-8', errors='replace').strip())
    return result.stdout

def audit_history(root: Path) -> tuple[list[str], int, int]:
    problems=[]; seen=set(); commits=git(root, 'rev-list', '--all').decode().splitlines()
    if not commits: raise RuntimeError('No reachable commits to audit.')
    for commit in commits:
        metadata=git(root, 'show', '-s', '--format=raw', commit)
        if any(p.search(metadata) for p in KEY_PATTERNS): problems.append(f'Credential-pattern finding in commit metadata: {commit}')
        for entry in git(root, 'ls-tree', '-r', '-z', commit).split(b'\0'):
            if not entry: continue
            header, raw_name=entry.split(b'\t',1); mode,kind,sha=header.decode().split(); name=raw_name.decode('utf-8')
            if mode == '120000' or kind != 'blob': problems.append(f'Unexpected Git tree entry: {name}');continue
            identity=(sha,name)
            if identity in seen:continue
            seen.add(identity);problems.extend(audit_file(name, git(root, 'cat-file', 'blob', sha)))
    return problems,len(commits),len(seen)

def main() -> int:
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--root',type=Path,default=Path(__file__).resolve().parents[1])
    parser.add_argument('--git-history',action='store_true')
    args=parser.parse_args(); root=args.root.resolve(); problems=[]; count=0
    for path in sorted(root.rglob('*')):
        relative=path.relative_to(root)
        if any(p in IGNORED_WORKING for p in relative.parts):continue
        if path.is_symlink():problems.append(f'Symlink is not allowed: {relative}');continue
        if path.is_file():count+=1;problems.extend(audit_file(relative.as_posix(),path.read_bytes()))
    for needed in ('LICENSE','THIRD-PARTY-NOTICES.md','licenses/Apache-2.0.txt','WITHHELD-ASSETS.json','docs/SOURCE-PROVENANCE.md'):
        if not (root/needed).is_file():problems.append(f'Missing provenance/notice: {needed}')
    if args.git_history:
        try:
            history,commits,blobs=audit_history(root);problems.extend(history)
            print(f'Reachable history audited: {commits} commits, {blobs} blob/path versions')
        except (RuntimeError,UnicodeError,ValueError) as error:problems.append(f'Git audit failed: {error}')
    if problems:
        print('\n'.join(problems));return 1
    print(f'SOURCE EXPORT AUDIT PASSED: {count} files, no forbidden payload/credential-pattern findings')
    return 0

if __name__=='__main__':sys.exit(main())
