using System;
using System.Threading.Tasks;

namespace Botwinder.Service
{
    public class Program
    {
        public static void Main(string[] args)
        {
	        Console.WriteLine("Botwinder.Service: Connecting...");

	        BotClient client = new BotClient();
	        client.Connect().Wait();

	        Console.WriteLine("Botwinder.Service: Connected.");

			while(true)
		        Task.Delay(300000).Wait();
        }
    }
}
