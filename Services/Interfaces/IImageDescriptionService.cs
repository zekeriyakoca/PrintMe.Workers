using PrintMe.Workers.Models;

namespace PrintMe.Workers.Services;

public interface IImageDescriptionService
{
    Task<ImageDefinition> DescribeImage(string analyzeOfImage, string descriptionOfImage, string imageUrl, int retryCount = 0);
}