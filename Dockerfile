FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY Directory.Build.props Wellhead.sln ./
COPY src/Wellhead.Api/Wellhead.Api.csproj src/Wellhead.Api/
COPY tests/Wellhead.Tests/Wellhead.Tests.csproj tests/Wellhead.Tests/
RUN dotnet restore src/Wellhead.Api/Wellhead.Api.csproj

COPY src/Wellhead.Api/ src/Wellhead.Api/
RUN dotnet publish src/Wellhead.Api/Wellhead.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_RUNNING_IN_CONTAINER=true
COPY --from=build /app ./
RUN useradd --uid 10001 --create-home appuser
USER appuser
EXPOSE 8080
ENTRYPOINT ["dotnet", "Wellhead.Api.dll"]
