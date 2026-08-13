# 平台后端镜像（含前端静态资源 wwwroot；构建上下文 = 仓库根目录）
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY Directory.Build.props .
COPY src/ ./src/
RUN dotnet publish src/Station.Platform/Station.Platform.Api -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Station.Platform.Api.dll"]
