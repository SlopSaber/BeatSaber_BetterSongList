using BetterSongList.UI;
using BetterSongList.Util;
using HarmonyLib;
using System;
using System.Collections.Generic;
using static SelectLevelCategoryViewController;

namespace BetterSongList.HarmonyPatches {
	[HarmonyPatch(typeof(LevelFilteringNavigationController), nameof(LevelFilteringNavigationController.ShowPacksInSecondChildController))]
	static class PackPreselect {
		public static BeatmapLevelPack restoredPack = null;
		static LevelSelectionFlowCoordinator activeFlow;
		static LevelFilteringNavigationController activeController;
		static string pendingPackName;
		static string initialPackId;
		static bool allowRestoration = true;
		static int presentationDepth;
		static int activationDepth;

		internal static void Begin(LevelSelectionFlowCoordinator flow, bool restore) {
			activeFlow = flow;
			activeController = flow.levelSelectionNavigationController._levelFilteringNavigationController;
			allowRestoration = restore;
			pendingPackName = null;
			initialPackId = null;
			++activationDepth;
		}

		internal static void EndActivation() {
			if(activationDepth > 0)
				--activationDepth;
		}

		internal static void WaitForPack(string packName, BeatmapLevelPack initialPack) {
			pendingPackName = packName;
			initialPackId = initialPack?.packID;
		}

		internal static void CategorySelected(SelectLevelCategoryViewController view) {
			if(activationDepth == 0 && activeController != null && ReferenceEquals(view, activeController._selectLevelCategoryViewController)) {
				pendingPackName = null;
				allowRestoration = true;
			}
		}

		internal static bool KeepPendingPackName(AnnotatedBeatmapLevelCollectionsViewController view) {
			if(activeController == null || !ReferenceEquals(view, activeController._annotatedBeatmapLevelCollectionsViewController))
				return false;
			if(presentationDepth > 0)
				return pendingPackName != null;
			pendingPackName = null;
			allowRestoration = true;
			return false;
		}

		internal static void LevelSelected(LevelCollectionTableView table) {
			if(presentationDepth == 0 && activationDepth == 0 && activeFlow != null &&
				ReferenceEquals(table, activeFlow.levelSelectionNavigationController
					._levelCollectionNavigationController._levelCollectionViewController._levelCollectionTableView))
				pendingPackName = null;
		}

		internal static void Stop(LevelSelectionFlowCoordinator flow = null) {
			if(flow != null && !ReferenceEquals(flow, activeFlow))
				return;
			activeFlow = null;
			activeController = null;
			pendingPackName = null;
			initialPackId = null;
			restoredPack = null;
		}

		internal static BeatmapLevelPack LoadAvailablePack(string name, IReadOnlyList<BeatmapLevelPack> availablePacks) =>
			restoredPack = PlaylistsUtil.GetAvailablePack(name, availablePacks);

		public static void LoadPackFromCollectionName() {
			if(restoredPack?.shortPackName == Config.Instance.LastPack)
				return;

			if(Config.Instance.LastPack == null) {
				restoredPack = null;
				return;
			}

			restoredPack = PlaylistsUtil.GetPack(Config.Instance.LastPack);
		}

		[HarmonyPriority(int.MinValue)]
		static void Prefix(LevelFilteringNavigationController __instance, IReadOnlyList<BeatmapLevelPack> beatmapLevelPacks, ref bool __state) {
			++presentationDepth;
			__state = true;
			var ownsRequest = ReferenceEquals(activeController, __instance);
			if(ownsRequest && !allowRestoration)
				return;
			if(ownsRequest && pendingPackName != null && pendingPackName != Config.Instance.LastPack)
				pendingPackName = null;
			var waiting = ownsRequest && pendingPackName != null;
			if(__instance._levelPackIdToBeSelectedAfterPresent != null &&
				!(waiting && __instance._levelPackIdToBeSelectedAfterPresent == initialPackId)) {
				if(ownsRequest)
					pendingPackName = null;
				return;
			}

			var pack = LoadAvailablePack(waiting ? pendingPackName : Config.Instance.LastPack, beatmapLevelPacks);
			if(pack != null)
				__instance._levelPackIdToBeSelectedAfterPresent = pack.packID;
		}

		static void Finalizer(bool __state) {
			if(__state && presentationDepth > 0)
				--presentationDepth;
		}

