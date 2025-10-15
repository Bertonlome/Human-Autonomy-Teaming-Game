using Game.Autoload;
using Godot;
using System;

namespace Game.UI;

public partial class OptionsMenu : CanvasLayer
{
	private const string SFX_BUS_NAME = "SFX";
	private const string MUSIC_BUS_NAME = "Music";
	private const string GEIGER_BUS_NAME = "Geiger";

	[Signal]
	public delegate void DonePressedEventHandler();

	private Button sfxUpButton;
	private Button sfxDownButton;
	private Label sfxLabel;
	private Button musicUpButton;
	private Button musicDownButton;
	private Label musicLabel;
	private Button geigerUpButton;
	private Button geigerDownButton;
	private Label geigerLabel;
	private Button windowButton;
	private Button doneButton;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		sfxUpButton = GetNode<Button>("%SFXUpButton");
		sfxDownButton = GetNode<Button>("%SFXDownButton");
		sfxLabel = GetNode<Label>("%SFXLabel");

		musicUpButton = GetNode<Button>("%MusicUpButton");
		musicDownButton = GetNode<Button>("%MusicDownButton");
		musicLabel = GetNode<Label>("%MusicLabel");

		geigerUpButton = GetNode<Button>("%GeigerUpButton");
		geigerDownButton = GetNode<Button>("%GeigerDownButton");
		geigerLabel = GetNode<Label>("%GeigerLabel");

		windowButton = GetNode<Button>("%WindowButton");
		doneButton = GetNode<Button>("%DoneButton");

		AudioHelpers.RegisterButtons(new Button[] {sfxUpButton, sfxDownButton, musicUpButton, musicDownButton, geigerUpButton, geigerDownButton, windowButton, doneButton });
		UpdateDisplay();

		sfxUpButton.Pressed += () => {
			ChangeBusVolume(SFX_BUS_NAME, .1f);
		};
		sfxDownButton.Pressed += () => {
			ChangeBusVolume(SFX_BUS_NAME, -.1f);
		};

		musicUpButton.Pressed += () => {
			ChangeBusVolume(MUSIC_BUS_NAME, .1f);
		};
		musicDownButton.Pressed += () => {
			ChangeBusVolume(MUSIC_BUS_NAME, -.1f);
		};

		geigerUpButton.Pressed += () => {
			ChangeBusVolume(GEIGER_BUS_NAME, .1f);
		};
		geigerDownButton.Pressed += () => {
			ChangeBusVolume(GEIGER_BUS_NAME, -.1f);
		};

		windowButton.Pressed += OnWindowButtonPressed;
		doneButton.Pressed += OnDoneButtonPressed;
	}

	private void UpdateDisplay()
	{
		sfxLabel.Text = Mathf.Round(OptionsHelper.GetBusVolumePercent(SFX_BUS_NAME) * 10).ToString();
		musicLabel.Text = Mathf.Round(OptionsHelper.GetBusVolumePercent(MUSIC_BUS_NAME) * 10).ToString();
		geigerLabel.Text = Mathf.Round(OptionsHelper.GetBusVolumePercent(GEIGER_BUS_NAME) * 10).ToString();
		windowButton.Text = OptionsHelper.IsFullScreen() ? "Fullscreen" : "Windowed";
	}

	private void ChangeBusVolume(string busName, float change)
	{
		var GetBusVolumePercent = OptionsHelper.GetBusVolumePercent(busName);
		GetBusVolumePercent = Mathf.Clamp(GetBusVolumePercent + change, 0, 1);
		OptionsHelper.SetBusVolumePercent(busName, GetBusVolumePercent);
		UpdateDisplay();
	}

	private void OnWindowButtonPressed()
	{
		OptionsHelper.ToggleWindowMode();
		UpdateDisplay();
	}

	private void OnDoneButtonPressed()
	{
		EmitSignal(SignalName.DonePressed);
	}
}
