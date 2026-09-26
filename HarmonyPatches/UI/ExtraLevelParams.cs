using BeatSaberMarkupLanguage;
using BetterSongList.Util;
using HarmonyLib;
using HMUI;
using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using IPA.Utilities;
using TMPro;
using UnityEngine;
using System.Reflection;
using System.Numerics;
using Vector3 = UnityEngine.Vector3;

namespace BetterSongList.HarmonyPatches.UI {
	[HarmonyPatch(typeof(StandardLevelDetailView), nameof(StandardLevelDetailView.RefreshContent))]
	static class ExtraLevelParams {
		static GameObject extraUI = null;
		static TextMeshProUGUI[] fields = null;

		static HoverHintController hhc = null;
		static Sprite favIcon = null;
		static Task<Sprite> favoriteIconLoad;
		static Coroutine fieldSetup;
		static string appliedLeaderboard;
		static string appliedMiscSetting;
		static bool waitingForSongDetails;

		static async void SetFavoriteIcon(ImageView icon) {
			try {
				if(favIcon == null)
					favIcon = await (favoriteIconLoad ??= Utilities.LoadSpriteFromAssemblyAsync("BetterSongList.UI.FavoritesIcon.png"));
				if(icon != null)
					icon.sprite = favIcon;
			} catch(Exception ex) {
				Plugin.Log.Warn($"Loading the favorites icon failed: {ex}");
			}
		}

		static async void RefreshAfterSongDetails() {
			waitingForSongDetails = true;
			try {
				if(await SongDetailsUtil.TryGet() != null)
					UpdateState();
			} catch(Exception ex) {
				Plugin.Log.Warn($"Refreshing song details failed: {ex}");
			} finally {
				waitingForSongDetails = false;
			}
		}

		static IEnumerator ProcessFields(TextMeshProUGUI[] viewFields) {
			//Need to wait until the end of frame for reasons beyond my understanding
			yield return new WaitForEndOfFrame();
			fieldSetup = null;
			if(extraUI == null || viewFields != fields)
				yield break;
			var leaderboard = Config.Instance.PreferredLeaderboard;
			var miscSetting = Config.Instance.PreferredMiscSetting;

			static void ModifyValue(TextMeshProUGUI text, string hoverHint, string iconName) {
				var icon = text.transform.parent.Find("Icon").GetComponent<ImageView>();

				if(iconName == "Favorites") {
					SetFavoriteIcon(icon);
				} else {
					icon.SetImageAsync($"#{iconName}Icon");
				}

				GameObject.DestroyImmediate(text.GetComponentInParent<LocalizedHoverHint>());
				var hhint = text.GetComponentInParent<HoverHint>();

				if(hhint == null)
					return;

				if(hhc == null)
					hhc = UnityEngine.Object.FindFirstObjectByType<HoverHintController>();

				// Normally zenjected, not here obviously. I dont think the Controller is ever destroyed so we dont need to explicit null check
				ReflectionUtil.SetField(hhint, "_hoverHintController", hhc);
				hhint.text = hoverHint;
			}

			if(leaderboard == "ScoreSaber") {
				ModifyValue(fields[0], "ScoreSaber PP Value", "Difficulty");
			} else {
				if(leaderboard == "BeatLeader")
					ModifyValue(fields[0], "BeatLeader PP Value", "Difficulty");
			}
			if(leaderboard == "ScoreSaber") {
				ModifyValue(fields[1], "ScoreSaber Star Rating", "Favorites");
			} else {
				if(leaderboard == "BeatLeader")
					ModifyValue(fields[1], "BeatLeader Star Rating", "Favorites");
			}
			if(miscSetting == "Reaction Time") {
				ModifyValue(fields[2], "RT (Reaction Time)", "Clock");
			} else if(miscSetting == "Jump Distance") {
				ModifyValue(fields[2], "JD (Jump Distance)", "Measure");
			} else {
				if(miscSetting == "Map Age")
					ModifyValue(fields[2], "BeatSaver upload age (Months)", "Clock");
			}
			ModifyValue(fields[3], "NJS (Note Jump Speed)", "FastNotes");

			fields[0].richText = true;
			fields[0].characterSpacing = -3f;
			appliedLeaderboard = leaderboard;
			appliedMiscSetting = miscSetting;
		}

		static StandardLevelDetailView lastInstance = null;

		public static void UpdateState() {
			if(lastInstance != null && lastInstance.isActiveAndEnabled)
				lastInstance.RefreshContent();
		}

