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
		if (instance.GetChildCount() > 1)
			{
				return;
			}
		instance.AddChild(floatingtext);
		floatingtext.SetText(message);
		floatingtext.GlobalPosition = floatingtext.GetGlobalMousePosition();
		
		// Scale inversely to camera zoom for consistent visual size
		var camera = instance.GetViewport().GetCamera2D();
		if (camera != null)
		{
			floatingtext.Scale = Vector2.One / camera.Zoom;
		}
	}

	public static void ShowMessageAtBuildingPosition(string message, Node2D buildingNode)
	{
		var floatingtext = instance.floatingtextScene.Instantiate<FloatingText>();
		if (instance.GetChildCount() > 0)
			{
				return;
			}
		instance.AddChild(floatingtext);
		floatingtext.SetText(message);
		floatingtext.GlobalPosition = buildingNode.GlobalPosition;
		
		// Scale inversely to camera zoom for consistent visual size
		var camera = instance.GetViewport().GetCamera2D();
		if (camera != null)
		{
			floatingtext.Scale = Vector2.One / camera.Zoom * 0.6f;
		}
	}
}
