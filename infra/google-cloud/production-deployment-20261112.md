# AI production deployment — 2026-11-12

## Cloud Run

- Project: `gen-lang-client-0675088605` (number `679679810390`).
- Region: `asia-southeast1`.
- Service: `taletale-ai-production`.
- Revision: `taletale-ai-production-00001-wxw`, Ready, 100% of production service traffic.
- Canonical URL / audience: `https://taletale-ai-production-m63yiaft7q-as.a.run.app`.
- Alternate service URL: `https://taletale-ai-production-679679810390.asia-southeast1.run.app`.
- Image: `asia-southeast1-docker.pkg.dev/gen-lang-client-0675088605/taletale-ai/ai-api@sha256:5c25c0e3ad9be9ef84ab9e17cc45115c12e94a66fdb16cc3503ce1dc2f0a9a25`.
- Image source: the immutable image serving staging revision `taletale-ai-staging-00004-btp`.
  No local feature-branch source was built or published for this deployment.
- Environment: `Production`; Vertex `global`, model `gemini-3.8-flash`.
- Runtime: `taletale-ai-runtime@gen-lang-client-0675088605.iam.gserviceaccount.com`.
- Resources: 1 vCPU, 1 GiB RAM, concurrency 4, request deadline 300s; min 0 / max 2 instances.
- Internal credential: Secret Manager `taletale-ai-production-internal-key`, version `1`.

Cloud Run IAM remains required; no `allUsers` or `allAuthenticatedUsers` binding.
Runtime identity obtains ADC from Cloud Run. Local ADC, Core/AWS, database and media are unchanged.
Staging service was retained. Its configured internal-key version 1 was already disabled;
that secret was not re-enabled or changed.

## Explicitly approved IAM changes

- Secret Accessor on **only** `taletale-ai-production-internal-key` for runtime and deployer AI identities.
- Cloud Run Developer and Invoker on **only** `taletale-ai-production` for
  `taletale-ai-deployer@gen-lang-client-0675088605.iam.gserviceaccount.com`.
- No project-wide Cloud Run role, service account key, or public invoker was added.
- Existing WIF condition was inspected and already required this repository/owner,
  `main`/`dev` refs and environment `ai-production`; it was not changed.

## Live verification

Verified at 17:24 on 2026-11-12 (Asia/Saigon), using `cloudrun.py verify-vertex`:

- Anonymous request denied by IAM.
- Authenticated `/health`, `/swagger/index.html`, `/swagger/v1/swagger.json`: HTTP 200.
- Missing and invalid internal keys denied.
- Real Vertex outline generated; provider/model/usage/unique request ID validated.
- Provider: `VertexAI`; model/version: `gemini-3.8-flash`.
- Generation ID: `db74d153250c4f61a52ad46197aa7f9f`.
- Input tokens: 764; output tokens: 171; provider latency: 12,848 ms.
- No ERROR entries returned by the post-deployment startup log check.

Offline rollout/authentication suite: 18 tests passed. A reproduced bootstrap bug was fixed locally:
gcloud reports a missing service as `Cannot find service [name]` instead of always using
`NOT_FOUND`. The script now recognizes the exact target's message while still rejecting
permission errors, mismatched service names and attempts to bootstrap an existing service.

This verifies the deployed AI outline path, not all business features or Core-to-AI integration.
This was a direct admin bootstrap, not a successful GitHub Actions run. There is no prior
production revision to roll back to at this first bootstrap. Later changes should use the
candidate → live smoke → exact revision promotion workflow.

## GitHub Environment identifiers

These are the values to use in environment `ai-production`; no secret content is included:

```text
GCP_RUNTIME_SERVICE_ACCOUNT=taletale-ai-runtime@gen-lang-client-0675088605.iam.gserviceaccount.com
GCP_DEPLOY_SERVICE_ACCOUNT=taletale-ai-deployer@gen-lang-client-0675088605.iam.gserviceaccount.com
GCP_WORKLOAD_IDENTITY_PROVIDER=projects/679679810390/locations/global/workloadIdentityPools/taletale-github/providers/github
AI_INTERNAL_KEY_SECRET=taletale-ai-production-internal-key
AI_INTERNAL_KEY_SECRET_VERSION=1
AI_TEXT_MODEL=gemini-3.8-flash
```

GitHub Environment variables were not inspected or modified during this deployment.
No commit, push, merge, workflow dispatch, Core URL change or AWS operation was performed.

## Swagger

```powershell
gcloud run services proxy taletale-ai-production --project=gen-lang-client-0675088605 --region=asia-southeast1 --port=8086
```

Open `http://127.0.0.1:8086/swagger/index.html`. Use an authorized Google identity for the proxy,
and retrieve the production internal key through Secret Manager for Swagger Authorize.
Never paste that key into Git, chat or command-line arguments.
