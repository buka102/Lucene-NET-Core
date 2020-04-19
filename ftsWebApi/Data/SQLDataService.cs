using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Polly;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ftsWebApi.Data
{
    public class SQLDataService : ISQLService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<SQLDataService> _logger;
        private SqlConnection _connection;
        private Polly.Retry.AsyncRetryPolicy _retryPolicy;
        private const int RetryCount = 3;
        private const int RetryInitialDelayInMs = 200;
        public SQLDataService(IConfiguration configuration, ILogger<SQLDataService> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }


        public void Dispose()
        {
            if (_connection != null && _connection.State == ConnectionState.Open)
            {
                _connection.Close();
                _logger.LogDebug("Connection is closed.");
            }
        }

        public async Task<SqlConnection> GetConnection(CancellationToken cancellationToken)
        {
            if (_connection == null)
            {
                _connection = new SqlConnection(_configuration["DatabaseConnectionString"]);

            }

            if (_connection.State != ConnectionState.Open)
            {
                try
                {
                    await _connection.OpenAsync(cancellationToken);
                    _logger.LogDebug("New connection is created");
                }
                catch (Exception ex)
                {
                    _logger.LogTrace(ex, "Resetting connection");
                    _connection = new SqlConnection(_configuration["DatabaseConnectionString"]);
                    _logger.LogDebug("New connection is created");
                    await _connection.OpenAsync(cancellationToken);
                }

                _logger.LogTrace("Connection is opened");
            }

            return _connection;
        }

        private static void GuardConnectionIsNotNull(SqlCommand command)
        {
            if (command.Connection == null)
            {
                throw new InvalidOperationException("ConnectionHasNotBeenInitialized");
            }
        }

        private Polly.Retry.AsyncRetryPolicy RetryPolicy
        {
            get
            {
                if (_retryPolicy == null)
                {
                    _retryPolicy = Policy.Handle<Exception>().WaitAndRetryAsync(
                     retryCount: RetryCount, // Retry 3 times
                     sleepDurationProvider: attempt => TimeSpan.FromMilliseconds(RetryInitialDelayInMs * Math.Pow(2, attempt - 1)), // Exponential backoff based on an initial 200 ms delay.
                     onRetry: (exception, attempt) =>
                     {
                     // Capture some information for logging/telemetry.
                     _logger.LogWarning($"ExecuteReaderWithRetryAsync: Retry {attempt} due to {exception}.");
                     });
                }

                return _retryPolicy;
            }
        }

        public async Task<SqlDataReader> ExecuteReaderWithRetryAsync(SqlCommand sqlCommand, System.Data.CommandBehavior commandBehavior, CancellationToken cancellationToken)
        {
            GuardConnectionIsNotNull(sqlCommand);

            return await RetryPolicy.ExecuteAsync<SqlDataReader>(async token => {
                var connection = sqlCommand.Connection;
                if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync(token);
                return await sqlCommand.ExecuteReaderAsync(commandBehavior, token);

            }, cancellationToken);

        }

        public async Task<object> ExecuteScalarWithRetryAsync(SqlCommand sqlCommand, CancellationToken cancellationToken)
        {
            GuardConnectionIsNotNull(sqlCommand);

            return await RetryPolicy.ExecuteAsync<object>(async token => {
                var connection = sqlCommand.Connection;
                if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync(token);
                return await sqlCommand.ExecuteScalarAsync(cancellationToken);

            }, cancellationToken);

        }

        public async Task ExecuteNonQueryWithRetryAsync(SqlCommand sqlCommand, CancellationToken cancellationToken)
        {
            GuardConnectionIsNotNull(sqlCommand);

            await RetryPolicy.ExecuteAsync(async token => {
                var connection = sqlCommand.Connection;
                if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync(token);
                await sqlCommand.ExecuteNonQueryAsync(cancellationToken);

            }, cancellationToken);

        }

    }
}
