namespace PrintMe.Workers.Models;


[Flags]
public enum CategoryEnum : long
{
    None = 0,

    // Nature Subcategories (1-15)
    NaturePrints = 1 << 0,    // 1
    Botanical = 1 << 1,       // 2
    Animals = 1 << 2,         // 4
    SpaceAndAstronomy = 1 << 3,  // 8
    MapsAndCities = 1 << 4,  // 16
    Nature = NaturePrints | Botanical | Animals | SpaceAndAstronomy | MapsAndCities, // 31

    // Vintage & Retro Subcategories (18-32)
    RetroAndVintage = 1 << 7,   // 128
    BlackAndWhite = 1 << 8,     // 256
    GoldAndSilver = 1 << 9,     // 512
    HistoricalPrints = 1 << 10,  // 1024
    ClassicPosters = 1 << 11,    // 2048
    VintageAndRetro = RetroAndVintage | BlackAndWhite | GoldAndSilver | HistoricalPrints | ClassicPosters, // 3968

    // Art Styles Subcategories (34-48)
    Illustrations = 1 << 14,     // 16384
    Photographs = 1 << 15,       // 32768
    ArtPrints = 1 << 16,         // 65536
    TextPosters = 1 << 17,       // 131072
    Graphical = 1 << 18,         // 262144
    ArtStyles = Illustrations | Photographs | ArtPrints | TextPosters | Graphical, // 511744

    // Famous Painters Subcategories (50-64)
    FamousPainters = 1 << 21,    // 2097152
    IconicPhotos = 1 << 22,      // 4194304
    StudioCollections = 1 << 23, // 8388608
    ModernArtists = 1 << 24,     // 16777216
    AbstractArt = 1 << 25,       // 33554432
    FamousPaintersCategory = FamousPainters | IconicPhotos | StudioCollections | ModernArtists | AbstractArt // 67108864
}