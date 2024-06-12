using Microsoft.EntityFrameworkCore;
using PrintMe.Workers.Enums;

namespace PrintMe.Workers.Models;

[Owned]
public class ProductImage
{
    public int ProductId { get; set; }
    public string ImageUrl { get; set; }
    public string ThumbnailUrl { get; set; }
    public ImageCategory Category { get; set; }
}