		[HarmonyPatch(typeof(AnnotatedBeatmapLevelCollectionsViewController), nameof(AnnotatedBeatmapLevelCollectionsViewController.SetData))]
		static class RestoreWhenPacksArrive {
			[HarmonyPriority(int.MinValue)]
			static void Prefix(AnnotatedBeatmapLevelCollectionsViewController __instance,
				IReadOnlyList<BeatmapLevelPack> annotatedBeatmapLevelCollections, ref int selectedItemIndex, ref bool __state) {
				++presentationDepth;
				__state = true;
				if(activeController == null || pendingPackName == null ||
					!ReferenceEquals(__instance, activeController._annotatedBeatmapLevelCollectionsViewController))
					return;
				if(!activeController.isInViewControllerHierarchy || activeController.selectedLevelCategory != LevelCategory.CustomSongs ||
					pendingPackName != Config.Instance.LastPack) {
					pendingPackName = null;
					return;
				}

				// Catalog publication can supply this list after the initial fallback was shown.
				var pack = LoadAvailablePack(pendingPackName, annotatedBeatmapLevelCollections);
				if(pack == null)
					return;
				var explicitPackId = activeController._levelPackIdToBeSelectedAfterPresent;
				var incomingPackId = selectedItemIndex >= 0 && selectedItemIndex < annotatedBeatmapLevelCollections.Count
					? annotatedBeatmapLevelCollections[selectedItemIndex].packID : null;
				if((explicitPackId != null && explicitPackId != initialPackId && explicitPackId != pack.packID) ||
					(incomingPackId != null && incomingPackId != initialPackId && incomingPackId != pack.packID &&
					 incomingPackId != __instance.selectedAnnotatedBeatmapLevelPack?.packID)) {
					pendingPackName = null;
					return;
				}
				for(var i = 0; i < annotatedBeatmapLevelCollections.Count; ++i) {
					if(annotatedBeatmapLevelCollections[i].packID != pack.packID)
						continue;
					selectedItemIndex = i;
					restoredPack = annotatedBeatmapLevelCollections[i];
					pendingPackName = null;
					initialPackId = null;
					break;
				}
			}

			static void Finalizer(bool __state) => PackPreselect.Finalizer(__state);
		}
	}

	// Animation might get stuck when switching category if it hasn't finished.
	[HarmonyPatch(typeof(LevelFilteringNavigationController), nameof(LevelFilteringNavigationController.HandleSelectLevelCategoryViewControllerDidSelectLevelCategory))]
	static class PackPreselectAnimationFix {
		static void Postfix(LevelFilteringNavigationController __instance) {
			__instance._annotatedBeatmapLevelCollectionsViewController._annotatedBeatmapLevelCollectionsGridView._animator.DespawnAllActiveTweens();
		}
	}

	// For some reason the collection is trying to be closed when it hasn't been opened yet.
	[HarmonyPatch(typeof(AnnotatedBeatmapLevelCollectionsGridView), nameof(AnnotatedBeatmapLevelCollectionsGridView.CloseLevelCollection))]
	static class CloseLevelCollectionFix {
		static bool Prefix(AnnotatedBeatmapLevelCollectionsGridView __instance) => __instance._gridView.columnCount != 0;
	}

	[HarmonyPatch(typeof(LevelSelectionFlowCoordinator), nameof(LevelSelectionFlowCoordinator.DidActivate))]
	static class LevelSelectionFlowCoordinator_DidActivate {
		static void Prefix(LevelSelectionFlowCoordinator __instance, bool addedToHierarchy, bool firstActivation, ref bool __state) {
			if(!addedToHierarchy)
				return;

			if(firstActivation)
				FilterUI.Init();
			PackPreselect.Begin(__instance, __instance._startState == null);
			__state = true;

			if(__instance._startState != null) {
#if DEBUG
				Plugin.Log.Debug("Not restoring last state because we are starting off from somewhere!");
#endif
				FilterUI.SetFilter(null, false, false);
				return;
			}

			if(!Enum.TryParse(Config.Instance.LastCategory, out LevelCategory restoreCategory))
				restoreCategory = LevelCategory.None;

			if(Config.Instance.LastSong == null ||
			   !__instance
			   .levelSelectionNavigationController
			   ._levelFilteringNavigationController
			   ._beatmapLevelsModel
			   ._allLoadedBeatmapLevelsRepository
			   .TryGetBeatmapLevelById(Config.Instance.LastSong, out var lastSelectedLevel)
			)
				lastSelectedLevel = null;

			var packName = Config.Instance.LastPack;
			var pack = PackPreselect.LoadAvailablePack(packName, __instance.levelSelectionNavigationController
				._levelFilteringNavigationController._beatmapLevelsModel._allLoadedBeatmapLevelsRepository.beatmapLevelPacks);
			var waitForPlaylist = pack == null && packName != null && PlaylistsUtil.hasPlaylistLib &&
				restoreCategory == LevelCategory.CustomSongs;

			if(restoreCategory == LevelCategory.All || restoreCategory == LevelCategory.Favorites || 
				(pack == null && restoreCategory == LevelCategory.CustomSongs))
				pack = SongCore.Loader.CustomLevelsPack;
			if(waitForPlaylist)
				PackPreselect.WaitForPack(packName, pack);

			__instance._startState = new LevelSelectionFlowCoordinator.State(
				restoreCategory,
				pack,
				new BeatmapKey(),
				lastSelectedLevel);
		}

		static void Finalizer(bool __state, Exception __exception, LevelSelectionFlowCoordinator __instance) {
			if(__state) {
				PackPreselect.EndActivation();
				if(__exception != null)
					PackPreselect.Stop(__instance);
			}
		}

		[HarmonyPatch(typeof(LevelSelectionFlowCoordinator), nameof(LevelSelectionFlowCoordinator.DidDeactivate))]
		static class ClearRestoreRequest {
			static void Prefix(LevelSelectionFlowCoordinator __instance, bool removedFromHierarchy) {
				if(removedFromHierarchy)
					PackPreselect.Stop(__instance);
			}
		}
	}
}
