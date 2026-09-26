using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.Parser;
using BetterSongList.HarmonyPatches.UI;
using HMUI;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;

namespace BetterSongList.UI.SplitViews {
	class Settings {
		public static readonly Settings instance = new Settings();
		Settings() { }

		[UIParams] readonly BSMLParserParams parserParams = null;

		Config cfgi => Config.Instance;
		readonly string version = $"BetterSongList v{Assembly.GetExecutingAssembly().GetName().Version.ToString(3)} by Kinsi55";

		static readonly IReadOnlyList<object> preferredLeaderboardChoices = new List<object>() { "ScoreSaber", "BeatLeader" };
		static readonly IReadOnlyList<object> preferredMiscSettingChoices = new List<object>() { "Reaction Time", "Jump Distance", "Map Age" };
		string preferredLeaderboard {
			get => Config.Instance.PreferredLeaderboard;
			set => Config.Instance.PreferredLeaderboard = value;
		}
		string preferredMiscSetting {
			get => Config.Instance.PreferredMiscSetting;
			set => Config.Instance.PreferredMiscSetting = value;
		}

		static void SettingsClosed() {
			SongDeleteButton.UpdateState();
			ScrollEnhancements.UpdateState();
			// In some cases this can throw - Too bad!
			try {
				ExtraLevelParams.UpdateState(); 
			} catch { }
			Config.Instance.Changed();
		}


		[UIComponent("sponsorsText")] CurvedTextMeshPro sponsorsText = null;
		Task<string> sponsorDownload;
		void OpenSponsorsLink() => Process.Start("https://github.com/sponsors/kinsi55");
		async void OpenSponsorsModal() {
			parserParams.EmitEvent("CloseSettings");
			var text = sponsorsText;
			if(text == null)
				return;
			text.text = "Loading...";
			if(sponsorDownload == null || sponsorDownload.IsCompleted)
				sponsorDownload = DownloadSponsors();
			var description = await sponsorDownload;
			if(text == null)
				return;
			text.text = description;
			text.gameObject.SetActive(false);
			text.gameObject.SetActive(true);
		}

		static async Task<string> DownloadSponsors() {
			try {
				using(var client = new WebClient())
					return await client.DownloadStringTaskAsync("http://kinsi.me/sponsors/bsout.php");
			} catch {
				return "Failed to load";
			}
		}
	}
}
