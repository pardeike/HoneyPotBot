using System.Collections.Concurrent;

namespace HoneyPotBot;

public class MessageTracker(int deltaInterval, int minMsgLength, bool linkRequired, double similarityThreshold, string honeypotChannelName)
{
	private readonly ConcurrentDictionary<ulong, List<TrackedMessage>> _userMessages = new();
	private readonly ConcurrentDictionary<ulong, DateTimeOffset> _honeypotDetectedUsers = new();
	private readonly int _deltaInterval = deltaInterval;
	private readonly int _minMsgLength = minMsgLength;
	private readonly bool _linkRequired = linkRequired;
	private readonly double _similarityThreshold = similarityThreshold;
	private readonly string _honeypotChannelName = honeypotChannelName;

	public bool ShouldTrackMessage(string content)
	{
		if (content.Length < _minMsgLength)
			return false;

		if (_linkRequired && !ContainsLink(content))
			return false;

		return true;
	}

	public void AddMessage(ulong userId, ulong channelId, string content, DateTimeOffset timestamp)
	{
		PurgeOldMessages(userId, timestamp);

		var messages = _userMessages.GetOrAdd(userId, _ => []);
		lock (messages)
		{
			messages.Add(new TrackedMessage(channelId, content, timestamp));
		}
	}

	public (bool isDuplicate, ulong firstChannelId) CheckForDuplicate(ulong userId, ulong currentChannelId, string content, DateTimeOffset timestamp)
	{
		if (!_userMessages.TryGetValue(userId, out var messages))
			return (false, 0);

		lock (messages)
		{
			foreach (var msg in messages)
			{
				if (msg.ChannelId == currentChannelId)
					continue;

				if ((timestamp - msg.Timestamp).TotalSeconds > _deltaInterval)
					continue;

				if (AreSimilar(content, msg.Content))
					return (true, msg.ChannelId);
			}
		}

		return (false, 0);
	}

	public (SpamDetectionResult result, string reason, ulong channelId) CheckMessage(string channelName, ulong userId, ulong channelId, string content, DateTimeOffset timestamp)
	{
		if (content == null)
			return (SpamDetectionResult.Ignored, "Message content is null", 0);

		CleanupHoneypotDetections(timestamp);

		if (_honeypotDetectedUsers.TryGetValue(userId, out var detectionTime))
		{
			if ((timestamp - detectionTime).TotalSeconds <= _deltaInterval)
				return (SpamDetectionResult.HoneypotDetected, "User previously posted in honeypot channel", 0);
		}

		if (channelName == _honeypotChannelName)
		{
			_honeypotDetectedUsers[userId] = timestamp;
			return (SpamDetectionResult.HoneypotTriggered, $"Posted in honeypot channel '{_honeypotChannelName}'", 0);
		}

		if (!ShouldTrackMessage(content))
			return (SpamDetectionResult.Ignored, "Message doesn't meet tracking criteria", 0);

		var (isDuplicate, firstChannelId) = CheckForDuplicate(userId, channelId, content, timestamp);

		if (isDuplicate)
			return (SpamDetectionResult.DuplicateDetected, $"Similar message previously posted in channel {firstChannelId}", firstChannelId);

		AddMessage(userId, channelId, content, timestamp);

		return (SpamDetectionResult.Clean, "Message is clean", 0);
	}

	private void PurgeOldMessages(ulong userId, DateTimeOffset currentTime)
	{
		if (!_userMessages.TryGetValue(userId, out var messages))
			return;

		lock (messages)
		{
			_ = messages.RemoveAll(m => (currentTime - m.Timestamp).TotalSeconds > _deltaInterval);

			if (messages.Count == 0)
				_ = _userMessages.TryRemove(userId, out _);
		}
	}

	private void CleanupHoneypotDetections(DateTimeOffset currentTime)
	{
		var expiredUsers = _honeypotDetectedUsers
			.Where(kvp => (currentTime - kvp.Value).TotalSeconds > _deltaInterval)
			.Select(kvp => kvp.Key)
			.ToArray();

		foreach (var userId in expiredUsers)
		{
			_honeypotDetectedUsers.TryRemove(userId, out _);
		}
	}

	public void PerformPeriodicCleanup(DateTimeOffset currentTime)
	{
		CleanupHoneypotDetections(currentTime);

		var userIds = _userMessages.Keys.ToArray();
		foreach (var userId in userIds)
		{
			PurgeOldMessages(userId, currentTime);
		}
	}

	private static bool ContainsLink(string content)
	{
		if (content.Contains("http://", StringComparison.OrdinalIgnoreCase) ||
			 content.Contains("https://", StringComparison.OrdinalIgnoreCase))
			return true;

		if (content.Contains("www.", StringComparison.OrdinalIgnoreCase))
		{
			var wwwIndex = content.IndexOf("www.", StringComparison.OrdinalIgnoreCase);
			if (wwwIndex + 4 < content.Length && content.IndexOf('.', wwwIndex + 4) > wwwIndex)
				return true;
		}

		if (content.Contains("](") && content.Contains("["))
		{
			var markdownPattern = System.Text.RegularExpressions.Regex.Match(content, @"\[.+?\]\(.+?\)");
			if (markdownPattern.Success)
				return true;
		}

		var urlPattern = System.Text.RegularExpressions.Regex.Match(content, @"\b[a-z0-9-]+\.[a-z]{2,}(/\S*)?\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
		if (urlPattern.Success)
		{
			var tld = urlPattern.Value.Split('/')[0].Split('.').Last().ToLower();
			var commonTlds = new[] { "com", "org", "net", "io", "co", "ly", "me", "gg", "tv", "dev", "app", "ai" };
			if (commonTlds.Contains(tld))
				return true;
		}

		return false;
	}

	private bool AreSimilar(string text1, string text2)
	{
		var normalized1 = NormalizeText(text1);
		var normalized2 = NormalizeText(text2);

		var distance = LevenshteinDistance(normalized1, normalized2);
		var maxLength = Math.Max(normalized1.Length, normalized2.Length);

		if (maxLength == 0)
			return true;

		var similarity = 1.0 - (double)distance / maxLength;
		return similarity >= _similarityThreshold;
	}

	private static string NormalizeText(string text) => text.ToLowerInvariant().Trim();

	private static int LevenshteinDistance(string s1, string s2)
	{
		var len1 = s1.Length;
		var len2 = s2.Length;

		if (len1 == 0) return len2;
		if (len2 == 0) return len1;

		const int maxComparisonLength = 500;
		if (len1 > maxComparisonLength)
		{
			s1 = s1.Substring(0, maxComparisonLength);
			len1 = maxComparisonLength;
		}
		if (len2 > maxComparisonLength)
		{
			s2 = s2.Substring(0, maxComparisonLength);
			len2 = maxComparisonLength;
		}

		var d = new int[len1 + 1, len2 + 1];

		for (var i = 0; i <= len1; i++)
			d[i, 0] = i;

		for (var j = 0; j <= len2; j++)
			d[0, j] = j;

		for (var i = 1; i <= len1; i++)
		{
			for (var j = 1; j <= len2; j++)
			{
				var cost = s1[i - 1] == s2[j - 1] ? 0 : 1;
				d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
			}
		}

		return d[len1, len2];
	}

	private record TrackedMessage(ulong ChannelId, string Content, DateTimeOffset Timestamp);
}

public enum SpamDetectionResult
{
	Clean,
	Ignored,
	HoneypotTriggered,
	HoneypotDetected,
	DuplicateDetected
}
