"""BuildKit index versus Cloud Run platform-manifest regression coverage."""
from contextlib import redirect_stdout
import importlib.util
import io
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location('image_cloudrun', Path(__file__).resolve().parents[1] / 'cloudrun.py')
cloudrun = importlib.util.module_from_spec(spec)
spec.loader.exec_module(cloudrun)

INDEX = 'sha256:35bc46dc5bfebc293371091a08ee889b00602107a9d9207de46976cdff9b5ca2'
AMD64 = 'sha256:bb79d49f19963363e76437a49bcc56447788eb738f942ad40e40c60c68b2d1db'
MANIFEST_TYPE = 'application/vnd.oci.image.manifest.v1+json'


def descriptor(digest=AMD64, os='linux', architecture='amd64', **extra):
    return dict(mediaType=MANIFEST_TYPE, digest=digest,
                platform=dict(os=os, architecture=architecture), **extra)


def index(*manifests):
    return dict(schemaVersion=2, mediaType='application/vnd.oci.image.index.v1+json', manifests=list(manifests))


class ImageDigestTests(unittest.TestCase):
    def test_buildkit_index_resolves_to_cloud_run_amd64_manifest_not_attestation(self):
        attestation = descriptor('sha256:' + 'c' * 64, os='unknown', architecture='unknown',
                                 annotations={'vnd.docker.reference.type': 'attestation-manifest'})
        self.assertEqual(cloudrun.resolve_image_digest(index(descriptor(), attestation), INDEX), AMD64)

    def test_manifest_list_selects_linux_amd64_among_other_platforms(self):
        value = index(descriptor('sha256:' + 'a' * 64, architecture='arm64'), descriptor())
        value['mediaType'] = 'application/vnd.docker.distribution.manifest.list.v2+json'
        self.assertEqual(cloudrun.resolve_image_digest(value, INDEX), AMD64)

    def test_single_manifest_preserves_original_immutable_digest(self):
        for media_type in (MANIFEST_TYPE, 'application/vnd.docker.distribution.manifest.v2+json'):
            with self.subTest(media_type=media_type):
                value = dict(schemaVersion=2, mediaType=media_type, config={}, layers=[])
                self.assertEqual(cloudrun.resolve_image_digest(value, AMD64), AMD64)

    def test_missing_or_ambiguous_linux_amd64_is_rejected(self):
        for manifests in ([], [descriptor(architecture='arm64')], [descriptor(os='windows')],
                          [descriptor(), descriptor('sha256:' + 'a' * 64)],
                          [descriptor(annotations={'vnd.docker.reference.type': 'attestation-manifest'})]):
            with self.subTest(manifests=manifests), self.assertRaises(RuntimeError):
                cloudrun.resolve_image_digest(index(*manifests), INDEX)

    def test_invalid_platform_digest_or_non_image_descriptor_is_rejected(self):
        for bad in (descriptor(digest='latest'), descriptor(digest='sha256:1234'),
                    descriptor() | {'mediaType': 'application/vnd.oci.image.index.v1+json'}):
            with self.subTest(descriptor=bad), self.assertRaises(RuntimeError):
                cloudrun.resolve_image_digest(index(bad), INDEX)

    def test_invalid_input_never_falls_back_to_build_index(self):
        for value in (None, [], {}, {'schemaVersion': 1}, index() | {'manifests': None},
                      {'schemaVersion': 2, 'mediaType': 'unrecognized'}):
            with self.subTest(value=value), self.assertRaises(RuntimeError):
                cloudrun.resolve_image_digest(value, INDEX)
        with self.assertRaises(RuntimeError):
            cloudrun.resolve_image_digest(index(descriptor()), 'latest')

    def test_cli_outputs_only_the_resolved_digest(self):
        with tempfile.TemporaryDirectory() as directory:
            manifest = Path(directory) / 'manifest.json'
            manifest.write_text(json.dumps(index(descriptor())))
            output = io.StringIO()
            with patch.object(sys, 'argv', ['cloudrun.py', 'image-digest', '--manifest', str(manifest),
                                           '--build-digest', INDEX]), redirect_stdout(output):
                cloudrun.main()
            self.assertEqual(output.getvalue().strip(), AMD64)


if __name__ == '__main__':
    unittest.main()
