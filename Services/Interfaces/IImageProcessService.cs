using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace PrintMe.Workers.Services;

public interface IImageProcessService
{
    Task<Image<Rgba32>> DownloadImageAsync(string url);
    Task<Stream> DownloadImageAsStream(string url);
    Image<Rgba32> ResizeImageTo(Image<Rgba32> originalImage, int width, int height);

    double CalculateColorDensity(Image<Rgba32> image);
}