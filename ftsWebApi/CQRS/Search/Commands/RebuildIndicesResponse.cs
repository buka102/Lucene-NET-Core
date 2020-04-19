using ftsWebApi.CQRS.Shared;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ftsWebApi.CQRS.Search.Commands
{
    public class RebuildIndicesResponse : BaseResponse
    {
        public bool Success { get; set; }
        public int? NumberOfUpdates { get; set; }
        public int? NumberOfDeletes { get; set; }
        public DateTimeOffset? CurrentSyncPoint { get; set; }
    }
}
