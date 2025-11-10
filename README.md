# HoneyPotBot

## About

This Discord bot fights against spammers by monitoring a honeypot channel called 'intro'. When non-privileged users post in this channel, the bot deletes all their messages from past 5 seconds and continues deleting any new messages for the next 15 seconds. Privileged users (administrators and moderators) are not affected.

## Setup

1. Clone the repository.
2. Create a Discord bot and get its token from the [Discord Developer Portal](https://discord.com/developers/applications).
3. Install .NET 9 SDK from [Microsoft](https://dotnet.microsoft.com/en-us/download/dotnet/9.0).
4. Navigate to the project directory and run:
	```bash
	dotnet run
	```

## Environment Variables

- `DISCORD_TOKEN` (required): the Discord bot token from the Discord Developer Portal.
- `LOG_FORMAT` (default: `text`): the log format, either `text` or `json`.
- `LOG_LEVEL` (default: `Information`): the log level, either `Trace`, `Debug`, `Information`, `Warning`, `Error`, or `Critical`.
- `PAST_MSG_INTERVAL` (default: `5`): the number of seconds to look back for older messages to be deleted
- `FUTURE_MSG_INTERVAL` (default: `15`): the number of seconds that newer messages will be deleted
