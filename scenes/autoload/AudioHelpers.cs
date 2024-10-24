using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Godot;

namespace Game.Autoload;
public partial class AudioHelpers : Node
{

	private static AudioHelpers instance;

	private AudioStreamPlayer explosionAudioStreamPlayer;
	private AudioStreamPlayer clickAudioStreamPlayer;
	private AudioStreamPlayer victoryAudioStreamPlayer;
	private AudioStreamPlayer musicAudioStreamPlayer;




	public override void _Notification(int what)
	{
		if(what == NotificationSceneInstantiated)
		{
			instance = this;
		}
	}
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		explosionAudioStreamPlayer = GetNode<AudioStreamPlayer>("ExplosionAudioStreamPlayer");
		clickAudioStreamPlayer = GetNode<AudioStreamPlayer>("ClickAudioStreamPlayer");
		victoryAudioStreamPlayer = GetNode<AudioStreamPlayer>("VictoryAudioStreamPlayer");
		musicAudioStreamPlayer = GetNode<AudioStreamPlayer>("MusicAudioStreamPlayer");

		musicAudioStreamPlayer.Finished += OnMusicFinished;

	}

	public static void PlayVictory()
	{
		instance.victoryAudioStreamPlayer.Play();
	}
	public static void PlayBuildingDestruction()
	{
		instance.explosionAudioStreamPlayer.Play();
	}

	public static void RegisterButtons(IEnumerable<Button> buttons)
	{
		foreach(var button in buttons)
		{
			button.Pressed += instance.OnButtonPressed;
		}
	}

	private void OnMusicFinished()
	{
		GetTree().CreateTimer(5).Timeout += OnMusicDelayTimerTimeout;
	}

	private void OnMusicDelayTimerTimeout()
	{
		musicAudioStreamPlayer.Play();
	}


	private void OnButtonPressed()
	{
		clickAudioStreamPlayer.Play();
	}
}
