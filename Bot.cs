using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.Rest;
using Discord.WebSocket;
using Botwinder.entities;

using guid = System.UInt64;

namespace Botwinder.Service
{
	public class SkywinderClient
	{
		internal readonly DiscordSocketClient Client = new DiscordSocketClient();
		private  readonly Config Config = Config.Load();
		private  readonly Regex RegexCommandParams = new Regex("\"[^\"]+\"|\\S+", RegexOptions.Compiled);
		private CancellationTokenSource MainUpdateCancel;
		private Task MainUpdateTask;

		private DateTime LastShardCleanupTime = DateTime.UtcNow;

		private string RaidSync = "999";
		private string RaidFailedDrives = "9";

		public SkywinderClient()
		{
			this.Client.MessageReceived += ClientOnMessageReceived;
			this.Client.MessageUpdated += ClientOnMessageUpdated;
			this.Client.Disconnected += ClientDisconnected;
		}

		public async Task Connect()
		{
			await this.Client.LoginAsync(TokenType.Bot, this.Config.BotToken).ConfigureAwait(false);
			await this.Client.StartAsync().ConfigureAwait(false);

			if( this.MainUpdateTask == null )
			{
				this.MainUpdateCancel = new CancellationTokenSource();
				this.MainUpdateTask = Task.Factory.StartNew(MainUpdate, this.MainUpdateCancel.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
			}
		}

//Update
		private async Task MainUpdate()
		{
			while( !this.MainUpdateCancel.IsCancellationRequested )
			{
				DateTime frameTime = DateTime.UtcNow;

				if( this.Client.ConnectionState != ConnectionState.Connected ||
				    this.Client.LoginState != LoginState.LoggedIn )
				{
					await Task.Delay(10000);
					continue;
				}

				try
				{
					//Update
					SocketGuild mainGuild;
					SocketTextChannel statusChannel;
					RestUserMessage statusMessage;
					if( (mainGuild = this.Client.GetGuild(this.Config.MainGuildId)) != null &&
					    (statusChannel = mainGuild.GetTextChannel(this.Config.StatusChannelId)) != null &&
					    (statusMessage = (RestUserMessage) await statusChannel.GetMessageAsync(this.Config.StatusMessageId)) != null )
					{
						StringBuilder shards = new StringBuilder();
						GlobalContext dbContext = GlobalContext.Create(this.Config.GetDbConnectionString());
						Shard globalCount = new Shard();
						foreach( Shard shard in dbContext.Shards )
						{
							globalCount.ServerCount += shard.ServerCount;
							globalCount.UserCount += shard.UserCount;
							globalCount.MemoryUsed += shard.MemoryUsed;
							globalCount.ThreadsActive += shard.ThreadsActive;
							globalCount.MessagesTotal += shard.MessagesTotal;
							globalCount.MessagesPerMinute += shard.MessagesPerMinute;
							globalCount.OperationsRan += shard.OperationsRan;
							globalCount.OperationsActive += shard.OperationsActive;
							globalCount.Disconnects += shard.Disconnects;

							shards.AppendLine(shard.GetShortStatsString());
						}

						if( DateTime.UtcNow - this.LastShardCleanupTime > TimeSpan.FromMinutes(3) )
						{
							this.LastShardCleanupTime = DateTime.UtcNow;
							foreach( Shard shard in dbContext.Shards )
							{
								shard.TimeStarted = DateTime.MinValue;
								shard.IsConnecting = false;
							}

							this.RaidSync = Bash.Run("lvs raid5 -o 'lv_name,copy_percent,vg_missing_pv_count' | grep raid5 | awk '{print $2}'");
							this.RaidFailedDrives = Bash.Run("lvs raid5 -o 'lv_name,copy_percent,vg_missing_pv_count' | grep raid5 | awk '{print $3}'");
						}

						string[] cpuTemp = Bash.Run("sensors | grep Package | sed 's/Package id [01]:\\s*+//g' | sed 's/\\s*(high = +85.0°C, crit = +95.0°C)//g'").Split('\n');
						string cpuLoad = Bash.Run("grep 'cpu ' /proc/stat | awk '{print ($2+$4)*100/($2+$4+$5)}'");
						string memoryUsed = Bash.Run("free | grep Mem | awk '{print $3/$2 * 100.0}'");
						string message = "Server Status: <https://status.valkyrja.app>\n" +
						                 $"```md\n[   Last update ][ {Utils.GetTimestamp(DateTime.UtcNow)} ]\n" +
						                 $"[  Memory usage ][ {double.Parse(memoryUsed):#00.00} %                 ]\n" +
						                 $"[      CPU Load ][ {double.Parse(cpuLoad):#00.00} %                 ]\n" +
						                 $"[     CPU0 Temp ][ {cpuTemp[0]}                  ]\n" +
						                 $"[     CPU1 Temp ][ {cpuTemp[1]}                  ]\n" +
						                 $"[       Threads ][ {globalCount.ThreadsActive:#000}                     ]\n" +
						                 $"[     Raid Sync ][ {double.Parse(this.RaidSync):000.00} %                ]\n" +
						                 $"[ Raid Failures ][ {int.Parse(this.RaidFailedDrives):0}                       ]\n" +
						                 $"```\n" +
						                 $"**Shards: `{dbContext.Shards.Count()}`**\n\n" +
						                 $"{shards.ToString()}";

						dbContext.SaveChanges();
						dbContext.Dispose();

						await statusMessage.ModifyAsync(m => m.Content = message);
					}
				}
				catch(Exception exception)
				{
					LogException(exception, "--Update");
				}

				TimeSpan deltaTime = DateTime.UtcNow - frameTime;
				await Task.Delay(TimeSpan.FromMilliseconds(Math.Max(1, (TimeSpan.FromSeconds(1f / this.Config.TargetFps) - deltaTime).TotalMilliseconds)));
			}
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

		private Task ClientDisconnected(Exception exception)
		{
			Console.WriteLine($"Discord Client died:\n{  exception.Message}\nShutting down.");
			Environment.Exit(0); //HACK - The library often reconnects in really shitty way and no longer works
			return Task.CompletedTask;
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
