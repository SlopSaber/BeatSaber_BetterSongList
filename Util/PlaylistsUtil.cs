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

		internal static BeatmapLevelPack GetAvailablePack(string packName, IReadOnlyList<BeatmapLevelPack> availablePacks) {
			if(packName == null)
				return null;
			if(packs != null && packs.TryGetValue(packName, out var cached))
				return cached;
			if(availablePacks == null)
				return null;

			for(var i = 0; i < availablePacks.Count; ++i) {
				var pack = availablePacks[i];
				if(pack.shortPackName == packName)
					return pack;
			}
			return hasPlaylistLib ? GetAvailablePlaylistPack(packName, availablePacks) : null;
		}

		static BeatmapLevelPack GetAvailablePlaylistPack(string packName, IReadOnlyList<BeatmapLevelPack> availablePacks) {
			for(var i = 0; i < availablePacks.Count; ++i) {
				var pack = availablePacks[i];
				if(pack is PlaylistLevelPack && pack.packName == packName)
					return pack;
			}
			return null;
		}

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
					foreach(var x in BeatSaberPlaylistsLib.PlaylistManager.DefaultManager.GetAllPlaylists(true)) {
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
