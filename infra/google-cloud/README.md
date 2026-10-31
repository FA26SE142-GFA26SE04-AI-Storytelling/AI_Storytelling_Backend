# TaleTale AI API on Google Cloud Run

This deployment owns only the AI API. Core, AWS/CDK, the Core workflow, database,
and media providers in Core are unchanged. No Terraform is used.

## First deployment: 2026-10-31

The user selected the existing Google project for the first deployment. A separate
production project has **not** been created; this is a staging deployment.

- Project: `gen-lang-client-0675088605` (`679679810390`).
- Service: `taletale-ai-staging`, region `asia-southeast1`.
- URL: `https://taletale-ai-staging-679679810390.asia-southeast1.run.app`.
- Canonical URL/audience returned by the API: `https://taletale-ai-staging-m63yiaft7q-as.a.run.app`.
- Runtime: `taletale-ai-runtime@gen-lang-client-0675088605.iam.gserviceaccount.com`.
- Registry: `asia-southeast1-docker.pkg.dev/gen-lang-client-0675088605/taletale-ai/ai-api`.
- Internal key: Secret Manager `taletale-ai-staging-internal-key`, version `1`.
- Initial revision: `taletale-ai-staging-00001-khw`.
- Initial image digest: `sha256:1fecded2f1eae8c4defb620fd7d09a507c9ac3a74b9bf59ea89d2ff02cd1d691`.
- Initial model: `gemini-3.8-flash`, Vertex location `global`.
- Initial resources: 1 CPU, 1 GiB, concurrency 4, timeout 300s, min 0/max 2 instances.

Cloud Run is reachable over HTTPS and requires IAM authentication. Swagger being
enabled does not make this service public. The runtime uses its attached Google
service account through ADC. Neither local ADC nor a JSON service account key is
uploaded. Local `gcloud auth application-default login` and `compose.adc.yaml`
remain unchanged.

## Open Swagger

Sign in to gcloud with an identity that has Cloud Run Invoker on this service:

```powershell
gcloud run services proxy taletale-ai-staging --project=gen-lang-client-0675088605 --region=asia-southeast1 --port=8085
```

Open `http://127.0.0.1:8085/swagger/index.html`. The proxy supplies Cloud Run IAM
authentication. Use Swagger's **Authorize** button to supply the internal key for
business API requests. Obtain that key through Secret Manager using an authorized
identity; never paste it into chat, a command argument, Git, or this document.

If gcloud prompts to install the Cloud Run proxy component, install it using your
normal SDK setup. The first deployment verification used the Google proxy binary
in a temporary task directory to avoid modifying the local SDK or ADC.
Bind the proxy only to loopback, and stop it when testing is complete.

## Bootstrap resources (Google Cloud Console)

1. Confirm project and billing. Enable Cloud Run, Artifact Registry, Cloud Build
   (for the first remote build), Secret Manager, Vertex AI, IAM Service Account
   Credentials, and Security Token Service APIs.
2. Create the Docker repository `taletale-ai` in Singapore.
3. Create the runtime identity and grant Vertex AI User on the model project.
4. Generate a new environment-specific internal key directly in Secret Manager.
   Grant Secret Accessor to the runtime identity on that secret only.
5. Deploy using `cloud-run.template.yaml` via `cloudrun.py`. Never disable the
   invoker IAM check or add `allUsers`/`allAuthenticatedUsers` bindings.
6. Create `taletale-ai-deployer`, restricted to the AI registry (Writer), AI service
   (Cloud Run Developer and Invoker), runtime service account (Service Account
   User), and AI internal-key secret (Secret Accessor). The deployer has OpenID
   Connect Identity Token Creator on itself for authenticated CI smoke tests.
   Cloud Run Developer must be service-scoped, not project-wide.
7. Configure GitHub OIDC in pool `taletale-github`, provider `github`, on the
   deployer. Restrict to repository ID `1361038590`, owner ID `322398367`, refs
   `main`/`dev`, and GitHub environment `ai-staging`.

The first deployment used the existing default Cloud Build service account. It
was granted Artifact Registry Writer and Logs Writer to complete the one-time
remote build. Ongoing GitHub builds use the dedicated deployer and AI registry.

## Repeat deployment

