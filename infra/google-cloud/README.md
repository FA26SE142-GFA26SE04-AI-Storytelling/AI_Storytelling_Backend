# TaleTale AI API: production trong project hiện tại

Chỉ áp dụng AI API. Core trên AWS, database, migration/seed và provider media trong Core không được thay đổi.

## Đích triển khai

- Google Cloud project: `gen-lang-client-0675088605`, project number `679679810390`.
- Cloud Run service: `taletale-ai-production`, region `asia-southeast1`.
- Registry: `asia-southeast1-docker.pkg.dev/gen-lang-client-0675088605/taletale-ai/ai-api`.
- Mặc định model: `gemini-3.8-flash`, Vertex location `global`.
- 1 CPU, 1 GiB RAM, concurrency 4, request timeout 300 giây, provider timeout 120 giây.
- Min instances 0, max instances 2. Scale-to-zero có cold start; credit/quota không bảo đảm mọi lượt gọi miễn phí.

Workflow không còn deploy staging và không yêu cầu project production riêng.
Service staging cũ không bị xóa hoặc chuyển traffic. Không thay URL Core đang dùng trước khi production được kiểm chứng.

## Luồng GitHub Actions

`test → build image → deploy candidate 0% traffic → smoke candidate → chuyển 100% traffic sang đúng revision`

- Push có thay đổi phù hợp trên `main`: chạy test và production. Trên `dev`: chỉ test.
- Chạy thủ công: chọn `main` và bật `promote_production`.
- Environment `ai-production` nên có required reviewers và deployment branch rule chỉ cho `main`.
- Không build lại giữa smoke và promotion. Image dùng digest; revision theo run ID/attempt, tag cố định `candidate`.
- Candidate được gọi qua URL tag, nhưng ID token dùng audience là URL canonical của service.
- Không tắt IAM. Bài `verify-vertex` kiểm tra anonymous bị chặn, health, Swagger/OpenAPI, internal key và một lượt tạo outline Vertex thật.
- Bài test yêu cầu `modelProvider=VertexAI`, model đúng cấu hình deploy, modelVersion/generationId không rỗng, requestId khớp lượt gọi duy nhất và input/output tokens cùng latency đều dương. Chỉ HTTP 200 chưa được tính đạt.
- Chỉ test thành công mới ghi `verified=true` và `vertex_verification` vào rollout state. Promotion yêu cầu bằng chứng provider/model, kiểm tra lại revision/image/tag và traffic cũ trước khi chuyển.
- Smoke lỗi: step chuyển traffic không chạy; traffic cũ giữ nguyên. Revision/tag candidate có thể còn để truy vết.
- Artifact `ai-rollout-<run>-<attempt>` giữ revision, digest, URL và phân bổ traffic cũ, không chứa credential.
- Smoke outline không chứng minh mọi chức năng content, quiz, media hoặc kết nối AWS đều hoàn tất.

## Test kết nối và sinh nội dung Vertex thật

Đây là bài integration qua `Cloud Run AI API → runtime service account → Google GenAI SDK → Vertex`.
Không dùng mock ở workflow deploy; test sử dụng token và có thể tiêu thụ credit. Nó xác minh cấu hình/quyền model và khả năng sinh output, không chứng minh toàn bộ nghiệp vụ.

Lệnh mẫu một dòng (thay URL canonical và tên secret, không nhập giá trị key):

```powershell
python infra/google-cloud/cloudrun.py verify-vertex --project gen-lang-client-0675088605 --url <PRODUCTION_CANONICAL_URL> --secret <PRODUCTION_SECRET_NAME> --secret-version 1 --expected-model gemini-3.8-flash
```

Local dùng gcloud identity hiện có với quyền Invoker và đọc secret. CI dùng ID token qua WIF,
gọi candidate URL với audience canonical và truyền `--rollout-state` để lưu bằng chứng trước khi chuyển traffic.
Kết quả chỉ log request/generation ID, provider/model/version, số token và latency; không log JWT, key hoặc truyện sinh ra.
Lệnh `smoke` cũ vẫn giữ cho kiểm tra nhanh. Workflow production dùng `verify-vertex`; artifact thiếu bằng chứng Vertex sẽ không được promote.

## Cấu hình environment ai-production

Trong GitHub → Settings → Environments → ai-production → Environment variables:

- `GCP_WORKLOAD_IDENTITY_PROVIDER`: đường dẫn provider WIF.
- `GCP_DEPLOY_SERVICE_ACCOUNT`: email deployer.
- `GCP_RUNTIME_SERVICE_ACCOUNT`: email runtime service account.
- `AI_INTERNAL_KEY_SECRET`: tên secret production trong Secret Manager, không phải giá trị key.
- `AI_INTERNAL_KEY_SECRET_VERSION`: version số cụ thể, ví dụ `1`; không dùng `latest`.
- `AI_TEXT_MODEL`: tùy chọn; mặc định `gemini-3.8-flash`.

Project, region và tên service được ghim trong workflow. Không còn cần biến `GCP_PROJECT_ID`.

Có thể dùng các identity hiện hữu sau khi kiểm tra quyền:
- Runtime: `taletale-ai-runtime@gen-lang-client-0675088605.iam.gserviceaccount.com`.
- Deployer: `taletale-ai-deployer@gen-lang-client-0675088605.iam.gserviceaccount.com`.
- Provider: `projects/679679810390/locations/global/workloadIdentityPools/taletale-github/providers/github`.

