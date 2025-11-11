using Discord;
using Discord.WebSocket;
using HoneyPotBot;
using Microsoft.Extensions.Logging;

var logFormat = Config.Get("LOG_FORMAT") ?? "text";
var logLevel = Enum.TryParse<LogLevel>(Config.Get("LOG_LEVEL"), out var level) ? level : LogLevel.Information;
var channelName = Config.Get("CHANNEL_NAME") ?? "intro";
var pastMsgInterval = int.TryParse(Config.Get("PAST_MSG_INTERVAL"), out var past) ? past : 300;
var futureMsgInterval = int.TryParse(Config.Get("FUTURE_MSG_INTERVAL"), out var future) ? future : 300;
var msgDeltaInterval = int.TryParse(Config.Get("MSG_DELTA_INTERVAL"), out var delta) ? delta : 120;
var minMsgLength = int.TryParse(Config.Get("MIN_MSG_LENGTH"), out var minLen) ? minLen : 40;
var linkRequired = bool.TryParse(Config.Get("LINK_REQUIRED"), out var linkReq) ? linkReq : true;
var msgSimilarityThreshold = double.TryParse(Config.Get("MSG_SIMILARITY_THRESHOLD"), out var simThresh) ? simThresh : 0.85;

using var loggerFactory = LoggerFactory.Create(builder =>
{
	_ = builder.SetMinimumLevel(logLevel);
	if (logFormat == "json")
		_ = builder.AddJsonConsole();
	else
		_ = builder.AddSimpleConsole(options =>
		{
			options.SingleLine = true;
			options.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
		});
});

var logger = loggerFactory.CreateLogger("HoneyPotBot");

var token = Config.Get("HONEYPOTBOT_TOKEN");
if (string.IsNullOrEmpty(token))
{
	logger.LogCritical("HONEYPOTBOT_TOKEN not found in ~/.api-keys configuration file");
	return;
}

logger.LogInformation("Starting HoneyPotBot on channel {ChannelName} (-{PastInterval}s / {FutureInterval}s)", channelName, pastMsgInterval, futureMsgInterval);
logger.LogInformation("Cross-channel spam detection enabled: delta={DeltaInterval}s, minLength={MinLength}, linkRequired={LinkRequired}, similarity={Similarity}", msgDeltaInterval, minMsgLength, linkRequired, msgSimilarityThreshold);

var messageTracker = new MessageTracker(msgDeltaInterval, minMsgLength, linkRequired, msgSimilarityThreshold, channelName);

var client = new DiscordSocketClient(new DiscordSocketConfig
{
	GatewayIntents = GatewayIntents.Guilds | GatewayIntents.GuildMessages | GatewayIntents.MessageContent
});

client.Log += msg =>
{
	var logLevel = msg.Severity switch
	{
		LogSeverity.Critical => LogLevel.Critical,
		LogSeverity.Error => LogLevel.Error,
		LogSeverity.Warning => LogLevel.Warning,
		LogSeverity.Info => LogLevel.Information,
		LogSeverity.Verbose => LogLevel.Debug,
		LogSeverity.Debug => LogLevel.Trace,
		_ => LogLevel.Information
	};
	return Task.CompletedTask;
};

client.Ready += () =>
{
	logger.LogInformation("Bot is ready and connected");
	return Task.CompletedTask;
};

client.MessageReceived += async message =>
{
	if (message.Channel is not SocketTextChannel channel)
		return;

	if (message.Author.IsBot)
		return;

	var guildUser = message.Author as SocketGuildUser;
	if (guildUser == null)
		return;

	if (guildUser.GuildPermissions.Administrator ||
		guildUser.GuildPermissions.ManageMessages ||
		guildUser.GuildPermissions.ModerateMembers) return;

	var guild = channel.Guild;
	var userId = message.Author.Id;
	var channelId = channel.Id;
	var content = message.Content;
	var timestamp = message.Timestamp;

	var (result, reason, firstChannelId) = messageTracker.CheckMessage(channel.Name, userId, channelId, content, timestamp);

	if (result == SpamDetectionResult.HoneypotTriggered)
	{
		logger.LogWarning("Potential spammer detected: {User} posted in #{ChannelName}", message.Author.Username, channelName);

		var triggerTime = timestamp;
		var startTime = triggerTime.AddSeconds(-pastMsgInterval);
		var endTime = triggerTime.AddSeconds(futureMsgInterval);

		logger.LogDebug("Triggered={TriggerTime} Start={StartTime} End={EndTime}", triggerTime, startTime, endTime);

		await DeleteUserMessagesInInterval(guild, userId, startTime, endTime, logger);

		_ = Task.Run(async () =>
		{
			await MonitorAndDeleteMessages(guild, userId, startTime, endTime, message.Author.Username, logger);
		});
	}
	else if (result == SpamDetectionResult.HoneypotDetected)
	{
		logger.LogWarning("Known spammer: {User} previously posted in honeypot channel, deleting message in #{CurrentChannel}", message.Author.Username, channel.Name);

		try
		{
			await message.DeleteAsync();
			logger.LogInformation("Deleted message from known spammer {User} in #{Channel}", message.Author.Username, channel.Name);
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Failed to delete message from known spammer {User} in #{Channel}", message.Author.Username, channel.Name);
		}
	}
	else if (result == SpamDetectionResult.DuplicateDetected)
	{
		logger.LogWarning("Cross-channel spam detected: {User} posted similar messages in multiple channels (first in channel {FirstChannelId}, now in #{CurrentChannel})", message.Author.Username, firstChannelId, channel.Name);

		var triggerTime = timestamp;
		var startTime = triggerTime.AddSeconds(-pastMsgInterval);
		var endTime = triggerTime.AddSeconds(futureMsgInterval);

		logger.LogDebug("Triggered={TriggerTime} Start={StartTime} End={EndTime}", triggerTime, startTime, endTime);

		await DeleteUserMessagesInInterval(guild, userId, startTime, endTime, logger);

		_ = Task.Run(async () =>
		{
			await MonitorAndDeleteMessages(guild, userId, startTime, endTime, message.Author.Username, logger);
		});
	}
};

