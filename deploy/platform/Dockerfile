# 平台后端镜像（含前端静态资源 wwwroot；构建上下文 = 仓库根目录）
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY Directory.Build.props .
COPY src/ ./src/
RUN dotnet publish src/Station.Platform/Station.Platform.Api -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
# 中文 PDF 导出依赖：wqy-microhei（含简体中文字形）；也可用 STATION__EXPORT__FONTFILE 指定字体文件覆盖
RUN apt-get update && apt-get install -y --no-install-recommends fonts-wqy-microhei \
    && rm -rf /var/lib/apt/lists/*
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Station.Platform.Api.dll"]
