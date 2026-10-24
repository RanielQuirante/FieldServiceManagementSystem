FROM mcr.microsoft.com/dotnet/sdk:10.0.402-noble AS build
WORKDIR /repo
COPY global.json nuget.config Directory.Build.props Directory.Packages.props .editorconfig ./
COPY .config/dotnet-tools.json .config/
COPY src/FieldService.Api/FieldService.Api.csproj src/FieldService.Api/packages.lock.json src/FieldService.Api/
RUN dotnet restore src/FieldService.Api/FieldService.Api.csproj --locked-mode -p:Configuration=Release
COPY src/FieldService.Api/ src/FieldService.Api/
RUN dotnet publish src/FieldService.Api/FieldService.Api.csproj --configuration Release --no-restore -p:UseAppHost=false --output /out/api && mkdir -p /out/logs

FROM build AS bundle
ARG TARGETARCH
RUN dotnet tool restore && ConnectionStrings__Migrations="Host=db;Port=5432;Database=fieldservice;Username=fieldservice_migrator" dotnet ef migrations bundle --project src/FieldService.Api --configuration Release --self-contained --target-runtime linux-$(echo "$TARGETARCH" | sed s/amd64/x64/) --output /out/efbundle

FROM mcr.microsoft.com/dotnet/aspnet:10.0.13-noble-chiseled-extra AS migrator
WORKDIR /app
COPY --from=bundle /out/efbundle /app/efbundle
USER 1654
ENTRYPOINT ["/app/efbundle"]

FROM mcr.microsoft.com/dotnet/aspnet:10.0.13-noble-chiseled-extra AS runtime
WORKDIR /app
COPY --from=build --chown=1654:1654 /out/logs /app/logs
COPY --from=build /out/api /app
USER 1654
EXPOSE 8080
ENTRYPOINT ["dotnet", "FieldService.Api.dll"]
