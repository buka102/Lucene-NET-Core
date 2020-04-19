using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FluentValidation;
using FluentValidation.AspNetCore;
using ftsWebApi.CQRS.Search.Queries;
using ftsWebApi.CQRS.Shared;
using ftsWebApi.Data;
using ftsWebApi.FTS;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ftsWebApi
{
    public class Startup
    {
        readonly string _allowSpecificOrigins = "_ftsCors";
        public IConfiguration Configuration { get; }
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddControllers().AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
                options.JsonSerializerOptions.IgnoreNullValues = true;
                var isJsonResponseModeSetToDebug = Configuration["JsonResponseMode"].Equals("debug", StringComparison.InvariantCultureIgnoreCase);
                options.JsonSerializerOptions.WriteIndented = isJsonResponseModeSetToDebug; // production mode will be condensed
            });

            //Add MemoryCache
            services.AddMemoryCache(setup=> {
                setup.ExpirationScanFrequency = TimeSpan.FromMinutes(1); //in debug you can make it 1 minute. In production, it should 5 minutes or more
            });

            //Add AzureLuceneConfiguration
            var luceneConfig = new AzureLuceneConfiguration();
            Configuration.Bind("LuceneConfiguration", luceneConfig);      //  binding configuration to typed config
            services.AddSingleton(luceneConfig);

            services.AddSingleton<ILuceneReaderService, AzureInMemoryCachedLuceneReader>();

            //Add SQL Service
            services.AddTransient<ISQLService, SQLDataService>();

            services.AddCors(options =>
            {
                options.AddPolicy(_allowSpecificOrigins,
                    builder =>
                    {
                        builder.WithOrigins("*");
                        builder.AllowAnyHeader();
                        builder.AllowAnyMethod();
                    });
            });

            services.AddHttpContextAccessor();

            services.AddMediatR(typeof(Startup));

            services.AddValidatorsFromAssembly(typeof(Startup).Assembly);

            services.AddTransient(typeof(IPipelineBehavior<, >),typeof(ValidatorBehavior<,>));
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseCors(_allowSpecificOrigins);


            app.UseRouting();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });
        }
    }
}
