﻿using System;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.Net;
using Discord.Rest;
using Discord.WebSocket;
using Valkyrja.entities;
using guid = System.UInt64;

namespace Valkyrja.service
{
	public class SkywinderClient
	{
		private Monitoring Monitoring;

		internal DiscordSocketClient Client = new DiscordSocketClient();
		private readonly Config Config = Config.Load();
		private readonly Regex RegexCommandParams = new Regex("\"[^\"]+\"|\\S+", RegexOptions.Compiled);
		private readonly Regex RegexPcp = new Regex("\\d+\\.?\\d*", RegexOptions.Compiled);
		private CancellationTokenSource MainUpdateCancel;
		private Task MainUpdateTask;

		private DateTime LastShardCleanupTime = DateTime.UtcNow;

		private string RootRaidSync = "-1";
		private string RootRaidFailedDrives = "-1";
		private string DataRaidSync = "-1";
		private string DataRaidFailedDrives = "-1";
		private string NvmeRaidSync = "-1";
		private string NvmeRaidFailedDrives = "-1";
		private bool ShuttingDown = false;
		public bool IsValkOnline = true;
		private int RestartCounter = 0;

		public SkywinderClient()
		{
			SetEvents();
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

				try
				{
					// Prep monitoring data
					Ping pingCloudflare = new Ping();
					Ping pingGoogle = new Ping(); //Not-a-google-anymore
					Ping pingDiscord = new Ping();
					//Ping pingVmF1 = new Ping();
					//Ping pingVmR1 = new Ping();
					Task<PingReply> pingReplyCloudflare = pingCloudflare.SendPingAsync("1.1.1.1", 1000);
					Task<PingReply> pingReplyGoogle = pingGoogle.SendPingAsync("4.2.2.4", 1000); //Not-a-google-anymore
					Task<PingReply> pingReplyDiscord = pingDiscord.SendPingAsync("gateway.discord.gg", 1000);
					//Task<PingReply> pingReplyVmF1 = pingVmF1.SendPingAsync("192.168.122.11", 1000);
					//Task<PingReply> pingReplyVmR1 = pingVmR1.SendPingAsync("192.168.122.21", 1000);
					string pcpRaw = Bash.Run("pmrep -s 2 kernel.cpu.util.idle mem.util.available disk.dev.total_bytes network.interface.total.bytes | tail -n 1");
					MatchCollection pcpArray = this.RegexPcp.Matches(pcpRaw);

					StringBuilder shards = new StringBuilder();
					GlobalContext dbContext = GlobalContext.Create(this.Config.GetDbConnectionString());
					foreach( Shard shard in dbContext.Shards )
					{
						shards.AppendLine(shard.GetShortStatsString());
					}

					this.IsValkOnline = dbContext.Shards.All(s => s.TimeStarted > DateTime.MinValue);

					if( DateTime.UtcNow - this.LastShardCleanupTime > TimeSpan.FromMinutes(3) )
					{
						this.LastShardCleanupTime = DateTime.UtcNow;
						foreach( Shard shard in dbContext.Shards )
						{
							shard.TimeStarted = DateTime.MinValue;
							shard.IsConnecting = false;
						}

						dbContext.SaveChanges();

						Bash.Run("lvs -o 'lv_name,copy_percent,vg_missing_pv_count' > raidstatus");
						this.RootRaidSync = Bash.Run("cat raidstatus | grep root | awk '{print $2}'");
						this.RootRaidFailedDrives = Bash.Run("cat raidstatus | grep root | awk '{print $3}'");
						this.DataRaidSync = Bash.Run("cat raidstatus | grep 'raid5\\s' | awk '{print $2}'");
						this.DataRaidFailedDrives = Bash.Run("cat raidstatus | grep 'raid5\\s' | awk '{print $3}'");
						this.NvmeRaidSync = Bash.Run("cat raidstatus | grep nvmeraid5 | awk '{print $2}'");
						this.NvmeRaidFailedDrives = Bash.Run("cat raidstatus | grep nvmeraid5 | awk '{print $3}'");
						this.Monitoring.RootRaidSync.Set(double.Parse(this.RootRaidSync));
						this.Monitoring.RootRaidFailedDrives.Set(double.Parse(this.RootRaidFailedDrives));
						this.Monitoring.DataRaidSync.Set(double.Parse(this.DataRaidSync));
						this.Monitoring.DataRaidFailedDrives.Set(double.Parse(this.DataRaidFailedDrives));
						this.Monitoring.NvmeRaidSync.Set(double.Parse(this.NvmeRaidSync));
						this.Monitoring.NvmeRaidFailedDrives.Set(double.Parse(this.NvmeRaidFailedDrives));
					}

					dbContext.Dispose();

					double cpuUtil = 0; //%
					double memUsed = 0; //GB
					double diskUtil = 0; //MB/s
					double netUtil = 0; //Mbps
					string[] temp = null;
					string cpuFrequency = "";
					long latencyCloudflare = 0;
					long latencyGoogle = 0; //Not-a-google-anymore
					long latencyDiscord = 0;
					//long latencyVmF1 = 0;
					//long latencyVmR1 = 0;

					try
					{
						cpuUtil = 100 - double.Parse(pcpArray[0].Value); //%
						memUsed = 128 - double.Parse(pcpArray[1].Value) / 1048576; //GB
						diskUtil = (double.Parse(pcpArray[2].Value) + double.Parse(pcpArray[3].Value) + double.Parse(pcpArray[4].Value) + double.Parse(pcpArray[5].Value) + double.Parse(pcpArray[6].Value) + double.Parse(pcpArray[7].Value) + double.Parse(pcpArray[8].Value)) / 1024; //MB/s
						netUtil = double.Parse(pcpArray[14].Value) * 8 / 1048576; //Mbps
						temp = Bash.Run("sensors | egrep '(Tctl|Tccd1|Tccd2|temp1)' | awk '{print $2}'").Split('\n');
						cpuFrequency = Bash.Run("grep MHz /proc/cpuinfo | awk '{ f = 0; if( $4 > f ) f = $4; } END { print f; }'");
						latencyCloudflare = (await pingReplyCloudflare).RoundtripTime;
						latencyGoogle = (await pingReplyGoogle).RoundtripTime; //Not-a-google-anymore
						latencyDiscord = (await pingReplyDiscord).RoundtripTime;
						//latencyVmF1 = (await pingReplyVmF1).RoundtripTime;
						//latencyVmR1 = (await pingReplyVmR1).RoundtripTime;
					}
					catch( Exception exception )
					{
						LogException(exception, "--Update: Monitoring data");
					}

					this.Monitoring.CpuUtil.Set(cpuUtil);
					this.Monitoring.MemUsed.Set(memUsed);
					this.Monitoring.DiskUtil.Set(diskUtil);
					this.Monitoring.NetUtil.Set(netUtil);
					if( temp != null && temp.Length > 0 )
					{
						this.Monitoring.CpuTemp.Set(double.Parse(temp[this.Config.CpuTempIndex].Trim('-', '+', '°', 'C')));
						this.Monitoring.GpuTemp.Set(double.Parse(temp[this.Config.GpuTempIndex].Trim('-', '+', '°', 'C')));
					}

					this.Monitoring.LatencyCloudflare.Set(latencyCloudflare);
					this.Monitoring.LatencyGoogle.Set(latencyGoogle); //Not-a-google-anymore
					this.Monitoring.LatencyDiscord.Set(latencyDiscord);
					//this.Monitoring.VmFedora1.Set(latencyVmF1);
					//this.Monitoring.VmRhel1.Set(latencyVmR1);

					if( this.Client == null ||
					    this.Client.ConnectionState != ConnectionState.Connected ||
					    this.Client.LoginState != LoginState.LoggedIn )
					{
						await Task.Delay(TimeSpan.FromMilliseconds(Math.Max(1, (TimeSpan.FromSeconds(1f / this.Config.TargetFps) - (DateTime.UtcNow - frameTime)).TotalMilliseconds)));
						continue;
					}

					string message = null;
					if( this.ShuttingDown )
						message = "Server Status: <https://status.valkyrja.app>\n" +
						          $"```md\n[        Last update ][ {Utils.GetTimestamp(DateTime.UtcNow)} ]\n" +
						          $"[              State ][ Down for Maintenance    ]```\n" +
						          $"<:offwinder:438702031155494912>";
					else
						message = "Server Status: <https://status.valkyrja.app>\n" +
						          $"```md\n[         Last update ][ {Utils.GetTimestamp(DateTime.UtcNow)} ]\n" +
						          $"[        Memory usage ][ {memUsed / 128 * 100:#00.00} % ({memUsed:000.00}/128 GB) ]\n" +
						          $"[     CPU utilization ][ {(cpuUtil):#00.00} %                 ]\n" +
						          $"[       CPU Frequency ][ {double.Parse(cpuFrequency) / 1000:#0.00} GHz                ]\n" + (temp.Length < 3
							          ? ""
							          : (
								          $"[       CPU Tctl Temp ][ {temp[this.Config.CpuTempIndex]}                 ]\n" +
								          $"[      CPU Tccd1 Temp ][ {temp[this.Config.Ccd1TempIndex]}                 ]\n" +
								          $"[      CPU Tccd2 Temp ][ {temp[this.Config.Ccd2TempIndex]}                 ]\n" +
								          $"[            GPU Temp ][ {temp[this.Config.GpuTempIndex]}                 ]\n")) +
						          $"[    Disk utilization ][ {diskUtil:#000.00} MB/s             ]\n" +
						          $"[ Network utilization ][ {netUtil:#000.00} Mbps             ]\n" +
						          $"[  CF Network latency ][ {latencyCloudflare:#0} ms                  {(latencyCloudflare < 10 ? "  " : latencyCloudflare < 100 ? " " : "")}]\n" +
						          $"[      Root Raid Sync ][ {double.Parse(this.RootRaidSync):000.00} %                ]\n" +
						          $"[  Root Raid Failures ][ {int.Parse(this.RootRaidFailedDrives):0}                       ]\n" +
						          $"[      Data Raid Sync ][ {double.Parse(this.DataRaidSync):000.00} %                ]\n" +
						          $"[  Data Raid Failures ][ {int.Parse(this.DataRaidFailedDrives):0}                       ]\n" +
						          $"[      NVMe Raid Sync ][ {double.Parse(this.NvmeRaidSync):000.00} %                ]\n" +
						          $"[  NVMe Raid Failures ][ {int.Parse(this.NvmeRaidFailedDrives):0}                       ]\n" +
						          $"```\n";

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
								try
								{
									if( this.Config.CreateNewMessages && server.StatusMessageId == 0 || (statusMessage = (RestUserMessage)await statusChannel.GetMessageAsync(server.StatusMessageId)) == null )
									{
										statusMessage = await statusChannel.SendMessageAsync("Loading status service...");
										this.Config.Servers.First(s => s.GuildId == server.GuildId).StatusMessageId = statusMessage.Id;
										this.Config.Save();
										continue;
									}

									string modifiedMessage = message;
									if( !this.ShuttingDown && this.Config.PrintShardsOnGuildId == server.GuildId )
									{
										modifiedMessage += shards.ToString();
									}

									await statusMessage.ModifyAsync(m => m.Content = modifiedMessage);
								}
								catch( HttpException exception )
								{
									this.Monitoring.Error500s.Inc();
									LogException(exception, "--Update: server loop - 500");
								}
							}
						}
						catch( Exception exception )
						{
							LogException(exception, "--Update: server loop");
						}
					}
				}
				catch( Exception exception )
				{
					LogException(exception, "Main Update");
				}

				await Task.Delay(TimeSpan.FromMilliseconds(Math.Max(1, (TimeSpan.FromSeconds(1f / this.Config.TargetFps) - (DateTime.UtcNow - frameTime)).TotalMilliseconds)));
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

		private async Task ClientDisconnected(Exception exception)
		{
			this.Monitoring.Disconnects.Inc();
			if( exception.Message == "Server requested a reconnect" ||
			    exception.Message == "Server missed last heartbeat" )
				Console.WriteLine($"Discord Client died:\n{  exception.Message}");
			else
			{
				Console.WriteLine($"Discord Client died:\n{  exception.Message}\nRestarting.");
				await Restart();
			}
		}

		private async Task Restart()
		{
			if( ++this.RestartCounter > 30 )
			{
				Environment.Exit(0);
				return;
			}

			await Task.Delay(TimeSpan.FromMinutes(1));
			Console.WriteLine($"Disposing of the Discord client");
			//this.Client.Dispose(); //Locks up.
			this.Client = null;
			await Task.Delay(TimeSpan.FromMinutes(1));
			Console.WriteLine($"Reconnecting...");
			this.Client = new DiscordSocketClient();
			SetEvents();
			await Connect();
		}

		private void SetEvents()
		{
			this.Client.MessageReceived += ClientOnMessageReceived;
			this.Client.MessageUpdated += ClientOnMessageUpdated;
			this.Client.Disconnected += ClientDisconnected;
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
			if( socketMessage.Content == null ||
			    string.IsNullOrWhiteSpace(this.Config.Prefix) ||
			    !socketMessage.Content.StartsWith(this.Config.Prefix) )
				return;

			string response = "", commandString = "", trimmedMessage = "";
			string[] parameters;
			if( !string.IsNullOrWhiteSpace(this.Config.Prefix) && socketMessage.Content.StartsWith(this.Config.Prefix) )
				GetCommandAndParams(socketMessage.Content, out commandString, out trimmedMessage, out parameters);

			try
			{
				if( this.Config.AdminIDs2.Contains(socketMessage.Author.Id) && commandString == "serviceRestart" )
				{
					if( string.IsNullOrWhiteSpace(trimmedMessage) || !this.Config.ServiceNames2.Contains(trimmedMessage + ".service") )
					{
						response = "Invalid parameter - service name";
					}
					else
					{
						Console.WriteLine("Executing !serviceRestart " + trimmedMessage + " | " + socketMessage.Author.Id + " | " + socketMessage.Author.Username);
						response = "Restart successful: `" + await Systemd.RestartService(trimmedMessage + ".service") + "`";
					}
				}
				else
				{

					if( !this.Config.AdminIDs.Contains(socketMessage.Author.Id) )
						return;

					switch( commandString ) //TODO: Replace by Valk's command handling...
					{
						case "serviceStatus":
							if( string.IsNullOrWhiteSpace(trimmedMessage) || !this.Config.ServiceNames.Contains(trimmedMessage + ".service") )
							{
								response = "Invalid parameter - service name";
								break;
							}

							Console.WriteLine("Executing !serviceStatus " + trimmedMessage + " | " + socketMessage.Author.Id + " | " + socketMessage.Author.Username);
							response = await Systemd.GetServiceStatus(trimmedMessage + ".service");
							break;
						case "serviceRestart":
							if( string.IsNullOrWhiteSpace(trimmedMessage) || !this.Config.ServiceNames.Contains(trimmedMessage + ".service") )
							{
								response = "Invalid parameter - service name";
								break;
							}

							Console.WriteLine("Executing !serviceRestart " + trimmedMessage + " | " + socketMessage.Author.Id + " | " + socketMessage.Author.Username);
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
