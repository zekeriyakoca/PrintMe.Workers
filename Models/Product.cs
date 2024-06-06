namespace PrintMe.Workers.Models;

public class Product
{
    public int Id { get; set; }
    public string OriginalImageUrl { get; set; }
    
    public string ImageUrl { get; set; }
    public string ThumbnailUrl { get; set; }
    public string MockupImageUrl { get; set; }
    public string MockupThumbnailUrl { get; set; }
    
    public string? Mockup2ImageUrl { get; set; }
    public string? Mockup2ThumbnailUrl { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
}