Đây là identifier, không phải credential. Không tự động tái sử dụng secret staging.
Tạo secret production qua kênh an toàn và cấp Secret Accessor cho runtime/deployer trên đúng secret.

**WIF phải cập nhật bên Google Cloud:** trust cho đúng repo, ref `refs/heads/main` và subject
`repo:FA26SE142-GFA26SE04-AI-Storytelling/AI_Storytelling_Backend:environment:ai-production`.
Điều kiện cũ chỉ chấp nhận `ai-staging` sẽ chặn workflow mới. Không mở trust cho mọi repo/branch.

Runtime cần Vertex AI User. Deployer cần quyền ghi registry, deploy và invoke đúng service,
actAs runtime, đọc đúng secret và tạo ID token qua WIF. Giữ quyền theo resource, không dùng Owner/Editor.
Không upload ADC local hoặc JSON service-account key. Không thêm allUsers/allAuthenticatedUsers.

## Khởi tạo production lần đầu

Rollout giữ nguyên traffic yêu cầu service đã tồn tại; workflow sẽ dừng sớm nếu chưa bootstrap.
Không thể giữ một revision production cũ nếu service chưa có revision nào.

Một admin chuẩn bị API, registry, secret và runtime/deployer permissions, rồi tạo service private một lần.
Chọn digest image đã biết từ registry. Lệnh mẫu PowerShell (thay các giá trị placeholder):

```powershell
$IMAGE = 'asia-southeast1-docker.pkg.dev/gen-lang-client-0675088605/taletale-ai/ai-api@sha256:<DIGEST_64_HEX>'
$RUNTIME = '<RUNTIME_SERVICE_ACCOUNT_EMAIL>'
$SECRET = '<PRODUCTION_SECRET_NAME>'
python infra/google-cloud/cloudrun.py deploy --project gen-lang-client-0675088605 --region asia-southeast1 --service taletale-ai-production --runtime $RUNTIME --image $IMAGE --model gemini-3.8-flash --secret $SECRET --secret-version 1 --environment production --bootstrap-production
```

Bootstrap chỉ chấp nhận service chưa tồn tại và không thay service production đang chạy.
Revision đầu có traffic của chính service mới; chưa kết nối Core/người dùng vào URL đó.
Sau bootstrap, cấp Invoker/Developer theo service cho deployer, chạy workflow candidate/smoke/promotion thành công
rồi mới kết nối Core. Không có rollback về revision trước ở lần bootstrap đầu.

Bootstrap không được script/workflow tự thực hiện. Thay đổi IAM/secret/cloud resources cần được admin thực hiện riêng.

## Rollback

Lấy `previous_traffic` trong artifact rollout. Chuyển về đúng revision đã lưu; ví dụ:

```powershell
gcloud run services update-traffic taletale-ai-production --project=gen-lang-client-0675088605 --region=asia-southeast1 --to-revisions=PREVIOUS_READY_REVISION=100
```

Nếu trước đó chia traffic nhiều revision, khôi phục đủ các phần trăm từ artifact.
Không dùng `--to-latest` vì latest có thể là candidate bị lỗi.
Promotion kiểm tra drift trước khi ghi, nhưng không thay thế việc kiểm soát người có quyền sửa traffic;
không deploy thủ công đồng thời với workflow. Không xóa revision trước khi hết nhu cầu rollback.

## Swagger và kết nối Core

```powershell
gcloud run services proxy taletale-ai-production --project=gen-lang-client-0675088605 --region=asia-southeast1 --port=8085
```

Mở `http://127.0.0.1:8085/swagger/index.html`; proxy identity phải có Invoker.
Internal key lấy từ Secret Manager qua kênh được cấp quyền, không đưa vào Git/chat/command argument.

Core trên AWS cần URL canonical production, internal key tương ứng và ID token Cloud Run IAM.
Không chỉ đổi BaseUrl nếu Core chưa có cơ chế lấy ID token. Images/TTS trong Core vẫn dùng cấu hình riêng.
Local ADC và Docker Compose không đổi.

## Kiểm thử offline

```powershell
python -m unittest discover -s infra/google-cloud/tests -p 'test_*.py' -v
```

Các test mock gcloud/HTTP, không deploy hoặc gọi Vertex. Chỉ workflow/cloud smoke thành công mới xác minh
WIF, Secret Manager, quyền invoke và provider thật. Chưa có tuyên bố đã production-ready cho toàn bộ nghiệp vụ.

## Service staging lịch sử

Ngày 2026-10-31 từng deploy `taletale-ai-staging` trong cùng project.
URL canonical cũ: `https://taletale-ai-staging-m63yiaft7q-as.a.run.app`.
Secret cũ: `taletale-ai-staging-internal-key`, version 1.
Workflow mới không sử dụng hoặc xóa service này. Chỉ dọn sau khi production và Core đã hoạt động đúng.

Tài liệu Google:
- [Deploy tagged revision và chuyển traffic](https://docs.cloud.google.com/run/docs/rollouts-rollbacks-traffic-migration).
- [ID-token audience khi gọi service](https://docs.cloud.google.com/run/docs/authenticating/service-to-service).
