using Game.Autoload;
using Game.Resources.Level;
using Godot;

namespace Game.UI;

public partial class LevelSelectSection : PanelContainer
{
	[Signal]
	public delegate void LevelSelectedEventHandler(int levelIndex);

	private Button button;
	private Label resourceCountLabel;
	private Label levelNumberLabel;
	private TextureRect completedIndicator;
	private int levelIndex;

	public override void _Ready()
	{
		button = GetNode<Button>("%Button");

		AudioHelpers.RegisterButtons(new Button[] {button});
		resourceCountLabel = GetNode<Label>("%ResourceCountLabel");
		levelNumberLabel = GetNode<Label>("%LevelNumberLabel");
		completedIndicator = GetNode<TextureRect>("%CompletedIndicator");

		button.Pressed += OnButtonPressed;
	}

	public void SetLevelDefinition(LevelDefinitionResource levelDefinitionResource)
	{
		resourceCountLabel.Text = levelDefinitionResource.StartingResourceCount.ToString();
		completedIndicator.Visible = SaveManager.IsLevelCompleted(levelDefinitionResource.Id);
	}

	public void SetLevelIndex(int index)
	{
		levelIndex = index;
		levelNumberLabel.Text = $"Level {index + 1}";
	}

	private void OnButtonPressed()
	{
		EmitSignal(SignalName.LevelSelected, levelIndex);
	}
}
