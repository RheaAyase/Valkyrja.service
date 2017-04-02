using System;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using Tmds.DBus;

using guid = System.UInt64;

namespace Botwinder.Service
{
	public class BotClient
	{
		private readonly Config Config = Config.Load();
		private readonly DiscordSocketClient Client = new DiscordSocketClient();

		public BotClient()
		{
			Client.MessageReceived += ClientOnMessageReceived;
			Client.MessageUpdated += ClientOnMessageUpdated;
		}

		public async Task Connect()
		{
			await Client.LoginAsync(TokenType.Bot, Config.BotToken).ConfigureAwait(false);
			await Client.StartAsync().ConfigureAwait(false);
		}

		private async Task ClientOnMessageUpdated(Cacheable<IMessage, ulong> cacheable, SocketMessage socketMessage, ISocketMessageChannel arg3)
		{
			await ClientOnMessageReceived(socketMessage);
		}

		private async Task ClientOnMessageReceived(SocketMessage socketMessage)
		{
			if( !Config.AdminIDs.Contains(socketMessage.Author.Id) )
				return;

			string response = "";
			if( socketMessage.Content.StartsWith("!serviceStatus") )
			{
				await socketMessage.Channel.SendMessageAsync(await Systemd.GetServiceStatus("botwinder"));
			}
			else if( socketMessage.Content.StartsWith("!serviceRestart") )
			{
				await socketMessage.Channel.SendMessageAsync("**Restart successful:** `" + await Systemd.RestartService("botwinder") + "`");
			}

			if( !string.IsNullOrWhiteSpace(response) )
				await socketMessage.Channel.SendMessageAsync(response);
		}
	}
}
