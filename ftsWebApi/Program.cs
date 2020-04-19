using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ftsWebApi
{
    public class Program
    {
        public static void Main(string[] args)
        {
            CreateHostBuilder(args).Build().Run();
        }

        public static IWebHostBuilder CreateHostBuilder(string[] args) =>
             WebHost.CreateDefaultBuilder(args)
             .ConfigureAppConfiguration((hostingContext, config) =>
             {
                 Console.WriteLine($"Application started: {DateTimeOffset.UtcNow}");

                 var env = hostingContext.HostingEnvironment;

                 Console.WriteLine($"Using 'appsettings.json' configuration file.");
                 config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

                 var environmentName = hostingContext.HostingEnvironment.EnvironmentName;
                 Console.WriteLine($"EnvironmentName: {environmentName}");

                 Console.WriteLine($"Using 'appsettings.{environmentName}.json' configuration file.");
                 config.AddJsonFile($"appsettings.{environmentName}.json", optional: true, reloadOnChange: true);

                 config.AddEnvironmentVariables();

             })
             .ConfigureLogging((hostingContext, logging) =>
             {
                 logging.SetMinimumLevel(LogLevel.Debug);
                 logging.AddConsole();

             })
             .UseStartup<Startup>();
    }
}