		static void Postfix(StandardLevelDetailView __instance) {
			if(extraUI == null) {
				// I wanted to make a custom UI for this with bsml first... But this is MUCH easier and probably looks better
				extraUI = GameObject.Instantiate(__instance._levelParamsPanel, __instance._levelParamsPanel.transform.parent).gameObject;
				extraUI.GetComponentInChildren<CanvasGroup>().alpha = 1;

				GameObject.Destroy(extraUI.GetComponent<LevelParamsPanel>());

				__instance._levelParamsPanel.transform.localPosition += new Vector3(0, 3.5f);
				extraUI.transform.localPosition -= new Vector3(0, 1f);

				fields = extraUI.GetComponentsInChildren<CurvedTextMeshPro>();
				appliedLeaderboard = appliedMiscSetting = null;
				if(fieldSetup != null)
					SharedCoroutineStarter.instance.StopCoroutine(fieldSetup);
				fieldSetup = null;
			}

			lastInstance = __instance;

			if(fields != null) {
				var beatmapKey = __instance.beatmapKey;

				if(!SongDetailsUtil.isAvailable) {
					fields[0].text = fields[1].text = "N/A";
				} else if(SongDetailsUtil.songDetails != null) {
					void wrapper() {
						// For now we can assume non-standard diff is unranked. Probably not changing any time soon i guess
						var ch = (SongDetailsCache.Structs.MapCharacteristic)BeatmapsUtil.GetCharacteristicFromDifficulty(beatmapKey);

						var mh = BeatmapsUtil.GetHashOfLevel(__instance._beatmapLevel);

						if(mh == null ||
							!SongDetailsUtil.songDetails.instance.songs.FindByHash(mh, out var song) ||
							!song.GetDifficulty(
								out var diff,
								(SongDetailsCache.Structs.MapDifficulty)beatmapKey.difficulty,
								ch
							)
						) {
							fields[0].text = fields[1].text = fields[3].text = "?";
							return;
						} else {
							var isSs = Config.Instance.PreferredLeaderboard == "ScoreSaber";
							float stars = isSs ? diff.stars : diff.starsBeatleader;

							if(stars <= 0) {
								fields[0].text = fields[1].text = "-";
							} else if(isSs) {
								var acc = .984f - (Math.Max(0, (diff.stars - 1.5f) / (12.5f - 1.5f) / Config.Instance.AccuracyMultiplier) * .027f);
								//acc *= 1 - ((1 - Config.Instance.AccuracyMultiplier) * 0.5f);
								var pp = PPUtil.PPPercentage(acc) * diff.stars * 42.1f;

								fields[0].text = string.Format("{0:0} <size=2.5>({1:0.0%})</size>", pp, acc);
								fields[1].text = diff.stars.ToString("0.0#");
							} else {
								fields[0].text = "?";
								fields[1].text = diff.starsBeatleader.ToString("0.0#");
							}
						}

						var basicData = __instance._beatmapLevel.GetDifficultyBeatmapData(beatmapKey.characteristic, beatmapKey.difficulty);
						var njs = basicData?.noteJumpMovementSpeed ?? 0;
						if(njs == 0)
							njs = BeatmapDifficultyMethods.NoteJumpMovementSpeed(beatmapKey.difficulty, 0, true);

						float JD = JumpDistanceCalculator.GetJd(__instance._beatmapLevel.beatsPerMinute, njs, basicData?.noteJumpStartBeatOffset ?? 0);
						float RT = JumpDistanceCalculator.GetRt(__instance._beatmapLevel.beatsPerMinute, njs, basicData?.noteJumpStartBeatOffset ?? 0);

						if(Config.Instance.PreferredMiscSetting == "Reaction Time") {
							fields[2].text = ((int)RT).ToString() + " ms";
						} else if(Config.Instance.PreferredMiscSetting == "Jump Distance") {
							fields[2].text = JD.ToString("F1");
						} else if(Config.Instance.PreferredMiscSetting == "Map Age") {
							fields[2].text = SortModels.FolderDateSorter.GetMapAgeMonths((int)song.uploadTimeUnix);
						}
						fields[3].text = njs.ToString("0.0#");
					}
					wrapper();
				} else if(!SongDetailsUtil.finishedInitAttempt && !waitingForSongDetails) {
					RefreshAfterSongDetails();
				}

				if(fieldSetup == null && (appliedLeaderboard != Config.Instance.PreferredLeaderboard ||
					appliedMiscSetting != Config.Instance.PreferredMiscSetting))
					fieldSetup = SharedCoroutineStarter.instance.StartCoroutine(ProcessFields(fields));
			}
		}
	}
}
