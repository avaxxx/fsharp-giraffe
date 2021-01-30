FROM mcr.microsoft.com/dotnet/aspnet:5.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:5.0-alpine AS build
WORKDIR /src
COPY ["fsharp-giraffe.fsproj", "./"]
RUN dotnet restore "fsharp-giraffe.fsproj"
COPY . .
WORKDIR "/src/."
RUN dotnet build "fsharp-giraffe.fsproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "fsharp-giraffe.fsproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "fsharp-giraffe.App.dll"]
