namespace PrintMe.Workers.Enums;

[Flags]
public enum CatalogTags : long
{
    OnSale = 1 << 0,
    Featured = 1 << 1,
    TopSellers = 1 << 2,
    OurPick = 1 << 3,
}