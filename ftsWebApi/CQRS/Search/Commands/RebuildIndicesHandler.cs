using ftsWebApi.CQRS.Shared;
using ftsWebApi.Data;
using ftsWebApi.FTS;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Index;
using Lucene.Net.Store;
using Lucene.Net.Util;
using MediatR;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ftsWebApi.CQRS.Search.Commands
{
    public class RebuildIndicesHandler : IRequestHandler<RebuildIndicesRequest, RebuildIndicesResponse>
    {
        private readonly int PageSize = 100;
        private readonly AzureLuceneConfiguration AzureLuceneConfiguration;
        private readonly ILogger<RebuildIndicesHandler> _logger;
        private readonly ISQLService _SQLservice;
        private readonly ILuceneReaderService _luceneReaderService;

        public RebuildIndicesHandler(AzureLuceneConfiguration azureLuceneConfiguration, ISQLService SQLservice
            , ILuceneReaderService luceneReaderService, ILogger<RebuildIndicesHandler> logger)
        {
            AzureLuceneConfiguration = azureLuceneConfiguration;
            _logger = logger;
            _SQLservice = SQLservice;
            _luceneReaderService = luceneReaderService;
        }

        public async Task<RebuildIndicesResponse> Handle(RebuildIndicesRequest request, CancellationToken cancellationToken)
        {
            _logger.LogDebug("RebuildIndicesResponseHandler started.");
            cancellationToken.ThrowIfCancellationRequested();

            IndexWriter writer = null;
            Lucene.Net.Store.Azure.AzureDirectory azureDirectory = null;
            DateTimeOffset lastSyncPoint = DateTimeOffset.MinValue;
            DateTimeOffset currentSyncPoint = DateTimeOffset.Now;
            int? updatedCount = null;
            int? deletedCount = null;
            try
            {
                // Ensures index backwards compatibility
                var AppLuceneVersion = LuceneVersion.LUCENE_48;

                //Azure configuration
                var accountSAS = new Microsoft.Azure.Storage.Auth.StorageCredentials(AzureLuceneConfiguration.SASToken);
                var accountWithSAS = new Microsoft.Azure.Storage.CloudStorageAccount(accountSAS, AzureLuceneConfiguration.AzureStorageAccountName, endpointSuffix: null, useHttps: true);

                var tempLocation = AzureLuceneConfiguration.TempDirectory ?? "temp";
                _logger.LogTrace("tempLocation: {0}", tempLocation);

                azureDirectory = new Lucene.Net.Store.Azure.AzureDirectory(accountWithSAS, tempLocation, containerName: AzureLuceneConfiguration.Container);
                //ensure RAMDirectory
                azureDirectory.CacheDirectory = new RAMDirectory();

                //create an analyzer to process the text
                var analyzer = new StandardAnalyzer(AppLuceneVersion);

                //create an index writer
                var indexConfig = new IndexWriterConfig(AppLuceneVersion, analyzer);

                writer = new IndexWriter(azureDirectory, indexConfig); //used to be dir
                _logger.LogTrace("IndexWriter is initialized");

                if (request.FullRebuild)
                {
                    _logger.LogInformation("Full Rebuild is requested. Deleting indices");
                    writer.DeleteAll();
                    writer.Commit();
                    _logger.LogTrace("Full Rebuild is committed.");
                }



                using (var dbConnection = await _SQLservice.GetConnection(cancellationToken))
                {
                    SqlCommand cmd;
                    if (!request.FullRebuild)
                    {
                        //we need last sync point only if it is not full rebuild
                        var dbCommand = @"SELECT TOP 1 LastSyncPoint FROM [dbo].[FTS_Config]";

                        cmd = new SqlCommand(dbCommand, dbConnection);
                        try
                        {
                            var untyped = await _SQLservice.ExecuteScalarWithRetryAsync(cmd, cancellationToken);
                            var lastSyncPointNullable = untyped as DateTimeOffset?;

                            if (lastSyncPointNullable.HasValue)
                            {
                                lastSyncPoint = lastSyncPointNullable.Value;
                            }

                            _logger.LogDebug("Last sync point is {0}", lastSyncPointNullable.HasValue ? lastSyncPointNullable.Value.ToString() : "'never'");

                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "unexpected failure to acquire LastSyncPoint from database");
                            throw;
                        }
                    }
                    else
                    {
                        lastSyncPoint = DateTimeOffset.MinValue;
                    }

                    

                    //determine number of records that will need to be processed

                    var dbCountCommand = @"SELECT COUNT(Id) from [dbo].[Test_Data] WHERE UpdatedAt >= @lastSyncPoint AND UpdatedAt < @currentSyncPoint AND DeletedAt IS NULL";
                    cmd = new SqlCommand(dbCountCommand, dbConnection);
                    cmd.Parameters.Add("@lastSyncPoint", System.Data.SqlDbType.DateTimeOffset);
                    cmd.Parameters["@lastSyncPoint"].Value = lastSyncPoint;
                    cmd.Parameters.Add("@currentSyncPoint", System.Data.SqlDbType.DateTimeOffset);
                    cmd.Parameters["@currentSyncPoint"].Value = currentSyncPoint;

                    try
                    {
                        var untyped = await _SQLservice.ExecuteScalarWithRetryAsync(cmd, cancellationToken);
                        updatedCount = untyped as int?;
                        _logger.LogDebug("Expected number of updated documents {0}", updatedCount.HasValue ? updatedCount.Value.ToString() : "'none'");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "unexpected failure to acquire number of documents to be updated from database");
                        throw;
                    }

                    

                    //working on deleted documents
                    
                    if (!request.FullRebuild)
                    {
                        //also need to remove "Deleted" documents. Only if not full rebuild of indices               
                        var dbDeletedCountCommand = @"SELECT COUNT(Id) from [dbo].[Test_Data] WHERE DeletedAt >= @lastSyncPoint AND DeletedAt<=@currentSyncPoint AND DeletedAt IS NOT NULL";
                        cmd = new SqlCommand(dbDeletedCountCommand, dbConnection);
                        cmd.Parameters.Add("@lastSyncPoint", System.Data.SqlDbType.DateTimeOffset);
                        cmd.Parameters["@lastSyncPoint"].Value = lastSyncPoint;
                        cmd.Parameters.Add("@currentSyncPoint", System.Data.SqlDbType.DateTimeOffset);
                        cmd.Parameters["@currentSyncPoint"].Value = currentSyncPoint;
                        try
                        {
                            var untyped = await _SQLservice.ExecuteScalarWithRetryAsync(cmd, cancellationToken);
                            deletedCount = untyped as int?;
                            _logger.LogDebug("Expected number of deleted documents {0}", deletedCount.HasValue ? updatedCount.Value.ToString() : "'none'");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "unexpected failure to acquire 'number of documents to be delete from indicies' from database");
                            throw;
                        }
                    }
                    
                }
                var atLeastOneUpdate = false;
                if (updatedCount.HasValue && updatedCount.Value > 0)
                {
                    _logger.LogDebug("Expected number of updated documents: {0}", updatedCount.Value);
                    //Start updating 'Updated records'
                    await UpdateIndicesWithAddedDocuments(lastSyncPoint, currentSyncPoint, updatedCount.Value, writer, cancellationToken);
                    atLeastOneUpdate = true;
                }
                else
                {
                    _logger.LogDebug("Expected number of updated documents: none ");
                }


                if (deletedCount.HasValue && deletedCount.Value > 0)
                {
                    _logger.LogDebug("Expected number of deleted documents: {0}", deletedCount.Value);
                    await UpdateIndicesWithDeletedDocuments(lastSyncPoint, currentSyncPoint, deletedCount.Value, writer, cancellationToken);
                    atLeastOneUpdate = true;
                }
                else
                {
                    _logger.LogDebug("Expected number of updated documents: none ");
                }

                if (atLeastOneUpdate)
                {
                    _logger.LogDebug("Expected number of updated documents: none ");
                    _luceneReaderService.Evict();
                    writer.Flush(triggerMerge: true, applyAllDeletes: true);
                    _logger.LogInformation("Indexes are updated");
                }

                //update LastSyncPoint
                using (var dbConnection = await _SQLservice.GetConnection(cancellationToken))
                {
                    var dbCommand = @"UPDATE [dbo].[FTS_Config] SET LastSyncPoint = @currentSyncPoint";

                    var cmd = new SqlCommand(dbCommand, dbConnection);
                    cmd.Parameters.Add("@currentSyncPoint", System.Data.SqlDbType.DateTimeOffset);
                    cmd.Parameters["@currentSyncPoint"].Value = currentSyncPoint;
                    try
                    {
                        await _SQLservice.ExecuteNonQueryWithRetryAsync(cmd, cancellationToken);
                        _logger.LogDebug("Last sync point is set to {0}", currentSyncPoint);

                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "unexpected failure to update LastSyncPoint in database");
                        throw;
                    }
                }


                var result = new RebuildIndicesResponse
                {
                    IsValid = true,
                    Success = true,
                    NumberOfUpdates = updatedCount,
                    NumberOfDeletes = deletedCount,
                    CurrentSyncPoint = currentSyncPoint
                };

                return result;

            }
            catch (LockObtainFailedException)
            {
                var result = new RebuildIndicesResponse();
                result.IsValid = false;
                result.Errors = new List<string>();
                result.Errors.Add("Failed to lock full text search index file. Probaly there is another job is running. Please try again later.");
                return result;
            }
            catch(Exception ex)
            {
                var result = new RebuildIndicesResponse();
                result.IsValid = false;
                result.Errors = new List<string>();
                result.Errors.Add("Unexpected error occured: "+ex.Message);
                return result;
            }
            finally
            {
                if (writer != null)
                {
                    writer.Dispose();
                }
                if (azureDirectory != null)
                {
                    azureDirectory.Dispose();
                }
            }
        }

        private async Task UpdateIndicesWithAddedDocuments(DateTimeOffset lastSyncPoint, DateTimeOffset currentSyncPoint, int numberOfUpdates, IndexWriter indexWriter, CancellationToken cancellationToken)
        {
            using (var dbConnection = await _SQLservice.GetConnection(cancellationToken))
            {
                var startRow = 1;

                var dbCountCommand = @"SELECT Id, ISNULL(Name,''), ISNULL(Content,''), UpdatedAt from [dbo].[Test_Data] 
                        WHERE UpdatedAt >= @lastSyncPoint AND UpdatedAt < @currentSyncPoint AND DeletedAt IS NULL
                        ORDER BY Id ASC OFFSET @StartRow - 1 ROWS FETCH NEXT @RowsPerPage ROWS ONLY ";


                while (numberOfUpdates >= startRow)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var cmd = new SqlCommand(dbCountCommand, dbConnection);
                    cmd.Parameters.Add("@lastSyncPoint", System.Data.SqlDbType.DateTimeOffset);
                    cmd.Parameters["@lastSyncPoint"].Value = lastSyncPoint;
                    cmd.Parameters.Add("@currentSyncPoint", System.Data.SqlDbType.DateTimeOffset);
                    cmd.Parameters["@currentSyncPoint"].Value = currentSyncPoint;
                    cmd.Parameters.Add("@StartRow", System.Data.SqlDbType.Int);
                    cmd.Parameters["@StartRow"].Value = startRow;
                    cmd.Parameters.Add("@RowsPerPage", System.Data.SqlDbType.Int);
                    cmd.Parameters["@RowsPerPage"].Value = PageSize;

                    try
                    {
                        using (var reader = await _SQLservice.ExecuteReaderWithRetryAsync(cmd, System.Data.CommandBehavior.SequentialAccess, cancellationToken))
                        {
                            while (await reader.ReadAsync())
                            {
                                var document_id = await reader.GetFieldValueAsync<int>(0);
                                var document_name = await reader.GetFieldValueAsync<string>(1);
                                var document_content = await reader.GetFieldValueAsync<string>(2);
                                var document_updatedAt = await reader.GetFieldValueAsync<DateTimeOffset>(3);

                                var updatedAtAsNumber = int.Parse(document_updatedAt.ToString("yyyyMMdd"));

                                var searchDocument = new SearchDocument()
                                {
                                    DocumentID = document_id.ToString(),
                                    Name = document_name,
                                    Content = document_content,
                                    UpdatedAt = updatedAtAsNumber
                                };

                                var doc = new Lucene.Net.Documents.Document
                                {
                                        // StringField indexes but doesn't tokenize
                                        new Lucene.Net.Documents.StringField("doc_id", searchDocument.DocumentID, Lucene.Net.Documents.Field.Store.YES),
                                        new Lucene.Net.Documents.StringField("name", searchDocument.Name, Lucene.Net.Documents.Field.Store.YES),
                                        new Lucene.Net.Documents.TextField("content", searchDocument.Content, Lucene.Net.Documents.Field.Store.YES),
                                        new Lucene.Net.Documents.Int32Field("updated", searchDocument.UpdatedAt, Lucene.Net.Documents.Field.Store.YES)
                                };

                                indexWriter.AddDocument(doc);
                                startRow++;
                            }
                        }


                        _logger.LogDebug("Processed {0} records (of {1} total) for update", (startRow-1), numberOfUpdates);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "unexpected failure during indexes update");
                        throw;
                    }

                }

                _logger.LogInformation("Processed {0} records for update of FTS indices. Completed.", (startRow - 1));

            }
        }
        private async Task UpdateIndicesWithDeletedDocuments(DateTimeOffset lastSyncPoint, DateTimeOffset currentSyncPoint, int numberOfDeletes, IndexWriter indexWriter, CancellationToken cancellationToken)
        {
            using (var dbConnection = await _SQLservice.GetConnection(cancellationToken))
            {
                var startRow = 1;

                var dbCountCommand = @"SELECT Id from [dbo].[Test_Data] 
                        WHERE DeletedAt >= @lastSyncPoint AND DeletedAt < @currentSyncPoint AND DeletedAt IS NOT NULL
                        ORDER BY Id ASC OFFSET @StartRow - 1 ROWS FETCH NEXT @RowsPerPage ROWS ONLY ";


                while (numberOfDeletes >= startRow)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var cmd = new SqlCommand(dbCountCommand, dbConnection);
                    cmd.Parameters.Add("@lastSyncPoint", System.Data.SqlDbType.DateTimeOffset);
                    cmd.Parameters["@lastSyncPoint"].Value = lastSyncPoint;
                    cmd.Parameters.Add("@currentSyncPoint", System.Data.SqlDbType.DateTimeOffset);
                    cmd.Parameters["@currentSyncPoint"].Value = currentSyncPoint;
                    cmd.Parameters.Add("@StartRow", System.Data.SqlDbType.Int);
                    cmd.Parameters["@StartRow"].Value = startRow;
                    cmd.Parameters.Add("@RowsPerPage", System.Data.SqlDbType.Int);
                    cmd.Parameters["@RowsPerPage"].Value = PageSize;

                    try
                    {
                        using (var reader = await _SQLservice.ExecuteReaderWithRetryAsync(cmd, System.Data.CommandBehavior.SequentialAccess, cancellationToken))
                        {
                            while (await reader.ReadAsync())
                            {
                                var document_id = await reader.GetFieldValueAsync<int>(0);

                                indexWriter.DeleteDocuments(new Term("doc_id", document_id.ToString()));
                                startRow++;
                            }
                        }


                        _logger.LogDebug("Processed {0} records (of {1} total) for delete", (startRow - 1), numberOfDeletes);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "unexpected failure during indexes dalete");
                        throw;
                    }

                }

                _logger.LogInformation("Processed {0} records for delete of FTS indices. Completed.", (startRow - 1));

            }
        }

    }
}
