namespace PrintMe.Workers.Enums;

[Flags]
public enum Category : long
{
    None = 0,

    // Nature & Landscapes Subcategories
    NaturePrints = 1 << 0,    // 1
    BotanicalArt = 1 << 1,    // 2
    AnimalArt = 1 << 2,       // 4
    SpaceAndAstronomy = 1 << 3,  // 8
    MapsAndCities = 1 << 4,   // 16
    Landscapes = 1 << 5,      // 32
    NatureAndLandscapes = NaturePrints | BotanicalArt | AnimalArt | SpaceAndAstronomy | MapsAndCities | Landscapes, // 63

    // Famous Painters Subcategories
    ArtPrints = 1 << 6,       // 64
    RenaissanceMasters = 1 << 7,  // 128
    DutchMasters = 1 << 8,    // 256
    ModernMasters = 1 << 9,   // 512
    AbstractArt = 1 << 10,    // 1024
    FamousPainters = ArtPrints | RenaissanceMasters | DutchMasters | ModernMasters | AbstractArt, // 1984

    // Posters Subcategories
    RetroAndVintage = 1 << 11,    // 2048
    BlackAndWhite = 1 << 12,      // 4096
    HistoricalPosters = 1 << 13,  // 8192
    ClassicPosters = 1 << 14,     // 16384
    TextPosters = 1 << 15,        // 32768
    MoviesAndGamesPosters = 1 << 16,  // 65536
    MusicPosters = 1 << 17,       // 131072
    SportsPosters = 1 << 18,      // 262144
    Posters = RetroAndVintage | BlackAndWhite | HistoricalPosters | ClassicPosters | TextPosters | MoviesAndGamesPosters | MusicPosters | SportsPosters, // 524287

    // Art Styles Subcategories
    Illustrations = 1 << 19,      // 524288
    Photographs = 1 << 20,        // 1048576
    IconicPhotos = 1 << 21,       // 2097152
    GeneralPosters = 1 << 22,     // 4194304
    KidsWallArt = 1 << 23,        // 8388608
    ArtStyles = Illustrations | Photographs | IconicPhotos | GeneralPosters | KidsWallArt // 16777215
}