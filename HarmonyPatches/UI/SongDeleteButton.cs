using BeatSaberMarkupLanguage;
using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.Parser;
using BetterSongList.UI;
using BetterSongList.Util;
using HarmonyLib;
using HMUI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using SongCore;
using UnityEngine;
using UnityEngine.UI;

namespace BetterSongList.HarmonyPatches.UI {
	[HarmonyPatch(typeof(StandardLevelDetailView), nameof(StandardLevelDetailView.RefreshContent))]
	static class SongDeleteButton {
		static Button deleteButton = null;

		static BeatmapLevel lastLevel = null;
		static DeleteConfirmHandler confirmHandler = null;
		static bool deleting;
		static bool stopping;

		static bool isWip => lastLevel != null && lastLevel.levelID.Contains(" WIP");

		public static void UpdateState() {
			if(deleteButton == null)
				return;

			deleteButton.interactable = !stopping && !deleting && lastLevel != null &&
				Loader.CustomLevelLoader != null && Loader.CustomLevelLoader._loadedBeatmapSaveData.ContainsKey(lastLevel.levelID) &&
				(Config.Instance.AllowWipDelete || !isWip);
		}

		internal static void Stop() {
			stopping = true;
			confirmHandler?.Dispose();
			confirmHandler = null;
			lastLevel = null;
			deleteButton = null;
		}

		class DeleteConfirmHandler : IDisposable {
			readonly StandardLevelDetailView owner;
			DeleteRequest pending;
			bool disposed;

			[UIParams] BSMLParserParams parserParams = null;

			public DeleteConfirmHandler(StandardLevelDetailView owner) => this.owner = owner;

			bool IsCurrent => !disposed && !stopping && owner != null && ReferenceEquals(confirmHandler, this);

			public void Dispose() {
				disposed = true;
				pending = null;
				parserParams = null;
			}

			public void ClearPending() => pending = null;

			[UIAction("#post-parse")]
			void PostParse() => parserParams.AddEvent("Close", ClearPending);

			public void ConfirmDelete() {
				if(!IsCurrent || deleting || lastLevel == null || parserParams == null ||
					!ReferenceEquals(owner._beatmapLevel, lastLevel) || (!Config.Instance.AllowWipDelete && isWip))
					return;
				var loader = Loader.Instance;
				var mapLoader = Loader.CustomLevelLoader;
				if(loader == null || mapLoader == null ||
					!mapLoader._loadedBeatmapSaveData.TryGetValue(lastLevel.levelID, out var data) ||
					string.IsNullOrEmpty(data.customLevelFolderInfo.folderPath)) {
					ShowFailure();
					return;
				}
				pending = new DeleteRequest(lastLevel, data.customLevelFolderInfo.folderPath, isWip, loader, mapLoader);
				parserParams.EmitEvent("Show");
			}

			void Confirm() {
				var request = pending;
				pending = null;
				if(!IsCurrent || deleting || request == null ||
					!ReferenceEquals(request.Level, lastLevel) || !ReferenceEquals(owner._beatmapLevel, request.Level) ||
					!ReferenceEquals(request.Loader, Loader.Instance) || !ReferenceEquals(request.MapLoader, Loader.CustomLevelLoader) ||
					(!Config.Instance.AllowWipDelete && request.Wip))
					return;

				if(!request.MapLoader._loadedBeatmapSaveData.TryGetValue(request.LevelId, out var data) ||
					!string.Equals(data.customLevelFolderInfo.folderPath, request.Path, StringComparison.Ordinal)) {
					ShowFailure();
					return;
				}
				deleting = true;
				UpdateState();
				_ = DeleteAsync(request);
			}

			async Task DeleteAsync(DeleteRequest request) {
				try {
					if(request.Wip) {
						var path = Path.GetFullPath(request.Path);
						// Finish recycling before catalog publication so a queued refresh cannot rediscover the map.
						await Task.Run(() => WinApi.DeleteFileOrFolder(path));
						await IPA.Utilities.UnityGame.SwitchToMainThreadAsync();
						if(stopping || !ReferenceEquals(request.Loader, Loader.Instance) ||
							!ReferenceEquals(request.MapLoader, Loader.CustomLevelLoader))
							return;
					}
					await request.Loader.DeleteSongsAsync(new List<string> { request.Path }, !request.Wip);
				} catch(OperationCanceledException) {
					Plugin.Log.Info("Map recycling was cancelled.");
				} catch(Exception ex) {
					await IPA.Utilities.UnityGame.SwitchToMainThreadAsync();
					Plugin.Log.Error($"Deleting map failed: {request.Path}");
					Plugin.Log.Error(ex);
					if(IsCurrent && ReferenceEquals(lastLevel, request.Level))
						ShowFailure();
				} finally {
					await IPA.Utilities.UnityGame.SwitchToMainThreadAsync();
					deleting = false;
					if(!stopping)
						UpdateState();
				}
			}

			void ShowFailure() => FilterUI.persistentNuts.ShowErrorASAP("Deleting the map failed because it failed. Deal with it");

			sealed class DeleteRequest {
				internal readonly BeatmapLevel Level;
				internal readonly string LevelId;
				internal readonly string Path;
				internal readonly bool Wip;
				internal readonly Loader Loader;
				internal readonly CustomLevelLoader MapLoader;

				internal DeleteRequest(BeatmapLevel level, string path, bool wip, Loader loader, CustomLevelLoader mapLoader) {
					Level = level;
					LevelId = level.levelID;
					Path = path;
					Wip = wip;
					Loader = loader;
					MapLoader = mapLoader;
				}
			}
		}

		[HarmonyPriority(int.MinValue)]
		static void Postfix(StandardLevelDetailView __instance) {
			if(stopping)
				return;
			if(deleteButton == null && __instance._practiceButton != null) {
				confirmHandler?.Dispose();
				confirmHandler = new DeleteConfirmHandler(__instance);
				var newButton = GameObject.Instantiate(__instance._practiceButton.gameObject, __instance._practiceButton.transform.parent);
				deleteButton = newButton.GetComponentInChildren<Button>();

				deleteButton.onClick.AddListener(confirmHandler.ConfirmDelete);

				newButton.GetComponentsInChildren<LayoutElement>().Last().minWidth = 12;
				newButton.transform.SetAsFirstSibling();

				var t = newButton.GetComponentInChildren<CurvedTextMeshPro>();

				var iconG = new GameObject("Icon");
				iconG.transform.SetParent(t.transform.parent, false);
				iconG.transform.localScale = new Vector3(0.69f, 0.69f);
				var icon = iconG.AddComponent<ImageView>();

				icon.color = t.color;
				icon._skew = 0.2f;
				icon.material = Resources.FindObjectsOfTypeAll<Material>().FirstOrDefault(m => m.name == "UINoGlow");
				icon.SetImageAsync("#DeleteIcon");

				GameObject.DestroyImmediate(t.gameObject);

				BSMLParser.Instance.Parse(
					Utilities.GetResourceContent(Assembly.GetExecutingAssembly(), "BetterSongList.UI.BSML.SongDeleteConfirm.bsml"),
					__instance.transform.parent.gameObject,
					confirmHandler
				);
			}

			var selected = __instance._beatmapLevel;
			var level = selected != null && !selected.hasPrecalculatedData ? selected : null;
			if(!ReferenceEquals(lastLevel, level))
				confirmHandler?.ClearPending();
			lastLevel = level;

			UpdateState();
		}
	}
}
