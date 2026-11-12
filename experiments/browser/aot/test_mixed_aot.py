"""Game-free checks for snapshot isolation, loader identity, and private output."""

import tempfile
import unittest
import xml.etree.ElementTree as ET
from pathlib import Path
from unittest.mock import patch

import mixed_aot as aot


class MixedAotTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.repo = Path(self.temporary.name)
        self.patch = patch.object(aot, 'REPO', self.repo)
        self.patch.start()
        self.addCleanup(self.patch.stop)
        self.runtime = self.repo / 'slopweb/runtime'
        self.runtime.mkdir(parents=True)
        real_runtime = Path(__file__).resolve().parents[3] / 'slopweb/runtime'
        # Real compile declarations catch duplicate Program.cs inclusion and loader drift.
        project = ET.parse(real_runtime / 'Probe.csproj')
        (self.runtime / 'Probe.csproj').write_bytes((real_runtime / 'Probe.csproj').read_bytes())
        for item in project.findall('.//Compile'):
            name = item.get('Include')
            (self.runtime / name).write_bytes((real_runtime / name).read_bytes())
        self.prepared = self.repo / 'prepared'
        self.prepared.mkdir()
        (self.prepared / 'main.js').write_bytes((real_runtime / 'wwwroot/main.js').read_bytes())
        for name in (
            'game/manifest.json',
            'game/Assembly-CSharp.dll',
            'mod/SlopWorld.dll',
            'Adapters.dll',
            *(f'game/{name}.dll' for name in aot.INTERPRETED),
            '_framework/dotnet.js',
            'assets/fonts/private-font.ttf',
            'assets/background/frame.bin',
        ):
            file = self.prepared / name
            file.parent.mkdir(parents=True, exist_ok=True)
            file.write_bytes(name.encode())
        self.sdk = self.repo / 'sdk/dotnet'
        self.sdk.parent.mkdir()
        self.sdk.touch()
        self.output = self.repo / 'experiments/browser/artifacts/mixed-test'

    def prepare(self):
        aot.prepare(self.prepared, self.output, self.sdk)

    def test_final_bytes_roots_properties_and_single_compile(self):
        self.prepare()
        candidate = self.output / 'candidate'
        self.assertEqual((candidate / 'assemblies/Assembly-CSharp.dll').read_bytes(), b'game/Assembly-CSharp.dll')
        project = ET.parse(candidate / 'Probe.csproj')
        roots = {item.get('Include') for item in project.findall('.//TrimmerRootAssembly')}
        self.assertTrue({'Probe', 'Assembly-CSharp', 'Adapters', 'SlopWorld', *aot.PACKAGES}.issubset(roots))
        self.assertEqual(project.findtext('.//WasmStripILAfterAOT'), 'false')
        self.assertEqual(project.findtext('.//JsonSerializerIsReflectionEnabledByDefault'), 'true')
        self.assertEqual(project.findtext('.//CompressionEnabled'), 'false')
        self.assertEqual(
            {
                item.get('Include'): item.get('Version')
                for item in project.findall('.//PackageReference')
                if item.get('Include') in aot.PACKAGES
            },
            aot.PACKAGES,
        )
        includes = [item.get('Include') for item in project.findall('.//Compile')]
        self.assertEqual(len(includes), len(set(includes)))
        self.assertEqual(includes.count('src/Program.cs'), 1)
        self.assertEqual((candidate / 'src/Program.cs').read_text().count('string LoadPublishedAssembly('), 1)
        self.assertNotIn('LoadPublishedAssembly', (self.runtime / 'Program.cs').read_text())
        targets = ET.parse(candidate / 'Directory.Build.targets')
        item = targets.find('.//WasmAssembliesToBundle')
        for owner in aot.INTERPRETED:
            self.assertIn(f"'{owner}'", item.get('Condition'))
        self.assertEqual(item.findtext('AOT_InternalForceToInterpret'), 'true')

    def test_loader_and_private_assets_survive_stage_without_source_mutation(self):
        original = (self.prepared / 'main.js').read_bytes()
        self.prepare()
        # Later shared preparation cannot change the captured assets or final IL.
        (self.prepared / 'assets/fonts/private-font.ttf').write_bytes(b'changed')
        framework = self.output / 'published/wwwroot/_framework'
        framework.mkdir(parents=True)
        (framework / 'dotnet.js').write_text('published runtime')
        (framework / 'dotnet.native.wasm').write_bytes(b'published wasm')
        (self.output / 'publish.complete').touch()
        aot.stage(self.output)
        staged = self.output / 'wwwroot'
        self.assertEqual((staged / 'assets/fonts/private-font.ttf').read_bytes(), b'assets/fonts/private-font.ttf')
        self.assertEqual((staged / '_framework/dotnet.js').read_text(), 'published runtime')
        self.assertEqual((self.prepared / 'main.js').read_bytes(), original)
        self.assertNotIn('Probe.LoadAssembly(', (staged / 'main.js').read_text())
        self.assertIn('LoadPublishedAssembly', (staged / 'main.js').read_text())
        self.assertIn('const bypassLogger = false;', (staged / 'main.js').read_text())
        with self.assertRaises(FileExistsError):
            aot.stage(self.output)

    def test_rejects_nonprivate_and_traversal_outputs(self):
        for output in (self.repo / 'tracked', self.output.parent, self.output / '../../../public'):
            with self.subTest(output=output), self.assertRaises(ValueError):
                aot.prepare(self.prepared, output, self.sdk)
        self.assertFalse(self.output.exists())

    def test_rejects_output_symlink_escape(self):
        self.output.parent.mkdir(parents=True)
        self.output.symlink_to(self.prepared, target_is_directory=True)
        with self.assertRaises(ValueError):
            self.prepare()

    def test_rejects_symlinked_artifacts_root(self):
        self.output.parent.parent.mkdir(parents=True, exist_ok=True)
        self.output.parent.symlink_to(self.prepared, target_is_directory=True)
        with self.assertRaises(ValueError):
            self.prepare()

    def test_rejects_input_symlinks(self):
        (self.prepared / 'private-link').symlink_to(self.sdk)
        with self.assertRaises(ValueError):
            self.prepare()
        self.assertFalse(self.output.exists())

    def test_rejects_overlap_and_existing_output_without_deleting_it(self):
        self.output.mkdir(parents=True)
        marker = self.output / 'keep'
        marker.write_text('keep')
        with self.assertRaises(FileExistsError):
            self.prepare()
        self.assertEqual(marker.read_text(), 'keep')
        with self.assertRaises(ValueError):
            aot.prepare(self.prepared, self.prepared / 'child', self.sdk)

    def test_duplicate_assembly_and_unknown_loader_fail_before_writing(self):
        (self.prepared / 'mod/Adapters.dll').write_bytes(b'other identity')
        with self.assertRaisesRegex(ValueError, 'Duplicate assembly'):
            self.prepare()
        (self.prepared / 'mod/Adapters.dll').unlink()
        with (self.prepared / 'main.js').open('a') as file:
            file.write('exports.Probe.LoadAssembly(unknown);')
        with self.assertRaisesRegex(ValueError, 'stream assembly load'):
            self.prepare()
        self.assertFalse(self.output.exists())

    def test_stage_requires_successful_publish(self):
        self.prepare()
        with self.assertRaises(ValueError):
            aot.stage(self.output)
        self.assertFalse((self.output / 'wwwroot').exists())

    def test_sdk_mismatch_is_rejected_before_publish(self):
        self.prepare()
        other = self.repo / 'other-sdk/dotnet'
        other.parent.mkdir()
        other.touch()
        with patch.object(aot.subprocess, 'run') as run:
            with self.assertRaises(ValueError):
                aot.publish(self.output, other)
            run.assert_not_called()
        self.assertFalse((self.output / 'publish.log').exists())

    def test_publish_uses_explicit_sdk_and_reuses_cache_after_failure(self):
        self.prepare()
        cached = self.output / 'candidate/cached.o'
        cached.write_bytes(b'expensive compilation')
        with patch.object(aot.subprocess, 'run') as run:
            aot.publish(self.output, self.sdk)
            command = run.call_args.args[0]
            self.assertEqual(command[0], str(self.sdk))
            self.assertIn('Release', command)
        for failure in (RuntimeError('failed compile'), KeyboardInterrupt()):
            with self.subTest(failure=type(failure).__name__):
                with (
                    patch.object(aot.subprocess, 'run', side_effect=failure),
                    self.assertRaises(type(failure)),
                ):
                    aot.publish(self.output, self.sdk)
                self.assertFalse((self.output / 'publish.complete').exists())
                with patch.object(aot.subprocess, 'run') as run:
                    aot.publish(self.output, self.sdk)
                    run.assert_called_once()
                self.assertTrue((self.output / 'publish.complete').exists())
                self.assertEqual(cached.read_bytes(), b'expensive compilation')
        self.assertEqual((self.output / 'publish.log').read_text().count('--- Publish attempt '), 5)

    def test_active_publish_rejects_publish_and_stage_without_mutation(self):
        self.prepare()
        complete = self.output / 'publish.complete'
        complete.touch()
        with aot.publish_lock(self.output), patch.object(aot.subprocess, 'run') as run:
            with self.assertRaisesRegex(RuntimeError, 'already active'):
                aot.publish(self.output, self.sdk)
            with self.assertRaisesRegex(RuntimeError, 'already active'):
                aot.stage(self.output)
            run.assert_not_called()
            self.assertTrue(complete.exists())
            self.assertFalse((self.output / 'publish.log').exists())
            self.assertFalse((self.output / 'wwwroot').exists())
        # Leaving the owning context releases the real flock.
        with patch.object(aot.subprocess, 'run') as run:
            aot.publish(self.output, self.sdk)
            run.assert_called_once()

    def test_stage_holds_publish_lock_during_copy(self):
        self.prepare()
        framework = self.output / 'published/wwwroot/_framework'
        framework.mkdir(parents=True)
        (framework / 'dotnet.js').touch()
        (framework / 'dotnet.native.wasm').touch()
        (self.output / 'publish.complete').touch()
        original_copytree = aot.shutil.copytree

        def copy_with_overlap(*args, **kwargs):
            with self.assertRaisesRegex(RuntimeError, 'already active'):
                aot.publish(self.output, self.sdk)
            return original_copytree(*args, **kwargs)

        with patch.object(aot.shutil, 'copytree', side_effect=copy_with_overlap):
            aot.stage(self.output)
        with aot.publish_lock(self.output):
            self.assertTrue((self.output / 'wwwroot').is_dir())

    def test_publish_failure_does_not_allow_staging(self):
        self.prepare()
        with (
            patch.object(aot.subprocess, 'run', side_effect=RuntimeError('failed compile')),
            self.assertRaises(RuntimeError),
        ):
            aot.publish(self.output, self.sdk)
        self.assertFalse((self.output / 'publish.complete').exists())
        with self.assertRaises(ValueError):
            aot.stage(self.output)


if __name__ == '__main__':
    unittest.main()
