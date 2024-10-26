using Game.Component;
using Game.UI;
using Godot;


namespace Game.Manager;

public partial class FloatingTextManager : Node
{
	[Export]
	private PackedScene floatingtextScene;

	private static FloatingTextManager instance;

    public override void _Notification(int what)
    {
        if (what == NotificationSceneInstantiated)
		{
			instance = this;
		}
    }

	public static void ShowMessageAtMousePosition(string message)
	{
		var floatingtext = instance.floatingtextScene.Instantiate<FloatingText>();
		instance.AddChild(floatingtext);
		floatingtext.SetText(message);
		floatingtext.GlobalPosition = floatingtext.GetGlobalMousePosition();
	}

	public static void ShowMessageAtBuildingPosition(string message, Node2D buildingNode)
	{
		var floatingtext = instance.floatingtextScene.Instantiate<FloatingText>();
		instance.AddChild(floatingtext);
		floatingtext.SetText(message);
		floatingtext.GlobalPosition = buildingNode.GlobalPosition;
	}
}
