"""Offline production rollout tests: no cloud mutation or provider calls."""
import argparse
from contextlib import redirect_stdout
import importlib.util
import io
import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location('rollout_cloudrun', Path(__file__).resolve().parents[1] / 'cloudrun.py')
cloudrun = importlib.util.module_from_spec(spec)
spec.loader.exec_module(cloudrun)


class ProductionRolloutTests(unittest.TestCase):
    def args(self, path):
        return argparse.Namespace(project='test-project', region='asia-southeast1', service='test-production',
            image='asia-docker.pkg.dev/test-project/repo/api@sha256:' + 'a' * 64,
            runtime='runtime@test-project.iam.gserviceaccount.com', model='test-model',
            secret='production-key', secret_version='1', environment='production', bootstrap_production=False,
            candidate_tag='candidate', revision_suffix='gh-12-1', rollout_state=str(path))

    def old_service(self):
        return {'status': {'url': 'https://service.run.app', 'traffic': [
            {'revisionName': 'old-revision', 'percent': 100}]}}

    def candidate_service(self):
        service = self.old_service()
        service['status']['traffic'].append({'revisionName': 'test-production-gh-12-1',
            'tag': 'candidate', 'percent': 0, 'url': 'https://candidate-service.run.app'})
        return service

    def revision(self, image):
        return {'spec': {'containers': [{'image': image}]},
                'status': {'conditions': [{'type': 'Ready', 'status': 'True'}]}}

    def test_candidate_preserves_traffic_and_pins_the_built_digest(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'state.json'
            args = self.args(path)
            with patch.object(cloudrun, 'service_description', side_effect=[self.old_service(), self.candidate_service()]), \
                    patch.object(cloudrun, 'require_private'), \
                    patch.object(cloudrun.subprocess, 'run') as run, \
                    patch.object(cloudrun, 'gcloud', return_value=json.dumps(self.revision(args.image))), \
                    redirect_stdout(io.StringIO()):
                cloudrun.deploy(args)
            command = run.call_args.args[0]
            self.assertIn('--no-traffic', command)
            self.assertIn('--invoker-iam-check', command)
            self.assertIn('--image=' + args.image, command)
            self.assertIn('--min-instances=0', command)
            state = json.loads(path.read_text())
            self.assertFalse(state['verified'])
            self.assertEqual(state['previous_traffic'], {'old-revision': 100})
            self.assertEqual(state['audience'], 'https://service.run.app')
            self.assertNotEqual(state['candidate_url'], state['audience'])

    def test_no_promotion_without_successful_smoke(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'state.json'
            path.write_text(json.dumps({'verified': False}))
            with patch.object(cloudrun, 'gcloud') as cli, self.assertRaisesRegex(RuntimeError, 'smoke'):
                cloudrun.promote(self.args(path))
            cli.assert_not_called()

    def verified_state(self, args):
        return dict(project=args.project, service=args.service, region=args.region,
            revision='test-production-gh-12-1', tag='candidate', image=args.image,
            previous_traffic={'old-revision': 100}, candidate_url='https://candidate-service.run.app',
            audience='https://service.run.app', verified=True, secret=args.secret, secret_version=args.secret_version,
            model=args.model, vertex_verification={'provider': 'VertexAI', 'model': args.model})

    def test_traffic_drift_prevents_promotion(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'state.json'; args = self.args(path)
            path.write_text(json.dumps(self.verified_state(args)))
            with patch.object(cloudrun, 'require_private'), \
                    patch.object(cloudrun, 'service_description', return_value={'status': {'traffic': [
                        {'revisionName': 'other-revision', 'percent': 100}]}}), \
                    patch.object(cloudrun, 'gcloud') as cli, self.assertRaisesRegex(RuntimeError, 'traffic changed'):
                cloudrun.promote(args)
            cli.assert_not_called()

    def test_promotion_targets_exact_revision_not_latest(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'state.json'; args = self.args(path)
            path.write_text(json.dumps(self.verified_state(args)))
            after = {'status': {'traffic': [{'revisionName': 'test-production-gh-12-1', 'percent': 100}]}}
            with patch.object(cloudrun, 'require_private'), \
                    patch.object(cloudrun, 'service_description', side_effect=[self.candidate_service(), after]), \
                    patch.object(cloudrun, 'gcloud', side_effect=[json.dumps(self.revision(args.image)), '']) as cli, \
                    redirect_stdout(io.StringIO()):
                cloudrun.promote(args)
            self.assertIn('--to-revisions=test-production-gh-12-1=100', cli.call_args_list[1].args)

    def test_changed_image_prevents_promotion(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'state.json'; args = self.args(path)
            path.write_text(json.dumps(self.verified_state(args)))
            with patch.object(cloudrun, 'require_private'), \
                    patch.object(cloudrun, 'service_description', return_value=self.candidate_service()), \
                    patch.object(cloudrun, 'gcloud', return_value=json.dumps(self.revision('different-image'))) as cli, \
                    self.assertRaisesRegex(RuntimeError, 'image'):
                cloudrun.promote(args)
            self.assertEqual(cli.call_count, 1)

    def test_smoke_rejects_wrong_candidate_and_clears_previous_verification(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'state.json'; args = self.args(path)
            path.write_text(json.dumps(self.verified_state(args)))
            args.url = 'https://wrong-service.run.app'; args.live = True
            with patch.object(cloudrun, 'gcloud') as cli, self.assertRaisesRegex(RuntimeError, 'exact candidate'):
                cloudrun.smoke(args)
            cli.assert_not_called()
            self.assertFalse(json.loads(path.read_text())['verified'])

    def test_bootstrap_cannot_overwrite_existing_production(self):
        with tempfile.TemporaryDirectory() as directory:
            args = self.args(Path(directory) / 'state.json'); args.bootstrap_production = True
            with patch.object(cloudrun, 'service_description', return_value=self.old_service()), \
                    patch.object(cloudrun.subprocess, 'run') as run, self.assertRaisesRegex(RuntimeError, 'new production'):
                cloudrun.deploy(args)
            run.assert_not_called()

    def test_smoke_success_marks_only_the_verified_candidate(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'state.json'; args = self.args(path)
            state = self.verified_state(args); state['verified'] = False
            path.write_text(json.dumps(state))
            args.url = state['candidate_url']; args.live = True
            args.identity_token_env = 'AI_TEST_ID_TOKEN'; args.identity_service_account = None
            document = {'components': {'securitySchemes': {'InternalApiKey': {'name': 'X-Internal-Api-Key'}}}}
            responses = [(403, b''), (200, b'healthy'), (200, b'html'), (200, json.dumps(document).encode()),
                         (401, b''), (401, b'')]
            def request(url, token=None, key=None, body=None):
                if responses:
                    return responses.pop(0)
                outline = dict(title='Test', outline=dict(opening='A', development='B', ending='C'),
                    requestId=body['requestId'], generationId='test-generation',
                    metadata=dict(modelProvider='VertexAI', model=args.model, modelVersion='model-version',
                                  inputTokens=100, outputTokens=30, latencyMs=1000))
                return 200, json.dumps(outline).encode()
            with patch.dict(os.environ, {'AI_TEST_ID_TOKEN': 'test-token'}), \
                    patch.object(cloudrun, 'gcloud', return_value='test-key'), \
                    patch.object(cloudrun, 'request', side_effect=request), redirect_stdout(io.StringIO()):
                cloudrun.smoke(args)
            self.assertTrue(json.loads(path.read_text())['verified'])
            self.assertEqual(json.loads(path.read_text())['vertex_verification']['input_tokens'], 100)

    def test_promote_rejects_old_smoke_without_vertex_evidence(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'state.json'; args = self.args(path)
            state = self.verified_state(args); state.pop('vertex_verification')
            path.write_text(json.dumps(state))
            with patch.object(cloudrun, 'gcloud') as cli, self.assertRaisesRegex(RuntimeError, 'Vertex/model evidence'):
                cloudrun.promote(args)
            cli.assert_not_called()

    def test_vertex_verification_rejects_wrong_provider_model_and_missing_usage(self):
        valid = dict(requestId='probe-1', generationId='generation-1', metadata=dict(modelProvider='VertexAI',
            model='test-model', modelVersion='version-1', inputTokens=100, outputTokens=30, latencyMs=1000))
        for changes in ({'modelProvider': 'mock'}, {'model': 'wrong-model'}, {'inputTokens': 0},
                        {'outputTokens': None}, {'latencyMs': float('nan')}, {'modelVersion': ''}):
            with self.subTest(changes=changes):
                result = valid | {'metadata': valid['metadata'] | changes}
                with self.assertRaises(RuntimeError):
                    cloudrun.validate_vertex_result(result, 'probe-1', 'test-model')
        with self.assertRaisesRegex(RuntimeError, 'identifiers'):
            cloudrun.validate_vertex_result(valid, 'different-request', 'test-model')

    def test_failed_smoke_never_leaves_previous_success_in_state(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'state.json'; args = self.args(path)
            state = self.verified_state(args); path.write_text(json.dumps(state))
            args.url = state['candidate_url']; args.live = True
            args.identity_token_env = 'AI_TEST_ID_TOKEN'; args.identity_service_account = None
            with patch.dict(os.environ, {'AI_TEST_ID_TOKEN': 'test-token'}), \
                    patch.object(cloudrun, 'gcloud', return_value='test-key'), \
                    patch.object(cloudrun, 'request', return_value=(200, b'')), \
                    self.assertRaisesRegex(RuntimeError, 'Anonymous'):
                cloudrun.smoke(args)
            self.assertFalse(json.loads(path.read_text())['verified'])


if __name__ == '__main__':
    unittest.main()
