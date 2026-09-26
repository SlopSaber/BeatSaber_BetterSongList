using System;
using System.Collections.Generic;
using System.Linq;

namespace BetterSongList.Util {
	public static class SongListLegendBuilder {
		public static IEnumerable<KeyValuePair<string, int>> BuildFor(BeatmapLevel[] beatmaps, Func<BeatmapLevel, string> displayValueTransformer, int entryLengthLimit = 6, int valueLimit = 28) {
			// Only the first index of each label is used; do not retain every song in a grouping.
			var labels = new HashSet<string>(StringComparer.Ordinal);
			var entries = new List<KeyValuePair<string, int>>();
			for(var i = 0; i < beatmaps.Length; i++) {
				var label = displayValueTransformer(beatmaps[i])?.ToUpperInvariant();
				if(label != null && labels.Add(label))
					entries.Add(new KeyValuePair<string, int>(label, i));
			}

			var amt = Math.Min(valueLimit, entries.Count);

			if(amt <= 1)
				yield break;

			for(var i = 0; i < amt; i++) {
				var bmi = (int)Math.Round(((float)(entries.Count - 1) / (amt - 1)) * i);

				var transformedResult = entries[bmi].Key;

				if(transformedResult.Length > entryLengthLimit)
					transformedResult = transformedResult.Substring(0, entryLengthLimit);

				yield return new KeyValuePair<string, int>(transformedResult, entries[bmi].Value);
			}
		}
	}
}
