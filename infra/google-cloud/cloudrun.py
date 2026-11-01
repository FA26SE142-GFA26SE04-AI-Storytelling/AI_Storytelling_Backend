"""AI-only packaging, manifest rendering, deployment, and authenticated smoke tests.

Uses the current gcloud identity. Never prints credentials or generated story text.
"""
import argparse
import json
import math
import os
from pathlib import Path
import re
import shutil
from string import Template
import subprocess
import tempfile
import uuid
from datetime import datetime, timezone
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
    state_path = getattr(args, "rollout_state", None)
    state = json.loads(Path(state_path).read_text()) if state_path else None
    if state is not None:
        state['verified'] = False
        state.pop('vertex_verification', None)
        Path(state_path).write_text(json.dumps(state, indent=2), encoding='utf-8')
    if state and (not args.live or args.url.rstrip('/') != state['candidate_url'] or
                  args.project != state['project'] or args.secret != state['secret'] or
                  args.secret_version != state['secret_version']):
        raise RuntimeError("Rollout smoke must target the exact candidate, project and secret with --live.")
    if args.identity_token_env is not None:
        token = os.environ.get(args.identity_token_env, "").strip()
        if not token:
            raise RuntimeError("The selected environment variable must contain a non-empty ID token.")
    else:
        token_args = ["auth", "print-identity-token"]
        if args.identity_service_account:
            token_args += ["--impersonate-service-account=" + args.identity_service_account,
                           "--audiences=" + (getattr(args, "audience", None) or args.url.rstrip("/"))]
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
        fixture['requestId'] = 'vertex-verification-' + uuid.uuid4().hex
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
        expected_model = getattr(args, 'expected_model', None) or (state or {}).get('model')
        if state is not None or getattr(args, 'require_vertex_metadata', False) or expected_model:
            proof = validate_vertex_result(result, fixture['requestId'], expected_model)
            if state is not None:
                state['vertex_verification'] = proof
            print('PASS: real Vertex integration; evidence=' + json.dumps(proof, ensure_ascii=True))
        else:
            print("PASS: live Vertex outline; metadata=" + json.dumps(metadata, ensure_ascii=True))
    if state is not None:
        state['verified'] = True
        Path(state_path).write_text(json.dumps(state, indent=2), encoding='utf-8')


def validate_vertex_result(result, request_id, expected_model):
    metadata = result.get('metadata')
    if not isinstance(metadata, dict) or metadata.get('modelProvider') != 'VertexAI':
        raise RuntimeError('Vertex integration failed: response did not identify VertexAI as provider.')
    if not expected_model or metadata.get('model') != expected_model:
        raise RuntimeError('Vertex integration failed: response model does not match the configured model.')
    if result.get('requestId') != request_id or not result.get('generationId') or not metadata.get('modelVersion'):
        raise RuntimeError('Vertex integration failed: missing or mismatched generation identifiers.')
    for field in ('inputTokens', 'outputTokens', 'latencyMs'):
        value = metadata.get(field)
        if isinstance(value, bool) or not isinstance(value, (int, float)) or not math.isfinite(value) or value <= 0:
            raise RuntimeError('Vertex integration failed: missing positive usage/timing field ' + field)
    return dict(checked_at=datetime.now(timezone.utc).isoformat(), request_id=request_id,
                generation_id=result['generationId'], provider=metadata['modelProvider'], model=metadata['model'],
                model_version=metadata['modelVersion'], input_tokens=metadata['inputTokens'],
                output_tokens=metadata['outputTokens'], latency_ms=metadata['latencyMs'])


def verify_vertex(args):
    args.live = True
    args.require_vertex_metadata = True
    smoke(args)


def service_description(args, allow_missing=False):
    command = [GCLOUD, 'run', 'services', 'describe', args.service, '--project=' + args.project,
               '--region=' + args.region, '--format=json']
    result = subprocess.run(command, capture_output=True, text=True)
    if result.returncode:
        if allow_missing and 'NOT_FOUND' in result.stderr:
            return None
        raise RuntimeError('Cannot read Cloud Run service; check its existence and deployment permissions.')
    return json.loads(result.stdout)


def require_private(args):
    policy = json.loads(gcloud('run', 'services', 'get-iam-policy', args.service, '--region=' + args.region,
                              '--project=' + args.project, '--format=json'))
    if any(member in ('allUsers', 'allAuthenticatedUsers') for binding in policy.get('bindings', [])
           for member in binding.get('members', [])):
        raise RuntimeError('Cloud Run policy is public; rollout stopped.')


