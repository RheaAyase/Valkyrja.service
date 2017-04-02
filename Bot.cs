using System;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;

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

			try
			{
				string response = "";
				try
				{
					if( socketMessage.Content.StartsWith("!serviceStatus") )
					{
						Console.WriteLine("Executing !serviceStatus");
						response = await Systemd.GetServiceStatus(Config.ServiceName);
					}
					else if( socketMessage.Content.StartsWith("!serviceRestart") )
					{
						Console.WriteLine("Executing !serviceRestart");
						response = "**Restart successful:** `" + await Systemd.RestartService(Config.ServiceName) + "`";
					}
				}
				catch( Exception e )
				{
					response = "Systemd haz spit out an error: \n  " + e.Message;
					LogException(e, socketMessage.Author.Username + socketMessage.Author.Discriminator + ": " + socketMessage.Content);
				}

				if( !string.IsNullOrWhiteSpace(response) )
					await socketMessage.Channel.SendMessageAsync(response);
			}
			catch( Exception e )
			{
				LogException(e, socketMessage.Author.Username + socketMessage.Author.Discriminator + ": " + socketMessage.Content);
			}
		}

		public void LogException(Exception e, string data = "")
		{
			Console.WriteLine("Exception: " + e.Message);

			if( string.IsNullOrWhiteSpace(data) )
				Console.WriteLine("Data: " + data);

			Console.WriteLine("Stack: " + e.StackTrace);
			Console.WriteLine(".......exception: " + e.Message);
		}
	}
}
