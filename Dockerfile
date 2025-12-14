FROM mcr.microsoft.com/dotnet/runtime:9.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["InvoiceSchedulerJob/InvoiceSchedulerJob.csproj", "InvoiceSchedulerJob/"]
RUN dotnet restore "./InvoiceSchedulerJob/InvoiceSchedulerJob.csproj"
COPY . .
WORKDIR "/src/InvoiceSchedulerJob"
RUN dotnet build "./InvoiceSchedulerJob.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./InvoiceSchedulerJob.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Create logs directory
RUN mkdir -p /app/logs

ENTRYPOINT ["dotnet", "InvoiceSchedulerJob.dll"]