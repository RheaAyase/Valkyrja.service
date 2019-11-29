using System;

namespace Valkyrja.service
{
    public class Program
    {
	    public static SkywinderClient Skywinder = null;

        public static void Main(string[] args)
        {
	        Connect();

			new ApiService(Skywinder).Run();
		}

	    public static void Connect()
	    {
		    Console.WriteLine("Valkyrja.Service: Connecting...");

		    if( Skywinder != null )
			    Skywinder.Client.Dispose();
		    Skywinder = new SkywinderClient();
		    Skywinder.Connect().Wait();

		    Console.WriteLine("Valkyrja.Service: Connected.");
	    }
    }
}
