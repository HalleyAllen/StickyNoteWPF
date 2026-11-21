FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY Shared/ Shared/
COPY Server/ Server/
RUN dotnet publish Server/StickyNote.Sync.Server.csproj -c Release -o /out

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /out .
USER root
RUN mkdir -p /data && chown -R app:app /data
USER app
ENV ASPNETCORE_HTTP_PORTS=8080
ENV Sync__DataDirectory=/data
EXPOSE 8080
ENTRYPOINT ["dotnet", "StickyNote.Sync.Server.dll"]
