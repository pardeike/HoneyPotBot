# HoneyPotBot

## About

This Discord bot fights against spammers using two detection methods:

1. **Honeypot Channel**: When non-privileged users post in the honeypot channel (default: 'intro'), the bot deletes all their messages from past 5 minutes and continues deleting any new messages for the next 5 minutes. Additionally, the user is flagged and all subsequent messages from them are deleted immediately without similarity checks for the configured time interval.

2. **Cross-Channel Spam Detection**: The bot tracks messages across all channels and detects when users post similar messages in multiple channels within a short time window. When duplicate messages are detected, the same deletion logic is triggered. This feature can be configured with:
   - Message length threshold to ignore short messages (default: 40 characters)
   - Link requirement to only track messages containing URLs
   - Time window for comparing messages (default: 120 seconds)
   - Similarity threshold for detecting duplicates (default: 0.85)

Privileged users (administrators and moderators) are not affected by either detection method.

## Setup

1. Clone the repository.
2. Create a Discord bot and get its token from the [Discord Developer Portal](https://discord.com/developers/applications).
3. Install .NET 9 SDK from [Microsoft](https://dotnet.microsoft.com/en-us/download/dotnet/9.0).
4. Create a configuration file at `~/.api-keys` with the following JSON format:
	```json
	{
		"HONEYPOTBOT_TOKEN": "your-discord-bot-token-here",
		"LOG_FORMAT": "text",
		"LOG_LEVEL": "Information",
		"PAST_MSG_INTERVAL": "300",
		"FUTURE_MSG_INTERVAL": "300"
	}
	```
5. Navigate to the project directory and run:
	```bash
	dotnet run
	```

## Configuration

Configuration is read from `~/.api-keys` file in JSON format with the following keys:

- `HONEYPOTBOT_TOKEN` (required): the Discord bot token from the Discord Developer Portal.
- `LOG_FORMAT` (default: `text`): the log format, either `text` or `json`.
- `LOG_LEVEL` (default: `Information`): the log level, either `Trace`, `Debug`, `Information`, `Warning`, `Error`, or `Critical`.
- `PAST_MSG_INTERVAL` (default: `300`): the number of seconds to look back for older messages to be deleted
- `FUTURE_MSG_INTERVAL` (default: `300`): the number of seconds that newer messages will be deleted
- `MSG_DELTA_INTERVAL` (default: `120`): the number of seconds to keep messages in history for cross-channel spam detection
- `MIN_MSG_LENGTH` (default: `40`): minimum message length in characters for cross-channel spam detection (shorter messages are ignored)
- `LINK_REQUIRED` (default: `true`): whether messages must contain links to be tracked for cross-channel spam detection
- `MSG_SIMILARITY_THRESHOLD` (default: `0.85`): similarity threshold (0.0 to 1.0) for detecting duplicate messages across channels. Higher values require more similarity.
