namespace PrintMe.Workers.Models;

public class QueueMessage
{
    public string ImageUrl { get; set; }
    public List<MockupTemplate> MockupTemplates { get; set; } = new();
}