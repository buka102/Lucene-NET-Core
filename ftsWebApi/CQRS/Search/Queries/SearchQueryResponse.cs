using ftsWebApi.CQRS.Search.Queries.Models;
using ftsWebApi.CQRS.Shared;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ftsWebApi.CQRS.Search.Queries
{
    public class SearchQueryResponse: BaseResponse
    {
        public SearchHitEntry[] Hits { get; set; }
        public int PageLength { get; set; }
        public int PageIndex { get; set; }
        public bool? HasMore { get; set; }
        public DateTimeOffset? IndexDateUtc { get; set; }
    }
}
