# 1. Image nền siêu nhẹ chỉ chứa môi trường chạy .NET 10
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

# 2. Image chứa bộ công cụ SDK để biên dịch code
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Sao chép các tệp cấu hình trước để tối ưu hóa bộ nhớ đệm của Docker
COPY ["AutoDubbingSystem.slnx", "./"]
COPY ["AutoDubbing.API/AutoDubbing.API.csproj", "AutoDubbing.API/"]
COPY ["AutoDubbing.Service/AutoDubbing.Service.csproj", "AutoDubbing.Service/"]
COPY ["AutoDubbing.Data/AutoDubbing.Data.csproj", "AutoDubbing.Data/"]
COPY ["AutoDubbing.Tests/AutoDubbing.Tests.csproj", "AutoDubbing.Tests/"]
RUN dotnet restore "AutoDubbingSystem.slnx"

# Sao chép toàn bộ mã nguồn còn lại và tiến hành build
COPY . .
WORKDIR "/src/AutoDubbing.API"
RUN dotnet build "AutoDubbing.API.csproj" -c Release -o /app/build

# 3. Đóng gói các tệp tin đã biên dịch
FROM build AS publish
RUN dotnet publish "AutoDubbing.API.csproj" -c Release -o /app/publish

# 4. Chuyển bản đóng gói vào Image nền ở bước 1 để chạy
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "AutoDubbing.API.dll"]
