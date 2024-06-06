using System;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Azure.CognitiveServices.Vision.ComputerVision;
using Microsoft.EntityFrameworkCore;
using Azure.Storage.Blobs;
using PrintMe.Workers;

namespace PrintMe.Workers
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var host = CreateHostBuilder(args).Build();
            host.Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureServices((context, services) =>
                {
                    string sqlConnectionString = context.Configuration["SqlConnectionString"];

                    services.AddDbContext<ApplicationDbContext>(options =>
                        options.UseSqlServer(sqlConnectionString));

                    services.AddSingleton(new ComputerVisionClient(new ApiKeyServiceClientCredentials(
                        Environment.GetEnvironmentVariable("CognitiveServicesSubscriptionKey")))
                    {
                        Endpoint = Environment.GetEnvironmentVariable("CognitiveServicesEndpoint")
                    });

                    services.AddSingleton(new BlobServiceClient(context.Configuration["StorageConnectionString"]));

                    services.AddHttpClient();
                    services.AddHostedService<Worker>();
                });
    }
}