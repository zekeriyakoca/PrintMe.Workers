using System;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Azure.CognitiveServices.Vision.ComputerVision;
using Microsoft.EntityFrameworkCore;
using Azure.Storage.Blobs;
using PrintMe.Workers;
using Azure.Storage.Queues;
using Microsoft.Extensions.Azure;
using PrintMe.Workers.Services;

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
                .ConfigureAppConfiguration((hostingContext, config) =>
                {
                    config.AddJsonFile($"appsettings.Development.json", optional: true);
                })
                .ConfigureServices((context, services) =>
                {
                    string sqlConnectionString = context.Configuration["SqlConnectionString"];

                    services.AddDbContext<ApplicationDbContext>(options =>
                        options.UseSqlServer(sqlConnectionString));

                    services.AddSingleton(new ComputerVisionClient(new ApiKeyServiceClientCredentials(
                        context.Configuration["CognitiveServicesSubscriptionKey"]))
                    {
                        Endpoint = context.Configuration["CognitiveServicesEndpoint"]
                    });

                    services.AddSingleton(new BlobServiceClient(context.Configuration["StorageConnectionString"]));
                    
                    services.AddSingleton(sp => new QueueClient(context.Configuration["StorageConnectionString"], "images-to-process"));

                    // services.AddTransient<IImageDescriptionService, OpenAIGptInstructImageDescriptionService>();
                    // services.AddTransient<IImageDescriptionService, OpenAIChatGpt4ImageDescriptionService>();
                    services.AddTransient<IImageDescriptionService, OpenAIAssistentImageDescriptionService>();
                    services.AddTransient<IImageProcessService, ImageProcessService>();

                    services.AddHttpClient();
                    services.AddHostedService<Worker>();
                });
    }
}