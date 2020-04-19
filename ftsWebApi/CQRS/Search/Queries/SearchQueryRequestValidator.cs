using FluentValidation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ftsWebApi.CQRS.Search.Queries
{
	public class SearchQueryRequestValidator : AbstractValidator<SearchQueryRequest>
	{
		public SearchQueryRequestValidator()
		{

			RuleFor(m=>m.PageIndex).GreaterThanOrEqualTo(0).When(m=>m.PageIndex.HasValue)
				.WithMessage("pageIndex (if defined) must be greater or equal than 0.");
			RuleFor(m => m.PageLength).InclusiveBetween(1, 100).When(m => m.PageLength.HasValue)
				.WithMessage("pageLength (if defined) must be between 1 and 100 (inclusively).");
			RuleFor(m => m.StartDate).InclusiveBetween(19000101, 20991231).When(m => m.StartDate.HasValue)
				.WithMessage("startDate (if defined) must be between 19000101 and 20991231 (inclusively).");
			RuleFor(m => m.EndDate).InclusiveBetween(19000101, 20991231).When(m => m.EndDate.HasValue)
				.WithMessage("endDate (if defined) must be between 19000101 and 20991231 (inclusively).");
		}
	}

}
