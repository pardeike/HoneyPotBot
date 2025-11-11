using Discord.WebSocket;
using System.Collections.Concurrent;

namespace HoneyPotBot;

public class MessageTracker
{
	private readonly ConcurrentDictionary<ulong, List<TrackedMessage>> _userMessages = new();
	private readonly ConcurrentDictionary<ulong, DateTimeOffset> _honeypotDetectedUsers = new();
	private readonly int _deltaInterval;
	private readonly int _minMsgLength;
	private readonly bool _linkRequired;
	private readonly double _similarityThreshold;
	private readonly string _honeypotChannelName;
	private readonly object _cleanupLock = new();

	public MessageTracker(int deltaInterval, int minMsgLength, bool linkRequired, double similarityThreshold, string honeypotChannelName)
	{
		_deltaInterval = deltaInterval;
		_minMsgLength = minMsgLength;
		_linkRequired = linkRequired;
		_similarityThreshold = similarityThreshold;
		_honeypotChannelName = honeypotChannelName;
	}

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
				// Skip messages from the same channel
				if (msg.ChannelId == currentChannelId)
					continue;

				// Check if message is within the time window
				if ((timestamp - msg.Timestamp).TotalSeconds > _deltaInterval)
					continue;

				// Check similarity
				if (AreSimilar(content, msg.Content))
					return (true, msg.ChannelId);
			}
		}

		return (false, 0);
	}

	public (SpamDetectionResult result, string reason, ulong channelId) CheckMessage(string channelName, ulong userId, ulong channelId, string content, DateTimeOffset timestamp)
	{
		// Clean up old honeypot detections
		CleanupHoneypotDetections(timestamp);

		// Check if user was previously detected in honeypot channel
		if (_honeypotDetectedUsers.TryGetValue(userId, out var detectionTime))
		{
			// If still within the tracking window, unconditionally flag as spam
			if ((timestamp - detectionTime).TotalSeconds <= _deltaInterval)
				return (SpamDetectionResult.HoneypotDetected, "User previously posted in honeypot channel", 0);
		}

		// Check if message is in honeypot channel
		if (channelName == _honeypotChannelName)
		{
			// Mark user as honeypot-detected for future messages
			_honeypotDetectedUsers[userId] = timestamp;
			return (SpamDetectionResult.HoneypotTriggered, $"Posted in honeypot channel '{_honeypotChannelName}'", 0);
		}

		// Check for cross-channel spam detection
		if (!ShouldTrackMessage(content))
			return (SpamDetectionResult.Ignored, "Message doesn't meet tracking criteria", 0);

		var (isDuplicate, firstChannelId) = CheckForDuplicate(userId, channelId, content, timestamp);

		if (isDuplicate)
			return (SpamDetectionResult.DuplicateDetected, $"Similar message previously posted in channel {firstChannelId}", firstChannelId);

		// Add message to tracker for future comparison
		AddMessage(userId, channelId, content, timestamp);

		return (SpamDetectionResult.Clean, "Message is clean", 0);
	}

	private void PurgeOldMessages(ulong userId, DateTimeOffset currentTime)
	{
		if (!_userMessages.TryGetValue(userId, out var messages))
			return;

		lock (messages)
		{
			messages.RemoveAll(m => (currentTime - m.Timestamp).TotalSeconds > _deltaInterval);

			// Clean up empty user entries
			if (messages.Count == 0)
			{
				lock (_cleanupLock)
				{
					// Double-check after acquiring lock
					if (messages.Count == 0)
						_userMessages.TryRemove(userId, out _);
				}
			}
		}
	}

	private void CleanupHoneypotDetections(DateTimeOffset currentTime)
	{
		var usersToRemove = new List<ulong>();

		foreach (var kvp in _honeypotDetectedUsers)
		{
			if ((currentTime - kvp.Value).TotalSeconds > _deltaInterval)
				usersToRemove.Add(kvp.Key);
		}

		foreach (var userId in usersToRemove)
		{
			_honeypotDetectedUsers.TryRemove(userId, out _);
		}
	}

	private static bool ContainsLink(string content)
	{
		return content.Contains("http://", StringComparison.OrdinalIgnoreCase) ||
				 content.Contains("https://", StringComparison.OrdinalIgnoreCase) ||
				 content.Contains("www.", StringComparison.OrdinalIgnoreCase);
	}

	private bool AreSimilar(string text1, string text2)
	{
		// Normalize texts for comparison
		var normalized1 = NormalizeText(text1);
		var normalized2 = NormalizeText(text2);

		// Calculate Levenshtein distance-based similarity
		var distance = LevenshteinDistance(normalized1, normalized2);
		var maxLength = Math.Max(normalized1.Length, normalized2.Length);

		if (maxLength == 0)
			return true;

		var similarity = 1.0 - (double)distance / maxLength;
		return similarity >= _similarityThreshold;
	}

	private static string NormalizeText(string text)
	{
		// Convert to lowercase and trim whitespace
		return text.ToLowerInvariant().Trim();
	}

	private static int LevenshteinDistance(string s1, string s2)
	{
		var len1 = s1.Length;
		var len2 = s2.Length;

		if (len1 == 0) return len2;
		if (len2 == 0) return len1;

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
