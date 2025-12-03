using Godot;
using Game.Services;

namespace Game.UI;

public partial class ApiKeyDialog : AcceptDialog
{
	private LineEdit apiKeyInput;
	private Label statusLabel;
	private GeminiApiService geminiApiService;
	
	public override void _Ready()
	{
		Title = "Configure Gemini API Key";
		DialogText = "Enter your Gemini API key to enable AI path optimization:";
		OkButtonText = "Save";
		
		// Create input field
		apiKeyInput = new LineEdit();
		apiKeyInput.PlaceholderText = "Enter your API key here...";
		apiKeyInput.Secret = true;
		apiKeyInput.CustomMinimumSize = new Vector2(400, 0);
		
		// Create status label
		statusLabel = new Label();
		statusLabel.Text = "Get your API key from: https://makersuite.google.com/app/apikey";
		statusLabel.AddThemeColorOverride("font_color", Colors.Gray);
		
		// Add to dialog
		var vbox = new VBoxContainer();
		vbox.AddChild(apiKeyInput);
		vbox.AddChild(statusLabel);
		AddChild(vbox);
		
		// Move vbox after buttons (hacky but works)
		MoveChild(vbox, 0);
		
		// Connect signals
		Confirmed += OnConfirmed;
		Canceled += OnCanceled;
		
		// Try to load existing key
		LoadExistingKey();
	}
	
	public void SetGeminiService(GeminiApiService service)
	{
		geminiApiService = service;
		LoadExistingKey();
	}
	
	private void LoadExistingKey()
	{
		string configPath = "user://gemini_config.txt";
		if (FileAccess.FileExists(configPath))
		{
			using var file = FileAccess.Open(configPath, FileAccess.ModeFlags.Read);
			if (file != null)
			{
				string existingKey = file.GetAsText().Trim();
				if (!string.IsNullOrEmpty(existingKey))
				{
					// Show masked version
					apiKeyInput.Text = existingKey;
					statusLabel.Text = "API key loaded from config";
					statusLabel.AddThemeColorOverride("font_color", Colors.Green);
				}
			}
		}
	}
	
	private void OnConfirmed()
	{
		string key = apiKeyInput.Text.Trim();
		
		if (string.IsNullOrEmpty(key))
		{
			statusLabel.Text = "API key cannot be empty";
			statusLabel.AddThemeColorOverride("font_color", Colors.Red);
			return;
		}
		
		// Save the key
		if (geminiApiService != null)
		{
			geminiApiService.SaveApiKey(key);
			GD.Print("API key saved successfully");
		}
		else
		{
			// Fallback: save directly
			string configPath = "user://gemini_config.txt";
			using var file = FileAccess.Open(configPath, FileAccess.ModeFlags.Write);
			if (file != null)
			{
				file.StoreString(key);
				GD.Print("API key saved to config file");
			}
		}
		
		Hide();
	}
	
	private void OnCanceled()
	{
		Hide();
	}
}
