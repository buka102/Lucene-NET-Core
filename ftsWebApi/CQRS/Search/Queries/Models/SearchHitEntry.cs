using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ftsWebApi.CQRS.Search.Queries.Models
{
    public class SearchHitEntry
    {
        public string DocId { get; set; }
        public float Rank { get; set; }
        public string Text { get; set; }
        public DateTime? ModifiedDate { get; set; }
    }
}
