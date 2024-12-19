using PrintMe.Workers.Enums;

namespace PrintMe.Workers.Models;

public class CompletionResponse
{
    public List<Choice> Choices { get; set; } = new List<Choice>();
    public ErrorResponse Error { get; set; }
}

public class Message
{
    public string Role { get; set; }
    public string Content { get; set; }
}

public class Choice
{
    public string Text { get; set; }
}

public class ChatResponse
{
    public string OnlyAnswer => Choices.FirstOrDefault()?.Message?.Content?.Trim() ?? String.Empty;
    public List<ChatChoice> Choices { get; set; } = new List<ChatChoice>();
    public ErrorResponse Error { get; set; }
}

public class ChatChoice
{
    public int Index { get; set; }
    public Message Message { get; set; }
}

public class ErrorResponse
{
    public string Message { get; set; }
    public string Type { get; set; }
    public string Param { get; set; }
    public string Code { get; set; }

    public override string ToString()
    {
        return String.Join(", ", new string[] { Message, Type, Param, Code });
    }
}

public class ImageDefinition
{
    public string Painter { get; set; }
    public string Title { get; set; } = "Painting";

    public string Motto { get; set; } = "Colors Speak Louder Than Words, Embrace the Art Within";

    public string Description { get; set; } =
        "A vibrant masterpiece capturing the essence of nature's beauty, with bold, sweeping strokes of color. The painting evokes a sense of tranquility and wonder, inviting viewers to immerse themselves in its enchanting landscape.";

    public Category Category { get; set; } = Category.None;
}