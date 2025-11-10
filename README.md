# HoneyPotBot

## About

This Discord bot observes the default text channel for new messages and removes messages of users that post there as well as any future messages anywhere on the server as well as some of their previous messages.

## Setup

1. Clone the repository.
2. Create a Discord bot and get its token from the [Discord Developer Portal](https://discord.com/developers/applications).
3. Install .NET 9 SDK from [Microsoft](https://dotnet.microsoft.com/en-us/download/dotnet/9.0).
4. Navigate to the project directory and run:
	```bash
	dotnet run
	```

## Environment Variables

- `LOG_FORMAT` (default: `text`): the log format, either `text` or `json`.
- `LOG_LEVEL` (default: `Information`): the log level, either `Trace`, `Debug`, `Information`, `Warning`, `Error`, or `Critical`.
- `PAST_MSG_INTERVAL` (default: `5`): the number of seconds to look back for older messages to be deleted
- `FUTURE_MSG_INTERVAL` (default: `10`): the number of seconds that newer messages will be deleted
