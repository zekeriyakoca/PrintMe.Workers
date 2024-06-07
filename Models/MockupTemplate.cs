using System.Drawing;

namespace PrintMe.Workers.Models;

public class MockupTemplate
{
    public int Id { get; set; }
    public string TemplateImageUrl { get; set; }
    public Point[] Corners { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}