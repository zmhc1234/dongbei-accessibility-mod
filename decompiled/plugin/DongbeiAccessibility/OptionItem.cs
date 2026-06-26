namespace DongbeiAccessibility;

public class OptionItem
{
	public string Text { get; set; }

	public float ScreenX { get; set; }

	public float ScreenY { get; set; }

	public bool HasScreenPosition { get; set; }

	public object ClickableComponent { get; set; }

	public ChapterInfo ChapterInfo { get; set; }

	public int Index { get; set; } = -1;
}
