# HoneyPotBot

## About

This Discord bot fights against spammers by monitoring a honeypot channel called 'intro'. When non-privileged users post in this channel, the bot deletes all their messages from past 5 minutes and continues deleting any new messages for the next 5 minutes. Privileged users (administrators and moderators) are not affected.

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
