import argparse
import gzip
import hashlib
import io
import json
from pathlib import Path
import re
import tarfile
import uuid
import zipfile


ROOT = Path(__file__).resolve().parent.parent
NAMESPACE = uuid.UUID('30fabdd2-90f3-4b17-bd8d-1883d31817fb')


def metadata(path, folder=False):
    guid = uuid.uuid5(NAMESPACE, path).hex
    extra = 'folderAsset: yes\n' if folder else ''
    return f'fileFormatVersion: 2\nguid: {guid}\n{extra}DefaultImporter:\n  externalObjects: {{}}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n'.encode()


def digest(data):
    return hashlib.sha256(data).hexdigest()


def source_bytes(path):
    return path.read_bytes().replace(b'\r\n', b'\n')


def write_metadata(path, text):
    with path.open('w', encoding='utf-8', newline='') as stream:
        stream.write(text.replace('\n', '\r\n'))


def build(output):
    manifest = json.loads((ROOT / 'package.json').read_text(encoding='utf-8-sig'))
    version = manifest['version']
    if not re.fullmatch(r'\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?', version):
        raise ValueError('Invalid release version')
    files = {}
    for name in ['package.json', 'README.md', 'LICENSE', 'CHANGELOG.md', 'GLOSSARY.md', 'Editor.meta']:
        path = ROOT / name
        files[name] = source_bytes(path)
        if name.endswith('.meta'):
            continue
        sidecar = ROOT / (name + '.meta')
        if not sidecar.is_file():
            raise ValueError(f'Missing committed metadata: {sidecar.name}')
        files[name + '.meta'] = source_bytes(sidecar)
    for path in sorted((ROOT / 'Editor').rglob('*')):
        if path.is_symlink():
            raise ValueError('Symbolic links are not release inputs')
        if path.is_file():
            if path.suffix not in ['.cs', '.asmdef', '.meta']:
                raise ValueError(f'Unexpected Editor file: {path.name}')
            files[path.relative_to(ROOT).as_posix()] = source_bytes(path)
            if path.suffix != '.meta' and not Path(str(path) + '.meta').is_file():
                raise ValueError(f'Missing metadata: {path.name}')
    for path in sorted((ROOT / 'Documentation~').rglob('*.md')):
        files[path.relative_to(ROOT).as_posix()] = source_bytes(path)
    files['README.md'] = files['README.md'].replace(b'](Tests~/README.md)', b'](https://github.com/ainoobmanpg/avatar-fork/blob/main/Tests~/README.md)')
    output.mkdir(parents=True, exist_ok=True)
    zip_name = f'{manifest["name"]}-{version}.zip'
    unity_name = f'AvatarFork-{version}.unitypackage'
    if any(output.iterdir()):
        raise ValueError('Use a new empty output directory')
    zip_data = io.BytesIO()
    with zipfile.ZipFile(zip_data, 'w', compression=zipfile.ZIP_DEFLATED) as archive:
        for name, data in sorted(files.items()):
            item = zipfile.ZipInfo(name, (2026, 1, 1, 0, 0, 0))
            item.create_system = 0
            item.compress_type = zipfile.ZIP_DEFLATED
            item.external_attr = 0o100644 << 16
            archive.writestr(item, data)
    (output / zip_name).write_bytes(zip_data.getvalue())
    unity_files = {name: data for name, data in files.items() if name != 'package.json' and not name.endswith('.meta')}
    unity_files = {name.replace('Documentation~/', 'Documentation/'): data for name, data in unity_files.items()}
    for name in ['README.md', 'CHANGELOG.md']:
        unity_files[name] = unity_files[name].replace(b'Documentation~/', b'Documentation/')
    folders = {'Assets/AvatarFork', 'Assets/AvatarFork/Editor'}
    for name in unity_files:
        folders.update(p.as_posix() for p in Path('Assets/AvatarFork', name).parents if p.as_posix() != 'Assets' and p.as_posix() != '.')
    entries = {}
    for folder in sorted(folders):
        meta = metadata(folder, True)
        entries[folder] = (None, meta)
    for name, data in unity_files.items():
        original = name.replace('Documentation/', 'Documentation~/')
        asset_path = 'Assets/AvatarFork/' + name
        meta = files.get(original + '.meta', metadata(asset_path))
        guid = uuid.uuid5(NAMESPACE, asset_path).hex.encode()
        meta = re.sub(rb'(?m)^guid: [0-9a-f]{32}', b'guid: ' + guid, meta)
        entries[asset_path] = (data, meta)
    tar_data = io.BytesIO()
    with tarfile.open(fileobj=tar_data, mode='w') as archive:
        for name, (data, meta) in sorted(entries.items()):
            guid = re.search(rb'^guid: ([0-9a-f]{32})\r?$', meta, re.M).group(1).decode()
            members = {'pathname': name.encode(), 'asset.meta': meta}
            if data is not None:
                members['asset'] = data
            for leaf, content in members.items():
                item = tarfile.TarInfo(f'{guid}/{leaf}')
                item.size = len(content)
                item.mode = 0o644
                archive.addfile(item, io.BytesIO(content))
    with (output / unity_name).open('wb') as stream:
        with gzip.GzipFile(filename='', mode='wb', fileobj=stream, mtime=0) as zipped:
            zipped.write(tar_data.getvalue())
    listing_path = ROOT / 'vpm' / 'index.json'
    listing = json.loads(listing_path.read_text(encoding='utf-8')) if listing_path.exists() else {
        'name': 'Avatar Fork Beta', 'id': 'com.ainoobmanpg.avatar-fork.repository',
        'url': 'https://raw.githubusercontent.com/ainoobmanpg/avatar-fork/main/vpm/index.json',
        'author': 'sakuchichi_vrc', 'packages': {}}
    release_manifest = dict(manifest)
    release_manifest['url'] = f'https://github.com/ainoobmanpg/avatar-fork/releases/download/v{version}/{zip_name}'
    release_manifest['zipSHA256'] = digest(zip_data.getvalue())
    versions = listing['packages'].setdefault(manifest['name'], {}).setdefault('versions', {})
    if version in versions and versions[version] != release_manifest:
        raise ValueError('This version already has different release contents; increment the version')
    versions[version] = release_manifest
    write_metadata(output / 'index.json', json.dumps(listing, ensure_ascii=False, indent=2) + '\n')
    sums = {name: digest((output / name).read_bytes()) for name in [zip_name, unity_name, 'index.json']}
    write_metadata(output / 'SHA256SUMS.txt', ''.join(f'{value}  {name}\n' for name, value in sums.items()))
    write_metadata(output / 'contents.json', json.dumps({'version': version, 'zip': sorted(files), 'unitypackage': sorted(entries), 'sha256': sums}, ensure_ascii=False, indent=2) + '\n')
    print(json.dumps({'version': version, 'output': str(output.resolve()), 'files': len(files), 'sha256': sums}))


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--output', type=Path, required=True)
    build(parser.parse_args().output)
