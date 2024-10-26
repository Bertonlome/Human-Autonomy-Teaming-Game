using Game.Component;
using Godot;

namespace Game.Building;


public partial class GroundRobot : Node2D
{
	[Export]
	private BuildingComponent buildingComponent;
	[Export]
	private AnimatedSprite2D fireAnimatedSprite2D;
	[Export]
	private AnimatedSprite2D animatedSprite2D;

	private AudioStreamPlayer audioStreamPlayer;


	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		audioStreamPlayer = GetNode<AudioStreamPlayer>("AudioStreamPlayer");

		fireAnimatedSprite2D.Visible = false;

		buildingComponent.Disabled += OnDisabled;
		buildingComponent.Enabled += OnEnabled;
	}

	private void OnDisabled()
	{
		audioStreamPlayer.Play();
		animatedSprite2D.Play("destroyed");
		fireAnimatedSprite2D.Visible = true;
	}

		private void OnEnabled()
	{
		animatedSprite2D.Play("Sci-fi");
		fireAnimatedSprite2D.Visible = false;
	}
}
