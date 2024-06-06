using System.Drawing;
using Azure.Storage.Blobs;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Emgu.CV.Util;
using Microsoft.Azure.CognitiveServices.Vision.ComputerVision;
using Newtonsoft.Json;
using PrintMe.Workers.Models;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace PrintMe.Workers;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ComputerVisionClient _computerVisionClient;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly BlobServiceClient _blobServiceClient;

    public Worker(ILogger<Worker> logger, IHttpClientFactory httpClientFactory, ComputerVisionClient computerVisionClient, IServiceScopeFactory serviceScopeFactory,
        BlobServiceClient blobServiceClient)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _computerVisionClient = computerVisionClient;
        _serviceScopeFactory = serviceScopeFactory;
        _blobServiceClient = blobServiceClient;
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
                ImageUrl = "https://media.allure.com/photos/65d8f1e8e923c6a4feaf9a02/4:3/w_2664,h_1998,c_limit/dua%20lipa.jpg",
                MockupTemplates = new List<MockupTemplate>
                {
                    new MockupTemplate
                    {
                        TemplateImageUrl = "https://as2.ftcdn.net/v2/jpg/03/09/71/15/1000_F_309711557_EeuDwldG8Sqcc6nvDvn1rsRD4oB67eCB.jpg",
                        X = 100,
                        Y = 100,
                        Width = 500,
                        Height = 500
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

            var message = JsonConvert.DeserializeObject<QueueMessage>(queueMessage);
            var templateObject = message.MockupTemplates[0];
            var originalImageUrl = message.ImageUrl;

            var originalImage = await DownloadImage(originalImageUrl);
            var templateImage = await DownloadImage(templateObject.TemplateImageUrl);

            var targetArea = new Rectangle(templateObject.X, templateObject.Y, templateObject.Width, templateObject.Height);
            var resultImage = CreateMockup(templateImage, originalImage, targetArea);

            var resizedResultImage = resultImage.Resize(1000, 1000, Inter.Linear);
            var thumbnailResultImage = resizedResultImage.Resize(100, 100, Inter.Linear);

            var description = await DescribeImage(originalImageUrl);

            string resultImageUrl = await UploadImage(resizedResultImage, "printme-processed-images");
            string thumbnailUrl = await UploadImage(thumbnailResultImage, "printme-processed-images-thumbnails");

            var product = new Product
            {
                OriginalImageUrl = originalImageUrl,
                TemplateImageUrl = templateObject.TemplateImageUrl,
                ResultImageUrl = resultImageUrl,
                ThumbnailUrl = thumbnailUrl,
                Name = description
            };

            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            dbContext.Products.Add(product);
            await dbContext.SaveChangesAsync();
        }
    }

    private async Task<Image<Bgr, byte>> DownloadImage(string url)
    {
        var client = _httpClientFactory.CreateClient();
        var response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        var imageStream = await response.Content.ReadAsStreamAsync();
        return new Image<Bgr, byte>(new Bitmap(imageStream).ToMat());
    }

    private Image<Bgr, byte> CreateMockup(Image<Bgr, byte> templateImage, Image<Bgr, byte> sourceImage, Rectangle targetArea)
    {
        var resizedSource = sourceImage.Resize(targetArea.Width, targetArea.Height, Inter.Linear);
        templateImage.ROI = targetArea;
        resizedSource.CopyTo(templateImage);
        templateImage.ROI = Rectangle.Empty;
        return templateImage;
    }

    private Image<Bgr, byte> CreateMockup(Image<Bgr, byte> templateImage, Image<Bgr, byte> sourceImage, PointF[] targetCorners)
    {
        // Define the source points (corners of the source image)
        PointF[] sourcePoints = new PointF[]
        {
            new PointF(0, 0),
            new PointF(sourceImage.Width - 1, 0),
            new PointF(sourceImage.Width - 1, sourceImage.Height - 1),
            new PointF(0, sourceImage.Height - 1)
        };

        // Calculate the perspective transformation matrix
        Mat transformMatrix = CvInvoke.GetPerspectiveTransform(sourcePoints, targetCorners);

        // Warp the source image to fit the target polygon
        Image<Bgr, byte> warpedImage = new Image<Bgr, byte>(templateImage.Width, templateImage.Height);
        CvInvoke.WarpPerspective(sourceImage, warpedImage, transformMatrix, new Size(templateImage.Width, templateImage.Height));

        // Create a mask for the target polygon
        Mat mask = new Mat(templateImage.Size, DepthType.Cv8U, 1);
        mask.SetTo(new MCvScalar(0));
        VectorOfPointF polygon = new VectorOfPointF(targetCorners);
        CvInvoke.FillConvexPoly(mask, polygon, new MCvScalar(255));

        // Use the mask to copy the warped image onto the template
        CvInvoke.BitwiseAnd(warpedImage, warpedImage, templateImage, mask);

        return templateImage;
    }

    private async Task<string> DescribeImage(string imageUrl)
    {
        var descriptionResult = await _computerVisionClient.DescribeImageAsync(imageUrl);
        if (descriptionResult.Captions.Count > 0)
        {
            return descriptionResult.Captions[0].Text;
        }

        return "No description available";
    }

    private async Task<string> UploadImage(Image<Bgr, byte> image, string containerName)
    {
        var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
        await containerClient.CreateIfNotExistsAsync();

        var blobName = Guid.NewGuid().ToString() + ".jpg";
        var blobClient = containerClient.GetBlobClient(blobName);

        using (var ms = new MemoryStream())
        {
            image.ToBitmap().Save(ms, System.Drawing.Imaging.ImageFormat.Jpeg);
            ms.Position = 0;
            await blobClient.UploadAsync(ms, true);
        }

        return blobClient.Uri.ToString();
    }
}