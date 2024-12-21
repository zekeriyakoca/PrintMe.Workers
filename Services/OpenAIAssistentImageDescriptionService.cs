using System.Text;
using Newtonsoft.Json;
using PrintMe.Workers.Models;

namespace PrintMe.Workers.Services;

public class OpenAIAssistentImageDescriptionService : IImageDescriptionService
{
    private readonly HttpClient _openAiClient;
    private readonly ILogger<OpenAIChatGpt4ImageDescriptionService> _logger;
    private readonly string _assistantId;

    public OpenAIAssistentImageDescriptionService(HttpClient openAiClient, ILogger<OpenAIChatGpt4ImageDescriptionService> logger, IConfiguration configuration)
    {
        _openAiClient = openAiClient;
        _openAiClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {configuration["OpenAIKey"]}");
        _openAiClient.DefaultRequestHeaders.Add("OpenAI-Beta", "assistants=v2");
        _assistantId = configuration["OpenAIAssistantId"] ?? throw new ArgumentNullException("OpenAIAssistantId in configuration");
        _logger = logger;
    }

    public async Task<ImageDefinition> DescribeImage(string analyzeOfImage, string descriptionOfImage, string imageUrl, int retryCount = 0)
    {
        _logger.LogInformation("Parameters for request of openAI model = analyzeOfImage: {analyzeOfImage}, descriptionOfImage: {descriptionOfImage}", analyzeOfImage,
            descriptionOfImage);

        try
        {
            var threadId = await CreateNewThreadAndRun(descriptionOfImage, imageUrl);

            Thread.Sleep(2000); // Wait a bit to give AI some time to respond

            var assistantResponse = await FetchAssistantResponse(0, threadId);

            if (String.IsNullOrWhiteSpace(assistantResponse))
            {
                _logger.LogInformation("AI Assistant hasn't responded and going with default descriptions.");
                return new ImageDefinition();
            }

            try
            {
                _logger.LogInformation("Text response of openAI model: {responseContent}", assistantResponse);
                return JsonConvert.DeserializeObject<ImageDefinition>(assistantResponse);
            }
            catch
            {
                _logger.LogError("Unable to parse JSON response of openAI model: {responseContent}", assistantResponse);
                throw;
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Unable to describe image with openAI model: {message}", e.Message);
        }

        return new ImageDefinition();
    }

    private async Task<string> CreateNewThreadAndRun(string descriptionOfImage, string imageUrl)
    {
        var assistantBody = new
        {
            assistant_id = _assistantId,
            thread = new
            {
                messages = new dynamic[]
                {
                    new { role = "user", content = $"Do your thing for the image I provide. Image title is : {descriptionOfImage}" },
                    new { role = "user", content = new dynamic[] { new { type = "image_url", image_url = new { url = imageUrl } } } }
                },
            }
        };

        var requestContent = new StringContent(JsonConvert.SerializeObject(assistantBody), Encoding.UTF8, "application/json");

        var response = await _openAiClient.PostAsync("https://api.openai.com/v1/threads/runs", requestContent);
        response.EnsureSuccessStatusCode();
        var responseContent = await response.Content.ReadAsStringAsync();

        var runCreationResponse = JsonConvert.DeserializeObject<ThreadAndRunCreationResponse>(responseContent);
        return runCreationResponse?.ThreadId ?? throw new Exception("Thread&Run could not be created!");
    }

    private async Task<string> FetchAssistantResponse(int retryCount, string threadId)
    {
        var response = await _openAiClient.GetAsync($"https://api.openai.com/v1/threads/{threadId}/messages?limit=2");
        response.EnsureSuccessStatusCode();
        var responseContent = await response.Content.ReadAsStringAsync();

        var assistanceChatResponse = JsonConvert.DeserializeObject<ThreadMessagesResponse>(responseContent);
        if (assistanceChatResponse == null || assistanceChatResponse.Data?.Count < 2)
        {
            _logger.LogInformation($"AI Assistant hasn't responded yet.");
            if (retryCount >= 3)
            {
                throw new Exception("Unable to get response from openAI Assistant after 3 retries.");
            }

            _logger.LogWarning($"Retrying : {retryCount}/3");
            return await FetchAssistantResponse(retryCount + 1, threadId);
        }

        return assistanceChatResponse!.Data.First().Content?.FirstOrDefault()?.Text?.Value?.Trim() ?? String.Empty;
    }
}