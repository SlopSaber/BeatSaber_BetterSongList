using BetterSongList.Util;
using System.Threading;
using System.Threading.Tasks;

namespace BetterSongList.FilterModels {
	public sealed class PlayedFilter : IFilter {
		public bool isReady => LocalScoresUtil.hasScores;

		bool intendedPlayedState = false;

		public PlayedFilter(bool unplayed = false) {
			intendedPlayedState = !unplayed;
		}

		public async Task Prepare(CancellationToken cancelToken) {
			try {
				await LocalScoresUtil.LoadAsync();
			} catch { }
		}

		public string GetUnavailabilityReason() => SongDetailsUtil.GetUnavailabilityReason();

		public bool GetValueFor(BeatmapLevel level) {
			if(!LocalScoresUtil.hasScores)
				return true;

			return LocalScoresUtil.HasLocalScore(level) == intendedPlayedState;
		}
	}
}
