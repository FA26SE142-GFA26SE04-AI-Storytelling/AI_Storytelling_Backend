"""AI-only packaging, manifest rendering, deployment, and authenticated smoke tests.

Uses the current gcloud identity. Never prints credentials or generated story text.
"""
import argparse
import json
import os
from pathlib import Path
import re
import shutil
from string import Template
import subprocess
import tempfile
import urllib.error
import urllib.request

ROOT = Path(__file__).resolve().parents[2]
GCLOUD = shutil.which("gcloud") or shutil.which("gcloud.cmd") or "gcloud"


def gcloud(*args):
    return subprocess.check_output([GCLOUD, *args], text=True).strip()


def request(url, token=None, key=None, body=None):
    headers = {}
    if token:
        headers["Authorization"] = "Bearer " + token
    if key:
        headers["X-Internal-Api-Key"] = key
    data = None if body is None else json.dumps(body).encode()
    if data is not None:
        headers["Content-Type"] = "application/json"
    try:
        with urllib.request.urlopen(urllib.request.Request(url, data=data, headers=headers), timeout=290) as response:
            return response.status, response.read()
    except urllib.error.HTTPError as error:
        return error.code, error.read()


def smoke(args):
    if args.identity_token_env is not None:
        token = os.environ.get(args.identity_token_env, "").strip()
        if not token:
            raise RuntimeError("The selected environment variable must contain a non-empty ID token.")
    else:
        token_args = ["auth", "print-identity-token"]
        if args.identity_service_account:
            token_args += ["--impersonate-service-account=" + args.identity_service_account, "--audiences=" + args.url.rstrip("/")]
        token = gcloud(*token_args)
    key = gcloud("secrets", "versions", "access", args.secret_version, "--secret=" + args.secret,
                 "--project=" + args.project)
    url = args.url.rstrip("/")
    status, _ = request(url + "/health")
    if status not in (401, 403):
        raise RuntimeError(f"Anonymous traffic must be denied by Cloud Run IAM, got {status}")
    print("PASS: anonymous traffic is denied by Cloud Run IAM")
    for path in ("/health", "/swagger/index.html", "/swagger/v1/swagger.json"):
        status, body = request(url + path, token)
        if status != 200:
            raise RuntimeError(f"Authenticated {path}: HTTP {status}")
        if path.endswith(".json"):
            document = json.loads(body)
            schemes = document.get("components", {}).get("securitySchemes", {})
            if schemes.get("InternalApiKey", {}).get("name") != "X-Internal-Api-Key":
                raise RuntimeError("OpenAPI is missing internal-key authentication")
        print("PASS: authenticated " + path)
    for credential in (None, "invalid-test-only-key"):
        status, _ = request(url + "/api/ai/outline", token, credential, {})
        if status != 401:
            raise RuntimeError(f"Invalid internal credential: HTTP {status}")
    print("PASS: missing and invalid internal keys are denied")
    if args.live:
        fixture = json.loads((Path(__file__).parent / "smoke-outline.json").read_text(encoding="utf-8"))
        status, body = request(url + "/api/ai/outline", token, key, fixture)
        if status != 200:
            try:
                code = json.loads(body).get("errorCode")
            except (ValueError, AttributeError):
                code = "NON_JSON"
            raise RuntimeError(f"Vertex outline smoke: HTTP {status}, errorCode={code}")
        result = json.loads(body)
        if not result.get("title") or not all(result.get("outline", {}).get(k) for k in ("opening", "development", "ending")):
            raise RuntimeError("Vertex smoke did not return a complete outline")
        metadata = result.get("metadata", {})
        print("PASS: live Vertex outline; metadata=" + json.dumps(metadata, ensure_ascii=True))


