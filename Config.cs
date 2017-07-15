using System;
using System.IO;
using Newtonsoft.Json;

using guid = System.UInt64;

namespace Botwinder.Service
{
	public class Config
	{
		public const string Filename = "config.json";
		public const guid Rhea = 89805412676681728;
		public const string RheaName = "Rhea#0321";
		public const int MessageCharacterLimit = 2000;

		public string BotToken = "";
		public string Prefix = "!";
		public guid[] AdminIDs = { Rhea, 89777099576979456 };
		public string[] ServiceNames = { "botwinder", "coriolis" };

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
	}
}
