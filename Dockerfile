FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY src/MistChess.Core/MistChess.Core.csproj src/MistChess.Core/
COPY src/MistChess.Web/MistChess.Web.csproj src/MistChess.Web/
RUN dotnet restore src/MistChess.Web/MistChess.Web.csproj

COPY src/MistChess.Core/ src/MistChess.Core/
COPY src/MistChess.Web/ src/MistChess.Web/
RUN dotnet publish src/MistChess.Web/MistChess.Web.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080
COPY --from=build /app/publish .
ENTRYPOINT ["sh", "-c", "export ASPNETCORE_URLS=\"http://0.0.0.0:${PORT:-8080}\"; exec dotnet MistChess.Web.dll"]
