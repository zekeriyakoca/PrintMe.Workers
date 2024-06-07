using PrintMe.Workers.Models;

namespace PrintMe.Workers;
using Microsoft.EntityFrameworkCore;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

    public DbSet<CatalogItem> CatalogItems { get; set; }
}