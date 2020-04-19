using MediatR;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ftsWebApi.CQRS.Search.Queries
{
    public class SearchQueryRequest : IRequest<SearchQueryResponse>
    {
        public string Query { get; set; }
        public int? StartDate { get; set; }
        public int? EndDate { get; set; }
        public int? PageLength { get; set; }
        public int? PageIndex { get; set; }
    }
}
