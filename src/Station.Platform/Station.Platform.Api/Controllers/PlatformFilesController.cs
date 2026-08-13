using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using SqlSugar;
using Station.Contracts;
using Station.Contracts.Api;
using Station.Contracts.Reporting;
using Station.Infrastructure.Repositories;
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
    private readonly IHttpClientFactory _httpFactory;
    private readonly AuthService _authorization;
    private readonly IDataScopeProvider _dataScope;

    public PlatformFilesController(
        IRepository<PlatformFileMetadata> files,
        IRepository<PlatformStation> stations,
        IHttpClientFactory httpFactory,
        AuthService authorization,
        IDataScopeProvider dataScope)
    {
        _files = files;
        _stations = stations;
        _httpFactory = httpFactory;
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
}
