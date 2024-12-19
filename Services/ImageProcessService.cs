using PrintMe.Workers.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace PrintMe.Workers.Services;

public class ImageProcessService : IImageProcessService
{
    private readonly ILogger<ImageProcessService> _logger;
    private readonly HttpClient _httpClient;

    public ImageProcessService(ILogger<ImageProcessService> logger, IHttpClientFactory clientFactory)
    {
        _logger = logger;
        _httpClient = clientFactory.CreateClient();
    }
    
    public async Task<Image<Rgba32>> DownloadImageAsync(string url)
    {
        _logger.LogInformation("Image is being downloaded. url: {url}", url);
        var image = await Image.LoadAsync<Rgba32>(await DownloadImageAsStream(url));
        _logger.LogInformation("Image has been downloaded. url: {url}", url);
        return image;
    }
    
    public async Task<Stream> DownloadImageAsStream(string url)
    {
        var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStreamAsync();
    }
    
    public Image<Rgba32> ResizeImageTo(Image<Rgba32> originalImage, int width, int height)
    {
        var resizedImage = originalImage.Clone(ctx => ctx.Resize(new ResizeOptions()
        {
            Mode = ResizeMode.Max,
            Size = new Size(width, height),
            Sampler = KnownResamplers.Lanczos8
        }));
        return resizedImage;
    }

    public double CalculateColorDensity(Image<Rgba32> image)
    {
        if (image == null)
        {
            throw new ArgumentNullException(nameof(image));
        }

        int width = image.Width;
        int height = image.Height;
        int totalPixels = width * height;
        int nonWhitePixels = 0;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                // Get the pixel color from the Image<Rgba32>
                Rgba32 pixel = image[x, y];

                // Check if the pixel is not white
                if (!IsWhite(pixel))
                {
                    nonWhitePixels++;
                }
            }
        }

        // Calculate the density of non-white pixels
        double nonWhiteDensity = (double)nonWhitePixels / totalPixels;

        double minDensity = 1.2;
        double maxDensity = 1.49;
        double colorDensity = minDensity + (maxDensity - minDensity) * nonWhiteDensity;

        return colorDensity;
    }
    
    public async Task<Image<Rgba32>> CreateMockup(Image<Rgba32> sourceImage, MockupTemplate template)
    {
        var templateImage = await DownloadImageAsync(template.TemplateImageUrl);

        var targetArea = new Rectangle(template.X, template.Y, template.Width, template.Height);

        var resizedSource = ResizeAndCrop(sourceImage, targetArea);
        templateImage.Mutate(ctx => ctx.DrawImage(resizedSource, targetArea.Location, 1f));
        return templateImage;
    }

    private Image<Rgba32> ResizeAndCrop(Image<Rgba32> sourceImage, Rectangle targetArea)
    {
        var (newWidth, newHeight) = CalculateNewDimensions(sourceImage, targetArea);

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

    private static (int newWidth, int newHeight) CalculateNewDimensions(Image<Rgba32> sourceImage, Rectangle targetArea)
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

        return (newWidth, newHeight);
    }

    private static bool IsWhite(Rgba32 color)
    {
        // Adjust this threshold as needed
        return color.R > 240 && color.G > 240 && color.B > 240;
    }
}