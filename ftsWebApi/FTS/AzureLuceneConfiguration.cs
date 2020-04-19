using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ftsWebApi.FTS
{
    public class AzureLuceneConfiguration
    {
        public string Container { get; set; }
        public string AzureStorageAccountName { get; set; }
        public string SASToken { get; set; }
        public string TempDirectory { get; set; }

    }
}
