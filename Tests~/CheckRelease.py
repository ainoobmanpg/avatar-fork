import argparse
import hashlib
import json
from pathlib import Path, PurePosixPath
import re
import tarfile
import zipfile


def check(directory):
    listing = json.loads((directory / 'index.json').read_text(encoding='utf-8'))
    entries = listing['packages']['com.ainoobmanpg.avatar-fork']['versions']
    checksums = {}
    for line in (directory / 'SHA256SUMS.txt').read_text().splitlines():
        value, name = line.split('  ', 1)
        assert Path(name).name == name
        assert hashlib.sha256((directory / name).read_bytes()).hexdigest() == value, name
        checksums[name] = value
    archives = list(directory.glob('com.ainoobmanpg.avatar-fork-*.zip'))
    assert len(archives) == 1
    with zipfile.ZipFile(archives[0]) as archive:
        names = archive.namelist()
        assert len(names) == len(set(names))
        assert all(not PurePosixPath(n).is_absolute() and '..' not in PurePosixPath(n).parts for n in names)
        assert all(n.startswith(('Editor/', 'Documentation~/')) or n in {
            'Editor.meta', 'package.json', 'package.json.meta', 'README.md', 'README.md.meta',
            'LICENSE', 'LICENSE.meta', 'CHANGELOG.md', 'CHANGELOG.md.meta', 'GLOSSARY.md', 'GLOSSARY.md.meta'} for n in names)
        assert all(not n.startswith(('private~/', 'reports~/', 'Tests~/', '.git/')) for n in names)
        manifest = json.loads(archive.read('package.json'))
        assert b'](Tests~/' not in archive.read('README.md')
        assert 'Editor/AvatarForkText.cs' in names and 'Editor/AvatarForkText.cs.meta' in names
        version = manifest['version']
        release = entries[version]
        assert release['name'] == manifest['name'] and release['zipSHA256'] == checksums[archives[0].name]
        assert release['url'].endswith('/v' + version + '/' + archives[0].name)
        assert json.loads(archive.read('Editor/AvatarFork.Editor.asmdef'))['includePlatforms'] == ['Editor']
        assert b'Copyright (c) 2026 sakuchichi_vrc' in archive.read('LICENSE')
        assert b'MIT' in archive.read('LICENSE')
        source_guids = {re.search(rb'guid: ([0-9a-f]{32})', archive.read(n)).group(1) for n in names if n.endswith('.meta')}
        source_code = {n: archive.read(n) for n in names if n.startswith('Editor/') and not n.endswith('.meta')}
    unity_path = directory / ('AvatarFork-' + version + '.unitypackage')
    with tarfile.open(unity_path, 'r:gz') as archive:
        members = archive.getnames()
        assert len(members) == len(set(members))
        roots = {PurePosixPath(n).parts[0] for n in members}
        assert all(re.fullmatch('[0-9a-f]{32}', n) for n in roots)
        assert not roots.intersection(g.decode() for g in source_guids), 'Formats must not share GUIDs'
        imported = {}
        for guid in roots:
            path = archive.extractfile(guid + '/pathname').read().decode()
            assert path == 'Assets/AvatarFork' or path.startswith('Assets/AvatarFork/')
            assert '..' not in PurePosixPath(path).parts
            meta = archive.extractfile(guid + '/asset.meta').read()
            assert re.search(rb'guid: ([0-9a-f]{32})', meta).group(1).decode() == guid
            if guid + '/asset' in members:
                assert path not in imported
                imported[path] = archive.extractfile(guid + '/asset').read()
        assert 'Assets/AvatarFork/package.json' not in imported
        for name, content in source_code.items():
            assert imported['Assets/AvatarFork/' + name] == content, name
        assert b'Copyright (c) 2026 sakuchichi_vrc' in imported['Assets/AvatarFork/LICENSE']
        assert b'Documentation/' in imported['Assets/AvatarFork/README.md']
    print(json.dumps({'success': True, 'version': version, 'zip_files': len(names), 'unity_assets': len(imported), 'independent_guids': True, 'sha256': checksums}))


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--dir', type=Path, required=True)
    check(parser.parse_args().dir)
