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
	private AudioStreamPlayer robotMoveAudioStreamPlayer;
	private AudioStreamPlayer endOfLineSoundStreamPlayer;
	private AudioStreamPlayer characterOneStreamPlayer;
	private AudioStreamPlayer characterTwoStreamPlayer;
	private AudioStreamPlayer characterThreeStreamPlayer;
	private AudioStreamPlayer characterFourStreamPlayer;
	private AudioStreamPlayer introMusicStreamPlayer;
	private int noiseReductionForTextSounds = 35;


	public override void _Notification(int what)
	{
		if (what == NotificationSceneInstantiated)
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
		robotMoveAudioStreamPlayer = GetNode<AudioStreamPlayer>("RobotMoveAudioStreamPlayer");
		endOfLineSoundStreamPlayer = GetNode<AudioStreamPlayer>("EndOfLineSoundStreamPlayer");
		characterOneStreamPlayer = GetNode<AudioStreamPlayer>("CharacterOneStreamPlayer");
		characterTwoStreamPlayer = GetNode<AudioStreamPlayer>("CharacterTwoStreamPlayer");
		characterThreeStreamPlayer = GetNode<AudioStreamPlayer>("CharacterThreeStreamPlayer");
		characterFourStreamPlayer = GetNode<AudioStreamPlayer>("CharacterFourStreamPlayer");
		introMusicStreamPlayer = GetNode<AudioStreamPlayer>("IntroMusicStreamPlayer");

		musicAudioStreamPlayer.Finished += OnMusicFinished;
		introMusicStreamPlayer.Finished += OnIntroMusicFinished;

	}

	public static void PlayVictory()
	{
		instance.victoryAudioStreamPlayer.Play();
	}

	public static void PlayFailed()
	{
		instance.explosionAudioStreamPlayer.Play(); // TO CHANGE FOR A FAILURE SOUND
	}
	public static void PlayBuildingDestruction()
	{
		instance.explosionAudioStreamPlayer.Play();
	}

	public static void PlayCharacterOne()
	{
		instance.characterOneStreamPlayer.VolumeDb = - instance.noiseReductionForTextSounds;
		instance.characterOneStreamPlayer.Play();
	}
	public static void PlayCharacterTwo()
	{
		instance.characterTwoStreamPlayer.VolumeDb = - instance.noiseReductionForTextSounds;
		instance.characterTwoStreamPlayer.Play();
	}
	public static void PlayCharacterThree()
	{
		instance.characterThreeStreamPlayer.VolumeDb = - instance.noiseReductionForTextSounds;
		instance.characterThreeStreamPlayer.Play();
	}
	public static void PlayCharacterFour()
	{
		instance.characterFourStreamPlayer.VolumeDb = - instance.noiseReductionForTextSounds;
		instance.characterFourStreamPlayer.Play();
	}
	public static void PlayEndOfLineSound()
	{
		instance.endOfLineSoundStreamPlayer.VolumeDb = - instance.noiseReductionForTextSounds / 1.5f;
		instance.endOfLineSoundStreamPlayer.Play();
	}

	public static void PlayIntroMusic()
	{
		instance.musicAudioStreamPlayer.Stop();
		instance.introMusicStreamPlayer.Play();
	}

	public static void PlayMusic()
	{
		instance.introMusicStreamPlayer.Stop();
		if (instance.musicAudioStreamPlayer.Playing)
			return;
		instance.musicAudioStreamPlayer.Play();
	}

	public static void PlayMove()
	{
		instance.robotMoveAudioStreamPlayer.Play();
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
	private void OnIntroMusicFinished()
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
