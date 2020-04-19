using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ftsWebApi.FTS
{
    public class SearchDocument
    {
        public string DocumentID { get; set; }
        public string Name { get; set; }
        public string Content { get; set; }
        public int UpdatedAt { get; set; }
    }
}
