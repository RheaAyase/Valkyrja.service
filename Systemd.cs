using System;
using System.Linq;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using System.Threading.Tasks;
using Tmds.DBus;

namespace Botwinder.Service
{
	public class Systemd
	{
		public struct Unit
		{
			public string Name;
			public string Description;
			public string LoadState;
			public string ActiveState;
			public string SubState;
			public string FollowUnit;
			public ObjectPath ObjectPath;
			public UInt32 JobId;
			public string JobType;
			public ObjectPath JobObjectPath;
		}

		[DBusInterface("org.freedesktop.systemd1.Manager")]
		public interface IManager: IDBusObject
		{
			Task<Unit[]> ListUnitsAsync();
		}

		[DBusInterface("org.freedesktop.systemd1")]
		public interface ISystemd: IManager
		{
			Task QuitAsync();
		}

		public static async Task<string> GetServiceStatus(string serviceName)
		{
			StringBuilder statusString = new StringBuilder();
			using( var connection = new Connection(Address.Session) )
			{
				await connection.ConnectAsync();
				IManager systemd = connection.CreateProxy<ISystemd>("org.freedesktop.systemd1", "/org/freedesktop/systemd1") as IManager;
				Unit[] units = await systemd.ListUnitsAsync();
				for( int i = 0; i < units.Length; i++ )
				{
					if( units[i].Name != serviceName )
						continue;

					statusString.AppendFormat("**Service Name:** `{0}`\n" +
					                          "**Status:** `{1}`", units[i].Name, units[i].ActiveState);
				}
			}

			if( statusString.Length == 0 )
				return "Service not found.";
			return statusString.ToString();
		}

		public static async Task<bool> RestartService(string serviceName)
		{
			throw new NotImplementedException();
		}
	}
}
