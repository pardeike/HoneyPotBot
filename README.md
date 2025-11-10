# HoneyPotBot

## About

This Discord bot fights against spammers by monitoring a honeypot channel called 'intro'. When non-privileged users post in this channel, the bot deletes all their messages from past 5 seconds and continues deleting any new messages for the next 15 seconds. Privileged users (administrators and moderators) are not affected.

## Setup

1. Clone the repository.
2. Create a Discord bot and get its token from the [Discord Developer Portal](https://discord.com/developers/applications).
3. Configure the bot in the Developer Portal:

   **OAuth2 Scopes** (in the OAuth2 → General section):
   - `bot` - Required to add the bot to servers

   **Bot Permissions** (in the Bot section or when generating invite URL):
   - `View Channels` (Read Messages/View Channels)
   - `Send Messages`
   - `Manage Messages` - Required to delete spammer messages
   - `Read Message History` - Required to read past messages

   **Privileged Gateway Intents** (in the Bot section):
   - `Server Members Intent` - Not required
   - `Presence Intent` - Not required
   - `Message Content Intent` - **REQUIRED** - Must be enabled for the bot to read message content

   The bot invite URL should have the `bot` scope with permission value `76800` (or select the permissions listed above).

4. Install .NET 9 SDK from [Microsoft](https://dotnet.microsoft.com/en-us/download/dotnet/9.0).
5. Create a configuration file at `~/.api-keys` with the following JSON format:
	```json
	{
		"HONEYPOTBOT_TOKEN": "your-discord-bot-token-here",
		"LOG_FORMAT": "text",
		"LOG_LEVEL": "Information",
		"PAST_MSG_INTERVAL": "5",
		"FUTURE_MSG_INTERVAL": "15"
	}
	```
6. Navigate to the project directory and run:
	```bash
	dotnet run
	```

## Configuration

Configuration is read from `~/.api-keys` file in JSON format with the following keys:

- `HONEYPOTBOT_TOKEN` (required): the Discord bot token from the Discord Developer Portal.
- `LOG_FORMAT` (default: `text`): the log format, either `text` or `json`.
- `LOG_LEVEL` (default: `Information`): the log level, either `Trace`, `Debug`, `Information`, `Warning`, `Error`, or `Critical`.
- `PAST_MSG_INTERVAL` (default: `5`): the number of seconds to look back for older messages to be deleted
- `FUTURE_MSG_INTERVAL` (default: `15`): the number of seconds that newer messages will be deleted
