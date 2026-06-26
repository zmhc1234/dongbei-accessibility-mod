namespace DongbeiAccessibility;

public class SettingItem
{
	public enum SettingType
	{
		Unknown,
		Slider,
		Toggle,
		Dropdown,
		Button,
		Text
	}

	public string Name { get; set; }

	public SettingType Type { get; set; }

	public object Component { get; set; }

	public object ClickComponent { get; set; }

	public float Value { get; set; }

	public float MinValue { get; set; }

	public float MaxValue { get; set; }

	public bool IsOn { get; set; }

	public int SelectedIndex { get; set; }

	public string[] Options { get; set; }

	public float ScreenY { get; set; }

	public float ScreenX { get; set; }

	public bool HasScreenPosition { get; set; }
}