await client.LoginAsync(TokenType.Bot, token);
await client.StartAsync();

logger.LogInformation("Bot started successfully");

_ = Task.Run(async () =>
{
	while (true)
	{
		await Task.Delay(TimeSpan.FromMinutes(5));
		try
		{
			messageTracker.PerformPeriodicCleanup(DateTimeOffset.UtcNow);
			logger.LogDebug("Performed periodic cleanup of message tracker");
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Error during periodic cleanup");
		}
	}
});

await Task.Delay(-1);

static bool CanBotAccessChannel(SocketTextChannel channel, SocketGuild guild, ILogger logger)
{
	var botUser = guild.CurrentUser;
	var permissions = botUser.GetPermissions(channel);
	return permissions.ViewChannel && permissions.ManageMessages;
}

static async Task DeleteUserMessagesInInterval(SocketGuild guild, ulong userId, DateTimeOffset startTime, DateTimeOffset endTime, ILogger logger)
{
	var tasks = new List<Task>();

	foreach (var channel in guild.TextChannels)
	{
		if (!CanBotAccessChannel(channel, guild, logger))
			continue;

		tasks.Add(Task.Run(async () =>
		{
			try
			{
				var messages = await channel.GetMessagesAsync(100).FlattenAsync();
				var userMessages = messages
					.Where(m => m.Author.Id == userId && m.Timestamp >= startTime && m.Timestamp <= endTime)
					.ToList();

				foreach (var msg in userMessages)
				{
					try
					{
						await msg.DeleteAsync();
						logger.LogInformation("Deleted message from {User} in #{Channel} at {Timestamp}: {Message}", msg.Author.Username, channel.Name, msg.Timestamp, msg.Content);
					}
					catch (Exception ex)
					{
						logger.LogError(ex, "Failed to delete message {MessageId} in #{Channel}", msg.Id, channel.Name);
					}
				}
			}
			catch
			{
				logger.LogError("Failed to get messages from #{Channel}", channel.Name);
			}
		}));
	}

	await Task.WhenAll(tasks);
}

static async Task MonitorAndDeleteMessages(SocketGuild guild, ulong userId, DateTimeOffset startTime, DateTimeOffset endTime, string username, ILogger logger)
{
	var now = DateTimeOffset.UtcNow;
	var remainingTime = endTime - now;

	if (remainingTime > TimeSpan.Zero)
		logger.LogInformation("Monitoring user {User} for {Seconds} more seconds", username, remainingTime.TotalSeconds);

	while (DateTimeOffset.UtcNow < endTime)
	{
		await Task.Delay(1000);

		var tasks = new List<Task>();

		foreach (var channel in guild.TextChannels)
		{
			if (!CanBotAccessChannel(channel, guild, logger))
				continue;

			tasks.Add(Task.Run(async () =>
			{
				try
				{
					var messages = await channel.GetMessagesAsync(10).FlattenAsync();
					var userMessages = messages
						.Where(m => m.Author.Id == userId && m.Timestamp >= startTime && m.Timestamp <= endTime)
						.ToList();

					foreach (var msg in userMessages)
					{
						try
						{
							await msg.DeleteAsync();
							logger.LogInformation("!!! Deleted new message from user {UserId} in #{Channel}: {Message}", userId, channel.Name, msg.Content);
						}
						catch (Exception ex)
						{
							logger.LogError(ex, "Failed to delete message {MessageId} in #{Channel}", msg.Id, channel.Name);
						}
					}
				}
				catch
				{
					logger.LogError("Failed to monitor messages in #{Channel}", channel.Name);
				}
			}));
		}

		await Task.WhenAll(tasks);
	}
}
