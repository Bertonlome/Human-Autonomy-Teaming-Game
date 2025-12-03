using Godot;
using Game.Component;

namespace Game.Building;

public partial class PaintedTile : Node2D
{
	private ColorRect colorRect;
	private Label tileNumberLabel;
	private LineEdit labelEdit;

	// Public properties for easy access to tile data
	public BuildingComponent AssociatedRobot { get; set; }
	public int TileNumber { get; private set; }
	public string Annotation => labelEdit?.Text ?? "";
	public Vector2I GridPosition { get; set; }
	public bool IsReachable { get; set; } = true;

	public override void _Ready()
	{
		colorRect = GetNode<ColorRect>("ColorRect");
		tileNumberLabel = GetNode<Label>("TileNumberLabel");
		labelEdit = GetNode<LineEdit>("LabelEdit");
		
		// Connect Enter key to release focus
		labelEdit.TextSubmitted += OnLabelEditTextSubmitted;
	}

	private void OnLabelEditTextSubmitted(string newText)
	{
		// Release focus when Enter is pressed
		labelEdit.ReleaseFocus();
	}

	public void SetColor(Color color)
	{
		color.A = 0.5f; // Set to 50% transparency
		colorRect.Color = color;
	}

	public void SetNumberLabel(int number)
	{
		TileNumber = number;
		tileNumberLabel.Text = number.ToString();
	}

	public void DisplayLabelEdit()
	{
		labelEdit.Visible = true;
		labelEdit.GrabFocus();
	}
	
	public void SetAnnotation(string text)
	{
		if (labelEdit != null)
		{
			labelEdit.Text = text;
		}
	}

}
