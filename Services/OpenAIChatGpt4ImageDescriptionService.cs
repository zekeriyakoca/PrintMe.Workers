using System.Text;
using Newtonsoft.Json;
using PrintMe.Workers.Models;
using PrintMe.Workers.Services.Constants;

namespace PrintMe.Workers.Services;

public class OpenAIChatGpt4ImageDescriptionService : IImageDescriptionService
{
    private readonly HttpClient _openAIClient;
    private readonly ILogger<OpenAIChatGpt4ImageDescriptionService> _logger;

    public OpenAIChatGpt4ImageDescriptionService(HttpClient openAIClient, ILogger<OpenAIChatGpt4ImageDescriptionService> logger, IConfiguration configuration)
    {
        _openAIClient = openAIClient;
        _openAIClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {configuration["OpenAIKey"]}");
        _logger = logger;
    }

    public async Task<ImageDefinition> DescribeImage(string analyzeOfImage, string descriptionOfImage, string imageBase64, int retryCount = 0)
    {
        _logger.LogInformation("Parameters for request of openAI model = analyzeOfImage: {analyzeOfImage}, descriptionOfImage: {descriptionOfImage}", analyzeOfImage,
            descriptionOfImage);

        var requestBody = new
        {
            model = "gpt-4o",
            top_p= 1,
            messages = new dynamic[]
            {
                new { role = "system", content = new dynamic[] { new { type = "text", text = "You are an expert in image recognition and description." } } },
                new { role = "user", content = new dynamic[] { new { type = "text", text = ImageDescriptionConstants.GetPromptForImage(descriptionOfImage) }, new { type = "image_url", image_url = new { url = imageBase64} } } },
            },
            max_tokens = 500,
            temperature = 0.1
        };

        var requestContent = new StringContent(JsonConvert.SerializeObject(requestBody), Encoding.UTF8, "application/json");

        try
        {
            var response = await _openAIClient.PostAsync("https://api.openai.com/v1/chat/completions", requestContent);
            var responseContent = await response.Content.ReadAsStringAsync();
            var completionResponse = JsonConvert.DeserializeObject<ChatResponse>(responseContent);

            if (completionResponse?.Error != null)
            {
                _logger.LogError($"Error response from openAI model: {completionResponse.Error}");
                if (retryCount >= 3)
                {
                    throw new Exception("Unable to get response from openAI model after 3 retries.");
                }

                _logger.LogWarning($"Retrying : {retryCount}/3");
                return await DescribeImage(analyzeOfImage, descriptionOfImage, imageBase64, retryCount + 1);
            }

            if (completionResponse.Choices.Count > 0)
            {
                try
                {
                    _logger.LogInformation("Text response of openAI model: {responseContent}", completionResponse.OnlyAnswer.Trim());
                    return JsonConvert.DeserializeObject<ImageDefinition>(completionResponse.OnlyAnswer.Trim());
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