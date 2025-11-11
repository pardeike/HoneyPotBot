using Discord;
using Discord.WebSocket;
using HoneyPotBot;
using Microsoft.Extensions.Logging;

var logFormat = Config.Get("LOG_FORMAT") ?? "text";
var logLevel = Enum.TryParse<LogLevel>(Config.Get("LOG_LEVEL"), out var level) ? level : LogLevel.Information;
var channelName = Config.Get("CHANNEL_NAME") ?? "intro";
var pastMsgInterval = int.TryParse(Config.Get("PAST_MSG_INTERVAL"), out var past) ? past : 300;
var futureMsgInterval = int.TryParse(Config.Get("FUTURE_MSG_INTERVAL"), out var future) ? future : 300;

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
	// logger.Log(logLevel, "{Source}: {Message}", msg.Source, msg.Message);
	return Task.CompletedTask;
};

client.Ready += () =>
{
	logger.LogInformation("Bot is ready and connected");
	return Task.CompletedTask;
};

client.MessageReceived += async message =>
{
	// logger.LogWarning("Received message in {Channel} from {User}: {Content}", message.Channel.Name, message.Author.Username, message.Content);

	if (message.Channel is not SocketTextChannel channel)
		return;

	if (channel.Name != channelName)
		return;

	if (message.Author.IsBot)
		return;

	var guildUser = message.Author as SocketGuildUser;
	if (guildUser == null)
		return;

	if (guildUser.GuildPermissions.Administrator ||
		guildUser.GuildPermissions.ManageMessages ||
		guildUser.GuildPermissions.ModerateMembers) return;

	logger.LogWarning("Potential spammer detected: {User} posted in #{ChannelName}", message.Author.Username, channelName);

	// Calculate time window for message deletion:
	// - triggerTime: when the user posted in the honeypot channel
	// - startTime: pastMsgInterval seconds before the trigger (to catch earlier spam)
	// - endTime: futureMsgInterval seconds after the trigger (to catch continued spam)
	// Both boundaries are inclusive (>= startTime AND <= endTime)
	var triggerTime = message.Timestamp;
	var startTime = triggerTime.AddSeconds(-pastMsgInterval);
	var endTime = triggerTime.AddSeconds(futureMsgInterval);

	logger.LogDebug("Triggered={TriggerTime} Start={StartTime} End={EndTime}", triggerTime, startTime, endTime);

	var guild = channel.Guild;
	var userId = message.Author.Id;

	await DeleteUserMessagesInInterval(guild, userId, startTime, endTime, logger);

	_ = Task.Run(async () =>
	{
		await MonitorAndDeleteMessages(guild, userId, startTime, endTime, message.Author.Username, logger);
	});
};

await client.LoginAsync(TokenType.Bot, token);
await client.StartAsync();

logger.LogInformation("Bot started successfully");

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
