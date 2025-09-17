using Godot;
using Game.Autoload;
using System;
using System.Threading;

public partial class IntroCutScene : Node
{
	private string textToType = "";
	private float charRevealInterval = 0.07f; // Time in seconds between each character
	private bool typing = false;
	private bool skipRequested = false;
	private CancellationTokenSource typingCts = null;
	private Label typeWriterTextLabel;
	private Button returnToMenuButton;
	[Export(PropertyHint.File, "*.tscn")]
	private string mainMenuScenePath;
	private Random rng = new Random();

	private TextureRect imageBackground;

	public override void _Ready()
	{
		typeWriterTextLabel = GetNode<Label>("%TypeWriterText");
		returnToMenuButton = GetNode<Button>("%ReturnToMenuButton");
		imageBackground = GetNode<TextureRect>("%Image");
		AudioHelpers.RegisterButtons(new Button[] { returnToMenuButton });
		returnToMenuButton.Pressed += OnReturnToMenuButtonPressed;
		StartIntro();
	}

	private async void StartIntro()
	{
		AudioHelpers.PlayIntroMusic();
		await StartTyping("Year 2178.\nHumanity embarks on its first mission to explore a distant exoplanet.");
		imageBackground.Texture = GD.Load<Texture2D>("res://assets/introImage/scene2.png");
		await ToSignal(GetTree().CreateTimer(1.0f), "timeout");
		await StartTyping("From orbit,\nthe mysterious world appears…\nsilent and unexplored.");
		await ToSignal(GetTree().CreateTimer(1.0f), "timeout");
		typeWriterTextLabel.Text = "";
		imageBackground.Texture = GD.Load<Texture2D>("res://assets/introImage/scene3.png");
		await ToSignal(GetTree().CreateTimer(1.0f), "timeout");
		await StartTyping("The spaceship begins its approach towards this alien world.");
		await ToSignal(GetTree().CreateTimer(1.0f), "timeout");
		typeWriterTextLabel.Text = "";
		imageBackground.Texture = GD.Load<Texture2D>("res://assets/introImage/scene4.png");
		await ToSignal(GetTree().CreateTimer(1.0f), "timeout");
		await StartTyping("Clouds part,\nrevealing alien soil below.");
		await ToSignal(GetTree().CreateTimer(1.0f), "timeout");
		typeWriterTextLabel.Text = "";
		imageBackground.Texture = GD.Load<Texture2D>("res://assets/introImage/scene5.png");
		await ToSignal(GetTree().CreateTimer(1.0f), "timeout");
		await StartTyping("Touchdown:\nThe landing base establishes humanity’s first presence here.");
		await ToSignal(GetTree().CreateTimer(1.0f), "timeout");
		typeWriterTextLabel.Text = "";
		imageBackground.Texture = GD.Load<Texture2D>("res://assets/introImage/scene6.png");
		await ToSignal(GetTree().CreateTimer(1.0f), "timeout");
		await StartTyping("Meanwhile, on Earth,\nhumans monitor the mission\nfrom the control room.");
		await ToSignal(GetTree().CreateTimer(1.0f), "timeout");
		typeWriterTextLabel.Text = "";
		imageBackground.Texture = GD.Load<Texture2D>("res://assets/introImage/scene7.png");
		await ToSignal(GetTree().CreateTimer(1.0f), "timeout");
		await StartTyping("Remember, the goal of the mission is to investigate and analyze the mysterious monolith,\n the ground rover is equipped for that task.");
		await ToSignal(GetTree().CreateTimer(1.0f), "timeout");
		typeWriterTextLabel.Text = "";
		imageBackground.Texture = GD.Load<Texture2D>("res://assets/introImage/scene7bis.png");
		await ToSignal(GetTree().CreateTimer(1.0f), "timeout");
		await StartTyping("The monolith is known to produce disturbances in the gravitational field of the planet.\nUse the robots' sensors to measure these anomalies and locate the monolith.");
		await ToSignal(GetTree().CreateTimer(1.0f), "timeout");
		typeWriterTextLabel.Text = "";
		imageBackground.Texture = GD.Load<Texture2D>("res://assets/introImage/scene8.png");
		await ToSignal(GetTree().CreateTimer(1.0f), "timeout");
		await StartTyping("Other tasks await:\nstrange minerals — red, green, and blue —\nmust be collected and analyzed.");
		await ToSignal(GetTree().CreateTimer(1.0f), "timeout");
		typeWriterTextLabel.Text = "";
		imageBackground.Texture = GD.Load<Texture2D>("res://assets/introImage/scene9.png");
		await ToSignal(GetTree().CreateTimer(1.0f), "timeout");
		await StartTyping("Beware, the autonomous robots have limits:\nthey must be within antenna coverage\nto operate across the planet.\n Robots can chain and deploy antennas to extend coverage.");
		await ToSignal(GetTree().CreateTimer(1.0f), "timeout");
		typeWriterTextLabel.Text = "";
		imageBackground.Texture = GD.Load<Texture2D>("res://assets/introImage/scene10.png");
		await ToSignal(GetTree().CreateTimer(1.0f), "timeout");
		await StartTyping("Also, batteries are finite.\nRovers must gather alien wood\nand return it to the base to power up the recharge station.");
		await ToSignal(GetTree().CreateTimer(1.0f), "timeout");
		typeWriterTextLabel.Text = "";
		imageBackground.Texture = GD.Load<Texture2D>("res://assets/introImage/scene11.png");
		await ToSignal(GetTree().CreateTimer(1.0f), "timeout");
		await StartTyping("Exploration brings risks:\na ground rover may become stuck\nin alien soil.");
		await ToSignal(GetTree().CreateTimer(1.0f), "timeout");
		typeWriterTextLabel.Text = "";
		imageBackground.Texture = GD.Load<Texture2D>("res://assets/introImage/scene12.png");
		await ToSignal(GetTree().CreateTimer(1.0f), "timeout");
		await StartTyping("But interdependence emerges —\na UAV drone can rescue the rover,\ncarrying it to safety.");
		await ToSignal(GetTree().CreateTimer(1.0f), "timeout");
		typeWriterTextLabel.Text = "";
		imageBackground.Texture = GD.Load<Texture2D>("res://assets/introImage/scene13.png");
		await ToSignal(GetTree().CreateTimer(1.0f), "timeout");
		await StartTyping("Together,\nhumans and autonomous agents\nform a team.");
		await ToSignal(GetTree().CreateTimer(1.0f), "timeout");
		typeWriterTextLabel.Text = "";
		imageBackground.Texture = GD.Load<Texture2D>("res://assets/introImage/scene14.png");
		await ToSignal(GetTree().CreateTimer(1.0f), "timeout");
		await StartTyping("Your mission: Learn how to build trust, coordination, and understanding in Human-Autonomy Teams.\nAre you ready?");
		await ToSignal(GetTree().CreateTimer(1.0f), "timeout");
	}

