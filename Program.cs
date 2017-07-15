using System;
using System.Threading.Tasks;
using Discord;

namespace Botwinder.Service
{
    public class Program
    {
	    public static SkywinderClient Skywinder = null;

        public static void Main(string[] args)
        {
	        Connect();

			while( true )
			{
				Task.Delay(300000).Wait();
				if( Skywinder.Client.ConnectionState == ConnectionState.Disconnected )
					Connect();
			}
		}

	    public static void Connect()
	    {
		    Console.WriteLine("Botwinder.Service: Connecting...");

		    if( Skywinder != null )
			    Skywinder.Client.Dispose();
		    Skywinder = new SkywinderClient();
		    Skywinder.Connect().Wait();

		    Console.WriteLine("Botwinder.Service: Connected.");
	    }
    }
}
