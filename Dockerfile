FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /repo
COPY . .
RUN dotnet publish src/VehicleLocator.Worker/VehicleLocator.Worker.csproj \
    -c Release -o /out/worker
RUN dotnet publish src/VehicleLocator.MockWriters/VehicleLocator.MockWriters.csproj \
    -c Release -o /out/mockwriters

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app
COPY --from=build /out/worker ./worker
COPY --from=build /out/mockwriters ./mockwriters