def allocated_traffic(service):
    traffic = service.get('status', {}).get('traffic', [])
    allocations = {}
    for entry in traffic:
        if entry.get('percent', 0):
            revision = entry.get('revisionName')
            if not revision:
                raise RuntimeError('Traffic has no resolved revision; rollout stopped.')
            allocations[revision] = allocations.get(revision, 0) + entry['percent']
    return allocations


def promote(args):
    state = json.loads(Path(args.rollout_state).read_text())
    if state.get('verified') is not True:
        raise RuntimeError('Candidate has not passed live smoke; traffic unchanged.')
    evidence = state.get('vertex_verification', {})
    if evidence.get('provider') != 'VertexAI' or not state.get('model') or evidence.get('model') != state['model']:
        raise RuntimeError('Candidate has no verified Vertex/model evidence; traffic unchanged.')
    if (args.project, args.service, args.region) != (state['project'], state['service'], state['region']):
        raise RuntimeError('Rollout state targets a different service.')
    require_private(args)
    service = service_description(args)
    if allocated_traffic(service) != state['previous_traffic']:
        raise RuntimeError('Serving traffic changed since deployment; refusing to overwrite it.')
    if validate_candidate(args, service, state['revision'], state['tag'], state['image']) != state['candidate_url']:
        raise RuntimeError('Candidate URL changed after smoke; refusing promotion.')
    gcloud('run', 'services', 'update-traffic', args.service, '--project=' + args.project,
           '--region=' + args.region, '--to-revisions=' + state['revision'] + '=100', '--quiet')
    if allocated_traffic(service_description(args)) != {state['revision']: 100}:
        raise RuntimeError('Traffic verification failed; inspect the recorded previous traffic for rollback.')
    print('Production traffic now serves the smoke-tested revision: ' + state['revision'])


def validate_candidate(args, service, revision, tag, image):
    tagged = next((entry for entry in service.get('status', {}).get('traffic', [])
                   if entry.get('tag') == tag and entry.get('revisionName') == revision), None)
    if not tagged or not tagged.get('url'):
        raise RuntimeError('Candidate tag does not point to the expected revision.')
    description = json.loads(gcloud('run', 'revisions', 'describe', revision, '--project=' + args.project,
                                   '--region=' + args.region, '--format=json'))
    if not any(c.get('type') == 'Ready' and c.get('status') == 'True'
               for c in description.get('status', {}).get('conditions', [])):
        raise RuntimeError('Candidate revision is not Ready.')
    if description.get('spec', {}).get('containers', [{}])[0].get('image') != image:
        raise RuntimeError('Candidate image does not match the immutable image built by this run.')
    return tagged['url']


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
    if not re.fullmatch(r'[1-9][0-9]*', args.secret_version):
        raise RuntimeError('Deploy requires a numeric Secret Manager version, not latest.')
    if not re.fullmatch(r"[a-z0-9.-]+/[a-z0-9_./-]+@sha256:[a-f0-9]{64}", args.image):
        raise RuntimeError("Deploy requires a fully qualified immutable image digest")
    values = {
        "SERVICE": args.service, "PROJECT_ID": args.project, "IMAGE_DIGEST": args.image,
        "RUNTIME_SERVICE_ACCOUNT": args.runtime, "MODEL": args.model,
        "INTERNAL_KEY_SECRET": args.secret, "SECRET_VERSION": args.secret_version,
        "MIN_INSTANCES": "0",
        "MAX_INSTANCES": "2",
        "ASPNETCORE_ENVIRONMENT": "Production" if args.environment == "production" else "Staging",
    }
    # All substitutions are identifiers, never secret contents or arbitrary YAML.
    if any(not re.fullmatch(r"[A-Za-z0-9@_./:+-]+", value) for value in values.values()):
        raise RuntimeError("Unsafe deployment identifier")
    if args.environment == 'production' and not getattr(args, 'bootstrap_production', False):
        return deploy_candidate(args, values)
    if getattr(args, 'bootstrap_production', False):
        if args.environment != 'production' or service_description(args, allow_missing=True) is not None:
            raise RuntimeError('Bootstrap is only for a new production service; use candidate rollout for updates.')
    manifest = Template((Path(__file__).parent / "cloud-run.template.yaml").read_text()).substitute(values)
    with tempfile.TemporaryDirectory(prefix="taletale-ai-manifest-") as directory:
        path = Path(directory) / "service.yaml"
        path.write_text(manifest, encoding="utf-8")
        subprocess.run([GCLOUD, "run", "services", "replace", str(path), "--region=" + args.region,
                        "--project=" + args.project, "--quiet"], check=True)
    require_private(args)
    print("Cloud Run deployment completed; no public IAM binding")


