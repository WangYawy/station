using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Runtime.Versioning;
using System.Text;
using Station.Application.Collecting;
using Station.Infrastructure.Recorders;
using Vanara.PInvoke;

namespace Station.Desktop.Infrastructure.Collecting;

/// <summary>
/// 真实 MTP（媒体传输协议）采集源：通过 Windows 便携设备 API（WPD）枚举/读取/擦除 MTP 记录仪。
/// 仅支持 Windows；MTP 无盘符，根目录使用虚拟路径 <c>MTP://{PnP设备ID}</c>，
/// 绑定文件（station_bind.ini）读写同样走 WPD 内容 API（设备不支持写时给出明确错误）。
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class MtpCollectSource : ICollectSource, IRecorderRootFileStore
{
    private const char PartSeparator = '\u001F';

    private readonly CollectOptions _options;

    public MtpCollectSource(CollectOptions options)
    {
        _options = options;
    }

    public string SourceKey => "mtp";

    public string GetRecorderRoot(CollectDeviceInfo device)
    {
        var (pnpId, _) = ResolveDevice();
        return MtpRoot.RootScheme + pnpId;
    }

    public async Task<IReadOnlyList<SourceFileInfo>> ScanAsync(
        CollectDeviceInfo device,
        CancellationToken cancellationToken)
    {
        await Task.Yield();
        var (pnpId, _) = ResolveDevice();
        using var session = OpenDevice(pnpId);
        var list = new List<SourceFileInfo>();
        await WalkObjectsAsync(session.Content, PortableDeviceApi.WPD_DEVICE_OBJECT_ID, pnpId, list, cancellationToken);
        return list;
    }

    public async Task CopyAsync(
        SourceFileInfo file,
        string destinationPath,
        Func<double, Task>? onProgress,
        CancellationToken cancellationToken)
    {
        await Task.Yield();
        var (pnpId, objectId) = ParseRelativePath(file.RelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

        using var session = OpenDevice(pnpId);
        var stream = session.Resources.GetStream(
            objectId,
            PortableDeviceApi.WPD_RESOURCE_DEFAULT,
            STGM.STGM_READ | STGM.STGM_SHARE_DENY_NONE,
            out _);
        try
        {
            await using var output = new FileStream(
                destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, _options.ChunkBytes);
            await CopyStreamAsync(stream, output, file.Size, onProgress, cancellationToken);
        }
        finally
        {
            Marshal.ReleaseComObject(stream);
        }
    }

    public async Task EraseAsync(CollectDeviceInfo device, CancellationToken cancellationToken)
    {
        await Task.Yield();
        var (pnpId, _) = ResolveDevice();
        using var session = OpenDevice(pnpId);
        var files = new List<(string ObjectId, string FileName)>();
        await WalkObjectsAsync(session.Content, PortableDeviceApi.WPD_DEVICE_OBJECT_ID, pnpId, files, cancellationToken);

        foreach (var (objectId, fileName) in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_options.FileExtensions.Contains(Path.GetExtension(fileName), StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            DeleteObject(session.Content, objectId);
        }
    }

    // ---------- IRecorderRootFileStore（绑定文件走 WPD） ----------

    public string? ReadFile(string recorderRoot, string fileName)
    {
        var (pnpId, _) = ParseRoot(recorderRoot);
        EnsureDeviceConnected(pnpId);
        using var session = OpenDevice(pnpId);
        var (objectId, _) = FindRootFile(session.Content, fileName);
        if (objectId is null)
        {
            return null;
        }

        var stream = session.Resources.GetStream(
            objectId,
            PortableDeviceApi.WPD_RESOURCE_DEFAULT,
            STGM.STGM_READ | STGM.STGM_SHARE_DENY_NONE,
            out _);
        try
        {
            using var memory = new MemoryStream();
            CopyStreamSync(stream, memory, 64 * 1024);
            return Encoding.UTF8.GetString(memory.ToArray());
        }
        finally
        {
            Marshal.ReleaseComObject(stream);
        }
    }

    public void WriteFile(string recorderRoot, string fileName, string content)
    {
        var (pnpId, _) = ParseRoot(recorderRoot);
        EnsureDeviceConnected(pnpId);
        using var session = OpenDevice(pnpId);
        var (existingId, _) = FindRootFile(session.Content, fileName);
        if (existingId is not null)
        {
            DeleteObject(session.Content, existingId);
        }

        var values = (PortableDeviceApi.IPortableDeviceValues)new PortableDeviceApi.PortableDeviceValues();
        values.SetStringValue(PortableDeviceApi.WPD_OBJECT_PARENT_ID, PortableDeviceApi.WPD_DEVICE_OBJECT_ID);
        values.SetStringValue(PortableDeviceApi.WPD_OBJECT_NAME, fileName);
        values.SetStringValue(PortableDeviceApi.WPD_OBJECT_ORIGINAL_FILE_NAME, fileName);
        values.SetGuidValue(PortableDeviceApi.WPD_OBJECT_CONTENT_TYPE, PortableDeviceApi.WPD_CONTENT_TYPE_DOCUMENT);
        values.SetGuidValue(PortableDeviceApi.WPD_OBJECT_FORMAT, PortableDeviceApi.WPD_OBJECT_FORMAT_TEXT);

        var bytes = Encoding.UTF8.GetBytes(content);
        try
        {
            uint optimalBufferSize = 0;
            var created = session.Content.CreateObjectWithPropertiesAndData(
                values, out var dataStream, ref optimalBufferSize);
            try
            {
                var written = Marshal.AllocHGlobal(sizeof(int));
                try
                {
                    dataStream.Write(bytes, bytes.Length, written);
                    dataStream.Commit(0); // STGC.DEFAULT
                }
                finally
                {
                    Marshal.FreeHGlobal(written);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(dataStream);
            }

            _ = created;
        }
        catch (COMException ex)
        {
            throw new InvalidOperationException(
                $"MTP 设备不支持写入绑定文件 {fileName}（部分记录仪限制 MTP 下创建文件）：{ex.Message}", ex);
        }
    }

    public void DeleteFile(string recorderRoot, string fileName)
    {
        var (pnpId, _) = ParseRoot(recorderRoot);
        EnsureDeviceConnected(pnpId);
        using var session = OpenDevice(pnpId);
        var (objectId, _) = FindRootFile(session.Content, fileName);
        if (objectId is not null)
        {
            DeleteObject(session.Content, objectId);
        }
    }

    // ---------- WPD 内部实现 ----------

    private (string PnpId, string FriendlyName) ResolveDevice()
    {
        var manager = (PortableDeviceApi.IPortableDeviceManager)new PortableDeviceApi.PortableDeviceManager();
        var deviceIds = ListDeviceIds(manager);
        foreach (var id in deviceIds)
        {
            string friendly;
            try
            {
                friendly = PortableDeviceApi.GetDeviceFriendlyName(manager, id) ?? string.Empty;
            }
            catch
            {
                friendly = string.Empty;
            }

            if (string.IsNullOrWhiteSpace(_options.MtpDeviceFilter) ||
                friendly.Contains(_options.MtpDeviceFilter, StringComparison.OrdinalIgnoreCase) ||
                id.Contains(_options.MtpDeviceFilter, StringComparison.OrdinalIgnoreCase))
            {
                return (id, friendly);
            }
        }

        throw new InvalidOperationException("未检测到 MTP 设备（请通过 USB 连接记录仪并选择 MTP 模式）");
    }

    /// <summary>绑定根目录中的设备必须在线：无设备或设备已更换时给出明确错误，避免打开失效 PnP ID。</summary>
    private static void EnsureDeviceConnected(string pnpId)
    {
        var manager = (PortableDeviceApi.IPortableDeviceManager)new PortableDeviceApi.PortableDeviceManager();
        var deviceIds = ListDeviceIds(manager);
        if (deviceIds.Length == 0)
        {
            throw new InvalidOperationException("未检测到 MTP 设备（请通过 USB 连接记录仪并选择 MTP 模式）");
        }

        if (!deviceIds.Contains(pnpId, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("MTP 设备已更换或未连接，与绑定根目录不一致，请重新接入记录仪");
        }
    }

    private static string[] ListDeviceIds(PortableDeviceApi.IPortableDeviceManager manager)
    {
        manager.RefreshDeviceList();
        return PortableDeviceApi.GetDevices(manager, forceRefresh: false);
    }

    private static WpdSession OpenDevice(string pnpId)
    {
        var clientInfo = (PortableDeviceApi.IPortableDeviceValues)new PortableDeviceApi.PortableDeviceValues();
        clientInfo.SetStringValue(PortableDeviceApi.WPD_CLIENT_NAME, "StationCollector");
        clientInfo.SetUnsignedIntegerValue(PortableDeviceApi.WPD_CLIENT_MAJOR_VERSION, 1);
        clientInfo.SetUnsignedIntegerValue(PortableDeviceApi.WPD_CLIENT_MINOR_VERSION, 0);
        clientInfo.SetUnsignedIntegerValue(PortableDeviceApi.WPD_CLIENT_REVISION, 0);
        clientInfo.SetUnsignedIntegerValue(PortableDeviceApi.WPD_CLIENT_SECURITY_QUALITY_OF_SERVICE, 2); // SECURITY_IMPERSONATION
        clientInfo.SetUnsignedIntegerValue(PortableDeviceApi.WPD_CLIENT_DESIRED_ACCESS, 0xC0000000); // GENERIC_READ | GENERIC_WRITE

        var device = (PortableDeviceApi.IPortableDevice)new PortableDeviceApi.PortableDevice();
        device.Open(pnpId, clientInfo);
        return new WpdSession(device);
    }

    private async Task WalkObjectsAsync(
        PortableDeviceApi.IPortableDeviceContent content,
        string parentObjectId,
        string pnpId,
        List<SourceFileInfo> files,
        CancellationToken cancellationToken)
    {
        foreach (var objectId in EnumerateChildren(content, parentObjectId, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var values = content.Properties().GetValues(objectId, null);
            var contentType = SafeGetString(values, PortableDeviceApi.WPD_OBJECT_CONTENT_TYPE);
            var isContainer = IsContainer(contentType, values);
            if (isContainer)
            {
                await WalkObjectsAsync(content, objectId, pnpId, files, cancellationToken);
                continue;
            }

            var name = SafeGetString(values, PortableDeviceApi.WPD_OBJECT_ORIGINAL_FILE_NAME) ??
                       SafeGetString(values, PortableDeviceApi.WPD_OBJECT_NAME);
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var size = SafeGetUInt64(values, PortableDeviceApi.PKEY_GenericObj_ObjectSize);
            var modified = SafeGetDate(values) ?? DateTime.UtcNow;
            files.Add(new SourceFileInfo($"{pnpId}{PartSeparator}{objectId}", name, (long)size, modified));
        }
    }

    private async Task WalkObjectsAsync(
        PortableDeviceApi.IPortableDeviceContent content,
        string parentObjectId,
        string pnpId,
        List<(string ObjectId, string FileName)> files,
        CancellationToken cancellationToken)
    {
        foreach (var objectId in EnumerateChildren(content, parentObjectId, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var values = content.Properties().GetValues(objectId, null);
            var contentType = SafeGetString(values, PortableDeviceApi.WPD_OBJECT_CONTENT_TYPE);
            if (IsContainer(contentType, values))
            {
                await WalkObjectsAsync(content, objectId, pnpId, files, cancellationToken);
                continue;
            }

            var name = SafeGetString(values, PortableDeviceApi.WPD_OBJECT_ORIGINAL_FILE_NAME) ??
                       SafeGetString(values, PortableDeviceApi.WPD_OBJECT_NAME);
            if (!string.IsNullOrWhiteSpace(name))
            {
                files.Add((objectId, name));
            }
        }
    }

    private static IEnumerable<string> EnumerateChildren(
        PortableDeviceApi.IPortableDeviceContent content,
        string parentObjectId,
        CancellationToken cancellationToken)
    {
        var enumerator = content.EnumObjects(0, parentObjectId, null);
        var buffer = new string[16];
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var hr = enumerator.Next((uint)buffer.Length, buffer, out var fetched);
            hr.ThrowIfFailed("MTP 枚举对象失败");
            if (fetched == 0)
            {
                yield break;
            }

            for (var i = 0; i < fetched; i++)
            {
                yield return buffer[i];
            }

            if (fetched < buffer.Length)
            {
                yield break;
            }
        }
    }

    private static bool IsContainer(string? contentType, PortableDeviceApi.IPortableDeviceValues values)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            // 无内容类型：按是否有子对象判断（避免把无扩展名的功能性对象当文件）
            return SafeGetUInt64(values, PortableDeviceApi.PKEY_GenericObj_ObjectSize) == 0 &&
                   SafeGetString(values, PortableDeviceApi.PKEY_GenericObj_ObjectFileName) is null;
        }

        return contentType.Equals(PortableDeviceApi.WPD_CONTENT_TYPE_FOLDER.ToString(), StringComparison.OrdinalIgnoreCase) ||
               contentType.Equals(PortableDeviceApi.WPD_CONTENT_TYPE_FUNCTIONAL_OBJECT.ToString(), StringComparison.OrdinalIgnoreCase) ||
               contentType.Equals(PortableDeviceApi.WPD_CONTENT_TYPE_ALL.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private (string? ObjectId, string? FileName) FindRootFile(
        PortableDeviceApi.IPortableDeviceContent content,
        string fileName)
    {
        foreach (var objectId in EnumerateChildren(content, PortableDeviceApi.WPD_DEVICE_OBJECT_ID, CancellationToken.None))
        {
            var values = content.Properties().GetValues(objectId, null);
            if (IsContainer(SafeGetString(values, PortableDeviceApi.WPD_OBJECT_CONTENT_TYPE), values))
            {
                continue;
            }

            var name = SafeGetString(values, PortableDeviceApi.WPD_OBJECT_ORIGINAL_FILE_NAME) ??
                       SafeGetString(values, PortableDeviceApi.WPD_OBJECT_NAME);
            if (string.Equals(name, fileName, StringComparison.OrdinalIgnoreCase))
            {
                return (objectId, name);
            }
        }

        return (null, null);
    }

    private static void DeleteObject(PortableDeviceApi.IPortableDeviceContent content, string objectId)
    {
        var ids = (PortableDeviceApi.IPortableDevicePropVariantCollection)new PortableDeviceApi.PortableDevicePropVariantCollection();
        var value = new Ole32.PROPVARIANT_UNMGD(objectId, VarEnum.VT_LPWSTR);
        ids.Add(in value);
        content.Delete(PortableDeviceApi.DELETE_OBJECT_OPTIONS.PORTABLE_DEVICE_DELETE_WITH_RECURSION, ids);
    }

    private static async Task CopyStreamAsync(
        IStream source,
        Stream destination,
        long totalSize,
        Func<double, Task>? onProgress,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[64 * 1024];
        var pcbRead = Marshal.AllocHGlobal(sizeof(int));
        try
        {
            long copied = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                source.Read(buffer, buffer.Length, pcbRead);
                var read = Marshal.ReadInt32(pcbRead);
                if (read <= 0)
                {
                    break;
                }

                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                copied += read;
                if (onProgress is not null)
                {
                    await onProgress(totalSize <= 0 ? 1d : Math.Min(1d, (double)copied / totalSize));
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(pcbRead);
        }
    }

    private static void CopyStreamSync(IStream source, Stream destination, int bufferSize)
    {
        var buffer = new byte[bufferSize];
        var pcbRead = Marshal.AllocHGlobal(sizeof(int));
        try
        {
            while (true)
            {
                source.Read(buffer, buffer.Length, pcbRead);
                var read = Marshal.ReadInt32(pcbRead);
                if (read <= 0)
                {
                    break;
                }

                destination.Write(buffer, 0, read);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(pcbRead);
        }
    }

    private static (string PnpId, string ObjectId) ParseRelativePath(string relativePath)
    {
        var index = relativePath.IndexOf(PartSeparator);
        return index < 0
            ? throw new InvalidOperationException($"无效的 MTP 文件路径: {relativePath}")
            : (relativePath[..index], relativePath[(index + 1)..]);
    }

    private static (string PnpId, string Tail) ParseRoot(string recorderRoot)
    {
        if (!MtpRoot.IsMtpRoot(recorderRoot))
        {
            throw new InvalidOperationException($"不是 MTP 虚拟根目录: {recorderRoot}");
        }

        var value = recorderRoot[MtpRoot.RootScheme.Length..];
        return (value, value);
    }

    private static string? SafeGetString(PortableDeviceApi.IPortableDeviceValues values, Ole32.PROPERTYKEY key)
    {
        try
        {
            return values.GetStringValue(key);
        }
        catch
        {
            return null;
        }
    }

    private static ulong SafeGetUInt64(PortableDeviceApi.IPortableDeviceValues values, Ole32.PROPERTYKEY key)
    {
        try
        {
            return values.GetUnsignedLargeIntegerValue(key);
        }
        catch
        {
            return 0;
        }
    }

    private static DateTime? SafeGetDate(PortableDeviceApi.IPortableDeviceValues values)
    {
        try
        {
            var value = values.GetValue(PortableDeviceApi.PKEY_GenericObj_DateModified).Value;
            return value switch
            {
                DateTime date => date.ToUniversalTime(),
                long ticks => new DateTime(ticks, DateTimeKind.Utc),
                _ => null
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>WPD 设备会话：释放时关闭设备。</summary>
    private sealed class WpdSession : IDisposable
    {
        public WpdSession(PortableDeviceApi.IPortableDevice device)
        {
            Device = device;
            Content = device.Content();
            Resources = Content.Transfer();
        }

        public PortableDeviceApi.IPortableDevice Device { get; }
        public PortableDeviceApi.IPortableDeviceContent Content { get; }
        public PortableDeviceApi.IPortableDeviceResources Resources { get; }

        public void Dispose()
        {
            try
            {
                Device.Close();
            }
            catch
            {
                // 设备已拔出等情况忽略
            }

            Marshal.ReleaseComObject(Resources);
            Marshal.ReleaseComObject(Content);
            Marshal.ReleaseComObject(Device);
        }
    }
}
