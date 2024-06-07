using PrintMe.Workers.Enums;

namespace PrintMe.Workers.Models;

public class QueueMessageContent
{
    
    public string ImageId { get; set; }
    public string ImageUrl { get; set; }
    
    public string ImageDescription { get; set; }
    public CatalogTags? Tag { get; set; }
    public List<MockupTemplate> MockupTemplates { get; set; } = new();
}