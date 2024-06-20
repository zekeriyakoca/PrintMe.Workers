using Azure.Storage.Blobs;
using Microsoft.Azure.CognitiveServices.Vision.ComputerVision;
using Newtonsoft.Json;
using PrintMe.Workers.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Text;
using Azure.Storage.Queues;
using Azure.Storage.Queues.Models;
using Microsoft.Azure.CognitiveServices.Vision.ComputerVision.Models;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;
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
    private readonly HttpClient _httpClient;
    private readonly HttpClient _openAIClient;

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
        _openAIClient = factory.CreateClient();
        _openAIClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {configuration["OpenAIKey"]}");
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

    private async Task ProcessMessage(string queueMessage)
    {
        using var scope = _serviceScopeFactory.CreateScope();
        _logger.LogInformation($"C# Queue trigger function processed: {queueMessage}");

        var message = JsonConvert.DeserializeObject<QueueMessageContent>(queueMessage);
        var imageId = message.ImageId;
        var originalImageUrl = message.ImageUrl;

        var originalImage = await DownloadImage(originalImageUrl);
        var resizedImage = originalImage.Clone(ctx => ctx.Resize(new ResizeOptions()
        {
            Mode = ResizeMode.Max,
            Size = new Size(1000, 1000),
            Sampler = KnownResamplers.Lanczos8
        }));
        
        _logger.LogInformation("Original image resized: imageId: {imageId}", imageId);
        
        var thumbnailImage = originalImage.Clone(ctx => ctx.Resize(new ResizeOptions()
        {
            Mode = ResizeMode.Max,
            Size = new Size(500, 500),
            Sampler = KnownResamplers.Lanczos8,
            
        }));
        
        _logger.LogInformation("Thumbnail for original image created: imageId: {imageId}", imageId);
        
        var resizedImageEntity = new ProductImage
        {
            ImageUrl = await UploadImage(resizedImage, $"printme-processed-images", imageId, $"{imageId}"),
            ThumbnailUrl = await UploadImage(thumbnailImage, $"printme-processed-images", imageId, $"{imageId}-thumbnail")
        };

        await UploadMobileVersions(thumbnailImage, imageId, imageId, originalImage);

        var analyzeOfImage = await AnalyzeImageWithComputerVision(resizedImageEntity.ImageUrl);

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
            Price = 49,
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

        product.ProductImages.Add(resizedImageEntity);
        _logger.LogInformation("Product to be created with its definitions: product: {productJSON}", JsonConvert.SerializeObject(product));

        var mockupIndex = 1;
        foreach (var template in message.MockupTemplates)
        {
            var templateImage = await DownloadImage(template.TemplateImageUrl);

            var targetArea = new Rectangle(template.X, template.Y, template.Width, template.Height);
            var resultImage = CreateMockup(templateImage, originalImage, targetArea);

            var resizedResultImage = resultImage.Clone(ctx => ctx.Resize(1000, resultImage.Height * 1000 / resultImage.Width, KnownResamplers.Lanczos8));
            var thumbnailResultImage = resultImage.Clone(ctx => ctx.Resize(500, resultImage.Height * 500 / resultImage.Width, KnownResamplers.Lanczos8));
            var mockupImage = new ProductImage
            {
                ImageUrl = await UploadImage(resizedResultImage, $"printme-processed-images", imageId, $"{imageId}-mockup{mockupIndex}"),
                ThumbnailUrl = await UploadImage(thumbnailResultImage, $"printme-processed-images", imageId, $"{imageId}-mockup{mockupIndex}-thumbnail")
            };
            
            await UploadMobileVersions(thumbnailResultImage, imageId, $"{imageId}-mockup{mockupIndex}",resultImage);

            _logger.LogInformation("Mockup create: imageId: {imageId}, originalImageUrl: {originalImageUrl}", imageId, originalImageUrl);
            product.ProductImages.Add(mockupImage);
            mockupIndex++;
        }

        await using var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        dbContext.CatalogItems.Add(product);
        await dbContext.SaveChangesAsync();
        _logger.LogInformation("Product created in DB : product: {productJSON}", JsonConvert.SerializeObject(product));
    }

    private async Task UploadMobileVersions(Image<Rgba32> thumbnailImage, string imageId, string imageName, Image<Rgba32> originalImage)
    {
        // thumbnailImage is good enough for mobile
        await UploadImage(thumbnailImage, $"printme-processed-images", imageId, $"{imageName}-m");
        var mobileOfThumbnail = originalImage.Clone(ctx => ctx.Resize(new ResizeOptions()
        {
            Mode = ResizeMode.Max,
            Size = new Size(420, 420),
            Sampler = KnownResamplers.Lanczos8,
        }));
        await UploadImage(mobileOfThumbnail, $"printme-processed-images", imageId, $"{imageName}-thumbnail-m");
    }

    private async Task<Image<Rgba32>> DownloadImage(string url)
    {
        _logger.LogInformation("Image is being downloaded. url: {url}", url);
        var image =  await Image.LoadAsync<Rgba32>(await DownloadImageAsStream(url));
        _logger.LogInformation("Image has been downloaded. url: {url}", url);
        return image;
    }

    private async Task<string> UploadImage(Image<Rgba32> image, string containerName, string blobFolder, string imageName)
    {
        var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
        await containerClient.CreateIfNotExistsAsync();

        var blobName = blobFolder + "/" + imageName + ".jpeg";
        var blobClient = containerClient.GetBlobClient(blobName);

        using (var ms = new MemoryStream())
        {
            await image.SaveAsJpegAsync(ms);
            ms.Position = 0;
            await blobClient.UploadAsync(ms, true);
        }

        _logger.LogInformation("Image uploaded to blob storage: blobFolder: {blobFolder}, imageName: {imageName}", blobFolder, imageName);
        return blobClient.Uri.ToString();
    }

    private async Task<Stream> DownloadImageAsStream(string url)
    {
        var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStreamAsync();
    }

    private Image<Rgba32> CreateMockup(Image<Rgba32> templateImage, Image<Rgba32> sourceImage, Rectangle targetArea)
    {
        var resizedSource = ResizeAndCrop(sourceImage, targetArea);
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
        var resizedImage = sourceImage.Clone(ctx => ctx.Resize(newWidth, newHeight, KnownResamplers.Lanczos8));

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
        await using (Stream imageStream = await DownloadImageAsStream(imageUrl))
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

    private async Task<ImageDefinition> DescribeImage(string analyzeOfImage, string descriptionOfImage, int retryCount = 0)
    {
        
        _logger.LogInformation("Parameters for request of openAI model = analyzeOfImage: {analyzeOfImage}, descriptionOfImage: {descriptionOfImage}", analyzeOfImage, descriptionOfImage);
        var prompt = $@"
Given the image description (this is the name of the file; ignore it if it's a generic name like 'download', numbers or online repo name like 'unsplash') as '{descriptionOfImage}' and analyzeOfImage as '{analyzeOfImage}' generated by Azure Computer Vision service, try to determine which painting it is. If you don't think this is a painting by a known painter, provide a good title, motto, and description. Find a proper category and return a JSON object with the following structure:

{{
    ""Painter"": ""<Name of the painter if known, otherwise null value>"",
    ""Title"": ""<Name or Title of the painting>"",
    ""Motto"": ""<Short Description of the painting. Max 12 words>"",
    ""Description"": ""<A detailed description of the painting. Max 40 words>"",
    ""Category"": <Category number corresponding to the following Enum>
}}

public enum CategoryEnum : long
{{
    None = 0,
    NaturePrints = 1,
    Botanical = 2,
    Animals = 4,
    SpaceAndAstronomy = 8,
    MapsAndCities = 16,
    Nature = 31,
    RetroAndVintage = 128,
    BlackAndWhite = 256,
    GoldAndSilver = 512,
    HistoricalPrints = 1024,
    ClassicPosters = 2048,
    VintageAndRetro = 3968,
    Illustrations = 16384,
    Photographs = 32768,
    ArtPrints = 65536,
    TextPosters = 131072,
    Graphical = 262144,
    ArtStyles = 511104,
    FamousPainters = 2097152,
    IconicPhotos = 4194304,
    StudioCollections = 8388608,
    ModernArtists = 16777216,
    AbstractArt = 33554432,
    FamousPaintersCategory = 67108863
}}

For example, category should be 1 for NaturePrints, 3 for Botanical, 5 for Animals, 8 for both Botanical and Animals.
";


        var requestBody = new
        {
            model = "gpt-3.5-turbo-instruct-0914",
            prompt = prompt,
            max_tokens = 500,
            temperature = 0.6
        };

        var requestContent = new StringContent(JsonConvert.SerializeObject(requestBody), Encoding.UTF8, "application/json");

        try
        {
            var response = await _openAIClient.PostAsync("https://api.openai.com/v1/completions", requestContent);
            var responseContent = await response.Content.ReadAsStringAsync();
            var completionResponse = JsonConvert.DeserializeObject<CompletionResponse>(responseContent);

            if(completionResponse?.Error != null)
            {
                _logger.LogError($"Error response from openAI model: {completionResponse.Error}");
                if (retryCount >= 3)
                {
                    throw new Exception("Unable to get response from openAI model after 3 retries.");
                }

                _logger.LogWarning($"Retrying : {retryCount}/3");
                return await DescribeImage(analyzeOfImage, descriptionOfImage, retryCount+1);
            }
            
            if (completionResponse.Choices.Count > 0)
            {
                try
                {
                    _logger.LogInformation("Text response of openAI model: {responseContent}", completionResponse.Choices[0].Text.Trim());
                    return JsonConvert.DeserializeObject<ImageDefinition>(completionResponse.Choices[0].Text.Trim());
                }
                catch
                {
                    _logger.LogError("Unable to parse JSON response of openAI model: {responseContent}", completionResponse);
                    throw;
                }
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Unable to describe image with openAI model: {message}", e.Message);

        }
       

        return new ImageDefinition();
    }
}