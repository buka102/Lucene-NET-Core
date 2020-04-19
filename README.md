# Description

This is a sample .NET Core application that implements Lucene.NET 4.8.0-beta07 search (link: https://github.com/apache/lucenenet) for web api usage. There are five major features (requirements):
- index is stored in Azure Blob Storage (custom version of AzureDirectory - original here: https://github.com/azure-contrib/AzureDirectory)
- search index is stored in .Net MemoryCache (do not use it if your index is over 200Mb)
- rebuild index is using database project to determine changes (see more below)
- be compatible with Azure Kubernetes Service (e.g. dockerized)
- use simplied CQRS (see more here: https://docs.microsoft.com/en-us/dotnet/architecture/microservices/microservice-ddd-cqrs-patterns/apply-simplified-microservice-cqrs-ddd-patterns)

Created and tested on: MacOS Catalina 10.15.4 (19E287)

# Prerequisites

- Azure Blob Storage account. Currently it requires SAS token to have full access on account level (roadmap to make it container specific)
- SQL Database (for sample data)
- Optional: Postman if you want to play with API collection.

# Installation and usage

## Prepare database

You can use SQL Server 2019 Developer Edition database if you run `docker-compose`.

Connect to DB using `sa` username and password (Azure Data Studio is awesome)

Import db structure and records from file 'testdb.bacpac'

Note: testdb.bacpac consists of records imported from https://data.world/arvin6/medical-records-10-yrs (encounter.csv, only two columns are used: 'Member_ID' and 'SOAP_Note' )

Search is done only on SOAP_Note (column `content`).

Reset `FTS_Config` table:

```
UPDATE [dbo].[FTS_Config] SET LastSyncPoint = NULL
```

## Update configuration

 - Open 'appsettings.json' file and change "LuceneConfiguration": 
    - "AzureStorageAccountName": "<your-azure-account-name-here>",
    - "Container": "<your-azure-container-name-here>",
    - "SASToken": "<your-azure-sas-token-here>",
    - "TempDirectory": "<your-local-directory-name-here>"
  
 - Open docker-compose.yaml file and change/note password for db. (optional)


## Run from VS 

You can open ftsLucene solution in Visual Studio 2019 and run it.



## Run from Docker-compose

run:
```
docker-compose -f docker-compose.yaml up
```

DB should be up and ports are open on 1433 (if you want to prevent SQL ports to be open, remove `ports` in `db` in `docker-compose.yaml`)

Application should be up and running on http://localhost:8000

If you make changes to code and need to rebuild, run:
```
docker-compose build
docker-compose -f docker-compose.yaml up
```

Note: Better, not to use `docker-container down` command in this case. If container with database deleted, then import of .bacpac is needed again, upon new db container creation. Alternatively, you can configure SQL server to store DB files in `volume` on your host.


## Testing in PostMan

Once code is running, you can use Postman collection to test API endpoints.


# API Description

## Rebuild index

This endpoint is used to force build (or rebuild) search index. 

Syntax:
```
[POST] /api/search/rebuild_index
```

optional header `fts_options` can have value `full` to force rebuild. Otherwise, application determines database changes after `[dbo].[FTS_Config]` column `LastSyncUTC`.
 
## Search query

Search query performs a full text search against existing index and returns results.

Syntax:

```
[GET] /api/search?query=<...>
```

optional query parameters:

- `startDate` is a numerical representation of start date (e.g. `2020-JAN-20` should be `20200120`). This will limit results to include documents after this date. 
- `endDate` is a numerical representation of end date (e.g. `2020-JAN-20` should be `20200120`). This will limit results to include documents before this date. 
- `pageLength` is a page size of the result set. Default: `30` 
- `pageIndex` is a zero-based index of the page you want retrieve. Default: `0` 

response is in json format:

```
  "hits": [
    {
      "docId": "10001",
      "rank": 0.8888,
      "text": "content",
      "modifiedDate": "2019-09-01T00:00:00"
    },
    ...
  ],
  "pageLength": 30,
  "pageIndex": 0,
  "hasMore": true,
  "indexDateUtc": "2020-04-02T04:01:14.1907939+00:00",
  "isValid": true
```

`rank` is a Lucene rank. You can read more in Lucene.Net documentation.


# Overall achitecture

## Determining data delta

Current implementation uses dynamic determination of `delta` if index rebuild is request (without `full` option). Delta is determined as following:

- determine missing timeframe as a different between `LastSyncUtc` (in `[dbo].[FTS_Config]` table) and current time
- determine records in database that were updated within missing timeframe (however, skipped `DeletedAt` records)
- determine records in database that were deleted within missing timeframe
- iterate through records in database (using pagination) to update index writer.
- merge and commit index in Azure Blob storage


## Explain MemoryCache usages

While it is possible to keep Search Index as a singleton, I prefer to use IMemoryCache. It is done to support multitenancy down the road. 

## Better DB handling

- Added Polly for handling transient SQL exception (https://docs.microsoft.com/en-us/azure/architecture/best-practices/retry-service-specific)
- Use `Microsoft.Data.SqlClient` instead of EntityFramework or `System.Data.SqlClient`


# Test

Open Postman and upload collection "LuceneNet_postman_collection"

Send "Rebuild index" request first. Upon success, run "Search". You can use any Lucene syntax in a query (https://lucene.apache.org/core/2_9_4/queryparsersyntax.html)

Note: if you running API solution from VS (not a container) - it will run on http://localhost:5000, so change port number for Postman requests accordingly.












