using System.Collections.Generic;
using UnityEngine;
using HarmonyLib;
using System.Threading;
using System.Threading.Tasks;

namespace BetterSongList.Util {
	public static class LocalScoresUtil {
		public static PlayerDataModel playerDataModel { get; private set; }
		static readonly object scoreLock = new object();
		static HashSet<string> playedMaps = new HashSet<string>(500);
		static List<string> scoresDuringLoad;
		static Task loadTask;
		static int loadVersion;
		static bool scoresAvailable;
		static bool stopping;

		public static bool hasScores => Volatile.Read(ref scoresAvailable);

		public static void Load() {
			if(stopping)
				return;
			++loadVersion;
			lock(scoreLock)
				scoresDuringLoad = null;
			if (playerDataModel == null)
				playerDataModel = Object.FindFirstObjectByType<PlayerDataModel>();
			Volatile.Write(ref scoresAvailable, playerDataModel != null);

			foreach(var x in playerDataModel?.playerData?.levelsStatsData) {
				if(!x.Value.validScore)
					continue;

				AddScore(x.Key.levelId);
			}
		}

		internal static async Task LoadAsync() {
			await IPA.Utilities.UnityGame.SwitchToMainThreadAsync();
			if(stopping)
				return;
			if(loadTask == null || loadTask.IsCompleted)
				loadTask = PrepareScoresAsync();
			await loadTask;
		}

		static async Task PrepareScoresAsync() {
			if(playerDataModel == null)
				playerDataModel = Object.FindFirstObjectByType<PlayerDataModel>();
			var model = playerDataModel;
			if(model == null) {
				Volatile.Write(ref scoresAvailable, false);
				return;
			}

			var validIds = new List<string>();
			var stats = model.playerData?.levelsStatsData;
			if(stats != null)
				foreach(var entry in stats)
					if(entry.Value.validScore)
						validIds.Add(entry.Key.levelId);

			string[] previousIds;
			lock(scoreLock) {
				previousIds = new string[playedMaps.Count];
				playedMaps.CopyTo(previousIds);
				scoresDuringLoad = new List<string>();
			}
			var version = ++loadVersion;
			try {
				var prepared = await Task.Run(() => {
					var result = new HashSet<string>(previousIds);
					result.UnionWith(validIds);
					return result;
				});
				await IPA.Utilities.UnityGame.SwitchToMainThreadAsync();
				if(stopping || version != loadVersion || model == null || !ReferenceEquals(model, playerDataModel))
					return;
				lock(scoreLock) {
					// Preserve scores recorded after the owner captured the load input.
					prepared.UnionWith(scoresDuringLoad);
					playedMaps = prepared;
				}
				Volatile.Write(ref scoresAvailable, true);
			} finally {
				await IPA.Utilities.UnityGame.SwitchToMainThreadAsync();
				if(version == loadVersion)
					lock(scoreLock)
						scoresDuringLoad = null;
			}
		}

		static void AddScore(string levelId) {
			lock(scoreLock)
				if(playedMaps.Add(levelId))
					scoresDuringLoad?.Add(levelId);
		}

		internal static void Stop() {
			stopping = true;
			++loadVersion;
			lock(scoreLock)
				scoresDuringLoad = null;
		}

		[HarmonyPatch(typeof(PlayerLevelStatsData), nameof(PlayerLevelStatsData.UpdateScoreData))]
		static class InterceptNewScores {
			static void Prefix(PlayerLevelStatsData __instance) {
				// Will become valid after this UpdateScoreData() call
				if(!__instance._validScore)
					AddScore(__instance._levelID);
			}
		}

		public static bool HasLocalScore(string levelId) {
			lock(scoreLock)
				return playedMaps.Contains(levelId);
		}

		public static bool HasLocalScore(BeatmapLevel level) => HasLocalScore(level.levelID);
	}
}
