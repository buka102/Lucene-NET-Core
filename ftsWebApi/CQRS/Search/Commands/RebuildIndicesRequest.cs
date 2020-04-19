using MediatR;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ftsWebApi.CQRS.Search.Commands
{
    public class RebuildIndicesRequest:IRequest<RebuildIndicesResponse>
    {
        public bool FullRebuild { get; set; }
    }
}
