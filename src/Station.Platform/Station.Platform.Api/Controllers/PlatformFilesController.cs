using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using SqlSugar;
using Station.Application.Audit;
using Station.Contracts;
using Station.Contracts.Api;
using Station.Contracts.Reporting;
using Station.Domain.Entities;
using Station.Infrastructure.IdGenerators;
using Station.Infrastructure.Persistence;
using Station.Infrastructure.Repositories;
using Station.Infrastructure.Security;
using Station.Platform.Domain.Entities;
using AuthService = Station.Application.Authorization.IAuthorizationService;
using Station.Application.Authorization;

namespace Station.Platform.Api.Controllers;

/// <summary>平台文件统一列表/检索/预览（元数据来自各采集站上报，预览经采集站代理转发）。</summary>
[ApiController]
[Authorize]
[Route("api/v1/files")]
public class PlatformFilesController : ControllerBase
{
    private readonly IRepository<PlatformFileMetadata> _files;
    private readonly IRepository<PlatformStation> _stations;
    private readonly IRepository<PlatformFileCorrection> _corrections;
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _configuration;
    private readonly IAuditLogService _audit;
    private readonly IIdGenerator _idGenerator;
    private readonly AuthService _authorization;
    private readonly IDataScopeProvider _dataScope;

    public PlatformFilesController(
        IRepository<PlatformFileMetadata> files,
        IRepository<PlatformStation> stations,
        IRepository<PlatformFileCorrection> corrections,
        IHttpClientFactory httpFactory,
        IConfiguration configuration,
        IAuditLogService audit,
        IIdGenerator idGenerator,
        AuthService authorization,
        IDataScopeProvider dataScope)
    {
        _files = files;
        _stations = stations;
        _corrections = corrections;
        _httpFactory = httpFactory;
        _configuration = configuration;
        _audit = audit;
        _idGenerator = idGenerator;
        _authorization = authorization;
        _dataScope = dataScope;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<FileMetadataView>>>> List(
        [FromQuery] long? stationId,
        [FromQuery] long? deptId,
        [FromQuery] string? keyword,
        [FromQuery] FileKind? kind,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int page = 1,
        [FromQuery] int size = 20)
    {
        var scope = await DataScopeHelper.GetScopeAsync(User, _authorization, _dataScope);
        var query = _files.AsQueryable()
            .Where(f =>
                (stationId == null || f.StationId == stationId) &&
                (deptId == null || f.DeptId == deptId) &&
                (kind == null || f.Kind == kind) &&
                (from == null || f.CollectedAt >= from) &&
                (to == null || f.CollectedAt <= to) &&
                (string.IsNullOrWhiteSpace(keyword) ||
                 f.FileName.Contains(keyword) || f.FileNo.Contains(keyword) || f.RecorderSerial.Contains(keyword)));
        if (!scope.IsAll)
        {
            query = query.Where(f => f.DeptId != null && scope.AllowedDeptIds.Contains(f.DeptId.Value));
        }

        var total = query.Count();
        var items = query.OrderBy(f => f.CollectedAt, OrderByType.Desc)
            .ToPageList(Math.Max(1, page), Math.Max(1, size));
        return Ok(ApiResponse<PagedResult<FileMetadataView>>.Ok(new PagedResult<FileMetadataView>(
            page, size, total, items.Select(ToView).ToList())));
    }

    [HttpGet("{fileNo}")]
    public async Task<ActionResult<ApiResponse<FileMetadataView>>> Detail(string fileNo)
    {
        var scope = await DataScopeHelper.GetScopeAsync(User, _authorization, _dataScope);
        var file = await _files.FirstAsync(f => f.FileNo == fileNo);
        return file is null || !IsInScope(scope, file.DeptId)
            ? NotFound(ApiResponse<FileMetadataView>.Fail(404, "文件不存在"))
            : Ok(ApiResponse<FileMetadataView>.Ok(ToView(file)));
    }

    /// <summary>预览代理：转发到采集站内置 Web 文件流端点（透传 Range）。</summary>
    [HttpGet("{fileNo}/preview")]
    public async Task<IActionResult> Preview(string fileNo)
    {
        try
        {
            var scope = await DataScopeHelper.GetScopeAsync(User, _authorization, _dataScope);
            var file = await _files.FirstAsync(f => f.FileNo == fileNo);
            if (file is null || !IsInScope(scope, file.DeptId))
            {
                return NotFound();
            }

            var station = await _stations.GetByIdAsync(file.StationId);
            if (station?.StationBaseUrl is null)
            {
                return BadRequest(new { message = "采集站未上报预览地址" });
            }

            var http = _httpFactory.CreateClient();
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"{station.StationBaseUrl.TrimEnd('/')}/api/v1/files/{fileNo}/stream");
            if (Request.Headers.TryGetValue("Range", out var range))
            {
                request.Headers.TryAddWithoutValidation("Range", range.ToString());
            }

            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            Response.StatusCode = (int)response.StatusCode;
            Response.ContentType = response.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";
            if (response.Content.Headers.ContentRange is { } contentRange)
            {
                Response.Headers["Content-Range"] = contentRange.ToString();
            }

            if (response.Content.Headers.ContentLength is { } contentLength)
            {
                Response.ContentLength = contentLength;
            }

            if (response.Headers.AcceptRanges is { } acceptRanges)
            {
                Response.Headers["Accept-Ranges"] = acceptRanges.ToString();
            }

            await using var stream = await response.Content.ReadAsStreamAsync();
            await stream.CopyToAsync(Response.Body);
            return new EmptyResult();
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = $"预览转发失败：{ex.Message}" });
        }
    }

    private static FileMetadataView ToView(PlatformFileMetadata f) => new(
        f.StationId, f.LocalFileId, f.FileNo, f.FileName, f.Size, f.Kind, f.Sm3,
        f.CollectedAt, f.RecorderSerial, f.UserNo, f.DeptCode, f.StorageLocation);

    private static bool IsInScope(DataScopeResult scope, long? deptId) =>
        scope.IsAll || (deptId != null && scope.AllowedDeptIds.Contains(deptId.Value));

    /// <summary>文件归属修正（file:manage）：更新归属并留痕（SM2 签名）。</summary>
    [HttpPut("{fileNo}/ownership")]
    public async Task<IActionResult> CorrectOwnership(string fileNo, [FromBody] CorrectOwnershipRequest request)
    {
        if (!await RequirePermissionAsync(PermissionCodes.FileManage))
        {
            return StatusCode(403, new { message = "无文件管理权限" });
        }

        var scope = await GetScopeAsync();
        var file = await _files.FirstAsync(f => f.FileNo == fileNo);
        if (file is null || !IsInScope(scope, file.DeptId))
        {
            return NotFound(new { message = "文件不存在" });
        }

        var correctedAt = DateTime.Now;
        var canonical = $"{file.FileNo}|{file.UserNo}|{request.UserNo}|{file.DeptCode}|{request.DeptCode}|" +
                        $"{User.Identity?.Name}|{correctedAt.ToUniversalTime():yyyy-MM-ddTHH:mm:ss}";
        var privateKey = PlatformCommandKeys.ReadPrivateKey(_configuration);
        var signature = string.IsNullOrWhiteSpace(privateKey)
            ? "unsigned"
            : Sm2LicenseSigner.Sign(privateKey, canonical);

        await _corrections.InsertAsync(new PlatformFileCorrection
        {
            Id = _idGenerator.NextId(),
            FileNo = file.FileNo,
            StationId = file.StationId,
            OldUserNo = file.UserNo,
            NewUserNo = request.UserNo,
            OldDeptCode = file.DeptCode,
            NewDeptCode = request.DeptCode,
            OperatorAccount = User.Identity?.Name,
            CorrectedAt = correctedAt,
            Signature = signature
        });

        file.UserNo = request.UserNo;
        file.DeptCode = request.DeptCode;
        await _files.UpdateAsync(file);
        await _audit.WriteAsync(new AuditLog
        {
            OperatorAccount = User.Identity?.Name,
            OperationType = "file.correct",
            Target = file.FileNo,
            Detail = $"归属修正：{request.UserNo ?? "无"}/{request.DeptCode ?? "无"}（签名 {signature[..Math.Min(16, signature.Length)]}…）",
            SourceIp = HttpContext.Connection.RemoteIpAddress?.ToString(),
            Result = 1
        });
        return Ok(ApiResponse<bool>.Ok(true));
    }

    /// <summary>文件归属修正记录（file:view）。</summary>
    [HttpGet("{fileNo}/corrections")]
    public async Task<IActionResult> Corrections(string fileNo)
    {
        if (!await RequirePermissionAsync(PermissionCodes.FileView))
        {
            return StatusCode(403, new { message = "无文件查看权限" });
        }

        var scope = await GetScopeAsync();
        var file = await _files.FirstAsync(f => f.FileNo == fileNo);
        if (file is null || !IsInScope(scope, file.DeptId))
        {
            return NotFound(new { message = "文件不存在" });
        }

        var list = (await _corrections.GetListAsync(c => c.FileNo == fileNo))
            .OrderByDescending(c => c.CorrectedAt)
            .Select(c => new FileCorrectionView(
                c.Id, c.FileNo, c.OldUserNo, c.NewUserNo, c.OldDeptCode, c.NewDeptCode,
                c.OperatorAccount, c.CorrectedAt, c.Signature))
            .ToList();
        return Ok(ApiResponse<List<FileCorrectionView>>.Ok(list));
    }

    private async Task<bool> RequirePermissionAsync(string code)
    {
        if (User.FindFirst("accountId") is not { } accountClaim ||
            !long.TryParse(accountClaim.Value, out var accountId))
        {
            return false;
        }

        return await _authorization.HasPermissionAsync(accountId, code);
    }

    private Task<DataScopeResult> GetScopeAsync() =>
        DataScopeHelper.GetScopeAsync(User, _authorization, _dataScope);
}

public sealed record CorrectOwnershipRequest(string? UserNo, string? DeptCode);

public sealed record FileCorrectionView(
    long Id,
    string FileNo,
    string? OldUserNo,
    string? NewUserNo,
    string? OldDeptCode,
    string? NewDeptCode,
    string? OperatorAccount,
    DateTime CorrectedAt,
    string Signature);
