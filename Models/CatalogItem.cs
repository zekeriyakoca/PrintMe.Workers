using System.ComponentModel.DataAnnotations.Schema;
using PrintMe.Workers.Enums;

namespace PrintMe.Workers.Models;

[Table("Catalog")]
public class CatalogItem
{
    public int Id { get; set; }
    
    public string Name { get; set; }

    public string Motto { get; set; }
    public string Description { get; set; }
    
    public string Owner { get; set; }

    public string SearchParameters { get; set; }

    public decimal Price { get; set; }

    public string PictureFileName { get; set; }
    
    public string OriginalImage { get; set; }

    public Category Category { get; set; } = Category.None;

    public CatalogType CatalogType { get; set; } = CatalogType.Default;
    
    public CatalogTags? Tags { get; set; }

    public PrintSize? Size { get; set; }

    // Quantity in stock
    public int AvailableStock { get; set; }

    // Available stock at which we should reorder
    public int RestockThreshold { get; set; }
    
    public int? SalePercentage { get; set; }


    // Maximum number of units that can be in-stock at any time (due to physicial/logistical constraints in warehouses)
    public int MaxStockThreshold { get; set; }

    /// <summary>
    /// True if item is on reorder
    /// </summary>
    public bool OnReorder { get; set; }
    
}