def package(args):
    destination = Path(args.output).resolve()
    if destination.exists():
        raise RuntimeError("Package output must be a new directory")
    destination.mkdir(parents=True)
    count = 0
    for source in (ROOT / "src/AI", ROOT / "src/Shared/StoryPlatform.Contracts"):
        for file in source.rglob("*"):
            if not file.is_file() or file.is_symlink():
                continue
            relative = file.relative_to(ROOT)
            if any(p.lower() in ("bin", "obj", "secrets", ".git", ".codex") for p in relative.parts):
                continue
            if file.suffix.lower() not in (".cs", ".csproj", ".props", ".targets", ".resx") and file.name != "Dockerfile":
                continue
            target = destination / relative
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(file, target)
            count += 1
    config_dir = destination / "infra/google-cloud"
    config_dir.mkdir(parents=True)
    shutil.copyfile(Path(__file__).parent / "cloudbuild.ai.yaml", config_dir / "cloudbuild.ai.yaml")
    shutil.copyfile(Path(__file__).parent / ".gcloudignore", destination / ".gcloudignore")
    print(f"Prepared AI-only package: {count} source files at {destination}")


def deploy(args):
    if not re.fullmatch(r"[a-z0-9.-]+/[a-z0-9_./-]+@sha256:[a-f0-9]{64}", args.image):
        raise RuntimeError("Deploy requires a fully qualified immutable image digest")
    values = {
        "SERVICE": args.service, "PROJECT_ID": args.project, "IMAGE_DIGEST": args.image,
        "RUNTIME_SERVICE_ACCOUNT": args.runtime, "MODEL": args.model,
        "INTERNAL_KEY_SECRET": args.secret, "SECRET_VERSION": args.secret_version,
        "MIN_INSTANCES": "1" if args.environment == "production" else "0",
        "MAX_INSTANCES": "5" if args.environment == "production" else "2",
        "ASPNETCORE_ENVIRONMENT": "Production" if args.environment == "production" else "Staging",
    }
    # All substitutions are identifiers, never secret contents or arbitrary YAML.
    if any(not re.fullmatch(r"[A-Za-z0-9@_./:+-]+", value) for value in values.values()):
        raise RuntimeError("Unsafe deployment identifier")
    manifest = Template((Path(__file__).parent / "cloud-run.template.yaml").read_text()).substitute(values)
    with tempfile.TemporaryDirectory(prefix="taletale-ai-manifest-") as directory:
        path = Path(directory) / "service.yaml"
        path.write_text(manifest, encoding="utf-8")
        subprocess.run([GCLOUD, "run", "services", "replace", str(path), "--region=" + args.region,
                        "--project=" + args.project, "--quiet"], check=True)
    policy = json.loads(gcloud("run", "services", "get-iam-policy", args.service, "--region=" + args.region,
                              "--project=" + args.project, "--format=json"))
    if any(member in ("allUsers", "allAuthenticatedUsers") for binding in policy.get("bindings", [])
           for member in binding.get("members", [])):
        raise RuntimeError("Cloud Run policy is public; do not proceed with handoff")
    print("Cloud Run deployment completed; no public IAM binding")


def main():
    parser = argparse.ArgumentParser()
    commands = parser.add_subparsers(dest="command", required=True)
    p = commands.add_parser("package")
    p.add_argument("--output", required=True)
    p.set_defaults(func=package)
    for command in ("deploy", "smoke"):
        p = commands.add_parser(command)
        p.add_argument("--project", required=True)
        p.add_argument("--secret", required=True)
        p.add_argument("--secret-version", default="1")
        if command == "deploy":
            p.add_argument("--service", required=True)
            p.add_argument("--region", default="asia-southeast1")
            p.add_argument("--image", required=True)
            p.add_argument("--runtime", required=True)
            p.add_argument("--model", required=True)
            p.add_argument("--environment", choices=("staging", "production"), default="staging")
            p.set_defaults(func=deploy)
        else:
            p.add_argument("--url", required=True)
            identity = p.add_mutually_exclusive_group()
            identity.add_argument("--identity-token-env", help="Read an existing ID token from this environment variable (CI).")
            identity.add_argument("--identity-service-account", help="Explicit gcloud impersonation for local/admin testing only.")
            p.add_argument("--live", action="store_true")
            p.set_defaults(func=smoke)
    args = parser.parse_args()
    args.func(args)


if __name__ == "__main__":
    main()