	private void OnReturnToMenuButtonPressed()
	{
		AudioHelpers.PlayMusic();
		LevelManager.ChangeToMainMenu();
	}

	public async System.Threading.Tasks.Task StartTyping(string text)
	{
		// Cancel any previous typing
		if (typingCts != null)
		{
			typingCts.Cancel();
			typingCts.Dispose();
		}
		typingCts = new CancellationTokenSource();
		var token = typingCts.Token;
		typeWriterTextLabel.Text = "";
		textToType = text;
		typing = true;
		skipRequested = false;
		await TypeTextAsync(token);
	}

	public void SkipToEnd()
	{
		skipRequested = true;
	}



	private async System.Threading.Tasks.Task TypeTextAsync(CancellationToken token)
	{
		for (int i = 0; i < textToType.Length; i++)
		{
			if (skipRequested)
			{
				typeWriterTextLabel.Text = textToType;
				typing = false;
				return;
			}
			if (token.IsCancellationRequested)
			{
				typing = false;
				return;
			}
			int value = rng.Next(1, 5);
			typeWriterTextLabel.Text += textToType[i];
			if (textToType[i] != ' ' && textToType[i] != '\n')
			{
				switch (value)
				{
					case 1:
						AudioHelpers.PlayCharacterOne();
						break;
					case 2:
						AudioHelpers.PlayCharacterTwo();
						break;
					case 3:
						AudioHelpers.PlayCharacterThree();
						break;
					case 4:
						AudioHelpers.PlayCharacterFour();
						break;
				}
			}
			else if (textToType[i] == '\n')
			{
				await ToSignal(GetTree().CreateTimer(0.3f), "timeout");
				AudioHelpers.PlayEndOfLineSound();
			}
			if (textToType[i] == ',' || textToType[i] == '.')
			{
				await ToSignal(GetTree().CreateTimer(0.3f), "timeout");
			}
			await ToSignal(GetTree().CreateTimer(charRevealInterval), "timeout");
		}
		typing = false;
	}
}