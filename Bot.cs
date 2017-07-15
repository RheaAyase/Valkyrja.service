using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;

using guid = System.UInt64;

namespace Botwinder.Service
{
	public class SkywinderClient
	{
		internal readonly DiscordSocketClient Client = new DiscordSocketClient();
		private  readonly Config Config = Config.Load();
		private  readonly Regex RegexCommandParams = new Regex("\"[^\"]+\"|\\S+", RegexOptions.Compiled);

		public SkywinderClient()
		{
			this.Client.MessageReceived += ClientOnMessageReceived;
			this.Client.MessageUpdated += ClientOnMessageUpdated;
		}

		public async Task Connect()
		{
			await this.Client.LoginAsync(TokenType.Bot, this.Config.BotToken).ConfigureAwait(false);
			await this.Client.StartAsync().ConfigureAwait(false);
		}

		private void GetCommandAndParams(string message, out string command, out string trimmedMessage, out string[] parameters)
		{
			string input = message.Substring(this.Config.Prefix.Length);
			trimmedMessage = "";
			parameters = null;

			MatchCollection regexMatches = this.RegexCommandParams.Matches(input);
			if( regexMatches.Count == 0 )
			{
				command = input.Trim();
				return;
			}

			command = regexMatches[0].Value;

			if( regexMatches.Count > 1 )
			{
				trimmedMessage = input.Substring(regexMatches[1].Index).Trim('\"', ' ', '\n');
				Match[] matches = new Match[regexMatches.Count];
				regexMatches.CopyTo(matches, 0);
				parameters = matches.Skip(1).Select(p => p.Value).ToArray();
				for(int i = 0; i < parameters.Length; i++)
					parameters[i] = parameters[i].Trim('"');
			}
		}

		private async Task ClientOnMessageUpdated(Cacheable<IMessage, ulong> cacheable, SocketMessage socketMessage, ISocketMessageChannel arg3)
		{
			await ClientOnMessageReceived(socketMessage);
		}

		private async Task ClientOnMessageReceived(SocketMessage socketMessage)
		{
			try
			{
				await HandleCommands(socketMessage);
			}
			catch( Exception e )
			{
				LogException(e, socketMessage.Author.Username + socketMessage.Author.Discriminator + ": " + socketMessage.Content);
			}
		}

		private async Task HandleCommands(SocketMessage socketMessage)
		{
			string commandString = "", trimmedMessage = "";
			string[] parameters;
			if( !string.IsNullOrWhiteSpace(this.Config.Prefix) && socketMessage.Content.StartsWith(this.Config.Prefix) )
				GetCommandAndParams(socketMessage.Content, out commandString, out trimmedMessage, out parameters);


			string response = "";
			if( !this.Config.AdminIDs.Contains(socketMessage.Author.Id) ||
			     string.IsNullOrWhiteSpace(this.Config.Prefix) ||
			    !socketMessage.Content.StartsWith(this.Config.Prefix) )
				return;

			try
			{
				switch(commandString) //TODO: Replace by Botwinder's command handling...
				{
					case "serviceStatus":
						if( string.IsNullOrWhiteSpace(trimmedMessage) || !this.Config.ServiceNames.Contains(trimmedMessage + ".service") )
						{
							response = "Invalid parameter - service name";
							break;
						}

						Console.WriteLine("Executing !serviceStatus "+ trimmedMessage +" | "+ socketMessage.Author.Id +" | "+ socketMessage.Author.Username);
						response = await Systemd.GetServiceStatus(trimmedMessage + ".service");
						break;
					case "serviceRestart":
						if( string.IsNullOrWhiteSpace(trimmedMessage) || !this.Config.ServiceNames.Contains(trimmedMessage + ".service") )
						{
							response = "Invalid parameter - service name";
							break;
						}

						Console.WriteLine("Executing !serviceRestart "+ trimmedMessage +" | "+ socketMessage.Author.Id +" | "+ socketMessage.Author.Username);
						response = "Restart successful: `" + await Systemd.RestartService(trimmedMessage + ".service") + "`";
						break;
					default:
						return;
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
