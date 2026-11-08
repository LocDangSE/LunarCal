# syntax=docker/dockerfile:1
# Linux build and test of the OS-agnostic domain layer (LunarCal.Core, net10.0).
# The WPF app and the Windows-TFM projects are NOT built here; they need Windows (ADR-0002).
# Purpose: prove Core stays portable and give a reproducible, toolchain-pinned test run (ADR-0023).
#
#   docker build --target test -t lunarcal-core-test .
#
# Base image pinned by digest (SEC-SC-1). SDK version must satisfy global.json (10.0.403, latestFeature).
FROM mcr.microsoft.com/dotnet/sdk:10.0.403-noble@sha256:212fe4cb74627d5adf9817a4088906ae3edff1bd853bb27565d4b46731efe61e AS build
ENV DOTNET_NOLOGO=true \
    DOTNET_CLI_TELEMETRY_OPTOUT=true \
    CI=true
WORKDIR /src

# Restore first (layer cache), in locked mode against the committed lock files (SEC-SC-1).
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/LunarCal.Core/LunarCal.Core.csproj src/LunarCal.Core/packages.lock.json src/LunarCal.Core/
COPY tests/LunarCal.Core.Tests/LunarCal.Core.Tests.csproj tests/LunarCal.Core.Tests/packages.lock.json tests/LunarCal.Core.Tests/
RUN dotnet restore tests/LunarCal.Core.Tests/LunarCal.Core.Tests.csproj --locked-mode

COPY src/LunarCal.Core/ src/LunarCal.Core/
COPY tests/LunarCal.Core.Tests/ tests/LunarCal.Core.Tests/
RUN dotnet build tests/LunarCal.Core.Tests/LunarCal.Core.Tests.csproj -c Release --no-restore

FROM build AS test
RUN dotnet test --project tests/LunarCal.Core.Tests/LunarCal.Core.Tests.csproj -c Release --no-build
