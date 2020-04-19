SET IDENTITY_INSERT [dbo].[FTS_Config] ON

IF NOT EXISTS (SELECT * FROM [dbo].[FTS_Config])
   BEGIN
       INSERT INTO [dbo].[FTS_Config] ([Id], [LastSyncPoint])
       VALUES (1, NULL)
   END

SET IDENTITY_INSERT [dbo].[FTS_Config] OFF
GO