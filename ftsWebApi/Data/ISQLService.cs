using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ftsWebApi.Data
{

    public interface ISQLService : IDisposable
    {
        Task<SqlConnection> GetConnection(CancellationToken cancellationToken);
        Task<SqlDataReader> ExecuteReaderWithRetryAsync(SqlCommand sqlCommand, System.Data.CommandBehavior commandBehavior, CancellationToken cancellationToken);
        Task<object> ExecuteScalarWithRetryAsync(SqlCommand sqlCommand, CancellationToken cancellationToken);
        Task ExecuteNonQueryWithRetryAsync(SqlCommand sqlCommand, CancellationToken cancellationToken);
    }
}
