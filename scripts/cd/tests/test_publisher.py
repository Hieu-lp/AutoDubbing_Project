import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import create_release as publisher
import updater

REVISION = 'a' * 40
PREFIX = 'ghcr.io/hieu-lp'
API = PREFIX + '/autodubbing-api@sha256:' + '1' * 64
WEB = PREFIX + '/autodubbing-user-web@sha256:' + '2' * 64


class PublisherTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        migrations = self.root / 'AutoDubbing.Data/Migrations'
        migrations.mkdir(parents=True)
        (migrations / 'Initial.Designer.cs').write_text('[Migration("20261019154054_InitialCreate")]', encoding='utf-8')
        self.revision_patch = patch('create_release.source_revision', return_value=REVISION)
        self.revision_patch.start()
        self.addCleanup(self.revision_patch.stop)

    def test_output_is_accepted_by_real_updater_contract(self):
        release = publisher.build_release(self.root, REVISION, 17, 1, API, WEB)
        self.assertEqual(updater.validate_release(release, PREFIX), release)
        self.assertEqual(release['sequence'], 17001)
        self.assertEqual(release['images'], {'api': API, 'user-web': WEB})
        self.assertEqual(release['migrations'], ['20261019154054_InitialCreate'])

    def test_rerun_has_new_sequence_without_overtaking_next_run(self):
        self.assertGreater(publisher.release_sequence(17, 2), publisher.release_sequence(17, 1))
        self.assertGreater(publisher.release_sequence(18, 1), publisher.release_sequence(17, 999))
        for number, attempt in ((0, 1), (True, 1), (17, 0), (17, 1000)):
            with self.assertRaises(ValueError):
                publisher.release_sequence(number, attempt)

    def test_wrong_checkout_revision_is_rejected(self):
        with self.assertRaisesRegex(ValueError, 'checked-out source'):
            publisher.build_release(self.root, 'b' * 40, 17, 1, API, WEB)

    def test_mixed_owners_wrong_service_or_mutable_tag_is_rejected(self):
        for web in ('ghcr.io/other/autodubbing-user-web@sha256:' + '2' * 64,
                    API, PREFIX + '/autodubbing-user-web:develop'):
            with self.assertRaises(ValueError):
                publisher.build_release(self.root, REVISION, 17, 1, API, web)

    def test_cli_writes_only_the_descriptor_and_scratch_dockerfile(self):
        output = self.root / 'output'
        arguments = ['create_release.py', '--revision', REVISION, '--run-number', '17', '--run-attempt', '2',
                     '--api-image', API, '--web-image', WEB, '--out', str(output)]
        with patch.object(sys, 'argv', arguments), patch('create_release.migration_ids', return_value=['20261019154054_InitialCreate']):
            publisher.main()
        self.assertEqual(sorted(p.name for p in output.iterdir()), ['Dockerfile', 'release.json'])
        release = json.loads((output / 'release.json').read_text(encoding='utf-8'))
        updater.validate_release(release, PREFIX)
        self.assertEqual(release['sequence'], 17002)
        self.assertEqual(release['revision'], REVISION)
        self.assertIn('COPY release.json /release.json', (output / 'Dockerfile').read_text())


if __name__ == '__main__':
    unittest.main()
