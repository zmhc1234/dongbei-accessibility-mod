namespace DongbeiAccessibility;

public class ChapterInfo
{
	public int Index { get; set; }

	public int ChapterNumber { get; set; }

	public string Name { get; set; }

	public string ProgressText { get; set; }

	public int ProgressReached { get; set; }

	public int ProgressTotal { get; set; }

	public bool IsLocked { get; set; }

	public object ButtonComponent { get; set; }
}
