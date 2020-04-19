using Lucene.Net.Index;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ftsWebApi.FTS
{
    public interface ILuceneReaderService
    {
        Task<LuceneIndexAndMetadata> GetReader(CancellationToken cancellationToken);
        void Evict();
    }
}
