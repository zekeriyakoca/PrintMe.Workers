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
    private readonly string _openAIApiKey;
    private readonly HttpClient _httpClient;

    public Worker(ILogger<Worker> logger, IHttpClientFactory httpClientFactory, ComputerVisionClient computerVisionClient, IServiceScopeFactory serviceScopeFactory,
        BlobServiceClient blobServiceClient, IHttpClientFactory factory, IConfiguration configuration)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _computerVisionClient = computerVisionClient;
        _serviceScopeFactory = serviceScopeFactory;
        _blobServiceClient = blobServiceClient;
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

            await Run(JsonConvert.SerializeObject(new QueueMessage()
            {
                ImageUrl =
                    "https://upload.wikimedia.org/wikipedia/commons/thumb/b/b2/Vincent_van_Gogh_-_Self-Portrait_-_Google_Art_Project.jpg/1200px-Vincent_van_Gogh_-_Self-Portrait_-_Google_Art_Project.jpg",
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
            await Task.Delay(1000, stoppingToken);
        }
    }

    public async Task Run(string queueMessage)
    {
        using (var scope = _serviceScopeFactory.CreateScope())
        {
            _logger.LogInformation($"C# Queue trigger function processed: {queueMessage}");

            var imageId = Guid.NewGuid().ToString();
            var message = JsonConvert.DeserializeObject<QueueMessage>(queueMessage);
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

            var imageDefinition = await DescribeImage(originalImageUrl);

            string resultImageUrl = await UploadImage(resizedResultImage, $"printme-processed-images", imageId, $"{imageId}-mockup");
            string thumbnailResultImageUrl = await UploadImage(thumbnailResultImage, $"printme-processed-images", imageId, $"{imageId}-mockup-thumbnail");
            string ImageUrl = await UploadImage(resizedImage, $"printme-processed-images", imageId, $"{imageId}");
            string thumbnailUrl = await UploadImage(thumbnailImage, $"printme-processed-images", imageId, $"{imageId}-thumbnail");

            var product = new CatalogItem()
            {
                CatalogType = CatalogType.Print,
                Category = imageDefinition.Category,
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
        var client = _httpClientFactory.CreateClient();
        var response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        var imageStream = await response.Content.ReadAsStreamAsync();
        return await Image.LoadAsync<Rgba32>(imageStream);
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


    private async Task<string> DescribeImageWithComputerVision(string imageUrl)
    {
        var descriptionResult = await _computerVisionClient.DescribeImageAsync(imageUrl);
        if (descriptionResult.Captions.Count > 0)
        {
            return descriptionResult.Captions[0].Text;
        }

        return "No description available";
    }

    public async Task<ImageDefinition> DescribeImage(string imageUrl)
    {
        var prompt = $@"
        Given the image URL: {imageUrl}, return a JSON object with the following structure:
        {{
            ""Painter"": ""<Name of the painter if known, otherwise null>"",
            ""Title"": ""<Name or Title of the painting>"",
            ""Description"": ""<A detailed description of the painting>"",
            ""Category"": <Category number corresponding to the following Enum: 
            public enum CategoryEnum : long
            {{
                None = 0,
                NaturePrints = 1 << 0,
                Botanical = 1 << 1,
                Animals = 1 << 2,
                SpaceAndAstronomy = 1 << 3,
                MapsAndCities = 1 << 4,
                Nature = NaturePrints | Botanical | Animals | SpaceAndAstronomy | MapsAndCities,
                RetroAndVintage = 1 << 7,
                BlackAndWhite = 1 << 8,
                GoldAndSilver = 1 << 9,
                HistoricalPrints = 1 << 10,
                ClassicPosters = 1 << 11,
                VintageAndRetro = RetroAndVintage | BlackAndWhite | GoldAndSilver | HistoricalPrints | ClassicPosters,
                Illustrations = 1 << 14,
                Photographs = 1 << 15,
                ArtPrints = 1 << 16,
                TextPosters = 1 << 17,
                Graphical = 1 << 18,
                ArtStyles = Illustrations | Photographs | ArtPrints | TextPosters | Graphical,
                FamousPainters = 1 << 21,
                IconicPhotos = 1 << 22,
                StudioCollections = 1 << 23,
                ModernArtists = 1 << 24,
                AbstractArt = 1 << 25,
                FamousPaintersCategory = FamousPainters | IconicPhotos | StudioCollections | ModernArtists | AbstractArt
            }}
            For example, category should be  1 for NaturePrints 3 for botanical 5 for Animals>
        }}";

        var requestBody = new
        {
            model = "gpt-3.5-turbo-instruct",
            prompt = prompt,
            max_tokens = 500,
            temperature = 0.3
        };

        var requestContent = new StringContent(JsonConvert.SerializeObject(requestBody), Encoding.UTF8, "application/json");

        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_openAIApiKey}");

        // var response = await _httpClient.PostAsync("https://api.openai.com/v1/completions", requestContent);
        // var responseContent = await response.Content.ReadAsStringAsync();
        var responseContent =
            "{\"id\":\"cmpl-9XGsWWbKSyBSyRbxpnB3hav1KAxdT\",\"object\":\"text_completion\",\"created\":1717716732,\"model\":\"gpt-3.5-turbo-instruct\",\"choices\":[{\"text\":\"\\n        {\\n            \\\"Painter\\\": \\\"Vincent van Gogh\\\",\\n            \\\"Title\\\": \\\"Self-Portrait\\\",\\n            \\\"Description\\\": \\\"Self-Portrait is an oil on canvas painting by the Dutch post-impressionist painter Vincent van Gogh. The painting depicts the artist's face in a green coat with a fur collar, against a background of dark brown and green. It was painted in September 1889 in Saint-R\\u00e9my-de-Provence, France, and is one of several self-portraits that Van Gogh painted during his stay at the asylum there.\\\",\\n            \\\"Category\\\": 3\\n        }\",\"index\":0,\"logprobs\":null,\"finish_reason\":\"stop\"}],\"usage\":{\"prompt_tokens\":473,\"completion_tokens\":128,\"total_tokens\":601}}";

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