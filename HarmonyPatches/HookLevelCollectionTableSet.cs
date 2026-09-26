using BetterSongList.FilterModels;
using BetterSongList.SortModels;
using BetterSongList.UI;
using BetterSongList.Util;
using HarmonyLib;
using HMUI;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace BetterSongList.HarmonyPatches {
	[HarmonyPatch(typeof(LevelCollectionTableView), nameof(LevelCollectionTableView.SetData))]
#if DEBUG
	public
#endif
	static class HookLevelCollectionTableSet {
		public static ISorter sorter;
		public static IFilter filter;
		public static IReadOnlyList<BeatmapLevel> lastInMapList { get; private set; }
		public static IReadOnlyList<BeatmapLevel> lastOutMapList { get; private set; }
		static LevelCollectionTableView lastTable;
		static Action<IReadOnlyList<BeatmapLevel>> recallLast;
		static readonly SemaphoreSlim processingSlot = new SemaphoreSlim(1, 1);
		static CancellationTokenSource refreshSource;
		static bool refreshOnEnable;
		static bool tryReselectLastSelectedLevel;
		static ProcessedList asyncPreprocessed;
		static KeyValuePair<string, int>[] customLegend;

		sealed class ProcessedList {
			public IReadOnlyList<BeatmapLevel> levels;
			public KeyValuePair<string, int>[] legend;
		}

		/// <summary>Refresh the song list using the last input collection.</summary>
		public static void Refresh(bool processAsync = false, bool clearAsyncResult = true) {
			if(lastInMapList == null || lastTable == null)
				return;

			if(clearAsyncResult)
				CancelRefresh();
			if(processAsync) {
				if(!lastTable.isActiveAndEnabled) {
					refreshOnEnable = true;
					return;
				}
				RefreshAsync();
				return;
			}

			var levels = lastInMapList;
			lastInMapList = null;
			recallLast(levels);
		}

		static void SetLoading(bool loading) =>
			XD.FunnyNull(FilterUI.persistentNuts._filterLoadingIndicator)?.gameObject.SetActive(loading);

		static void CancelRefresh() {
			var previous = refreshSource;
			refreshSource = null;
			asyncPreprocessed = null;
			previous?.Cancel();
			SetLoading(false);
		}

		static async void RefreshAsync() {
			refreshOnEnable = false;
			var source = refreshSource = new CancellationTokenSource();
			var token = source.Token;
			var table = lastTable;
			var input = lastInMapList;
			var selectedFilter = filter;
			var selectedSorter = sorter;
			var ascending = Config.Instance.SortAsc;
			var buildLegend = Config.Instance.EnableAlphabetScrollbar;
			SetLoading(true);
			try {
				// Let the current SetData call finish before publishing any completed work.
				await Task.Yield();
				token.ThrowIfCancellationRequested();
				var preparation = Task.WhenAll(
					selectedSorter?.isReady == false ? selectedSorter.Prepare(token) : Task.CompletedTask,
					selectedFilter?.isReady == false ? selectedFilter.Prepare(token) : Task.CompletedTask);
				await AwaitPreparation(preparation, token);
				await processingSlot.WaitAsync(token);
				ProcessedList result;
				try {
					token.ThrowIfCancellationRequested();
					var snapshot = input.ToArray();
					result = await Task.Run(() => Process(snapshot, selectedFilter, selectedSorter, ascending, buildLegend, token), token);
				} finally {
					processingSlot.Release();
				}

				// A later selection or a disabled/destroyed menu must never receive this result.
				if(token.IsCancellationRequested || refreshSource != source || table == null ||
					!table.isActiveAndEnabled || table != lastTable || input != lastInMapList)
					return;

				asyncPreprocessed = result;
				Refresh(false, false);
			} catch(OperationCanceledException) when(token.IsCancellationRequested) {
			} catch(Exception ex) {
				Plugin.Log.Warn($"Refreshing the song list failed: {ex}");
			} finally {
				if(refreshSource == source) {
					refreshSource = null;
					SetLoading(false);
				}
				source.Dispose();
			}
		}

		static async Task AwaitPreparation(Task preparation, CancellationToken token) {
			// Some shared providers ignore cancellation. Release this request while their cache finishes.
			var canceled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
			using(token.Register(() => canceled.TrySetResult(true))) {
				if(await Task.WhenAny(preparation, canceled.Task) != preparation) {
					_ = preparation.ContinueWith(task => { _ = task.Exception; }, CancellationToken.None,
						TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
					token.ThrowIfCancellationRequested();
				}
				await preparation;
				token.ThrowIfCancellationRequested();
			}
		}

		static ProcessedList Process(IReadOnlyList<BeatmapLevel> input, IFilter selectedFilter,
			ISorter selectedSorter, bool ascending, bool buildLegend, CancellationToken token) {
			var result = new ProcessedList { levels = input };
			if(selectedFilter?.isReady != true && selectedSorter?.isReady != true)
				return result;
			try {
				var levels = input.Where(level => {
					token.ThrowIfCancellationRequested();
					return true;
				});
				if(selectedFilter?.isReady == true)
					levels = levels.Where(selectedFilter.GetValueFor);
				if(selectedSorter?.isReady == true) {
					if(selectedSorter is ISorterCustom customSorter) {
						customSorter.DoSort(ref levels, ascending);
					} else {
						var primitive = (ISorterPrimitive)selectedSorter;
						float? GetValue(BeatmapLevel level) {
							token.ThrowIfCancellationRequested();
							return primitive.GetValueFor(level);
						}
						levels = ascending
							? levels.OrderBy(level => GetValue(level) ?? float.MaxValue)
							: levels.OrderByDescending(level => GetValue(level) ?? float.MinValue);
					}
				}
				var processed = levels.ToArray();
				token.ThrowIfCancellationRequested();
				result.levels = processed;
				if(selectedSorter?.isReady == true && selectedSorter is ISorterWithLegend legendSorter && buildLegend)
					result.legend = legendSorter.BuildLegend(processed)?.ToArray();
				token.ThrowIfCancellationRequested();
			} catch(OperationCanceledException) when(token.IsCancellationRequested) {
				throw;
			} catch(Exception ex) {
				Plugin.Log.Warn($"Filtering or sorting the song list failed: {ex}");
			}
			return result;
		}

		[HarmonyPriority(int.MaxValue)]
		static void Prefix(LevelCollectionTableView __instance, ref IReadOnlyList<BeatmapLevel> beatmapLevels,
			HashSet<string> favoriteLevelIds, ref bool beatmapLevelsAreSorted, bool sortBeatmapLevels) {
			// Keep playlist wrappers so duplicate entries retain their identity.
			if(HookSelectedCollection.lastSelectedCollection != null && PlaylistsUtil.hasPlaylistLib)
				beatmapLevels = PlaylistsUtil.GetLevelsForLevelCollection(HookSelectedCollection.lastSelectedCollection) ?? beatmapLevels;

			var sameInput = __instance == lastTable && beatmapLevels == lastInMapList;
			var preprocessed = asyncPreprocessed;
			if(!sameInput && preprocessed == null)
				CancelRefresh();

			lastTable = __instance;
			lastInMapList = beatmapLevels;
			var originallySorted = beatmapLevelsAreSorted;
			recallLast = levels => {
				if(__instance == null)
					return;
				tryReselectLastSelectedLevel = true;
				__instance.SetData(levels, favoriteLevelIds, originallySorted, sortBeatmapLevels);
			};

			if(sorter?.isReady == true)
				beatmapLevelsAreSorted = false;
			if(sameInput && lastOutMapList != null) {
				beatmapLevels = lastOutMapList;
				return;
			}

			if(preprocessed != null) {
				asyncPreprocessed = null;
				beatmapLevels = preprocessed.levels;
				customLegend = preprocessed.legend;
				return;
			}

			if(sorter?.isReady == false || filter?.isReady == false)
				Refresh(true);
			var result = Process(beatmapLevels, filter, sorter, Config.Instance.SortAsc,
				Config.Instance.EnableAlphabetScrollbar, CancellationToken.None);
			beatmapLevels = result.levels;
			customLegend = result.legend;
		}

		internal static void Suspend(LevelCollectionTableView table) {
			if(table != lastTable)
				return;
			refreshOnEnable |= refreshSource != null;
			CancelRefresh();
		}

		internal static void Resume(LevelCollectionTableView table) {
			if(table == lastTable && refreshOnEnable) {
				refreshOnEnable = false;
				Refresh(true);
			}
		}

		internal static void Release(LevelCollectionTableView table) {
			if(table != lastTable)
				return;
			CancelRefresh();
			lastTable = null;
			lastInMapList = lastOutMapList = null;
			recallLast = null;
			customLegend = null;
			refreshOnEnable = false;
		}

		static IEnumerator TryReselectLastSelectedSong(LevelCollectionTableView table) {
			yield return null;
			if(table == null || !table.isActiveAndEnabled || table != lastTable || (lastOutMapList?.Count ?? 0) == 0)
				yield break;
			var index = Math.Max(0, lastOutMapList.FindIndex(level => level.levelID == Config.Instance.LastSong) + (table._showLevelPackHeader ? 1 : 0));
			table._selectedRow = index;
			table._tableView.SelectCellWithIdx(index, false);
			table._tableView.ScrollToCellWithIdx(index, TableView.ScrollPositionType.Center, false);
		}

		static void Postfix(LevelCollectionTableView __instance, IReadOnlyList<BeatmapLevel> beatmapLevels) {
			lastOutMapList = beatmapLevels;
			if(tryReselectLastSelectedLevel) {
				SharedCoroutineStarter.instance.StartCoroutine(TryReselectLastSelectedSong(__instance));
				tryReselectLastSelectedLevel = false;
			}
			if(customLegend == null || customLegend.Length == 0) {
				if(beatmapLevels.Count == 0)
					__instance._alphabetScrollbar.gameObject.SetActive(false);
				return;
			}
			__instance._alphabetScrollbar.SetData(customLegend.Select(entry => new AlphabetScrollInfo.Data('?', entry.Value)).ToArray());
			for(var i = customLegend.Length; i-- != 0;)
				__instance._alphabetScrollbar._texts[i].text = customLegend[i].Key;
			((RectTransform)__instance._tableView.transform).offsetMin = new Vector2(((RectTransform)__instance._alphabetScrollbar.transform).rect.size.x + 1f, 0f);
			__instance._alphabetScrollbar.gameObject.SetActive(true);
		}
	}

	[HarmonyPatch(typeof(LevelCollectionTableView))]
	static class HookLevelCollectionTableLifetime {
		[HarmonyPostfix, HarmonyPatch("OnDisable")]
		static void OnDisable(LevelCollectionTableView __instance) => HookLevelCollectionTableSet.Suspend(__instance);
		[HarmonyPostfix, HarmonyPatch("OnEnable")]
		static void OnEnable(LevelCollectionTableView __instance) => HookLevelCollectionTableSet.Resume(__instance);
		[HarmonyPostfix, HarmonyPatch("OnDestroy")]
		static void OnDestroy(LevelCollectionTableView __instance) => HookLevelCollectionTableSet.Release(__instance);
	}
}
