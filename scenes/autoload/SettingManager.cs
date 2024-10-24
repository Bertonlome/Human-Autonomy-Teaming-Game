using Godot;


namespace Game.Autoload;

public partial class SettingManager : Node
{
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		RenderingServer.SetDefaultClearColor(new Color("47aba9"));
		GetViewport().GetWindow().MinSize = new Vector2I(1280, 720);
	}
}
