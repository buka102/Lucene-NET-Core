﻿CREATE TABLE [dbo].[FTS_Config]
(
	[Id] INT IDENTITY(1, 1) NOT NULL, 
	[LastSyncPoint] DATETIMEOFFSET NULL, 
    CONSTRAINT [PK_FTS_Config] PRIMARY KEY ([Id])
)
