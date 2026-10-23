# syntax=docker/dockerfile:1

# ---------------------------------------------------------------------------
# Build stage
# ---------------------------------------------------------------------------
# The build runs on the builder's own platform: the published app is portable, so an arm64 image needs no emulation here.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG Channel=local
ARG BuildNumber=0
ARG BuildDate=
WORKDIR /src

# Restore first (better layer caching).
COPY ["global.json", "Directory.Build.props", "./"]
COPY ["src/MatPhone.Core/MatPhone.Core.csproj", "src/MatPhone.Core/"]
COPY ["src/MatPhone/MatPhone.csproj", "src/MatPhone/"]
RUN dotnet restore "src/MatPhone/MatPhone.csproj"

COPY src src
# Fall back to today's date when BuildDate is not supplied, so the version never ends in "-".
RUN BUILD_DATE="${BuildDate:-$(date -u +%Y%m%d)}" && \
    dotnet publish "src/MatPhone/MatPhone.csproj" \
    -c Release -o /app/publish --no-restore \
    -p:UseAppHost=false \
    -p:Channel=$Channel -p:BuildNumber=$BuildNumber -p:BuildDate=$BUILD_DATE

# ---------------------------------------------------------------------------
# Runtime stage (ASP.NET Core base image)
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS="" \
    MATPHONE_PORT=6050 \
    MATPHONE_DATA=/data \
    TZ=Europe/Berlin \
    DOTNET_gcServer=0 \
    DOTNET_TieredPGO=1

# Configuration (JSON), the keys of the cookies and secrets, the recordings and greetings of the mailboxes, scratch space of the backup: one volume.
RUN mkdir -p /data && chown -R $APP_UID:$APP_UID /data
VOLUME ["/data"]

# Web interface; SIP for phones and providers (UDP and TCP); the audio (RTP and the WebRTC of the browser).
EXPOSE 6050 5060/udp 5060/tcp 6100-6199/udp

COPY --from=build /app/publish .
USER $APP_UID

# The app checks itself, so the image needs no extra tools.
HEALTHCHECK --interval=30s --timeout=8s --start-period=40s --retries=3 \
    CMD ["dotnet", "MatPhone.dll", "--healthcheck"]

ENTRYPOINT ["dotnet", "MatPhone.dll"]
