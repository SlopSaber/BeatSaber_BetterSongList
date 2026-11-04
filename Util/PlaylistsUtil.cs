using System.Collections.Generic;
using System.Linq;
using BeatSaberPlaylistsLib.Types;

namespace BetterSongList.Util {
	public static class PlaylistsUtil {
		public static bool hasPlaylistLib = false;

		public static void Init() {
			hasPlaylistLib = IPA.Loader.PluginManager.GetPluginFromId("BeatSaberPlaylistsLib") != null;
		}

		public static Dictionary<string, BeatmapLevelPack> packs = null;

		static string PackNameFromTitle(string title) =>
			title.IndexOfAny(new[] { '\t', '\n', '\r' }) < 0 ? title : title.Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ');

		public static BeatmapLevelPack GetPack(string packName) {
			if(packName == null)
				return null;

			if(packs == null) {
				packs =
					SongCore.Loader.BeatmapLevelsModelSO?._allLoadedBeatmapLevelsRepository.beatmapLevelPacks
					// There shouldnt be any duplicate name basegame playlists... But better be safe
					.GroupBy(x => x.shortPackName)
					.Select(x => x.First())
					.ToDictionary(x => x.shortPackName, x => x);
			}

			if(packs.TryGetValue(packName, out var p)) {
				return p;
			} else if(hasPlaylistLib) {
				BeatmapLevelPack wrapper() {
					if(!SongCore.Loader.AreSongsLoaded)
						return null;
					// Building a level pack resolves every song and requests the cover, so match on the title first.
					foreach(var x in BeatSaberPlaylistsLib.PlaylistManager.DefaultManager.GetAllPlaylists(true)) {
						var title = x.Title;
						if(title == null || PackNameFromTitle(title) != packName)
							continue;
						var playlistLevelPack = x.PlaylistLevelPack;
						if(playlistLevelPack.packName == packName)
							return playlistLevelPack;
					}
					return null;
				}
				return wrapper();
			}
			return null;
		}

		public static BeatmapLevel[] GetLevelsForLevelCollection(BeatmapLevelPack levelCollection) {
			if(levelCollection is PlaylistLevelPack playlist)
				return playlist.playlist.BeatmapLevels;
			return null;
		}
	}
}