def deploy_candidate(args, values):
    tag, suffix, output = args.candidate_tag, args.revision_suffix, args.rollout_state
    if not tag or not suffix or not output or not all(re.fullmatch(r'[a-z][a-z0-9-]*', value) for value in (tag, suffix)):
        raise RuntimeError('Production requires --candidate-tag, --revision-suffix and --rollout-state.')
    revision = args.service + '-' + suffix
    if len(revision) > 63 or revision.endswith('-') or tag.endswith('-'):
        raise RuntimeError('Invalid candidate revision or tag.')
    previous = service_description(args)
    require_private(args)
    previous_traffic = allocated_traffic(previous)
    if sum(previous_traffic.values()) != 100:
        raise RuntimeError('Production must have an existing resolved traffic allocation; bootstrap first.')
    environment = {
        'ASPNETCORE_ENVIRONMENT': 'Production', 'Swagger__Enabled': 'true', 'AI__Provider': 'Vertex',
        'AI__Google__AuthMode': 'Adc', 'AI__Google__ProjectId': args.project,
        'AI__Google__Location': 'global', 'AI__Google__Model': args.model,
        'AI__Google__TimeoutSeconds': '120', 'Logging__LogLevel__Default': 'Information',
        'Logging__LogLevel__Microsoft.AspNetCore': 'Warning',
    }
    with tempfile.TemporaryDirectory(prefix='taletale-ai-env-') as directory:
        path = Path(directory) / 'env.json'
        path.write_text(json.dumps(environment), encoding='utf-8')
        subprocess.run([GCLOUD, 'run', 'deploy', args.service, '--project=' + args.project,
                        '--region=' + args.region, '--image=' + args.image, '--service-account=' + args.runtime,
                        '--no-traffic', '--tag=' + tag, '--revision-suffix=' + suffix, '--port=8080',
                        '--cpu=1', '--memory=1Gi', '--concurrency=4', '--timeout=300',
                        '--min-instances=' + values['MIN_INSTANCES'], '--max-instances=' + values['MAX_INSTANCES'],
                        '--ingress=all', '--invoker-iam-check', '--env-vars-file=' + str(path),
                        '--set-secrets=AI__InternalApiKey=' + args.secret + ':' + args.secret_version,
                        '--startup-probe=httpGet.path=/health,periodSeconds=5,timeoutSeconds=3,failureThreshold=24',
                        '--liveness-probe=httpGet.path=/health,periodSeconds=30,timeoutSeconds=3,failureThreshold=3',
                        '--quiet'], check=True)
    current = service_description(args)
    require_private(args)
    if allocated_traffic(current) != previous_traffic:
        raise RuntimeError('Serving traffic changed unexpectedly; rollout stopped.')
    candidate_url = validate_candidate(args, current, revision, tag, args.image)
    state = dict(project=args.project, service=args.service, region=args.region, revision=revision,
                 tag=tag, image=args.image, candidate_url=candidate_url, audience=current['status']['url'],
                 previous_traffic=previous_traffic, secret=args.secret, secret_version=args.secret_version,
                 model=args.model, verified=False)
    Path(output).write_text(json.dumps(state, indent=2), encoding='utf-8')
    print('Candidate deployed without changing serving traffic: ' + revision)


def main():
    parser = argparse.ArgumentParser()
    commands = parser.add_subparsers(dest="command", required=True)
    p = commands.add_parser("package")
    p.add_argument("--output", required=True)
    p.set_defaults(func=package)
    p = commands.add_parser('promote')
    p.add_argument('--project', required=True)
    p.add_argument('--service', required=True)
    p.add_argument('--region', default='asia-southeast1')
    p.add_argument('--rollout-state', required=True)
    p.set_defaults(func=promote)
    for command in ("deploy", "smoke", "verify-vertex"):
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
            p.add_argument('--candidate-tag')
            p.add_argument('--revision-suffix')
            p.add_argument('--rollout-state')
            p.add_argument('--bootstrap-production', action='store_true')
            p.set_defaults(func=deploy)
        else:
            p.add_argument("--url", required=True)
            p.add_argument('--audience', help='Canonical service URL used as ID-token audience for a tagged revision URL.')
            p.add_argument('--rollout-state', help='Record successful live verification of this exact candidate.')
            p.add_argument('--expected-model', required=command == 'verify-vertex',
                           help='Require the real Vertex response to match this configured model.')
            identity = p.add_mutually_exclusive_group()
            identity.add_argument("--identity-token-env", help="Read an existing ID token from this environment variable (CI).")
            identity.add_argument("--identity-service-account", help="Explicit gcloud impersonation for local/admin testing only.")
            p.add_argument("--live", action="store_true")
            p.set_defaults(func=verify_vertex if command == 'verify-vertex' else smoke)
    args = parser.parse_args()
    args.func(args)


if __name__ == "__main__":
    main()