The first remote build used an explicitly packaged AI-only context because Docker
Desktop was not running. Packaging excludes Core/AWS, `.env`, ADC, appsettings,
build outputs, and unrelated repository files:

```powershell
python infra/google-cloud/cloudrun.py package --output "$env:TEMP/taletale-ai-source-NEW-UNIQUE-ID"
gcloud builds submit "$env:TEMP/taletale-ai-source-NEW-UNIQUE-ID" --project=gen-lang-client-0675088605 --region=asia-southeast1 --config=infra/google-cloud/cloudbuild.ai.yaml --substitutions=_IMAGE=asia-southeast1-docker.pkg.dev/gen-lang-client-0675088605/taletale-ai/ai-api:NEW-UNIQUE-TAG
```

Resolve the image digest from the successful build. Run `cloudrun.py deploy` with
`--project`, `--service`, `--runtime`, immutable `--image`, explicit `--model`, and
`--secret`. The script renders the versioned service template into a temporary
file, deploys it, and verifies there are no public IAM bindings.
Secrets are referenced by numeric version; rotation requires a new revision.

`.github/workflows/deploy-ai-api.yml` builds only the AI image after the AI tests,
then deploys staging and runs authenticated live smoke tests. It uses GitHub OIDC,
not a stored Google key. It runs for relevant pushes to `main`/`dev` or manual
dispatch from those branches. Its Google trust rejects other branches.
The workflow must be committed/merged before GitHub can execute it.

Create GitHub Environment `ai-staging`. Optionally set environment variable
`AI_TEXT_MODEL`; otherwise the baseline is `gemini-3.8-flash`.

Production promotion remains disabled by default. Before enabling it, bootstrap
a separate project/service and GitHub Environment `ai-production` with required
reviewers. Configure `GCP_PROJECT_ID`, `GCP_RUNTIME_SERVICE_ACCOUNT`,
`GCP_DEPLOY_SERVICE_ACCOUNT`, `GCP_WORKLOAD_IDENTITY_PROVIDER`,
`AI_INTERNAL_KEY_SECRET`, `AI_INTERNAL_KEY_SECRET_VERSION`, and `AI_TEXT_MODEL`.
Grant the production Cloud Run service agent read access to the staging image
repository so promotion uses the exact staging-tested digest. Its OIDC provider
must trust only `main` and environment `ai-production`. The workflow refuses to
deploy production into the first-deployment/staging project.

## Verification and remaining acceptance work

```powershell
python infra/google-cloud/cloudrun.py smoke --project gen-lang-client-0675088605 --url https://taletale-ai-staging-m63yiaft7q-as.a.run.app --secret taletale-ai-staging-internal-key --live
```

The smoke test does not print tokens, internal keys, or generated story text. It
checks IAM denial, health, Swagger UI/OpenAPI, missing/invalid internal keys, and
a real Vertex outline call. CI uses `--identity-service-account` for an audience-
bound token. This is not proof that every AI feature or the AWS integration works.

Before production: complete multi-feature live acceptance, compare candidate GA
models against the baseline on the agreed evaluation set, review output quality,
load-test, configure notification channels/budgets, and rehearse rollback. Do not
silently change model IDs if a candidate is unavailable.

Use Cloud Run's Metrics/Logs for latency, memory, instances, failures and 429s.
Vertex completion logs include input/output/thought/total tokens and finish reason.
Raw prompt/output logging has been removed from the Vertex client. Health probes
only check process health and do not prove Google permissions or model availability.

## Rollback and handoff

Record the currently serving revision before each update. Restore it with:

```powershell
gcloud run services update-traffic taletale-ai-staging --project=gen-lang-client-0675088605 --region=asia-southeast1 --to-revisions=PREVIOUS_READY_REVISION=100
```

For the first deployment there is no previous revision to restore. Future
deployments create revisions without deleting the previous image/configuration.

The Core/AWS team receives the canonical URL/audience, API/OpenAPI contract,
IAM Invoker requirements, and the internal-key secret location through an approved
secret channel. It must configure AWS-to-Google authentication independently.
Images/TTS in Core still require their own Google authentication. No AWS changes,
Core deployment, migrations, or Core-to-AI end-to-end verification were performed.
