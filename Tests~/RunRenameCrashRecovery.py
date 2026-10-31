import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import time


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def run(args):
    root = Path(__file__).resolve().parent.parent
    output = args.output.resolve()
    if not (args.template / 'ProjectSettings/ProjectVersion.txt').is_file() or not args.unity.is_file():
        raise ValueError('Specify an existing test template and Unity executable.')
    if output.is_relative_to(args.template.resolve()):
        raise ValueError('The disposable projects must be outside the template project.')
    if output.exists():
        raise ValueError('Use a new output directory; existing projects are never overwritten.')
    output.mkdir(parents=True)
    package = output / 'instrumented-package'
    shutil.copytree(args.package.resolve(), package)
    path = package / 'Editor/AvatarBranchRenamer.cs'
    source = path.read_text(encoding='utf-8')
    changes = {
        'static void RenameMove(string source,': '''static void CrashStop(string stage)
        {
            if (File.ReadAllText("Library/AvatarFork/crash-stage.txt") != stage) return;
            File.WriteAllText("Library/AvatarFork/crash-reached.txt", stage);
            while (true) System.Threading.Thread.Sleep(100);
        }

        static void RenameMove(string source,''',
        'moves?.Add(new KeyValuePair<string, string>(source, destination));': 'moves?.Add(new KeyValuePair<string, string>(source, destination));\n            if (moves != null && moves.Count == 1) CrashStop("firstMove");\n            if (moves != null && source == "Assets/CrashCopy") CrashStop("rootMove");',
        'RenameMove(temporary, plan.preview.destination, moves, backup);': 'RenameMove(temporary, plan.preview.destination, moves, backup);\n                        CrashStop("folderMove");',
        'File.WriteAllText(backup + "/complete.txt", "Complete", Utf8);': 'File.WriteAllText(backup + "/complete.txt", "Complete", Utf8);\n                CrashStop("complete");'
    }
    for old, new in changes.items():
        if source.count(old) != 1:
            raise ValueError('Instrumentation anchor is not unique: ' + old)
        source = source.replace(old, new)
    path.write_text(source, encoding='utf-8')
    results = {}
    for stage in ['firstMove', 'rootMove', 'folderMove', 'complete']:
        project = output / stage
        (project / 'Assets/Editor').mkdir(parents=True)
        shutil.copytree(args.template / 'ProjectSettings', project / 'ProjectSettings')
        (project / 'Packages').mkdir()
        manifest = json.loads((args.template / 'Packages/manifest.json').read_text(encoding='utf-8-sig'))
        manifest['dependencies']['com.ainoobmanpg.avatar-fork'] = 'file:' + package.as_posix()
        (project / 'Packages/manifest.json').write_text(json.dumps(manifest, indent=2), encoding='utf-8')
        shutil.copy2(root / 'Tests~/RenameCrashRecovery.cs', project / 'Assets/Editor')
        (project / 'Library/AvatarFork').mkdir(parents=True)
        (project / 'Library/AvatarFork/crash-stage.txt').write_text(stage, encoding='utf-8')
        command = [str(args.unity), '-batchmode', '-nographics', '-projectPath', str(project), '-executeMethod', 'AvatarForkRenameCrashRecovery.Start', '-logFile', str(project / 'start.log')]
        process = subprocess.Popen(command, creationflags=subprocess.CREATE_NO_WINDOW if hasattr(subprocess, 'CREATE_NO_WINDOW') else 0)
        deadline = time.monotonic() + 240
        marker = project / 'Library/AvatarFork/crash-reached.txt'
        while not marker.exists():
            if process.poll() is not None:
                raise RuntimeError('Unity exited before the crash point: ' + str(project / 'start.log'))
            if time.monotonic() > deadline:
                raise TimeoutError('The disposable crash test did not reach its marker: ' + str(project))
            time.sleep(.2)
        if marker.read_text() != stage:
            raise ValueError('The crash marker does not match the requested stage.')
        process.kill()
        process.wait(timeout=30)
        preserved = output / (stage + '-preserved')
        recovery = output / (stage + '-recovery')
        for clone in [preserved, recovery]:
            for name in ['Assets', 'Packages', 'ProjectSettings', 'Library/AvatarFork']:
                shutil.copytree(project / name, clone / name)
        operation = next((recovery / 'Library/AvatarFork/Rename').iterdir())
        preview = json.loads((operation / 'operation.json').read_text(encoding='utf-8-sig'))
        baseline = json.loads((recovery / 'Library/AvatarFork/crash-baseline.json').read_text())
        if stage != 'complete':
            backup_files = operation / 'files'
            assert backup_files.is_dir() and (operation / 'files.meta').is_file()
            assert all(digest(backup_files / Path(p).relative_to(preview['folder'])) == h for p, h in baseline['files'].items() if p != preview['folder'] + '.meta')
            assert digest(operation / 'files.meta') == baseline['files'][preview['folder'] + '.meta']
            moves = [json.loads(line) for line in (operation / 'moves.jsonl').read_text(encoding='utf-8-sig').splitlines()]
            current = recovery / preview['folder']
            for move in moves:
                if move['source'] == str(current.relative_to(recovery)).replace('\\', '/') and (move.get('completed') or not current.exists() and (recovery / move['destination']).exists()):
                    current = recovery / move['destination']
            assert current.resolve().is_relative_to((recovery / 'Assets').resolve()) and current.is_dir()
            if stage == 'rootMove':
                (recovery / 'Library/AvatarFork/current-folder.txt').write_text(current.relative_to(recovery).as_posix(), encoding='utf-8')
                check = subprocess.run([str(args.unity), '-batchmode', '-nographics', '-projectPath', str(recovery), '-executeMethod', 'AvatarForkRenameCrashRecovery.CheckInterrupted', '-quit', '-logFile', str(recovery / 'interrupted.log')], timeout=240)
                if check.returncode:
                    raise RuntimeError('Interrupted-state protection failed: ' + str(recovery))
            quarantine = recovery / 'RecoveryQuarantine'
            quarantine.mkdir()
            shutil.move(str(current), quarantine / current.name)
            shutil.move(str(current) + '.meta', quarantine / (current.name + '.meta'))
            shutil.copytree(backup_files, recovery / preview['folder'])
            shutil.copy2(operation / 'files.meta', recovery / (preview['folder'] + '.meta'))
        else:
            assert (operation / 'complete.txt').is_file()
            completed = {p.relative_to(recovery).as_posix(): digest(p) for p in (recovery / preview['destination']).rglob('*') if p.is_file()}
            meta = recovery / (preview['destination'] + '.meta')
            completed[meta.relative_to(recovery).as_posix()] = digest(meta)
            (recovery / 'Library/AvatarFork/crash-completed-files.json').write_text(json.dumps(completed), encoding='utf-8')
        manifest['dependencies']['com.ainoobmanpg.avatar-fork'] = 'file:' + args.package.resolve().as_posix()
        (recovery / 'Packages/manifest.json').write_text(json.dumps(manifest, indent=2), encoding='utf-8')
        result = subprocess.run([str(args.unity), '-batchmode', '-nographics', '-projectPath', str(recovery), '-executeMethod', 'AvatarForkRenameCrashRecovery.Verify', '-quit', '-logFile', str(recovery / 'verify.log')], timeout=240)
        if result.returncode:
            raise RuntimeError('Recovery verification failed: ' + str(recovery / 'verify.log'))
        verified = json.loads((recovery / 'Library/AvatarFork/crash-verified.json').read_text())
        assert verified['success']
        results[stage] = verified
        if stage == 'rootMove':
            results[stage]['interrupted'] = json.loads((recovery / 'Library/AvatarFork/interrupted-verified.json').read_text())
        (output / 'results.json').write_text(json.dumps(results, ensure_ascii=False, indent=2), encoding='utf-8')
        print(json.dumps({'stage': stage, **verified}, ensure_ascii=True), flush=True)


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--unity', type=Path, required=True)
    parser.add_argument('--template', type=Path, required=True)
    parser.add_argument('--package', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    run(parser.parse_args())
