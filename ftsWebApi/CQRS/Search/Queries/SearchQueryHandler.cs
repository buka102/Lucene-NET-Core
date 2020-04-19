using ftsWebApi.CQRS.Search.Queries.Models;
using ftsWebApi.FTS;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Index;
using Lucene.Net.Store;
using Lucene.Net.Util;
using MediatR;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ftsWebApi.CQRS.Search.Queries
{
    public class SearchQueryHandler : IRequestHandler<SearchQueryRequest, SearchQueryResponse>
    {
        private readonly ILuceneReaderService _luceneReaderService;
        private readonly AzureLuceneConfiguration AzureLuceneConfiguration;
        private readonly ILogger<SearchQueryHandler> _logger;
        private const string SearchByTerm = "content";
        private const string DateRangeByTerm = "updated";

        public SearchQueryHandler(ILuceneReaderService luceneReaderService,
            AzureLuceneConfiguration azureLuceneConfiguration, ILogger<SearchQueryHandler> logger)
        {
            _luceneReaderService = luceneReaderService;
            AzureLuceneConfiguration = azureLuceneConfiguration;
            _logger = logger;
        }

        public async Task<SearchQueryResponse> Handle(SearchQueryRequest request, CancellationToken cancellationToken)
        {
            IndexReader reader = null;
            try
            {
                // Ensures index backwards compatibility
                var AppLuceneVersion = LuceneVersion.LUCENE_48;

                //Used cached Lucene Index
                var readerWithMetadata = await _luceneReaderService.GetReader(cancellationToken);
                reader = readerWithMetadata.Index;

                //create an analyzer to process the text
                var analyzer = new StandardAnalyzer(AppLuceneVersion);

                var pageLength = request.PageLength ?? 30;
                var pageIndex = request.PageIndex ?? 0;

                //hardcoded
                var search_phrase = request.Query;

                var startDateQuery_as_int32 = 0; //min value
                if (request.StartDate.HasValue)
                {
                    //var startDateCriteria = DateTime.Parse("2020-01-01"); //if we want to support text to int transformation
                    startDateQuery_as_int32 = request.StartDate.Value;
                }

                var endDateQuery_as_int32 = int.MaxValue;
                if (request.EndDate.HasValue)
                {
                    //var endDateCriteria = DateTime.Parse("2021-12-31"); if we want to suppoer text to int transformation
                    endDateQuery_as_int32 = request.EndDate.Value;
                }




                //date filter
                var date_query = Lucene.Net.Search.NumericRangeQuery.NewInt32Range(DateRangeByTerm, startDateQuery_as_int32, endDateQuery_as_int32, true, true);

                //text query
                var query_classic_parser = new Lucene.Net.QueryParsers.Classic.QueryParser(LuceneVersion.LUCENE_48, SearchByTerm, analyzer);
                Lucene.Net.Search.Query text_query = query_classic_parser.Parse(search_phrase);

                //merging
                Lucene.Net.Search.BooleanQuery final_query = new Lucene.Net.Search.BooleanQuery();
                final_query.Add(text_query, Lucene.Net.Search.Occur.MUST);
                final_query.Add(date_query, Lucene.Net.Search.Occur.MUST);

                var searcher = new Lucene.Net.Search.IndexSearcher(reader);// writer.GetReader(applyAllDeletes: true));
                                                                           //var hits = searcher.Search(phrase, 20 /* top 20 */).ScoreDocs;
                var maxResult = pageLength * (pageIndex + 1) + 1; //need an extra one to determine if there is more

                _logger.LogDebug("Search criteria '{0}' with range ('{1}' to '{2}') with pageIndex {3} and pageLength {4}"
                    , search_phrase
                    , startDateQuery_as_int32, endDateQuery_as_int32
                    , pageIndex, pageLength);

                var results = searcher.Search(final_query, maxResult);

                _logger.LogDebug("Search result has {0} records (before skip).", results.ScoreDocs.Length);

                var response = new SearchQueryResponse();
                response.IsValid = true;
                response.PageIndex = pageIndex;
                response.PageLength = pageLength;
                response.IndexDateUtc = readerWithMetadata.LastIndexOffset;

                var lastPage = results.ScoreDocs.Skip(pageLength * pageIndex);

                if (lastPage.Count() == 0)
                {
                    //Console.WriteLine("No results.");
                    response.Hits = new SearchHitEntry[0];
                }
                else
                {
                    response.HasMore = lastPage.Count() > pageLength;   //if it has an extra one, then there is more

                    var records = new List<SearchHitEntry>();
                    foreach (var hit in lastPage.Take(pageLength)) //foreach (var hit in hits)
                    {
                        var foundDoc = searcher.Doc(hit.Doc);
                        var foundDocCreatedDate = foundDoc.GetField("updated").GetInt32Value();

                        DateTime parsedDate;

                        var searchResultEntry = new SearchHitEntry()
                        {
                            DocId = foundDoc.Get("doc_id"),
                            Text = foundDoc.Get("content"),
                            Rank = hit.Score,
                        };

                        if (foundDocCreatedDate.HasValue && DateTime.TryParseExact(foundDocCreatedDate.Value.ToString(), "yyyyMMdd", null,
                            DateTimeStyles.None, out parsedDate))
                        {

                            searchResultEntry.ModifiedDate = parsedDate;
                        }
                        else
                        {
                            searchResultEntry.ModifiedDate = null;
                        }
                        records.Add(searchResultEntry);


                    }
                    response.Hits = records.ToArray();

                }



                return response;
            }
            catch (Exception ex)
            {
                var result = new SearchQueryResponse();
                result.IsValid = false;
                result.Errors = new List<string>();
                result.Errors.Add("Unexpected error occured: " + ex.Message);
                return result;
            }
            finally
            {
                // since Lucene IndexReader is cached, it is cache responsibility to dispose Index and Directory properly
                //if (reader != null)
                //{
                //    reader.Dispose();
                //}
                //if (azureDirectory != null)
                //{
                //    azureDirectory.Dispose();
                //}
            }
        }
    }
}
