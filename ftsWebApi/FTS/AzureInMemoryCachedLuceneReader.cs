using ftsWebApi.Data;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Index;
using Lucene.Net.Util;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ftsWebApi.FTS
{


    public class AzureInMemoryCachedLuceneReader: ILuceneReaderService
    {
        private readonly AzureLuceneConfiguration _azureLuceneConfiguration;
        private readonly ISQLService _SQLservice;
        private readonly ILogger<AzureInMemoryCachedLuceneReader> _logger;
        private IMemoryCache _cache;
        private const string _cache_idx_key = "_lucene_index";
        private const int searchCacheExpirationInSeconds = 30*60;
        private SemaphoreSlim semaphoreSlim = new SemaphoreSlim(1);
        public AzureInMemoryCachedLuceneReader(IMemoryCache memoryCache,
            ISQLService SQLservice,
            AzureLuceneConfiguration azureLuceneConfiguration, 
            ILogger<AzureInMemoryCachedLuceneReader> logger)
        {
            _cache = memoryCache;
            _azureLuceneConfiguration = azureLuceneConfiguration;
            _SQLservice = SQLservice;
            _logger = logger;
        }

        private void CacheItemRemoved(object key, object value, EvictionReason reason, object state)
        {
            _logger.LogDebug("LuceneIndex is removed from cache with reason: {0}", reason);
            if (key.ToString() == _cache_idx_key) {
                var typed = value as LuceneIndexAndMetadata;
                if (typed != null)
                {
                    try
                    {
                        _logger.LogDebug("LuceneIndex try to dispose it...");
                        typed.Dispose();
                        _logger.LogTrace("LuceneIndex is disposed");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "LuceneIndex failed to dispose.");
                    }
                }
            }
        }

        public void Evict()
        {
            _cache.Remove(_cache_idx_key);
            _logger.LogDebug("Evicting LuceneIndex from cache by caller.");
        }

        public async Task<LuceneIndexAndMetadata> GetReader(CancellationToken cancellationToken)
        {

            LuceneIndexAndMetadata cached_reader;
            if (!_cache.TryGetValue(_cache_idx_key, out cached_reader))
            {
                await semaphoreSlim.WaitAsync();
                try
                {
                    // Key not in cache, so get data.
                    cached_reader = await GetReaderInternal(cancellationToken);

                    // Set cache options.
                    var cacheEntryOptions = new MemoryCacheEntryOptions()
                        .RegisterPostEvictionCallback(callback: CacheItemRemoved, state: this)
                        .SetAbsoluteExpiration(TimeSpan.FromSeconds(searchCacheExpirationInSeconds))
                        // Keep in cache for this time, reset time if accessed.
                        .SetSlidingExpiration(TimeSpan.FromSeconds(120));


                    // Save data in cache.
                    _cache.Set(_cache_idx_key, cached_reader, cacheEntryOptions);
                    _logger.LogDebug("LuceneIndex is added to cache.");
                }
                catch(Exception ex)
                {
                    _logger.LogError(ex, "Unexpected exception while putting LuceneIndex into cache");
                    if (cached_reader == null)
                    {
                        throw new ApplicationException("LuceneIndex is null", ex);
                    }
                }
                finally
                {
                    semaphoreSlim.Release();
                }
                
            }
            return cached_reader;
        }

        public async Task<LuceneIndexAndMetadata> GetReaderInternal(CancellationToken cancellationToken)
        {

            var resultObject = new LuceneIndexAndMetadata();

            //Azure configuration
            var accountSAS = new Microsoft.Azure.Storage.Auth.StorageCredentials(_azureLuceneConfiguration.SASToken);
            var accountWithSAS = new Microsoft.Azure.Storage.CloudStorageAccount(accountSAS, _azureLuceneConfiguration.AzureStorageAccountName, endpointSuffix: null, useHttps: true);

  
            _logger.LogInformation("_azureLuceneConfiguration.TempDirectory = {0}", _azureLuceneConfiguration.TempDirectory);

            var tempLocation = _azureLuceneConfiguration.TempDirectory ?? "temp";
            _logger.LogDebug("Lucene IndexReader is located in {0} azure storage account (container '{1}')"
                , _azureLuceneConfiguration.AzureStorageAccountName
                , _azureLuceneConfiguration.Container);

            var azureDirectory = new Lucene.Net.Store.Azure.AzureDirectory(accountWithSAS, tempLocation, containerName: _azureLuceneConfiguration.Container);
            //ensure RAMDirectory
            azureDirectory.CacheDirectory = new Lucene.Net.Store.RAMDirectory();

            var reader = DirectoryReader.Open(azureDirectory);

            _logger.LogDebug("Lucene IndexReader is acquired.");

            resultObject.Index = reader;

            using (var dbConnection = await _SQLservice.GetConnection(cancellationToken))
            {
                //we need last sync point only if it is not full rebuild
                var dbCommand = @"SELECT TOP 1 LastSyncPoint FROM [dbo].[FTS_Config]";
                var cmd = new SqlCommand(dbCommand, dbConnection);
                try
                {
                    var untyped = await _SQLservice.ExecuteScalarWithRetryAsync(cmd, cancellationToken);
                    var lastSyncPointNullable = untyped as DateTimeOffset?;

                    if (lastSyncPointNullable.HasValue)
                    {
                        resultObject.LastIndexOffset = lastSyncPointNullable.Value;
                    }

                    _logger.LogDebug("Last sync point is {0}", lastSyncPointNullable.HasValue ? lastSyncPointNullable.Value.ToString() : "'never'");

                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "unexpected failure to acquire LastSyncPoint from database");
                    throw;
                }
            }


            return resultObject;
        }

    }
}
