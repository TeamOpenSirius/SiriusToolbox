"""Offline regression checks for the shared MSBuild rolling-feed task."""
import subprocess
import tempfile
import zipfile
from pathlib import Path
from xml.sax.saxutils import escape

root = Path(__file__).resolve().parents[1]
(root / 'artifacts').mkdir(exist_ok=True)
with tempfile.TemporaryDirectory(prefix='rolling-feed-test-', dir=root / 'artifacts') as directory:
    work = Path(directory)
    staging = work / 'download'
    feed = work / 'feed'
    staging.mkdir()
    feed.mkdir()
    project = work / 'verify.proj'
    project.write_text(f'''<Project>
      <Import Project="{escape(str(root / 'Directory.Build.targets'))}" />
      <Target Name="Verify">
        <PrepareSiriusDataRollingFeed Feed="{escape(str(feed))}" Staging="{escape(str(staging))}" />
      </Target>
    </Project>''', encoding='utf-8')

    def package(filename, package_id, version):
        with zipfile.ZipFile(staging / (filename + '.nupkg'), 'w') as archive:
            archive.writestr(filename + '.nuspec',
                f'<package><metadata><id>{package_id}</id><version>{version}</version></metadata></package>')

    def run():
        return subprocess.run(['dotnet', 'msbuild', str(project), '-t:Verify', '-v:quiet'],
                              capture_output=True, text=True, encoding='utf-8', errors='replace')

    for name in ['Sirius.Protocol', 'Sirius.MasterData']:
        package(name, name, '1.0.2-ci.999')
    stale = feed / 'Sirius.Protocol.9.0.0.nupkg'
    stale.write_bytes(b'old')
    result = run()
    assert result.returncode == 0, result.stdout + result.stderr
    assert not stale.exists()
    assert (feed / 'Sirius.Protocol.1.0.2-ci.999.nupkg').is_file()
    assert (feed / 'resolved-version.txt').read_text().strip() == '1.0.2-ci.999'
    good = {p.name: p.read_bytes() for p in feed.iterdir()}

    for package_id, version in [('Sirius.MasterData', '1.0.2-ci.1000'),
                                ('Wrong.Package', '1.0.2-ci.999'),
                                ('Sirius.MasterData', '../bad')]:
        package('Sirius.MasterData', package_id, version)
        result = run()
        assert result.returncode != 0, 'Invalid package was accepted'
        assert {p.name: p.read_bytes() for p in feed.iterdir()} == good, 'Validation damaged the prior feed'
    print('PASS: prerelease filenames, stale-feed removal, version mismatch, wrong identity, path rejection, preserved prior feed')
