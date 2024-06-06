namespace PrintMe.Workers.Models;

public class Product
{
    public int Id { get; set; }
    public string OriginalImageUrl { get; set; }
    public string TemplateImageUrl { get; set; }
    public string ResultImageUrl { get; set; }
    public string ThumbnailUrl { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
}