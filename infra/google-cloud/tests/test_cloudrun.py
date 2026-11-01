"""Offline regression tests for Cloud Run smoke authentication (no Google calls)."""
import argparse
from contextlib import redirect_stderr, redirect_stdout
import importlib.util
import io
import json
import os
from pathlib import Path
import sys
import unittest
from unittest.mock import MagicMock, patch


SPEC = importlib.util.spec_from_file_location("cloudrun", Path(__file__).resolve().parents[1] / "cloudrun.py")
cloudrun = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(cloudrun)


class SmokeAuthenticationTests(unittest.TestCase):
    TOKEN = "test-only-identity-token"
    KEY = "test-only-internal-key"
    URL = "https://test-service.run.app"

    def args(self, **overrides):
        values = dict(project="test-project", secret="test-secret", secret_version="1",
                      url=self.URL + "/", identity_token_env=None,
                      identity_service_account=None, live=False)
        return argparse.Namespace(**(values | overrides))

    def responses(self):
        document = {"components": {"securitySchemes": {"InternalApiKey": {"name": "X-Internal-Api-Key"}}}}
        return [(403, b""), (200, b"Healthy"), (200, b"<html>Swagger</html>"),
                (200, json.dumps(document).encode()), (401, b""), (401, b"")]

    def test_environment_token_is_used_without_gcloud_token_or_impersonation(self):
        output = io.StringIO()
        with patch.dict(os.environ, {"AI_SMOKE_ID_TOKEN": self.TOKEN}, clear=True), \
                patch.object(cloudrun, "gcloud", return_value=self.KEY) as cli, \
                patch.object(cloudrun, "request", side_effect=self.responses()) as request, \
                redirect_stdout(output):
            cloudrun.smoke(self.args(identity_token_env="AI_SMOKE_ID_TOKEN"))
        cli.assert_called_once_with("secrets", "versions", "access", "1",
                                    "--secret=test-secret", "--project=test-project")
        self.assertEqual(request.call_args_list[0].args, (self.URL + "/health",))
        for call in request.call_args_list[1:]:
            self.assertEqual(call.args[1], self.TOKEN)
        self.assertNotIn(self.TOKEN, output.getvalue())
        self.assertNotIn(self.KEY, output.getvalue())

    def test_request_places_identity_token_and_internal_key_in_headers(self):
        response = MagicMock()
        response.__enter__.return_value.status = 200
        response.__enter__.return_value.read.return_value = b"{}"
        with patch.object(cloudrun.urllib.request, "urlopen", return_value=response) as open_url:
            cloudrun.request(self.URL + "/api/ai/outline", self.TOKEN, self.KEY, {})
        sent = open_url.call_args.args[0]
        headers = {key.lower(): value for key, value in sent.header_items()}
        self.assertEqual(headers["authorization"], "Bearer " + self.TOKEN)
        self.assertEqual(headers["x-internal-api-key"], self.KEY)

    def test_missing_empty_or_whitespace_environment_token_fails_without_fallback(self):
        for value in (None, "", "  \t "):
            with self.subTest(value=value):
                environment = {} if value is None else {"AI_SMOKE_ID_TOKEN": value}
                with patch.dict(os.environ, environment, clear=True), \
                        patch.object(cloudrun, "gcloud") as cli, \
                        patch.object(cloudrun, "request") as request, \
                        self.assertRaisesRegex(RuntimeError, "ID token"):
                    cloudrun.smoke(self.args(identity_token_env="AI_SMOKE_ID_TOKEN"))
                cli.assert_not_called()
                request.assert_not_called()

    def test_cli_rejects_conflicting_token_sources_before_any_google_call(self):
        argv = ["cloudrun.py", "smoke", "--project", "test-project", "--secret", "test-secret",
                "--url", self.URL, "--identity-token-env", "AI_SMOKE_ID_TOKEN",
                "--identity-service-account", "test-deployer@test-project.iam.gserviceaccount.com"]
        diagnostic = io.StringIO()
        with patch.object(sys, "argv", argv), patch.object(cloudrun, "gcloud") as cli, \
                redirect_stderr(diagnostic), self.assertRaises(SystemExit) as error:
            cloudrun.main()
        self.assertEqual(error.exception.code, 2)
        self.assertIn("not allowed with argument", diagnostic.getvalue())
        cli.assert_not_called()

    def test_local_default_and_explicit_impersonation_remain_available(self):
        for service_account in (None, "local-test@test-project.iam.gserviceaccount.com"):
            with self.subTest(service_account=service_account):
                with patch.object(cloudrun, "gcloud", side_effect=[self.TOKEN, self.KEY]) as cli, \
                        patch.object(cloudrun, "request", side_effect=self.responses()), \
                        redirect_stdout(io.StringIO()):
                    cloudrun.smoke(self.args(identity_service_account=service_account))
                expected = ["auth", "print-identity-token"]
                if service_account:
                    expected += ["--impersonate-service-account=" + service_account,
                                 "--audiences=" + self.URL]
                self.assertEqual(cli.call_args_list[0].args, tuple(expected))

    def test_http_failure_does_not_echo_token_key_or_response_body(self):
        output = io.StringIO()
        with patch.dict(os.environ, {"AI_SMOKE_ID_TOKEN": self.TOKEN}, clear=True), \
                patch.object(cloudrun, "gcloud", return_value=self.KEY), \
                patch.object(cloudrun, "request", side_effect=[(403, b""), (403, (self.TOKEN + self.KEY).encode())]), \
                redirect_stdout(output), self.assertRaises(RuntimeError) as error:
            cloudrun.smoke(self.args(identity_token_env="AI_SMOKE_ID_TOKEN"))
        diagnostic = output.getvalue() + str(error.exception)
        self.assertIn("HTTP 403", diagnostic)
        self.assertNotIn(self.TOKEN, diagnostic)
        self.assertNotIn(self.KEY, diagnostic)


if __name__ == "__main__":
    unittest.main()
