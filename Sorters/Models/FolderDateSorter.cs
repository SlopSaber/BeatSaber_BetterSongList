using BetterSongList.Util;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BetterSongList.SortModels {
	public sealed class FolderDateSorter : ISorterWithLegend, ISorterPrimitive {
		public bool isReady => wipTask == null && songTimes != null;

		/*
		 * TODO: For now, I need to use LevelId : int because I have to cast Playlists in LevelCollectionTableSet
		 * once that is gone (Fixed BS Playlist Lib) I can go back to BeatmapLevel : int
		 */
		static ConcurrentDictionary<string, int> songTimes = null;

		static TaskCompletionSource<bool> wipTask = null;
		static bool isLoading = false;
		public Task Prepare(CancellationToken cancelToken) => Prepare(false);
		Task Prepare(bool fullReload) {
			if(songTimes == null) {
				songTimes = new ConcurrentDictionary<string, int>();
				SongCore.Loader.SongsLoadedEvent += (_, _2) => Prepare(false);
			}

			var completion = wipTask ??= new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
			if(SongCore.Loader.AreSongsLoaded && !SongCore.Loader.AreSongsLoading && !isLoading) {
				isLoading = true;
				LoadDates(completion, fullReload);
			}
			return completion.Task;
		}

		static async void LoadDates(TaskCompletionSource<bool> completion, bool fullReload) {
			try {
				// Read Unity-owned repositories on the main thread; only filesystem work runs in the worker.
				var paths = new List<KeyValuePair<string, string>>();
				var repository = SongCore.Loader.BeatmapLevelsModelSO._customLevelsRepository;
				if(repository != null) {
					foreach(var pack in repository.beatmapLevelPacks) {
						if(!(pack is SongCore.OverrideClasses.SongCoreCustomBeatmapLevelPack))
							continue;
						foreach(var song in pack.AllBeatmapLevels()) {
							if(!fullReload && songTimes.ContainsKey(song.levelID))
								continue;
							if(SongCore.Loader.CustomLevelLoader._loadedBeatmapSaveData.TryGetValue(song.levelID, out var saveData)) {
								var folder = saveData.customLevelFolderInfo.folderPath;
								if(!string.IsNullOrEmpty(folder))
									paths.Add(new KeyValuePair<string, string>(song.levelID, Path.Combine(folder, "info.dat")));
							}
						}
					}
				}
				await Task.Run(() => {
					foreach(var entry in paths) {
						try {
							songTimes[entry.Key] = (int)File.GetCreationTimeUtc(entry.Value).ToUnixTime();
						} catch(IOException) {
						} catch(UnauthorizedAccessException) { }
					}
				});
			} catch(Exception ex) {
				Plugin.Log.Warn($"Getting song folder dates failed: {ex}");
			} finally {
				isLoading = false;
				wipTask = null;
				completion.TrySetResult(true);
			}
		}
		public float? GetValueFor(BeatmapLevel level) {
			if(songTimes.TryGetValue(level.levelID, out var oVal))
				return oVal;

			return null;
		}

		const float MONTH_SECS = 1f / (60 * 60 * 24 * 30.4f);

		public static string GetMapAgeMonths(int uploadDateUtc, int curUtc = 0) {
			if(curUtc == 0)
				curUtc = (int)DateTime.UtcNow.ToUnixTime();

			var months = (curUtc - uploadDateUtc) * MONTH_SECS;

			if(months < 1)
				return "<1 M";

			return Math.Round(months) + " M";
		}

		public IEnumerable<KeyValuePair<string, int>> BuildLegend(BeatmapLevel[] levels) {
			var currentUtc = (int)DateTime.UtcNow.ToUnixTime();
			return SongListLegendBuilder.BuildFor(levels, level =>
				songTimes.TryGetValue(level.levelID, out var date) ? GetMapAgeMonths(date, currentUtc) : null);
		}
	}
}
