using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;

var logFormat = Environment.GetEnvironmentVariable("LOG_FORMAT") ?? "text";
var logLevel = Enum.TryParse<LogLevel>(Environment.GetEnvironmentVariable("LOG_LEVEL"), out var level) ? level : LogLevel.Information;
var pastMsgInterval = int.TryParse(Environment.GetEnvironmentVariable("PAST_MSG_INTERVAL"), out var past) ? past : 5;
var futureMsgInterval = int.TryParse(Environment.GetEnvironmentVariable("FUTURE_MSG_INTERVAL"), out var future) ? future : 15;

using var loggerFactory = LoggerFactory.Create(builder =>
{
	builder.SetMinimumLevel(logLevel);
	if (logFormat == "json")
		builder.AddJsonConsole();
	else
		builder.AddSimpleConsole(options =>
		{
			options.SingleLine = true;
			options.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
		});
});

var logger = loggerFactory.CreateLogger("HoneyPotBot");

var token = Environment.GetEnvironmentVariable("HONEYPOTBOT_TOKEN");
if (string.IsNullOrEmpty(token))
{
	logger.LogCritical("HONEYPOTBOT_TOKEN environment variable is not set");
	return;
}

logger.LogInformation("Starting HoneyPotBot with PAST_MSG_INTERVAL={PastInterval}s and FUTURE_MSG_INTERVAL={FutureInterval}s", pastMsgInterval, futureMsgInterval);

var config = new DiscordSocketConfig
{
	GatewayIntents = GatewayIntents.Guilds | GatewayIntents.GuildMessages | GatewayIntents.MessageContent
};

var client = new DiscordSocketClient(config);

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
	logger.Log(logLevel, "{Source}: {Message}", msg.Source, msg.Message);
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

	if (channel.Name != "intro")
		return;

	if (message.Author.IsBot)
		return;

	var guildUser = message.Author as SocketGuildUser;
	if (guildUser == null)
		return;

	var isPrivileged = guildUser.GuildPermissions.Administrator ||
							 guildUser.GuildPermissions.ManageMessages ||
							 guildUser.GuildPermissions.ModerateMembers;

	if (isPrivileged)
	{
		logger.LogDebug("Ignoring message from privileged user {User} in intro channel", message.Author.Username);
		return;
	}

	logger.LogWarning("Potential spammer detected: {User} posted in intro channel", message.Author.Username);

	var triggerTime = message.Timestamp;
	var startTime = triggerTime.AddSeconds(-pastMsgInterval);
	var endTime = triggerTime.AddSeconds(futureMsgInterval);

	var guild = channel.Guild;
	var userId = message.Author.Id;

	await DeleteUserMessagesInInterval(guild, userId, startTime, endTime, logger);

	_ = Task.Run(async () =>
	{
		await MonitorAndDeleteMessages(guild, userId, endTime, message.Author.Username, logger);
	});
};

await client.LoginAsync(TokenType.Bot, token);
await client.StartAsync();

logger.LogInformation("Bot started successfully");

await Task.Delay(-1);

static async Task DeleteUserMessagesInInterval(SocketGuild guild, ulong userId, DateTimeOffset startTime, DateTimeOffset endTime, ILogger logger)
{
	var tasks = new List<Task>();

	foreach (var channel in guild.TextChannels)
	{
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
						logger.LogInformation("Deleted message from {User} in #{Channel} at {Timestamp}", msg.Author.Username, channel.Name, msg.Timestamp);
					}
					catch (Exception ex)
					{
						logger.LogError(ex, "Failed to delete message {MessageId} in #{Channel}", msg.Id, channel.Name);
					}
				}
			}
			catch (Exception ex)
			{
				logger.LogError(ex, "Failed to get messages from #{Channel}", channel.Name);
			}
		}));
	}

	await Task.WhenAll(tasks);
}

static async Task MonitorAndDeleteMessages(SocketGuild guild, ulong userId, DateTimeOffset endTime, string username, ILogger logger)
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
			tasks.Add(Task.Run(async () =>
			{
				try
				{
					var messages = await channel.GetMessagesAsync(10).FlattenAsync();
					var userMessages = messages
						.Where(m => m.Author.Id == userId && m.Timestamp <= endTime)
						.ToList();

					foreach (var msg in userMessages)
					{
						try
						{
							await msg.DeleteAsync();
							logger.LogInformation("Deleted new message from user {UserId} in #{Channel}", userId, channel.Name);
						}
						catch (Exception ex)
						{
							logger.LogError(ex, "Failed to delete message {MessageId} in #{Channel}", msg.Id, channel.Name);
						}
					}
				}
				catch (Exception ex)
				{
					logger.LogError(ex, "Failed to monitor messages in #{Channel}", channel.Name);
				}
			}));
		}

		await Task.WhenAll(tasks);
	}
}
