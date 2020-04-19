using CsvHelper.Configuration.Attributes;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Index;
using Lucene.Net.Store;
using Lucene.Net.Util;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace ConsoleApp
{

    public class DataRow
    {
        public string Name { get; set; }
        public string FavoritePhrase { get; set; }
    }

    public class EmployeeRecord
    {
        [Index(0)]
        public string EmpID { get; set; }
        [Index(1)]
        public string NamePrefix { get; set; }
        [Index(2)]
        public string FirstName { get; set; }
        [Index(3)]
        public string MiddleInitial { get; set; }
        [Index(4)]
        public string LastName { get; set; }
        [Index(5)]
        public string Gender { get; set; }
 }

    public class MitralRecord
    {
        [Index(1)]
        public string PatientID { get; set; }
        [Index(2)]
        public string Findings { get; set; }

    }

    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Initializing!");

            // Ensures index backwards compatibility
            var AppLuceneVersion = LuceneVersion.LUCENE_48;
            //Azure settings
            var skipAdding = true;
            if (skipAdding)
            {
                Console.WriteLine("Skipping adding new index...");
            }
            
            //var connectionString = "https://blobfuse4ask.blob.core.windows.net/lucenefts";

            //var sasToken = "?st=2020-03-23T19%3A16%3A45Z&se=2021-03-23T19%3A16%3A00Z&sp=racwdl&sv=2018-03-28&sr=c&sig=rhyoOcnOSNYeuKkTEZUapk8Vu4o4lypkHY0AE%2Bzqoj0%3D";
            var sasToken = "?sv=2018-03-28&ss=b&srt=sco&sp=rwdlacup&st=2020-03-23T19%3A27%3A55Z&se=2021-03-23T19%3A27%3A00Z&sig=mDMu2F2cZD8Fla%2BSa73opVX2q9uelgcv%2BenMubtX8W0%3D";
            var accountSAS = new Microsoft.Azure.Storage.Auth.StorageCredentials(sasToken);
            var accountWithSAS = new Microsoft.Azure.Storage.CloudStorageAccount(accountSAS, "blobfuse4ask", endpointSuffix: null, useHttps: true);

            //var cloudStorageAccount = Microsoft.Azure.Storage.CloudStorageAccount.Parse(connectionString);
            const string containerName = "lucenefts";

            var indexTempLocation = @"C:/_projects/lucene/fs_temp";

            var azureDirectory = new Lucene.Net.Store.Azure.AzureDirectory(accountWithSAS, indexTempLocation, containerName: containerName);
            //ensure RAMDirectory
            azureDirectory.CacheDirectory = new RAMDirectory(); 

            var indexLocation = @"C:/_projects/lucene/fsindex";
            var dir = FSDirectory.Open(indexLocation);
            

            //create an analyzer to process the text
            var analyzer = new StandardAnalyzer(AppLuceneVersion);

            //create an index writer
            var indexConfig = new IndexWriterConfig(AppLuceneVersion, analyzer);

            if (!skipAdding)
            {
                var writer = new IndexWriter(azureDirectory, indexConfig); //used to be dir

                Console.WriteLine("Clear index!");
                writer.DeleteAll();
                writer.Commit();

                Console.WriteLine("Reading data from CSV file!");
                List<MitralRecord> result;
                //load from CSV file
                using (TextReader fileReader = File.OpenText("./swmitral.csv")) //./data.csv
                {
                    var csv = new CsvHelper.CsvReader(fileReader, CultureInfo.InvariantCulture);
                    csv.Configuration.HasHeaderRecord = false;
                    csv.Read();
                    result = csv.GetRecords<MitralRecord>().ToList();
                }

                Console.WriteLine("Adding to index! {0}", result.Count);
                //var source = new
                //{
                //    Name = "Kermit the Frog",
                //    FavoritePhrase = "The quick brown fox jumps over the lazy dog"
                //};
                //var doc = new Lucene.Net.Documents.Document
                //    {
                //        // StringField indexes but doesn't tokenize
                //        new Lucene.Net.Documents.StringField("name", source.Name, Lucene.Net.Documents.Field.Store.YES),
                //        new Lucene.Net.Documents.TextField("favoritePhrase", source.FavoritePhrase, Lucene.Net.Documents.Field.Store.YES)
                //    };
                //writer.AddDocument(doc);
                var random = new Random();
                foreach (var dataRow in result)
                {

                    //add random date in last year
                    //mod_date:[20020101 TO 20030101]

                    var random_dos = DateTime.Today.AddDays(-1 * random.Next(0, 365));
                    var text_dos_as_int = int.Parse(random_dos.ToString("yyyyMMdd"));

                    var doc = new Lucene.Net.Documents.Document
                    {
                        // StringField indexes but doesn't tokenize
                        new Lucene.Net.Documents.StringField("patientId", dataRow.PatientID, Lucene.Net.Documents.Field.Store.YES),
                        new Lucene.Net.Documents.TextField("findinds", dataRow.Findings, Lucene.Net.Documents.Field.Store.YES),
                        new Lucene.Net.Documents.Int32Field("created", text_dos_as_int, Lucene.Net.Documents.Field.Store.YES)
                };
                    writer.AddDocument(doc);
                }


                writer.Flush(triggerMerge: false, applyAllDeletes: false);
                writer.Dispose();
            }
            
            
            Console.WriteLine("Starting search...");

            var ireader = DirectoryReader.Open(azureDirectory);
            var search_phrase = "-moderate +severe mitral regurgitation";
            var startDateCriteria = DateTime.Parse("2020-01-01");
            var endDateCriteria = DateTime.Parse("2021-12-31");
            Console.WriteLine("Search with phrase: '{0}' with date range {1:d} to {2:d}", search_phrase, startDateCriteria, endDateCriteria);
            // search with a phrase
            //var phrase = new Lucene.Net.Search.MultiPhraseQuery();
            //phrase.Add(new Term("findinds", "severe"));
            //phrase.Add(new Term("findinds", "mitral"));
            //phrase.Add(new Term("findinds", "regurgitation"));


            //date filter
            var startDateQuery_as_int32 = int.Parse(startDateCriteria.ToString("yyyyMMdd"));
            var endDateQuery_as_int32 = int.Parse(endDateCriteria.ToString("yyyyMMdd"));
            var date_query = Lucene.Net.Search.NumericRangeQuery.NewInt32Range("created", startDateQuery_as_int32, endDateQuery_as_int32, true, true);

            //text query
            var query_classic_parser = new Lucene.Net.QueryParsers.Classic.QueryParser(LuceneVersion.LUCENE_48, "findinds", analyzer);
            Lucene.Net.Search.Query text_query = query_classic_parser.Parse(search_phrase);

            //merging
            Lucene.Net.Search.BooleanQuery final_query = new Lucene.Net.Search.BooleanQuery();
            final_query.Add(text_query, Lucene.Net.Search.Occur.MUST);
            final_query.Add(date_query, Lucene.Net.Search.Occur.MUST);


            Console.WriteLine("Displaying results!");


            // re-use the writer to get real-time updates
            var searcher = new Lucene.Net.Search.IndexSearcher(ireader);// writer.GetReader(applyAllDeletes: true));
            //var hits = searcher.Search(phrase, 20 /* top 20 */).ScoreDocs;

            var results = searcher.Search(final_query, 20);

            if (results.ScoreDocs.Length == 0)
            { 
                Console.WriteLine("No results.");
            }
            foreach (var hit in results.ScoreDocs) //foreach (var hit in hits)
            {
                var foundDoc = searcher.Doc(hit.Doc);
                var foundDocCreatedDate = foundDoc.GetField("created").GetInt32Value();

                Console.Write("Score: {0}", hit.Score);
                Console.Write("; PatientID {0}", foundDoc.Get("patientId"));
                
                DateTime parsedDate;
                if (foundDocCreatedDate.HasValue && DateTime.TryParseExact(foundDocCreatedDate.Value.ToString(), "yyyyMMdd", null,
                          DateTimeStyles.None, out parsedDate))
                {

                    Console.Write("; created {0:d}", parsedDate);
                }
                else
                {
                    Console.Write("; created date is not defined");
                }
                
                Console.WriteLine("; Findinds: {0}", foundDoc.Get("findinds"));
            }


            Console.ReadLine();
        }
    }
}
