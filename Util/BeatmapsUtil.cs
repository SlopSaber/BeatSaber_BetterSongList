namespace BetterSongList.Util {
	static class BeatmapsUtil {

		public static string GetHashOfLevel(BeatmapLevel level) {
			return level == null ? null : GetHashOfLevelId(level.levelID);
		}
		
		public static string GetHashOfBeatmapKey(BeatmapKey key) {
			return GetHashOfLevelId(key.levelId);
		}
		
		private static string GetHashOfLevelId(string id) {
			if(id.Length < 53)
				return null;

			if(id[12] != '_') // custom_level_<hash, 40 chars>
				return null;

			return id.Substring(13, 40);
		}

		public static int GetCharacteristicFromDifficulty(BeatmapKey diff) {
			switch(diff.characteristic) {
				case BeatmapCharacteristic.Standard:
					return 1;
				case BeatmapCharacteristic.OneSaber:
					return 2;
				case BeatmapCharacteristic.NoArrows:
					return 3;
				case BeatmapCharacteristic.Degree90:
					return 4;
				case BeatmapCharacteristic.Degree360:
					return 5;
				default:
					return 0;
			}
		}

		public static string ConcatMappers(string[] allmappers) {
			if(allmappers.Length == 1)
				return allmappers[0];

			return string.Join(" ", allmappers);
		}
	}
}
