using System;
using System.IO;
using Newtonsoft.Json;

using guid = System.UInt64;

namespace Valkyrja.service
{
	public class Config
	{
		public class Server
		{
			public guid GuildId;
			public guid StatusChannelId;
			public guid StatusMessageId;
		}

		public const string Filename = "config.json";
		public const guid Rhea = 89805412676681728;
		public const string RheaName = "Rhea#1234";
		public const int MessageCharacterLimit = 2000;

		public string BotToken = "";
		public string Prefix = "!";
		public float TargetFps = 0.03f;
		public string Host = "127.0.0.1";
		public string Port = "3306";
		public string Username = "db_user";
		public string Password = "db_password";
		public string Database = "db_valkyrja";
		public string PrometheusEndpoint = "";
		public string PrometheusJob = "";
		public int CpuTempIndex;
		public int GpuTempIndex;
		public guid PrintShardsOnGuildId;
		public guid[] AdminIDs = { Rhea, 89777099576979456 };
		public string[] ServiceNames = { "valkyrja", "httpd" };
		public guid[] AdminIDs2 = { Rhea, 89777099576979456 }; // should not contain any IDs from AdminIDs
		public string[] ServiceNames2 = { "valkyrja", "httpd" };
		public Server[] Servers;
		public bool CreateNewMessages = false;

		private Config(){}
		public static Config Load()
		{
			string path = Filename;

			if( !File.Exists(path) )
			{
				string json = JsonConvert.SerializeObject(new Config(), Formatting.Indented);
				File.WriteAllText(path, json);
				Console.WriteLine("Default config created.");
				Environment.Exit(0);
			}

			Config config = JsonConvert.DeserializeObject<Config>(File.ReadAllText(path));
			return config;
		}

		public void Save()
		{
			string path = Path.Combine(Filename);
			string json = JsonConvert.SerializeObject(this, Formatting.Indented);
			File.WriteAllText(path, json);
		}

		public string GetDbConnectionString()
		{
			return $"server={this.Host};userid={this.Username};pwd={this.Password};port={this.Port};database={this.Database};sslmode=none;";
		}
	}
}
