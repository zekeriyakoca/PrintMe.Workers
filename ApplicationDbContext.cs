using PrintMe.Workers.Models;

namespace PrintMe.Workers;
using Microsoft.EntityFrameworkCore;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CatalogItem>(builder =>
        {
            builder.OwnsMany(c => c.ProductImages);
        });
        base.OnModelCreating(modelBuilder);
    }

    public DbSet<CatalogItem> CatalogItems { get; set; }
}