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
	private AudioStreamPlayer geigerClackAudioStreamPlayer;
	private AudioStreamPlayer geigerCrackleLoopAudioStreamPlayer;
	private int noiseReductionForTextSounds = 35;

	// Geiger counter state
	public static bool geigerActive { get; private set; } = false;
	private float currentAnomalyValue = 0f;
	private float smoothedAnomaly = 0f;
	private float currentRate = 0f;
	private float timeToNextClick = 0f;
	private RandomNumberGenerator rng = new RandomNumberGenerator();
	
	// Geiger settings
	private const float EMA_ALPHA = 0.15f;           // Smoothing factor
	private const float MIN_RATE = 0.1f;             // clicks/sec at anomaly ≈ 0
	private const float MAX_RATE = 40f;              // clicks/sec at anomaly ≈ 500
	private const float CRACKLE_BLEND_START = 35f;   // Start blending to loop
	private const float CRACKLE_FULL_AT = 40f;       // Fully crackly here
	private const float PITCH_JITTER = 0.04f;        // ±4%
	private const float VOLUME_JITTER_DB = 1.5f;     // ±1.5 dB


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
		geigerClackAudioStreamPlayer = GetNode<AudioStreamPlayer>("GeigerClackAudioStreamPlayer");
		geigerCrackleLoopAudioStreamPlayer = GetNode<AudioStreamPlayer>("GeigerCrackleLoopAudioStreamPlayer");

		musicAudioStreamPlayer.Finished += OnMusicFinished;
		introMusicStreamPlayer.Finished += OnIntroMusicFinished;

		// Initialize Geiger counter
		rng.Randomize();
		if (geigerCrackleLoopAudioStreamPlayer.Stream != null)
		{
			// For WAV files
			if (geigerCrackleLoopAudioStreamPlayer.Stream is AudioStreamWav wavStream)
			{
				wavStream.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
			}
		}
		
		geigerCrackleLoopAudioStreamPlayer.VolumeDb = -40f;  // Start inaudible
		geigerCrackleLoopAudioStreamPlayer.Play();
		geigerCrackleLoopAudioStreamPlayer.StreamPaused = true; // Start paused
	}

	public override void _Process(double delta)
	{
		if (!geigerActive) return;

		// 1) Smooth incoming anomaly
		smoothedAnomaly = Mathf.Lerp(smoothedAnomaly, currentAnomalyValue, EMA_ALPHA);

		// 2) Map to clicks/sec
		float targetRate = MapAnomalyToRate(smoothedAnomaly);
		currentRate = Mathf.Lerp(currentRate, targetRate, 0.2f);

		// 3) Drive Poisson click scheduling
		if (currentRate > 0.01f)
		{
			timeToNextClick -= (float)delta;
			if (timeToNextClick <= 0f)
			{
				PlayGeigerClick();
				ScheduleNextGeigerClick(currentRate);
			}
		}
		else
		{
			timeToNextClick = 0.5f;
		}

		// 4) Manage high-rate crackle crossfade
		UpdateCrackleLayer();
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

	// ===== Geiger Counter Methods =====

	/// <summary>
	/// Start Geiger counter when a robot is selected
	/// </summary>
	/// <param name="initialAnomalyValue">Initial anomaly reading (0-500)</param>
	public static void StartGeigerCounter(float initialAnomalyValue = 0f)
	{
		geigerActive = true;
		instance.currentAnomalyValue = initialAnomalyValue;
		instance.smoothedAnomaly = initialAnomalyValue;
		instance.PlayGeigerClick();
		float initialRate = instance.MapAnomalyToRate(initialAnomalyValue);
		instance.currentRate = initialRate;
		instance.ScheduleNextGeigerClick(initialRate);
	}

	/// <summary>
	/// Stop Geiger counter when robot is deselected
	/// </summary>
	public static void StopGeigerCounter()
	{
		geigerActive = false;
		instance.currentRate = 0f;
		instance.geigerCrackleLoopAudioStreamPlayer.StreamPaused = true;
		instance.geigerCrackleLoopAudioStreamPlayer.VolumeDb = -40f;
	}

	/// <summary>
	/// Update anomaly reading (call this from OnNewAnomalyReading)
	/// </summary>
	/// <param name="anomalyValue">New anomaly reading (0-500)</param>
	public static void UpdateAnomalyReading(float anomalyValue)
	{
		if (geigerActive)
		{
			instance.currentAnomalyValue = anomalyValue;
		}
	}

	private float MapAnomalyToRate(float anomaly)
	{
		anomaly = Mathf.Clamp(anomaly, 0f, 500f);
		// Mildly curved mapping (log-ish feel)
		float tlin = anomaly / 500f;
		float tcurve = Mathf.Pow(tlin, 0.7f); // More responsive early on
		return Mathf.Lerp(MIN_RATE, MAX_RATE, tcurve);
	}

	private void ScheduleNextGeigerClick(float rate)
	{
		rate = Mathf.Clamp(rate, 0.01f, MAX_RATE);
		// Exponential inter-arrival from Poisson process: -ln(U)/λ
		float u = Mathf.Max(1e-6f, rng.Randf());
		timeToNextClick = -Mathf.Log(u) / rate;
		// Cap extremely tiny intervals to avoid "machine gun" at max rate
		timeToNextClick = Mathf.Max(timeToNextClick, 0.005f);
	}

	private void PlayGeigerClick()
	{
		if (geigerClackAudioStreamPlayer.Stream == null) return;

		// Slight pitch randomization
		float pitch = 1f + RandSymmetric(PITCH_JITTER);
		geigerClackAudioStreamPlayer.PitchScale = pitch;

		// Volume jitter
		float volDb = RandSymmetric(VOLUME_JITTER_DB);
		geigerClackAudioStreamPlayer.VolumeDb = volDb;

		// Rapid retrigger: stop if currently playing to ensure a clean transient
		if (geigerClackAudioStreamPlayer.Playing) 
			geigerClackAudioStreamPlayer.Stop();
		geigerClackAudioStreamPlayer.Play();
	}

	private void UpdateCrackleLayer()
	{
		if (geigerCrackleLoopAudioStreamPlayer.Stream == null) return;

		float a = currentRate;
		float blend = 0f;
		if (a > CRACKLE_BLEND_START)
		{
			blend = Mathf.InverseLerp(CRACKLE_BLEND_START, CRACKLE_FULL_AT, a);
			blend = Mathf.Clamp(blend, 0f, 1f);
		}

		// Hysteresis: small deadband to prevent fluttering around the threshold
		const float dbEnter = 1.0f;
		const float dbExit = 0.5f;

		if (blend > 0f && geigerCrackleLoopAudioStreamPlayer.StreamPaused && currentRate > CRACKLE_BLEND_START + dbEnter)
			geigerCrackleLoopAudioStreamPlayer.StreamPaused = false;
		if (blend == 0f && !geigerCrackleLoopAudioStreamPlayer.StreamPaused && currentRate < CRACKLE_BLEND_START - dbExit)
			geigerCrackleLoopAudioStreamPlayer.StreamPaused = true;

		// Crossfade volume (gets louder as rate increases)
		float targetDb = Mathf.Lerp(-30f, -6f, blend);
		geigerCrackleLoopAudioStreamPlayer.VolumeDb = Mathf.Lerp(geigerCrackleLoopAudioStreamPlayer.VolumeDb, targetDb, 0.1f);
	}

	private float RandSymmetric(float magnitude)
	{
		// Returns a random value in [-magnitude, +magnitude]
		return (rng.Randf() * 2.0f - 1.0f) * magnitude;
	}
}
