using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SqlSugar;

namespace Station.Infrastructure.Db;

/// <summary>
/// 标记接口：代表专供后台批量任务（采集/同步）使用的长连接客户端。
/// 生命周期为 Scoped，由 DI 容器管理，确保同一 Scope 内所有 ILoopRepository 共享此实例。
/// </summary>
public interface ILoopSqlSugarClient : ISqlSugarClient { }

/// <summary>
/// 长连接专用 SqlSugar 客户端。
/// </summary>
public sealed class LoopSqlSugarClient : SqlSugarClient, ILoopSqlSugarClient
{
    public LoopSqlSugarClient(ConnectionConfig config) : base(config)
    {
        // 可以在此处添加长连接特有的初始化逻辑（如果有）
    }
}
