FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY CryptoTrading.App.ServiceContracts/ CryptoTrading.App.ServiceContracts/
COPY CryptoTrading.App.Core/ CryptoTrading.App.Core/
COPY CryptoTrading.App.Persistence/ CryptoTrading.App.Persistence/
COPY CryptoTrading.App.Exchange.BinanceNet/ CryptoTrading.App.Exchange.BinanceNet/
COPY CryptoTrading.App.MarketDataService/ CryptoTrading.App.MarketDataService/
COPY Binance/ Binance/
COPY Indicators/ Indicators/

RUN dotnet publish CryptoTrading.App.MarketDataService/CryptoTrading.App.MarketDataService.csproj \
    -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 5001
ENV ASPNETCORE_URLS=http://+:5001
ENTRYPOINT ["dotnet", "CryptoTrading.App.MarketDataService.dll"]
