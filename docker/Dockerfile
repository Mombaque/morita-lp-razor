FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["Morita.LP.Razor.csproj", "./"]
RUN dotnet restore "Morita.LP.Razor.csproj"
COPY . .
RUN dotnet build "Morita.LP.Razor.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Morita.LP.Razor.csproj" -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
EXPOSE 5173
ENV ASPNETCORE_URLS=http://+:5173
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Morita.LP.Razor.dll"]
