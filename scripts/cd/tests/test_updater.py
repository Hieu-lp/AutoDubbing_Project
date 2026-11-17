import io
import json
import subprocess
from pathlib import Path
import sys
import tarfile
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import updater as cd
import create_release

PREFIX = 'ghcr.io/hieu-lp'
REVISION = 'a' * 40
MIGRATIONS = ['20261019154054_InitialCreate']
OLD = {'api': 'sha256:' + '1' * 64, 'user-web': 'sha256:' + '2' * 64}
IMAGES = {'api': PREFIX + '/autodubbing-api@sha256:' + '3' * 64,
          'user-web': PREFIX + '/autodubbing-user-web@sha256:' + '4' * 64}


def release():
    return {'format': 1, 'channel': 'develop', 'sequence': 12, 'revision': REVISION,
            'images': dict(IMAGES), 'migrations': list(MIGRATIONS)}


class ManifestTests(unittest.TestCase):
    def test_release_inspection_does_not_install_or_query_sql(self):
        with tempfile.TemporaryDirectory() as directory:
            config = Path(directory) / 'host.json'
            cd.write_json(config, {})
            info = {'Id': 'engine-specific-identifier', 'Os': 'linux', 'Architecture': 'amd64',
                    'RootFS': {'Layers': ['fixture-layer']},
                    'Config': {'Labels': {'org.opencontainers.image.revision': REVISION,
                                          'org.opencontainers.image.source': 'https://github.com/Hieu-lp/AutoDubbing_Project'}}}
            output = io.StringIO()
            with patch.object(sys, 'argv', ['updater.py', 'release', '--config', str(config)]), \
                 patch('updater.Host') as host_class, patch('updater.wait_for_docker'), \
                 patch('updater.run', return_value=b''), patch('updater.image_info', return_value=info), \
                 patch('sys.stdout', output):
                host_class.return_value.channel_release.return_value = release()
                self.assertEqual(cd.main(), 0)
                host_class.return_value.up.assert_not_called()
                host_class.return_value.sql_json.assert_not_called()
            self.assertFalse(json.loads(output.getvalue())['deployment_attempted'])

    def test_cold_docker_wait_retries_without_starting_or_changing_services(self):
        with patch('updater.run', side_effect=[cd.CdError('daemon starting'), b'29']), patch('updater.time.sleep'):
            cd.wait_for_docker()

    def test_image_identity_compares_platform_contents_not_index_vs_config_ids(self):
        platform = {'Os': 'linux', 'Architecture': 'amd64', 'RootFS': {'Layers': ['layer-a']},
                    'Config': {'Labels': {'org.opencontainers.image.revision': REVISION}}}
        index_view = dict(platform, Id='registry-index-id')
        config_view = dict(platform, Id='image-config-id')
        self.assertEqual(cd.platform_content_identity(index_view), cd.platform_content_identity(config_view))
        other = dict(platform, RootFS={'Layers': ['other-filesystem']})
        self.assertNotEqual(cd.platform_content_identity(index_view), cd.platform_content_identity(other))
    def test_requires_complete_digest_pinned_pair_from_expected_owner(self):
        self.assertEqual(cd.validate_release(release(), PREFIX)['images'], IMAGES)
        for replacement in ('ghcr.io/other/autodubbing-api@sha256:' + '3' * 64,
                            PREFIX + '/autodubbing-api:develop'):
            value = release()
            value['images']['api'] = replacement
            with self.assertRaises(cd.CdError):
                cd.validate_release(value, PREFIX)
        value = release()
        del value['images']['user-web']
        with self.assertRaises(cd.CdError):
            cd.validate_release(value, PREFIX)

    def test_invalid_sequence_or_migration_manifest_is_rejected(self):
        for key, value in [('sequence', True), ('sequence', 0), ('revision', 'develop'),
                           ('migrations', []), ('migrations', MIGRATIONS * 2)]:
            candidate = release()
            candidate[key] = value
            with self.assertRaises(cd.CdError):
                cd.validate_release(candidate, PREFIX)
        for candidate in (None, [], {'format': 1, 'channel': 'develop', 'revision': REVISION, 'sequence': 12, 'images': []}):
            with self.assertRaises(cd.CdError):
                cd.validate_release(candidate, PREFIX)

    def test_migrations_are_read_from_official_designer_attributes(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            migrations = root / 'AutoDubbing.Data/Migrations'
            migrations.mkdir(parents=True)
            (migrations / 'MisleadingName.Designer.cs').write_text('[Migration("20261019172708_Second")]', encoding='utf-8')
            (migrations / 'OtherName.Designer.cs').write_text('[Migration("20261019154054_First")]', encoding='utf-8')
            (migrations / 'ModelSnapshot.cs').write_text('Snapshot', encoding='utf-8')
            self.assertEqual(create_release.migration_ids(root), ['20261019154054_First', '20261019172708_Second'])
            (migrations / 'Broken.Designer.cs').write_text('Missing Migration attribute', encoding='utf-8')
            with self.assertRaises(ValueError):
                create_release.migration_ids(root)

    def test_connection_parser_preserves_quoted_semicolons(self):
        values = cd.connection_parts('Server=sqlserver,1433;Password="a;Server=other;";Database=AutoDubbingDb')
        self.assertEqual(values['server'], 'sqlserver,1433')
        self.assertEqual(values['database'], 'AutoDubbingDb')
        with self.assertRaises(cd.CdError):
            cd.connection_parts('Server="unfinished')

    def test_registry_error_does_not_print_raw_credentials(self):
        raw = subprocess.CompletedProcess(['docker', 'pull'], 1, b'', b'unauthorized: sensitive-example-token')
        with patch('updater.subprocess.run', return_value=raw):
            with self.assertRaises(cd.CdError) as caught:
                cd.run(['docker', 'pull', PREFIX + '/autodubbing-release:develop'])
        self.assertIn('GHCR denied access', str(caught.exception))
        self.assertNotIn('sensitive-example-token', str(caught.exception))


class DeploymentTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.path = Path(self.temp.name) / 'host.json'
        cd.write_json(self.path, {'project': 'test', 'registry_prefix': PREFIX, 'sql_volume': 'existing'})
        self.initial = {'sequence': 0, 'revision': None, 'current_images': dict(OLD), 'rejected': None}
        cd.write_json(self.path.parent / 'state.json', self.initial)
        self.host = cd.Host(self.path)
        self.containers = {key: {'Image': value} for key, value in OLD.items()}
        self.patches = [patch.object(self.host, 'guard', return_value=self.containers),
                        patch.object(self.host, 'containers', return_value=self.containers),
                        patch.object(self.host, 'database', return_value={'migrations': [{'MigrationId': x} for x in MIGRATIONS]}),
                        patch.object(self.host, 'resolved'), patch.object(self.host, 'probe', return_value={'poi_count': 8}),
                        patch.object(self.host, 'up'), patch.object(self.host, 'event'),
                        patch('updater.run', return_value=b''),
                        patch('updater.image_info', side_effect=lambda image: {'Id': OLD['api' if 'autodubbing-api' in image else 'user-web'],
                             'Os': 'linux', 'Architecture': 'amd64', 'RootFS': {'Layers': ['fixture-layer']},
                             'Config': {'Labels': {'org.opencontainers.image.revision': REVISION}}})]
        self.mocks = [p.start() for p in self.patches]
        for p in self.patches:
            self.addCleanup(p.stop)
        self.guard, self.containers_mock, self.database, self.resolved, self.probe, self.up, self.event, self.run, self.image = self.mocks

    def test_success_commits_both_images_only_after_real_data_probe(self):
        self.host.deploy(release())
        self.up.assert_called_once_with(IMAGES)
        self.assertEqual(self.host.state['sequence'], 12)
        self.assertEqual(self.host.state['current_images'], IMAGES)
        self.assertEqual(self.host.state['previous_images'], OLD)
        self.assertNotIn('pending', cd.read_json(self.host.state_path))
        self.assertEqual(self.probe.call_count, 2)

    def test_missing_migration_blocks_before_pull_and_recreation(self):
        self.database.return_value = {'migrations': []}
        with self.assertRaisesRegex(cd.CdError, 'Migration mismatch'):
            self.host.deploy(release())
        self.run.assert_not_called()
        self.up.assert_not_called()
        self.assertEqual(self.host.state, self.initial)

    def test_database_startup_wait_is_read_only_and_tolerates_recovery(self):
        self.database.side_effect = [cd.CdError('database recovering'), {'migrations': []}]
        with patch('updater.time.sleep'):
            self.assertEqual(self.host.wait_database(), {'migrations': []})
        self.run.assert_not_called()
        self.up.assert_not_called()

    def test_changed_env_blocks_before_pull_and_recreation(self):
        self.resolved.side_effect = cd.CdError('Compose/env configuration changed')
        with self.assertRaises(cd.CdError):
            self.host.deploy(release())
        self.up.assert_not_called()
        self.run.assert_not_called()

    def test_partial_image_pull_leaves_both_old_services_untouched(self):
        self.run.side_effect = [b'', cd.CdError('Registry denied second image')]
        with self.assertRaises(cd.CdError):
            self.host.deploy(release())
        self.up.assert_not_called()
        self.assertNotIn('pending', cd.read_json(self.host.state_path))

    def test_wrong_image_revision_is_rejected(self):
        self.image.side_effect = None
        self.image.return_value = {'Config': {'Labels': {'org.opencontainers.image.revision': 'b' * 40}}}
        with self.assertRaisesRegex(cd.CdError, 'revision differs'):
            self.host.deploy(release())
        self.up.assert_not_called()

    def test_failed_new_version_restores_exact_old_image_ids(self):
        self.probe.side_effect = [{'poi_count': 8}, cd.CdError('new API failed'), {'poi_count': 8}]
        with self.assertRaisesRegex(cd.CdError, 'previous API/web restored'):
            self.host.deploy(release())
        self.assertEqual(self.up.call_args_list[0].args[0], IMAGES)
        self.assertEqual(self.up.call_args_list[1].args[0], OLD)
        self.assertEqual(self.host.state['sequence'], 0)
        self.assertEqual(self.host.state['current_images'], OLD)
        self.assertEqual(self.host.state['rejected'], cd.digest(release()))
        self.assertNotIn('pending', cd.read_json(self.host.state_path))

    def test_failed_rollback_retains_crash_recovery_journal(self):
        self.up.side_effect = [None, cd.CdError('old API did not start')]
        self.probe.side_effect = [{'poi_count': 8}, cd.CdError('new API failed')]
        with self.assertRaisesRegex(cd.CdError, 'operator attention'):
            self.host.deploy(release())
        journal = cd.read_json(self.host.state_path)['pending']
        self.assertEqual(journal['previous_images'], OLD)

    def test_interrupted_update_is_rolled_back_before_next_release(self):
        self.host.state['pending'] = {'previous_images': dict(OLD), 'release_id': 'failed-id'}
        self.host.save()
        self.host.recover()
        self.up.assert_called_once_with(OLD)
        self.assertEqual(self.host.state['rejected'], 'failed-id')
        self.assertNotIn('pending', cd.read_json(self.host.state_path))

    def test_rollback_retains_manifest_refs_for_existing_release_sequence(self):
        self.host.state.update(sequence=11, revision='b' * 40, current_images=dict(IMAGES))
        self.probe.side_effect = [{'poi_count': 8}, cd.CdError('new API failed'), {'poi_count': 8}]
        with self.assertRaises(cd.CdError):
            self.host.deploy(release())
        self.assertEqual(self.host.state['current_images'], IMAGES)
        self.assertEqual(self.host.state['sequence'], 11)

    def test_failed_release_is_not_repeated_each_poll(self):
        self.host.state['rejected'] = cd.digest(release())
        with patch.object(self.host, 'channel_release', return_value=release()):
            self.host.check()
        self.up.assert_not_called()
        self.event.assert_called_once_with('waiting_for_new_release', message='Previously failed release is not retried automatically.')

    def test_downgrade_and_reused_sequence_are_blocked(self):
        self.host.state.update(sequence=13, revision=REVISION)
        with patch.object(self.host, 'channel_release', return_value=release()):
            with self.assertRaisesRegex(cd.CdError, 'Older release'):
                self.host.check()
        self.host.state.update(sequence=12, revision=REVISION)
        with patch.object(self.host, 'channel_release', return_value=release()):
            with self.assertRaisesRegex(cd.CdError, 'reused'):
                self.host.check()
        self.up.assert_not_called()

    def test_channel_descriptor_is_read_without_starting_its_container(self):
        buffer = io.BytesIO()
        raw = json.dumps(release()).encode()
        with tarfile.open(fileobj=buffer, mode='w') as archive:
            member = tarfile.TarInfo('release.json')
            member.size = len(raw)
            archive.addfile(member, io.BytesIO(raw))
        self.run.side_effect = [b'', b'descriptor-container', buffer.getvalue(), b'']
        self.image.side_effect = None
        self.image.return_value = {'Id': 'descriptor-image'}
        self.assertEqual(self.host.channel_release(), release())
        operations = [call.args[0][:2] for call in self.run.call_args_list]
        self.assertEqual(operations, [['docker', 'pull'], ['docker', 'create'], ['docker', 'cp'], ['docker', 'rm']])

    def test_compose_recreates_only_api_web_without_dependencies(self):
        self.host.config.update(project_directory=self.temp.name, env_file='local.env', compose_files=['base.yaml'])
        cd.Host.up(self.host, IMAGES)
        command = self.run.call_args.args[0]
        self.assertEqual(command[-2:], ['api', 'user-web'])
        self.assertIn('--no-deps', command)
        self.assertIn('--no-build', command)
        self.assertNotIn('down', command)
        self.assertNotIn('sqlserver', command)


if __name__ == '__main__':
    unittest.main()
