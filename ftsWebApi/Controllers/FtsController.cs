using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ftsWebApi.CQRS.Search.Commands;
using ftsWebApi.CQRS.Search.Queries;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ftsWebApi.Controllers
{
    [Route("api/search")]
    [ApiController]
    public class FtsController : ControllerBase
    {
        private readonly IMediator _mediator;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public FtsController(IMediator mediator,
            IHttpContextAccessor httpContextAccessor)
        {
            _mediator = mediator;
            _httpContextAccessor = httpContextAccessor;
        }

        [Route("")]
        [HttpGet]
        public async Task<IActionResult> Search([FromQuery] string query, [FromQuery] int? startDate, [FromQuery] int? endDate, [FromQuery] int? pageIndex, [FromQuery] int? pageLength, CancellationToken cancellationToken)
        {
            var result = await _mediator.Send(new SearchQueryRequest()
            {
                Query = query, 
                StartDate = startDate,
                EndDate = endDate,
                PageIndex =pageIndex,
                PageLength =pageLength
            }, cancellationToken);

            return result.JsonResult();
        }

        [Route("rebuild_index")]
        [HttpPost]
        
        public async Task<IActionResult> RebuildIndex([FromHeader(Name ="fts_options")] string fts_options, CancellationToken cancellationToken)
        {
            var result = await _mediator.Send(new RebuildIndicesRequest()
            {
                FullRebuild = string.Equals(fts_options, "full", StringComparison.InvariantCultureIgnoreCase)
            }, cancellationToken);

            return result.JsonResult();
        }

    }
}