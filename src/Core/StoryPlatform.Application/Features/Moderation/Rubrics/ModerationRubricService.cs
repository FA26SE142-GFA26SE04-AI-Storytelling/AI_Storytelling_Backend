using System.Text.Json;
using System.Text.RegularExpressions;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Contracts.AI.Moderation;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.Moderation.Rubrics;

public interface IModerationRubricSource
{
    Task<RubricSnapshot> GetActiveAsync(CancellationToken ct);
}
public sealed record RubricWriteRequest(string VersionNo, string Name, RubricDefinition Definition, int Revision = 0);
public sealed record RubricRevisionRequest(int Revision);
public sealed record RubricCloneRequest(string VersionNo, string Name);
public sealed record RubricEvaluationRequest(int Revision, string Phase, string AgeBand, string Language, string Title, string Content);
public sealed record RubricDto(int Id, string VersionNo, string Name, string Status, int Revision,
    string SchemaVersion, string ContentHash, RubricDefinition Definition, int CreatedByUserId,
    DateTime CreatedAt, int? ActivatedByUserId, DateTime? ActivatedAt, DateTime? RetiredAt);

public sealed class ModerationRubricService(IUnitOfWork uow, IEnumerable<IModelModerationProvider> providers) : IModerationRubricSource
{
    // All catalog mutations serialize before reading rows. Negative ID is outside the story ID range.
    private const int CatalogLock = int.MinValue + 1349;
    private static readonly JsonSerializerOptions Json = RubricRules.Json;
    private async Task Guard(int userId, bool administrator, CancellationToken ct)
    {
        var user = await uow.Repository<UserAccount>().FirstOrDefaultAsync(x => x.Id == userId, cancellationToken: ct);
        if (user is null || user.IsDeleted || user.Status != AccountStatus.Active || user.LockedUntil > DateTime.UtcNow
            || (administrator ? user.Role != UserRole.Administrator : user.Role is not (UserRole.Moderator or UserRole.Administrator)))
            throw new ForbiddenException("RUBRIC_ACCESS_DENIED");
    }

