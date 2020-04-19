using Lucene.Net.Index;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ftsWebApi.FTS
{
    public class LuceneIndexAndMetadata : IDisposable
    {
        public DirectoryReader Index { get; set; }
        public DateTimeOffset? LastIndexOffset { get; set; }

        /// <summary>
        /// Lucene Index needs to be disposed.
        /// </summary>
        public void Dispose()
        {
            try
            {
                if (this.Index != null)
                {
                    try
                    {
                        if (this.Index.Directory != null)
                        {
                            this.Index.Directory.Dispose();
                        }
                    }
                    finally
                    {

                    }
                }
                this.Index.Dispose();
            }
            finally
            {

            }
        }
    }
}
