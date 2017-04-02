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
			string statusString = "";
			using( Connection connection = new Connection(Address.System) )
			{
				await connection.ConnectAsync();
				IManager systemd = connection.CreateProxy<ISystemd>("org.freedesktop.systemd1", "/org/freedesktop/systemd1") as IManager;
				Unit[] units = await systemd.ListUnitsAsync();

				Unit unit = units.First(u => u.Name == serviceName);
				statusString = string.Format("**Service Name:** `{0}`\n" +
											 "**Status:** `{1}`", unit.Name, unit.ActiveState);
			}

			return statusString;
		}

		public static async Task<bool> RestartService(string serviceName)
		{
			throw new NotImplementedException();
		}
	}
}
