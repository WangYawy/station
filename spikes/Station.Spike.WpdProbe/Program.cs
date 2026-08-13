using System.Reflection;
using Vanara.PInvoke;

// WPD API 探针：输出 PortableDeviceManager / PortableDevice / IPortableDevice* 接口公开成员，
// 并尝试枚举设备，用于 MTP 采集源实现前的 API 面确认。

void Dump(Type type, int depth = 0)
{
    Console.WriteLine($"{new string(' ', depth)}{type.FullName}");
    Console.WriteLine($"{new string(' ', depth)}  base={type.BaseType?.FullName}  ifaces={string.Join(",", type.GetInterfaces().Select(i => i.Name))}");
    foreach (var ctor in type.GetConstructors().Take(4))
    {
        Console.WriteLine($"  Ctor({string.Join(", ", ctor.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"))})");
    }

    foreach (var m in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                 .Where(m => !m.IsSpecialName)
                 .Take(20))
    {
        Console.WriteLine($"  {m.ReturnType.Name} {m.Name}({string.Join(", ", m.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"))})");
    }

    foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly).Take(10))
    {
        Console.WriteLine($"  prop {p.PropertyType.Name} {p.Name}");
    }
}

void DumpAll(Type type)
{
    Console.WriteLine($"\n### {type.FullName}  base={type.BaseType?.FullName}");
    foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly).Take(20))
    {
        Console.WriteLine($"  field {f.FieldType.Name} {f.Name}");
    }

    foreach (var m in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly).Take(40))
    {
        Console.WriteLine($"  {m.ReturnType.Name} {m.Name}({string.Join(", ", m.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"))})");
    }

    foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly).Take(12))
    {
        Console.WriteLine($"  prop {p.PropertyType.Name} {p.Name}");
    }
}

