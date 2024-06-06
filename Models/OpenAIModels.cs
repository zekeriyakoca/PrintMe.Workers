namespace PrintMe.Workers.Models;

public class CompletionResponse
{
    public List<Choice> Choices { get; set; } = new List<Choice>();
}
public class Choice
{
    public string Text { get; set; }
}

public class ImageDefinition
{
    public string Painter { get; set; } = "Unknown";
    public string Title { get; set; } = "Painting";
    public string Description { get; set; } = "";
    public CategoryEnum Category { get; set; } = CategoryEnum.None;
}