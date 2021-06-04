CREATE TABLE [dbo].[HistoricMarketData]
(
	[Id] INT NOT NULL PRIMARY KEY, 
    [Symbol] NCHAR(10) NOT NULL,
    [Interval] NCHAR(10) NOT NULL, 
    [OpenTime] DATETIME NOT NULL, 
    [Open] DECIMAL NULL, 
    [High] DECIMAL NULL, 
    [Low] DECIMAL NULL, 
    [Close] DECIMAL NOT NULL, 
    [Volume] DECIMAL NULL, 
    [CloseTime] DATETIME NOT NULL, 
    [NumberOfTrades] BIGINT NULL
    
)