try
{
    var managerType = typeof(PortableDeviceApi.PortableDeviceManager);
    Dump(managerType);
    Console.WriteLine();

    var deviceType = typeof(PortableDeviceApi.PortableDevice);
    Dump(deviceType);
    Console.WriteLine();

    Console.WriteLine("IPortableDeviceManager 接口成员:");
    Dump(typeof(PortableDeviceApi.IPortableDeviceManager));
    Console.WriteLine();

    Console.WriteLine("PortableDeviceApi 静态扩展方法:");
    foreach (var m in typeof(PortableDeviceApi).GetMethods(BindingFlags.Public | BindingFlags.Static)
                 .Where(m => m.Name is "GetDevices" or "GetDeviceFriendlyName" or "OpenDevice" or "GetDeviceDescription")
                 .Take(12))
    {
        Console.WriteLine($"  {m.ReturnType.Name} {m.Name}({string.Join(", ", m.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"))})");
    }

    Console.WriteLine();
    Console.WriteLine("====== PortableDeviceManager / PortableDevice 全部成员 ======");
    DumpAll(typeof(PortableDeviceApi.PortableDeviceManager));
    DumpAll(typeof(PortableDeviceApi.PortableDevice));

    Console.WriteLine();
    Console.WriteLine("====== 便携设备相关值类型/枚举（PROPVARIANT/PKEY 等） ======");
    var apiAssembly = typeof(PortableDeviceApi.PortableDeviceManager).Assembly;

    Console.WriteLine("--- PortableDeviceApi 静态类全部属性（截断前 120） ---");
    var apiType = typeof(PortableDeviceApi);
    foreach (var p in apiType.GetProperties(BindingFlags.Public | BindingFlags.Static).Take(120))
    {
        Console.WriteLine($"  prop {p.PropertyType.Name} {p.Name}");
    }

    Console.WriteLine("--- 全部包含 WPD_/PKEY_/WPD_RESOURCE 的成员名 ---");
    foreach (var name in apiAssembly.GetExportedTypes()
                 .SelectMany(t => t.GetMembers(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                 .Select(m => m.Name)
                 .Where(n => n.Contains("WPD_") || n.Contains("PKEY_") || n.Contains("WPD_RESOURCE"))
                 .Distinct()
                 .OrderBy(n => n))
    {
        Console.WriteLine($"  {name}");
    }

    Console.WriteLine("--- IPortableDeviceResources.GetStream 返回类型与 STGM / IStream ---");
    var streamType = typeof(PortableDeviceApi.IPortableDeviceResources).GetMethod("GetStream")!.ReturnType;
    Console.WriteLine($"  IStream 类型: {streamType.FullName}  assembly={streamType.Assembly.GetName().Name}");
    foreach (var m in streamType.GetMethods(BindingFlags.Public | BindingFlags.Instance).Where(m => !m.IsSpecialName).Take(25))
    {
        Console.WriteLine($"  {m.ReturnType.Name} {m.Name}({string.Join(", ", m.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"))})");
    }

    var stgmParam = typeof(PortableDeviceApi.IPortableDeviceResources).GetMethod("GetStream")!.GetParameters()[2];
    var stgmType = stgmParam.ParameterType;
    Console.WriteLine($"  GetStream 参数类型: {stgmParam.ParameterType.FullName}  assembly={stgmType.Assembly.GetName().Name}");
    if (stgmType is not null)
    {
        Console.WriteLine($"  STGM 枚举: {stgmType.FullName}");
        foreach (var name in (Enum.GetNames(stgmType).Where(n => n is "READ" or "WRITE" or "READWRITE" or "SHARE_DENY_NONE" or "DIRECT" or "TRANSACTED" or "DELETEONRELEASE" or "PRIORITY")).Take(30))
        {
            Console.WriteLine($"    {name}={Convert.ToInt64(Enum.Parse(stgmType, name))}");
        }
    }

    Console.WriteLine("--- PROPVARIANT / PROPVARIANT_UNMGD 成员 ---");
    foreach (var t in apiAssembly.GetExportedTypes().Where(t => t.Name is "PROPVARIANT" or "PROPVARIANT_UNMGD"))
    {
        Console.WriteLine($"\n### {t.FullName}");
        foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static).Take(30))
        {
            Console.WriteLine($"  field {f.FieldType.Name} {f.Name}");
        }

        foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static).Take(20))
        {
            Console.WriteLine($"  prop {p.PropertyType.Name} {p.Name}");
        }

        foreach (var c in t.GetConstructors())
        {
            Console.WriteLine($"  Ctor({string.Join(", ", c.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"))})");
        }
    }

    Console.WriteLine("--- DELETE_OBJECT_OPTIONS 枚举 ---");
    var delType = apiAssembly.GetExportedTypes().FirstOrDefault(t => t.Name == "DELETE_OBJECT_OPTIONS");
    if (delType is not null)
    {
        foreach (var name in Enum.GetNames(delType))
        {
            Console.WriteLine($"  {name}={Convert.ToInt64(Enum.Parse(delType, name))}");
        }
    }

    Console.WriteLine("--- HRESULT 关键成员 ---");
    var hrType = apiAssembly.GetType("Vanara.PInvoke.HRESULT");
    if (hrType is not null)
    {
        foreach (var m in hrType.GetMethods(BindingFlags.Public | BindingFlags.Instance).Where(m => m.Name is "ThrowIfFailed" or "ThrowIfError" or "ToInt32").Take(10))
        {
            Console.WriteLine($"  {m.ReturnType.Name} {m.Name}({string.Join(", ", m.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"))})");
        }

        foreach (var p in hrType.GetProperties(BindingFlags.Public | BindingFlags.Instance).Take(8))
        {
            Console.WriteLine($"  prop {p.PropertyType.Name} {p.Name}");
        }
    }

    Console.WriteLine("--- Vanara.Core 类型: PROPVARIANT / PROPVARIANT_UNMGD / HRESULT / STGM ---");
    var corePath = Path.Combine(AppContext.BaseDirectory, "Vanara.Core.dll");
    var coreAssembly = Assembly.LoadFrom(corePath);
    foreach (var type in coreAssembly.GetExportedTypes()
                 .Where(t => t.Name is "PROPVARIANT" or "PROPVARIANT_UNMGD" or "HRESULT" or "STGM")
                 .OrderBy(t => t.Name))
    {
        Console.WriteLine($"\n### {type.FullName}");
        foreach (var c in type.GetConstructors().Take(12))
        {
            Console.WriteLine($"  Ctor({string.Join(", ", c.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"))})");
        }

        foreach (var m in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                     .Where(m => !m.IsSpecialName && (m.Name is "ThrowIfFailed" or "op_Implicit" or "ToInt32" or "ToString" or "Clear"))
                     .Take(20))
        {
            Console.WriteLine($"  {m.ReturnType.Name} {m.Name}({string.Join(", ", m.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"))})");
        }

        foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly).Take(15))
        {
            Console.WriteLine($"  prop {p.PropertyType.Name} {p.Name}");
        }

        foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly).Take(10))
        {
            Console.WriteLine($"  field {f.FieldType.Name} {f.Name}");
        }
    }

    Console.WriteLine("--- 全程序集搜索 PROPVARIANT 类型 ---");
    foreach (var dll in Directory.GetFiles(AppContext.BaseDirectory, "Vanara*.dll"))
    {
        try
        {
            var asm = Assembly.LoadFrom(dll);
            foreach (var t in asm.GetExportedTypes().Where(t => t.Name.Contains("PROPVARIANT", StringComparison.OrdinalIgnoreCase)))
            {
                Console.WriteLine($"  {dll} -> {t.FullName}");
                foreach (var c in t.GetConstructors().Take(12))
                {
                    Console.WriteLine($"    Ctor({string.Join(", ", c.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"))})");
                }

                foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly).Take(10))
                {
                    Console.WriteLine($"    prop {p.PropertyType.Name} {p.Name}");
                }
            }
        }
        catch
        {
            // 跳过无法加载的程序集
        }
    }

    Console.WriteLine("--- PROPERTYKEY 类型位置 ---");
    foreach (var dll in Directory.GetFiles(AppContext.BaseDirectory, "Vanara*.dll"))
    {
        try
        {
            var asm = Assembly.LoadFrom(dll);
            foreach (var t in asm.GetExportedTypes().Where(t => t.Name == "PROPERTYKEY"))
            {
                Console.WriteLine($"  {dll} -> {t.FullName}");
            }
        }
        catch
        {
        }
    }
    foreach (var type in apiAssembly.GetExportedTypes()
                 .Where(t => t.Name.Contains("PortableDevice", StringComparison.OrdinalIgnoreCase) ||
                             t.Name.Contains("WPD_", StringComparison.OrdinalIgnoreCase) ||
                             t.Name is "PROPVARIANT" or "PropVariant" or "PKEY" or "REG_VALUE_TYPE")
                 .OrderBy(t => t.FullName))
    {
        DumpAll(type);
    }

    Console.WriteLine();
    Console.WriteLine("====== API 细分：PortableDevice 类和所有 IPortableDevice* 接口 ======");
    foreach (var type in apiAssembly.GetExportedTypes()
                 .Where(t => t.Name.Contains("PortableDevice", StringComparison.OrdinalIgnoreCase))
                 .OrderBy(t => t.FullName))
    {
        Console.WriteLine($"\n=== {type.FullName} ===");
        foreach (var m in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                     .Where(m => !m.IsSpecialName)
                     .Take(30))
        {
            Console.WriteLine($"  {m.ReturnType.Name} {m.Name}({string.Join(", ", m.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"))})");
        }

        foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Take(8))
        {
            Console.WriteLine($"  prop {p.PropertyType.Name} {p.Name}");
        }
    }

    Console.WriteLine();
    Console.WriteLine("====== 枚举当前 MTP/WPD 设备 ======");
    try
    {
        var manager = new PortableDeviceApi.PortableDeviceManager();
        if (manager is PortableDeviceApi.IPortableDeviceManager iManager)
        {
            iManager.RefreshDeviceList();
            var deviceIds = PortableDeviceApi.GetDevices(iManager, forceRefresh: false);
            Console.WriteLine($"检测到 {deviceIds.Length} 个便携设备:");
            foreach (var id in deviceIds)
            {
                try
                {
                    Console.WriteLine($"  {id}  friendly={PortableDeviceApi.GetDeviceFriendlyName(iManager, id)}  desc={PortableDeviceApi.GetDeviceDescription(iManager, id)}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  {id}  读取信息失败: {ex.Message}");
                }
            }
        }
        else
        {
            Console.WriteLine("PortableDeviceManager 未实现 IPortableDeviceManager 接口，需进一步确认 COM 获取方式。");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"设备枚举异常: {ex}");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"探针异常: {ex}");
}
