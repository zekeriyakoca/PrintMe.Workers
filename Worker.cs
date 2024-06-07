using System.ClientModel;
using Azure.Storage.Blobs;
using Microsoft.Azure.CognitiveServices.Vision.ComputerVision;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using PrintMe.Workers.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Drawing.Processing;
using System.IO;
using System.Text;
using Azure.Storage.Queues;
using Azure.Storage.Queues.Models;
using Microsoft.Azure.CognitiveServices.Vision.ComputerVision.Models;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;
using OpenAI;
using PrintMe.Workers.Enums;

namespace PrintMe.Workers;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ComputerVisionClient _computerVisionClient;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly BlobServiceClient _blobServiceClient;
    private readonly QueueClient _queueClient;
    private readonly string _openAIApiKey;
    private readonly HttpClient _httpClient;

    public Worker(ILogger<Worker> logger, IHttpClientFactory httpClientFactory, ComputerVisionClient computerVisionClient, IServiceScopeFactory serviceScopeFactory,
        BlobServiceClient blobServiceClient, IHttpClientFactory factory, IConfiguration configuration, QueueClient queueClient)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _computerVisionClient = computerVisionClient;
        _serviceScopeFactory = serviceScopeFactory;
        _blobServiceClient = blobServiceClient;
        _queueClient = queueClient;
        _httpClient = factory.CreateClient();
        _openAIApiKey = configuration["OpenAIKey"];
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);
            }

            // await ProcessTestMessage();

            await ProcessMessagesInQueue(stoppingToken);

            await Task.Delay(1000*10, stoppingToken);
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
        var response = await _queueClient.ReceiveMessagesAsync(maxMessages: 1, visibilityTimeout: TimeSpan.FromMinutes(1), stoppingToken);
        var messages = response.Value;
        if (messages.Length > 0)
        {
            foreach (QueueMessage message in messages)
            {
                await ProcessMessage(message.MessageText);

                // Delete the message after processing
                await _queueClient.DeleteMessageAsync(message.MessageId, message.PopReceipt, stoppingToken);
            }
        }
    }

    public async Task ProcessMessage(string queueMessage)
    {
        using (var scope = _serviceScopeFactory.CreateScope())
        {
            _logger.LogInformation($"C# Queue trigger function processed: {queueMessage}");
;
            var message = JsonConvert.DeserializeObject<QueueMessageContent>(queueMessage);
            var imageId = message.ImageId;
            var templateObject = message.MockupTemplates[0];
            var originalImageUrl = message.ImageUrl;

            var originalImage = await DownloadImage(originalImageUrl);
            var templateImage = await DownloadImage(templateObject.TemplateImageUrl);

            var targetArea = new Rectangle(templateObject.X, templateObject.Y, templateObject.Width, templateObject.Height);
            var resultImage = CreateMockup(templateImage, originalImage, targetArea);

            var resizedImage = originalImage.Clone(ctx => ctx.Resize(1000, originalImage.Height * 1000 / originalImage.Width));
            var thumbnailImage = resizedImage.Clone(ctx => ctx.Resize(200, resizedImage.Height * 200 / resizedImage.Width));
            var resizedResultImage = resultImage.Clone(ctx => ctx.Resize(1000, resultImage.Height * 1000 / resultImage.Width));
            var thumbnailResultImage = resizedResultImage.Clone(ctx => ctx.Resize(200, resizedResultImage.Height * 200 / resizedResultImage.Width));

            
            string resultImageUrl = await UploadImage(resizedResultImage, $"printme-processed-images", imageId, $"{imageId}-mockup");
            string thumbnailResultImageUrl = await UploadImage(thumbnailResultImage, $"printme-processed-images", imageId, $"{imageId}-mockup-thumbnail");
            string ImageUrl = await UploadImage(resizedImage, $"printme-processed-images", imageId, $"{imageId}");
            string thumbnailUrl = await UploadImage(thumbnailImage, $"printme-processed-images", imageId, $"{imageId}-thumbnail");
            
            var analyzeOfImage = await AnalyzeImageWithComputerVision(ImageUrl);
            var imageDefinition = await DescribeImage(analyzeOfImage, message.ImageDescription);


            var product = new CatalogItem()
            {
                CatalogType = CatalogType.Print,
                Category = imageDefinition.Category,
                Motto = imageDefinition.Motto + (String.IsNullOrEmpty(imageDefinition.Painter) ? "" : $" by {imageDefinition.Painter}"),
                Description = imageDefinition.Description,
                Name = imageDefinition.Title,
                Owner = "PrintMe",
                PictureFileName = imageId,
                Price = 39,
                Size = PrintSize.None,
                AvailableStock = 100,
                RestockThreshold = 10,
                MaxStockThreshold = 100,
                OnReorder = false,
                SalePercentage = 0,
                OriginalImage = originalImageUrl,
                SearchParameters = imageDefinition.Description,
                Tags = CatalogTags.Featured
            };

            await using var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            dbContext.CatalogItems.Add(product);
            await dbContext.SaveChangesAsync();
        }
    }

    private async Task<Image<Rgba32>> DownloadImage(string url)
    {
        return await Image.LoadAsync<Rgba32>(await DownloadImageAsStream(url));
    }
    private async Task<Stream> DownloadImageAsStream(string url)
    {
        var client = _httpClientFactory.CreateClient();
        var response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStreamAsync();
    }

    private Image<Rgba32> CreateMockup(Image<Rgba32> templateImage, Image<Rgba32> sourceImage, Rectangle targetArea)
    {
        var resizedSource = ResizeAndCrop(sourceImage, targetArea); // sourceImage.Clone(ctx => ctx.Resize(targetArea.Width, targetArea.Height));
        templateImage.Mutate(ctx => ctx.DrawImage(resizedSource, targetArea.Location, 1f));
        return templateImage;
    }

    private Image<Rgba32> ResizeAndCrop(Image<Rgba32> sourceImage, Rectangle targetArea)
    {
        // Calculate the aspect ratio of the target area
        float targetAspectRatio = (float)targetArea.Width / targetArea.Height;
        float sourceAspectRatio = (float)sourceImage.Width / sourceImage.Height;

        // Determine the new dimensions while maintaining the aspect ratio
        int newWidth, newHeight;
        if (sourceAspectRatio > targetAspectRatio)
        {
            // Source is wider than target aspect ratio
            newHeight = targetArea.Height;
            newWidth = (int)(sourceImage.Width * ((float)targetArea.Height / sourceImage.Height));
        }
        else
        {
            // Source is taller than target aspect ratio
            newWidth = targetArea.Width;
            newHeight = (int)(sourceImage.Height * ((float)targetArea.Width / sourceImage.Width));
        }

        // Resize the source image
        var resizedImage = sourceImage.Clone(ctx => ctx.Resize(newWidth, newHeight));

        // Calculate the crop rectangle to center the target area
        int cropX = (newWidth - targetArea.Width) / 2;
        int cropY = (newHeight - targetArea.Height) / 2;
        var cropRectangle = new Rectangle(cropX, cropY, targetArea.Width, targetArea.Height);

        // Crop the resized image
        var croppedImage = resizedImage.Clone(ctx => ctx.Crop(cropRectangle));

        return croppedImage;
    }


    private async Task<string> AnalyzeImageWithComputerVision(string imageUrl)
    {
        using (Stream imageStream = await DownloadImageAsStream(imageUrl))
        {
            var features = new VisualFeatureTypes?[] { VisualFeatureTypes.Description, VisualFeatureTypes.Tags, VisualFeatureTypes.Categories };
            ImageAnalysis analysis = await _computerVisionClient.AnalyzeImageInStreamAsync(imageStream, features);

            return $"Analyze Report of the image = "
                   + $"Tags: {String.Join(",", analysis.Tags.Select(x => x.Name))}. "
                   + $"Descriptions: {String.Join(",", analysis.Description.Captions.Select(x => x.Text))}. "
                   + $"nCategories: {String.Join(",", analysis.Categories.Select(x => x.Name))}.";
        }
    }
    
    private async Task<string> DescribeImageWithComputerVision(string imageUrl)
    {
        var descriptionResult = await _computerVisionClient.DescribeImageAsync(imageUrl);
        if (descriptionResult.Captions.Count > 0)
        {
            return descriptionResult.Captions[0].Text;
        }
    
        return "No description available";
    }

    public async Task<ImageDefinition> DescribeImage(string analyzeOfImage, string descriptionOfImage)
    {
        var prompt = $@"
        Given the image description(this is name of file. ignore it if it's a generic name like 'download') as '{descriptionOfImage}' and analyzeOfImage as '{analyzeOfImage}' generated by Azure Computer Vision service, try to find which painting is it. 
        If you don't think this is a painting by a painter
        return find a good title,motto and description. Find a proper category and return a JSON object with the following structure:
        {{
            ""Painter"": ""<Name of the painter if known, otherwise null>"",
            ""Title"": ""<Name or Title of the painting>"",
            ""Motto"": ""<Short Description of the painting. Max 12 words>"",
            ""Description"": ""<A detailed description of the painting. Max 40 words>"",
            ""Category"": <Category number corresponding to the following Enum: 
            public enum CategoryEnum : long
            {{
                None: 0,
                NaturePrints: 1,
                Botanical: 2,
                Animals: 4,
                SpaceAndAstronomy: 8,
                MapsAndCities: 16,
                Nature: 31,
                RetroAndVintage: 128,
                BlackAndWhite: 256,
                GoldAndSilver: 512,
                HistoricalPrints: 1024,
                ClassicPosters: 2048,
                VintageAndRetro: 3968,
                Illustrations: 16384,
                Photographs: 32768,
                ArtPrints: 65536,
                TextPosters: 131072,
                Graphical: 262144,
                ArtStyles: 511104,
                FamousPainters: 2097152,
                IconicPhotos: 4194304,
                StudioCollections: 8388608,
                ModernArtists: 16777216,
                AbstractArt: 33554432,
                FamousPaintersCategory: 67108863
            }}
            For example, category should be  1 for NaturePrints 3 for botanical 5 for Animals>
        }}";

        var requestBody = new
        {
            model = "gpt-3.5-turbo-instruct",
            prompt = prompt,
            max_tokens = 500,
            temperature = 0.4
        };

        var requestContent = new StringContent(JsonConvert.SerializeObject(requestBody), Encoding.UTF8, "application/json");

        
        _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"Bearer {_openAIApiKey}");

        var response = await _httpClient.PostAsync("https://api.openai.com/v1/completions", requestContent);
        var responseContent = await response.Content.ReadAsStringAsync();
        var completionResponse = JsonConvert.DeserializeObject<CompletionResponse>(responseContent);

        if (completionResponse.Choices.Count > 0)
        {
            try
            {
                return JsonConvert.DeserializeObject<ImageDefinition>(completionResponse.Choices[0].Text.Trim());
            }
            catch
            {
                _logger.LogError("Unable to parse JSON response of openAI model: {responseContent}", completionResponse);
            }
        }

        return new ImageDefinition();
    }


    private async Task<string> UploadImage(Image<Rgba32> image, string containerName, string blogFolder, string imageName)
    {
        var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
        await containerClient.CreateIfNotExistsAsync();

        var blobName = blogFolder + "/" + imageName + ".jpg";
        var blobClient = containerClient.GetBlobClient(blobName);

        using (var ms = new MemoryStream())
        {
            await image.SaveAsJpegAsync(ms);
            ms.Position = 0;
            await blobClient.UploadAsync(ms, true);
        }

        return blobClient.Uri.ToString();
    }
}