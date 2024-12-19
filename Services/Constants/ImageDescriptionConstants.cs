namespace PrintMe.Workers.Services.Constants
{
    public static class ImageDescriptionConstants
    {
        public static string GetPrompt(string descriptionOfImage, string analyzeOfImage) => $@"
Given the image description (this is the name of the file; ignore it if it's a generic name like 'download', numbers or online repo name like 'unsplash') as '{descriptionOfImage}' and analyzeOfImage as '{analyzeOfImage}' generated by Azure Computer Vision service, try to determine which painting it is.
If you don't think this is a painting by a known painter, provide a good title, motto, and description. Find a proper category and return a JSON object with the following structure:

{{
    ""Painter"": ""<Name of the painter if known, otherwise null value>"",
    ""Title"": ""<Name or Title of the painting>"",
    ""Motto"": ""<Short Description of the painting. Max 12 words>"",
    ""Description"": ""<A detailed description of the painting. Max 40 words>"",
    ""Category"": <Category number corresponding to the following Enum>
}}

public enum Category : long
{{
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
}}

For example, category should be 1 for NaturePrints, 3 for BotanicalArt, 4 for AnimalArt, 63 for NatureAndLandscapes, 1984 for FamousPainters, and 524287 for Posters.
Be precise in defining the category. The category is important.
RETURN ONLY JSON OBJECT. DO NOT RETURN ANYTHING ELSE.
";

        public static string GetPromptForImage(string descriptionOfImage) => $@"
Analyse the image provided. You can also get a clue from the name of the image file: {descriptionOfImage}. Decide If It's a Painting of a Known Painter or Not. If so, return :
{{
    ""Painter"": ""<Name of the painter>"",
    ""Title"": ""<Name or Title of the painting>"",
    ""Motto"": ""<Short Description of the painting. Max 12 words>"",
    ""Description"": ""<A detailed description of the painting. Max 40 words>"",
    ""Category"": <Category number corresponding to the following Enum>
}}

If not, it's a poster, photo or an ordinary image. Provide a title, motto, description, and category. Return a JSON object with this structure:

{{
    ""Painter"": null,
    ""Title"": ""<A good title for the image>"",
    ""Motto"": ""<Short Description of the image. Max 12 words>"",
    ""Description"": ""<A detailed description of the image. Max 40 words>"",
    ""Category"": <Category number corresponding to the following Enum. Try to return a combination of Categories. Use the power of flag>
}}

public enum Category : long
{{
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
}}

For example, category should be 1 for NaturePrints, 3 for BotanicalArt, 4 for AnimalArt, 63 for NatureAndLandscapes, 1984 for FamousPainters, and 524287 for Posters. Give multiple categories using the power of flags over enums if needed. Be precise in defining the category. The category is important.

**RETURN ONLY ONE JSON OBJECT. DON'T RETURN '''json... -LIKE WRAPPER. DO NOT RETURN ANYTHING ELSE.**
";
    }
}