using Azure.Storage.Blobs;
using Microsoft.Azure.CognitiveServices.Vision.ComputerVision;
using Newtonsoft.Json;
using PrintMe.Workers.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Azure.Storage.Queues;
using Azure.Storage.Queues.Models;
using Microsoft.Azure.CognitiveServices.Vision.ComputerVision.Models;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;
using PrintMe.Workers.Enums;
using PrintMe.Workers.Services;

namespace PrintMe.Workers;

public class Worker(
    ILogger<Worker> logger,
    ComputerVisionClient computerVisionClient,
    IServiceScopeFactory serviceScopeFactory,
    BlobServiceClient blobServiceClient,
    IImageDescriptionService imageDescriptionService,
    IImageProcessService imageProcessService,
    QueueClient queueClient)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);
            }

            await ProcessTestMessage();

            // await ProcessMessagesInQueue(stoppingToken);

            await Task.Delay(1000 * 15, stoppingToken);
        }
    }

    private async Task ProcessTestMessage()
    {
        await ProcessMessage(JsonConvert.SerializeObject(new QueueMessageContent()
        {
            ImageUrl =
                "https://upload.wikimedia.org/wikipedia/commons/thumb/b/b2/Vincent_van_Gogh_-_Self-Portrait_-_Google_Art_Project.jpg/1200px-Vincent_van_Gogh_-_Self-Portrait_-_Google_Art_Project.jpg",
            Tag = CatalogTags.Featured,
            MockupTemplates = new List<MockupTemplate>
            {
                new MockupTemplate
                {
                    TemplateImageUrl = "https://genstorageaccount3116.blob.core.windows.net/print-me-product-images/pexels-thought-catalog-317580-2401863.jpg",
                    X = 855,
                    Y = 1292,
                    Width = 2168 - 855,
                    Height = 2941 - 1292
                }
            }
        }));
    }

    private async Task ProcessMessagesInQueue(CancellationToken stoppingToken)
    {
        var response = await queueClient.ReceiveMessagesAsync(maxMessages: 1, visibilityTimeout: TimeSpan.FromMinutes(1), stoppingToken);
        var messages = response.Value;
        if (messages.Length > 0)
        {
            foreach (QueueMessage message in messages)
            {
                await ProcessMessage(message.MessageText);

                // Delete the message after processing
                await queueClient.DeleteMessageAsync(message.MessageId, message.PopReceipt, stoppingToken);
            }
        }
    }

    private async Task ProcessMessage(string queueMessage)
    {
        using var scope = serviceScopeFactory.CreateScope();
        logger.LogInformation($"C# Queue trigger function processed: {queueMessage}");

        var message = JsonConvert.DeserializeObject<QueueMessageContent>(queueMessage);
        var imageId = message.ImageId;
        var originalImageUrl = message.ImageUrl;

        var originalImage = await imageProcessService.DownloadImageAsync(originalImageUrl);
        var resizedImage = imageProcessService.ResizeImageTo(originalImage, 1000, 1000);

        logger.LogInformation("Original image resized: imageId: {imageId}", imageId);

        var thumbnailImage = imageProcessService.ResizeImageTo(originalImage, 500, 500);

        logger.LogInformation("Thumbnail for original image created: imageId: {imageId}", imageId);

        var resizedImageEntity = new ProductImage
        {
            ImageUrl = await UploadImage(resizedImage, $"printme-processed-images", imageId, $"{imageId}"),
            ThumbnailUrl = await UploadImage(thumbnailImage, $"printme-processed-images", imageId, $"{imageId}-thumbnail")
        };

        await UploadMobileVersions(thumbnailImage, imageId, imageId, originalImage);

        var analyzeOfImage = await AnalyzeImageWithComputerVision(resizedImageEntity.ImageUrl);

        var imageDefinition =
            await imageDescriptionService.DescribeImage(analyzeOfImage, message.ImageDescription, originalImageUrl); //resizedImage.ToBase64String(JpegFormat.Instance)

        var product = new CatalogItem()
        {
            CatalogType = CatalogType.Print,
            Category = imageDefinition.Category,
            Motto = imageDefinition.Motto + (String.IsNullOrEmpty(imageDefinition.Painter) ? "" : $" by {imageDefinition.Painter}"),
            Description = imageDefinition.Description,
            Name = imageDefinition.Title,
            Owner = "PrintMe",
            PictureFileName = imageId,
            Price = new decimal(4.40 * imageProcessService.CalculateColorDensity(originalImage)),
            Size = PrintSize.None,
            AvailableStock = 100,
            RestockThreshold = 10,
            MaxStockThreshold = 100,
            OnReorder = false,
            SalePercentage = 0,
            OriginalImage = originalImageUrl,
            SearchParameters = imageDefinition.Description,
            OriginalImageWidth = originalImage.Width,
            OriginalImageHeight = originalImage.Height,
            Tags = CatalogTags.Featured
        };

        product.ProductImages.Add(resizedImageEntity);
        logger.LogInformation("Product to be created with its definitions: product: {productJSON}", JsonConvert.SerializeObject(product));

        var mockupIndex = 1;
        foreach (var template in message.MockupTemplates)
        {
            var resultImage = await imageProcessService.CreateMockup(originalImage, template);

            var resizedResultImage = imageProcessService.ResizeImageTo(resultImage, 1000, resultImage.Height * 1000 / resultImage.Width);
            var thumbnailResultImage = imageProcessService.ResizeImageTo(resultImage, 500, resultImage.Height * 500 / resultImage.Width);

            var mockupImage = new ProductImage
            {
                ImageUrl = await UploadImage(resizedResultImage, $"printme-processed-images", imageId, $"{imageId}-mockup{mockupIndex}"),
                ThumbnailUrl = await UploadImage(thumbnailResultImage, $"printme-processed-images", imageId, $"{imageId}-mockup{mockupIndex}-thumbnail")
            };

            await UploadMobileVersions(thumbnailResultImage, imageId, $"{imageId}-mockup{mockupIndex}", resultImage);

            logger.LogInformation("Mockup create: imageId: {imageId}, originalImageUrl: {originalImageUrl}", imageId, originalImageUrl);
            product.ProductImages.Add(mockupImage);
            mockupIndex++;
        }

        await using var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        dbContext.CatalogItems.Add(product);
        await dbContext.SaveChangesAsync();
        logger.LogInformation("Product created in DB : product: {productJSON}", JsonConvert.SerializeObject(product));
    }

    private async Task UploadMobileVersions(Image<Rgba32> thumbnailImage, string imageId, string imageName, Image<Rgba32> originalImage)
    {
        var mobileOfOriginal = thumbnailImage; // thumbnailImage is good enough for mobile
        await UploadImage(mobileOfOriginal, $"printme-processed-images", imageId, $"{imageName}-m");

        var mobileOfThumbnail = imageProcessService.ResizeImageTo(originalImage, 420, 420);
        await UploadImage(mobileOfThumbnail, $"printme-processed-images", imageId, $"{imageName}-thumbnail-m");
    }


    private async Task<string> UploadImage(Image<Rgba32> image, string containerName, string blobFolder, string imageName)
    {
        var containerClient = blobServiceClient.GetBlobContainerClient(containerName);
        await containerClient.CreateIfNotExistsAsync();

        var blobName = blobFolder + "/" + imageName + ".jpeg";
        var blobClient = containerClient.GetBlobClient(blobName);

        using (var ms = new MemoryStream())
        {
            await image.SaveAsJpegAsync(ms);
            ms.Position = 0;
            await blobClient.UploadAsync(ms, true);
        }

        logger.LogInformation("Image uploaded to blob storage: blobFolder: {blobFolder}, imageName: {imageName}", blobFolder, imageName);
        return blobClient.Uri.ToString();
    }

    private async Task<string> AnalyzeImageWithComputerVision(string imageUrl)
    {
        await using Stream imageStream = await imageProcessService.DownloadImageAsStream(imageUrl);
        var features = new VisualFeatureTypes?[] { VisualFeatureTypes.Description, VisualFeatureTypes.Tags, VisualFeatureTypes.Categories };
        ImageAnalysis analysis = await computerVisionClient.AnalyzeImageInStreamAsync(imageStream, features);

        return $"Analyze Report of the image = "
               + $"Tags: {String.Join(",", analysis.Tags.Select(x => x.Name))}. "
               + $"Descriptions: {String.Join(",", analysis.Description.Captions.Select(x => x.Text))}. "
               + $"nCategories: {String.Join(",", analysis.Categories.Select(x => x.Name))}.";
    }

    private async Task<string> DescribeImageWithComputerVision(string imageUrl)
    {
        var descriptionResult = await computerVisionClient.DescribeImageAsync(imageUrl);
        if (descriptionResult.Captions.Count > 0)
        {
            return descriptionResult.Captions[0].Text;
        }

        return "No description available";
    }
}