FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY FfxivVrRelay.csproj .
RUN dotnet restore FfxivVrRelay.csproj
COPY . .
RUN dotnet publish FfxivVrRelay.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app
COPY --from=build /app .
ENV RELAY_PORT=7850
EXPOSE 7850/udp
USER app
ENTRYPOINT ["dotnet", "FfxivVrRelay.dll"]