    private async Task<ModerationRubricVersion> Row(int id, CancellationToken ct) =>
        await uow.Repository<ModerationRubricVersion>().FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, cancellationToken: ct)
        ?? throw new NotFoundException("ModerationRubricVersion", id);

    private static RubricDefinition Definition(ModerationRubricVersion row)
    {
        try { return JsonSerializer.Deserialize<RubricDefinition>(row.DefinitionJson, Json) ?? throw new ConflictException("RUBRIC_CONTENT_INVALID"); }
        catch (JsonException) { throw new ConflictException("RUBRIC_CONTENT_INVALID"); }
    }
    private static RubricDto Dto(ModerationRubricVersion row) => new(row.Id, row.VersionNo, row.Name, row.Status.ToString(),
        row.Revision, row.SchemaVersion, row.ContentHash, Definition(row), row.CreatedByUserId, row.CreatedAt,
        row.ActivatedByUserId, row.ActivatedAt, row.RetiredAt);
    public static RubricSnapshot Snapshot(ModerationRubricVersion row)
    {
        var result = new RubricSnapshot(row.Id, row.VersionNo, row.SchemaVersion, row.ContentHash, Definition(row));
        try { RubricRules.Validate(result); }
        catch (ArgumentException) { throw new ConflictException("RUBRIC_CONTENT_INVALID"); }
        return result;
    }
    public async Task<RubricSnapshot> GetActiveAsync(CancellationToken ct)
    {
        var rows = await uow.Repository<ModerationRubricVersion>().FindAsync(x => x.Code == "gray-zone"
            && x.Status == ModerationRubricStatus.Active && !x.IsDeleted, cancellationToken: ct);
        if (rows.Count != 1) throw new ConflictException("RUBRIC_ACTIVE_REQUIRED");
        return Snapshot(rows[0]);
    }
    public async Task<object> ListAsync(int userId, string? status, int pageIndex, int pageSize, CancellationToken ct)
    {
        await Guard(userId, false, ct);
        if (pageIndex < 1 || pageSize is < 1 or > 100 || (status is not null && status is not ("Draft" or "Active" or "Retired")))
            throw new BadRequestException("RUBRIC_QUERY_INVALID");
        ModerationRubricStatus? filter = status is null ? null : Enum.Parse<ModerationRubricStatus>(status);
        var page = await uow.Repository<ModerationRubricVersion>().GetPagedAsync(pageIndex, pageSize,
            x => !x.IsDeleted && (filter == null || x.Status == filter), q => q.OrderByDescending(x => x.Id), cancellationToken: ct);
        return new { items = page.Items.Select(Dto).ToArray(), page.TotalCount, pageIndex, pageSize };
    }
    public async Task<RubricDto> GetAsync(int userId, int id, CancellationToken ct)
    { await Guard(userId, false, ct); return Dto(await Row(id, ct)); }
    public async Task<RubricSnapshot> ActiveAsync(int userId, CancellationToken ct)
    { await Guard(userId, false, ct); return await GetActiveAsync(ct); }

    private static void ValidateWrite(RubricWriteRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.VersionNo) || request.VersionNo.Length > 50
            || !Regex.IsMatch(request.VersionNo, "^[a-zA-Z0-9][a-zA-Z0-9._-]*$")
            || string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 200) throw new BadRequestException("RUBRIC_METADATA_INVALID");
        try { RubricRules.Validate(request.Definition); }
        catch (ArgumentException) { throw new BadRequestException("RUBRIC_DEFINITION_INVALID"); }
    }
    private async Task<T> Mutation<T>(int userId, bool administrator, Func<Task<T>> action, CancellationToken ct)
    {
        await uow.BeginTransactionAsync(ct);
        try
        {
            await uow.AcquireTransactionLockAsync(CatalogLock, ct);
            await Guard(userId, administrator, ct);
            var result = await action();
            await uow.CommitTransactionAsync(ct);
            return result;
        }
        catch { await uow.RollbackTransactionAsync(CancellationToken.None); throw; }
    }
    private Task<ModerationRubricAuditLog> Audit(ModerationRubricVersion row, int userId, string action, string? before, CancellationToken ct) =>
        uow.Repository<ModerationRubricAuditLog>().AddAsync(new()
        {
            RubricVersionId = row.Id, ActorUserId = userId, Action = action,
            BeforeJson = before, AfterJson = JsonSerializer.Serialize(Dto(row), Json)
        }, ct);
    private async Task<ModerationRubricVersion> CreateRow(int userId, RubricWriteRequest request, CancellationToken ct)
    {
        ValidateWrite(request);
        if (await uow.Repository<ModerationRubricVersion>().ExistsAsync(x => x.Code == "gray-zone" && x.VersionNo == request.VersionNo, ct))
            throw new ConflictException("RUBRIC_VERSION_EXISTS");
        return await uow.Repository<ModerationRubricVersion>().AddAsync(new()
        {
            VersionNo = request.VersionNo, Name = request.Name.Trim(), SchemaVersion = RubricRules.SchemaVersion,
            DefinitionJson = RubricRules.Serialize(request.Definition), ContentHash = RubricRules.Hash(request.Definition), CreatedByUserId = userId
        }, ct);
    }
    public Task<RubricDto> CreateAsync(int userId, RubricWriteRequest request, CancellationToken ct) => Mutation(userId, false, async () =>
    {
        var row = await CreateRow(userId, request, ct);
        await uow.SaveChangesAsync(ct); // Generate ID for response and append-only audit FK.
        await Audit(row, userId, "Created", null, ct);
        return Dto(row);
    }, ct);
    public Task<RubricDto> CloneAsync(int userId, int id, RubricCloneRequest request, CancellationToken ct) => Mutation(userId, false, async () =>
    {
        var source = await Row(id, ct);
        var row = await CreateRow(userId, new(request.VersionNo, request.Name, Definition(source)), ct);
        await uow.SaveChangesAsync(ct);
        await Audit(row, userId, "Cloned", JsonSerializer.Serialize(new { sourceId = id }, Json), ct);
        return Dto(row);
    }, ct);
    private void Track(ModerationRubricVersion row, int revision, bool draftOnly = true)
    {
        if (row.Revision != revision) throw new ConflictException("RUBRIC_REVISION_STALE");
        if (draftOnly && row.Status != ModerationRubricStatus.Draft) throw new ConflictException("RUBRIC_IMMUTABLE");
        // Attach at the original revision before incrementing the concurrency token.
        uow.Repository<ModerationRubricVersion>().Update(row);
        row.Revision++;
    }
    public Task<RubricDto> UpdateAsync(int userId, int id, RubricWriteRequest request, CancellationToken ct) => Mutation(userId, false, async () =>
    {
        ValidateWrite(request);
        var row = await Row(id, ct);
        if (row.VersionNo != request.VersionNo) throw new BadRequestException("RUBRIC_VERSION_NUMBER_IMMUTABLE");
        var before = JsonSerializer.Serialize(Dto(row), Json);
        Track(row, request.Revision);
        row.Name = request.Name.Trim(); row.DefinitionJson = RubricRules.Serialize(request.Definition); row.ContentHash = RubricRules.Hash(request.Definition);
        await Audit(row, userId, "Updated", before, ct);
        return Dto(row);
    }, ct);
    public Task<bool> DeleteAsync(int userId, int id, int revision, CancellationToken ct) => Mutation(userId, false, async () =>
    {
        var row = await Row(id, ct);
        var before = JsonSerializer.Serialize(Dto(row), Json);
        Track(row, revision); row.IsDeleted = true;
        await Audit(row, userId, "Deleted", before, ct);
        return true;
    }, ct);
    public async Task<object> ValidateAsync(int userId, int id, int revision, CancellationToken ct)
    {
        await Guard(userId, false, ct);
        var row = await Row(id, ct);
        if (row.Revision != revision) throw new ConflictException("RUBRIC_REVISION_STALE");
        var snapshot = Snapshot(row);
        return new { valid = true, row.Id, row.Revision, snapshot.ContentHash, snapshot.SchemaVersion };
    }
    public Task<RubricDto> ActivateAsync(int userId, int id, int revision, CancellationToken ct) => Mutation(userId, true, async () =>
    {
        var row = await Row(id, ct);
        Snapshot(row);
        var before = JsonSerializer.Serialize(Dto(row), Json);
        if (row.Status != ModerationRubricStatus.Draft) throw new ConflictException("RUBRIC_IMMUTABLE");
        if (row.Revision != revision) throw new ConflictException("RUBRIC_REVISION_STALE");
        var active = await uow.Repository<ModerationRubricVersion>().FindAsync(x => x.Code == row.Code && x.Status == ModerationRubricStatus.Active && !x.IsDeleted, cancellationToken: ct);
        foreach (var previous in active)
        {
            var old = JsonSerializer.Serialize(Dto(previous), Json);
            Track(previous, previous.Revision, false); previous.Status = ModerationRubricStatus.Retired; previous.RetiredAt = DateTime.UtcNow;
            await Audit(previous, userId, "Retired", old, ct);
        }
        // Release the filtered unique index before activating the next row, within the same transaction.
        await uow.SaveChangesAsync(ct);
        Track(row, revision); row.Status = ModerationRubricStatus.Active; row.ActivatedAt = DateTime.UtcNow; row.ActivatedByUserId = userId;
        await Audit(row, userId, "Activated", before, ct);
        return Dto(row);
    }, ct);
    public async Task<object> AuditLogsAsync(int userId, int id, int pageIndex, int pageSize, CancellationToken ct)
    {
        await Guard(userId, false, ct);
        if (pageIndex < 1 || pageSize is < 1 or > 100) throw new BadRequestException("RUBRIC_QUERY_INVALID");
        // Deleted drafts remain auditable, including their deletion event.
        var page = await uow.Repository<ModerationRubricAuditLog>().GetPagedAsync(pageIndex, pageSize,
            x => x.RubricVersionId == id, q => q.OrderByDescending(x => x.Id), cancellationToken: ct);
        return new { items = page.Items.Select(x => new { x.Id, x.ActorUserId, x.Action, x.CreatedAt, x.BeforeJson, x.AfterJson }), page.TotalCount, pageIndex, pageSize };
    }
    public async Task<object> EvaluateAsync(int userId, int id, RubricEvaluationRequest request, CancellationToken ct)
    {
        await Guard(userId, false, ct);
        var row = await Row(id, ct);
        if (row.Revision != request.Revision) throw new ConflictException("RUBRIC_REVISION_STALE");
        var snapshot = Snapshot(row); // Pin before the provider call; no transaction/lock held during inference.
        if (string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.Content)) throw new BadRequestException("RUBRIC_EVALUATION_INPUT_INVALID");
        var scan = new GrayZoneScan(request.Phase, request.AgeBand, request.Language, GrayZoneEvaluator.TextParts(request.Title, request.Content), [])
        { RubricVersion = snapshot.Version, Rubric = snapshot };
        try { GrayZoneRules.ValidateRequest(new() { RequestId = "rubric-trial", SourceFingerprint = GrayZoneEvaluator.Fingerprint(scan), Phase = scan.Phase,
            AgeBand = scan.AgeBand, Language = scan.Language, RubricVersion = snapshot.Version, Rubric = snapshot,
            Parts = scan.Parts.Select(x => new GrayZonePart(x.Kind, x.Name, x.Text)).ToArray() }); }
        catch (ArgumentException) { throw new BadRequestException("RUBRIC_EVALUATION_INPUT_INVALID"); }
        var provider = providers.SingleOrDefault(x => x.Name == "Vertex") ?? throw new ConflictException("MODERATION_PROVIDER_NOT_REGISTERED");
        var result = await provider.EvaluateAsync(scan, ct);
        // Audit references the actual pinned snapshot even if this draft changed during inference.
        await Mutation(userId, false, async () =>
        {
            var current = await Row(id, ct);
            await uow.Repository<ModerationRubricAuditLog>().AddAsync(new() { RubricVersionId = current.Id, ActorUserId = userId,
                Action = "Evaluated", AfterJson = JsonSerializer.Serialize(new { snapshot.Version, snapshot.ContentHash, revision = request.Revision,
                    sourceFingerprint = GrayZoneEvaluator.Fingerprint(scan), result.Model, result.RiskScore }, Json) }, ct);
            return true;
        }, ct);
        return new { rubricId = id, snapshot.Version, snapshot.ContentHash, revision = request.Revision, provider = provider.Name, result.Model, result.RiskScore, result.Scores, result.Findings };
    }
}
