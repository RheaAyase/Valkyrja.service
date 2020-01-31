using System;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.Rest;
using Discord.WebSocket;
using Valkyrja.entities;
using guid = System.UInt64;

namespace Valkyrja.service
{
	public class SkywinderClient
	{
		private Monitoring Monitoring;

		internal readonly DiscordSocketClient Client = new DiscordSocketClient();
		private  readonly Config Config = Config.Load();
		private  readonly Regex RegexCommandParams = new Regex("\"[^\"]+\"|\\S+", RegexOptions.Compiled);
		private CancellationTokenSource MainUpdateCancel;
		private Task MainUpdateTask;

		private DateTime LastShardCleanupTime = DateTime.UtcNow;

		private string RootRaidSync = "-1";
		private string RootRaidFailedDrives = "-1";
		private string DataRaidSync = "-1";
		private string DataRaidFailedDrives = "-1";
		private bool ShuttingDown = false;
		public bool IsValkOnline = true;

		public SkywinderClient()
		{
			this.Client.MessageReceived += ClientOnMessageReceived;
			this.Client.MessageUpdated += ClientOnMessageUpdated;
			this.Client.Disconnected += ClientDisconnected;
		}

		~SkywinderClient()
		{
			this.Monitoring.Dispose();
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

			if( this.Monitoring == null )
				this.Monitoring = new Monitoring(this.Config);
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

				foreach( Config.Server server in this.Config.Servers )
				{
					try
					{
						//Update
						SocketGuild guild;
						SocketTextChannel statusChannel;
						RestUserMessage statusMessage;
						if( (guild = this.Client.GetGuild(server.GuildId)) != null &&
						    (statusChannel = guild.GetTextChannel(server.StatusChannelId)) != null )
						{
							if( server.StatusMessageId == 0 || (statusMessage = (RestUserMessage) await statusChannel.GetMessageAsync(server.StatusMessageId)) == null )
							{
								statusMessage = await statusChannel.SendMessageAsync("Loading status service...");
								this.Config.Servers.First(s => s.GuildId == server.GuildId).StatusMessageId = statusMessage.Id;
								this.Config.Save();
								continue;
							}

							Ping pingCloudflare = new Ping();
							Ping pingGoogle = new Ping();
							Ping pingDiscord = new Ping();
							Task<PingReply> pingReplyCloudflare = pingCloudflare.SendPingAsync("1.1.1.1", 1000);
							Task<PingReply> pingReplyGoogle = pingGoogle.SendPingAsync("8.8.8.8", 1000);
							Task<PingReply> pingReplyDiscord = pingDiscord.SendPingAsync("gateway.discord.gg", 1000);
							string pcpRaw = Bash.Run("pmrep -s 2 kernel.cpu.util.idle mem.util.available disk.dev.total_bytes network.interface.total.bytes | tail -n 1");
							Regex PcpRegex = new Regex("\\d+\\.?\\d*", RegexOptions.Compiled);
							MatchCollection pcpArray = PcpRegex.Matches(pcpRaw);

							double cpuUtil = 100 - double.Parse(pcpArray[0].Value); //%
							double memUsed = 128 - double.Parse(pcpArray[1].Value) / 1048576; //GB
							double diskUtil = (double.Parse(pcpArray[2].Value) + double.Parse(pcpArray[3].Value) + double.Parse(pcpArray[4].Value) + double.Parse(pcpArray[5].Value) + double.Parse(pcpArray[6].Value) + double.Parse(pcpArray[7].Value) + double.Parse(pcpArray[8].Value)) / 1024; //MB/s
							double netUtil = double.Parse(pcpArray[14].Value) * 8 / 1048576; //Mbps
							string[] temp = Bash.Run("sensors | egrep '(temp1|Tdie|Tctl)' | awk '{print $2}'").Split('\n');
							string cpuFrequency = Bash.Run("grep MHz /proc/cpuinfo | awk '{ f = 0; if( $4 > f ) f = $4; } END { print f; }'");
							long latencyCloudflare = (await pingReplyCloudflare).RoundtripTime;
							long latencyGoogle = (await pingReplyGoogle).RoundtripTime;
							long latencyDiscord = (await pingReplyDiscord).RoundtripTime;

							this.Monitoring.CpuUtil.Set(cpuUtil);
							this.Monitoring.MemUsed.Set(memUsed);
							this.Monitoring.DiskUtil.Set(diskUtil);
							this.Monitoring.NetUtil.Set(netUtil);
							this.Monitoring.CpuTemp.Set(double.Parse(temp[1].Trim('-', '+', '°', 'C')));
							this.Monitoring.GpuTemp.Set(double.Parse(temp[0].Trim('-', '+', '°', 'C')));
							this.Monitoring.LatencyCloudflare.Set(latencyCloudflare);
							this.Monitoring.LatencyGoogle.Set(latencyGoogle);
							this.Monitoring.LatencyDiscord.Set(latencyDiscord);


							string message;
							if( this.ShuttingDown )
							{
								message = "Server Status: <https://status.valkyrja.app>\n" +
								          $"```md\n[        Last update ][ {Utils.GetTimestamp(DateTime.UtcNow)} ]\n" +
								          $"[              State ][ Down for Maintenance    ]```\n" +
								          $"<:offwinder:438702031155494912>";

								await statusMessage.ModifyAsync(m => m.Content = message);
								continue;
							}

							message = "Server Status: <https://status.valkyrja.app>\n" +
							                 $"```md\n[         Last update ][ {Utils.GetTimestamp(DateTime.UtcNow)} ]\n" +
							                 $"[        Memory usage ][ {memUsed/128*100:#00.00} % ({memUsed:000.00}/128 GB) ]\n" +
							                 $"[     CPU utilization ][ {(cpuUtil):#00.00} %                 ]\n" +
							                 $"[       CPU Frequency ][ {double.Parse(cpuFrequency)/1000:#0.00} GHz                ]\n" +
							                 (temp.Length < 3 ? "" : (
							                 $"[       CPU Tdie Temp ][ {temp[1]}                 ]\n" +
							                 $"[       CPU Tctl Temp ][ {temp[2]}                 ]\n" +
							                 $"[            GPU Temp ][ {temp[0]}                 ]\n")) +
							                 $"[    Disk utilization ][ {diskUtil:#000.00} MB/s             ]\n" +
							                 $"[ Network utilization ][ {netUtil:#000.00} Mbps             ]\n" +
							                 $"[  CF Network latency ][ {latencyCloudflare:#0} ms                  {(latencyCloudflare < 10 ? "  " : latencyCloudflare < 100 ? " " : "")}]\n" +
							                 $"[      Root Raid Sync ][ {double.Parse(this.RootRaidSync):000.00} %                ]\n" +
							                 $"[  Root Raid Failures ][ {int.Parse(this.RootRaidFailedDrives):0}                       ]\n" +
							                 $"[      Data Raid Sync ][ {double.Parse(this.DataRaidSync):000.00} %                ]\n" +
							                 $"[  Data Raid Failures ][ {int.Parse(this.DataRaidFailedDrives):0}                       ]\n";

							int shardCount = 0;
							StringBuilder shards = new StringBuilder();
							if( this.Config.PrintShardsOnGuildId == server.GuildId )
							{
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

								this.IsValkOnline = dbContext.Shards.All(s => s.TimeStarted > DateTime.MinValue);
								shardCount = dbContext.Shards.Count();

								if( DateTime.UtcNow - this.LastShardCleanupTime > TimeSpan.FromMinutes(3) )
								{
									this.LastShardCleanupTime = DateTime.UtcNow;
									foreach( Shard shard in dbContext.Shards )
									{
										shard.TimeStarted = DateTime.MinValue;
										shard.IsConnecting = false;
									}

									this.RootRaidSync = Bash.Run("lvs fedora_keyra -o 'lv_name,copy_percent,vg_missing_pv_count' | grep root | awk '{print $2}'");
									this.RootRaidFailedDrives = Bash.Run("lvs fedora_keyra -o 'lv_name,copy_percent,vg_missing_pv_count' | grep root | awk '{print $3}'");
									this.DataRaidSync = Bash.Run("lvs raid5 -o 'lv_name,copy_percent,vg_missing_pv_count' | grep raid5 | awk '{print $2}'");
									this.DataRaidFailedDrives = Bash.Run("lvs raid5 -o 'lv_name,copy_percent,vg_missing_pv_count' | grep raid5 | awk '{print $3}'");
									this.Monitoring.RootRaidSync.Set(double.Parse(this.RootRaidSync));
									this.Monitoring.RootRaidFailedDrives.Set(double.Parse(this.RootRaidFailedDrives));
									this.Monitoring.DataRaidSync.Set(double.Parse(this.DataRaidSync));
									this.Monitoring.DataRaidFailedDrives.Set(double.Parse(this.DataRaidFailedDrives));
								}

								message = message + $"[            Threads ][ {globalCount.ThreadsActive:#000}                     ]\n";
								dbContext.SaveChanges();
								dbContext.Dispose();
							}

							message = message + "```\n";

							if( this.Config.PrintShardsOnGuildId == server.GuildId )
								message = message + $"**Shards: `{shardCount}`**\n\n{shards.ToString()}";

							await statusMessage.ModifyAsync(m => m.Content = message);
						}
					}
					catch(Exception exception)
					{
						LogException(exception, "--Update");
					}
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
				switch(commandString) //TODO: Replace by Valk's command handling...
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
					case "maintenance":
					case "shutdown":
					case "poweroff":
						this.ShuttingDown = true;
						response = "State: `Down for Maintenance`";
						break;
					case "endMaintenance":
						this.ShuttingDown = false;
						response = "State: `Online`";
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

		public string GetPrefix(guid serverId)
		{
			GlobalContext dbContext = GlobalContext.Create(this.Config.GetDbConnectionString());
			string prefix = dbContext.ServerConfigurations.FirstOrDefault(c => c.ServerId == serverId)?.CommandPrefix ?? this.Config.Prefix;
			dbContext.Dispose();
			return prefix;
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
