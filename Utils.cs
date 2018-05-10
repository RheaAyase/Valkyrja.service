using System;
using System.Runtime.CompilerServices;

using guid = System.UInt64;

namespace Botwinder.entities
{
	public class Utils
	{
		public static Random Random{ get; set; } = new Random();

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static DateTime GetTimeFromId(guid id)
		{
			return new DateTime((long)(((id / 4194304) + 1420070400000) * 10000 + 621355968000000000));
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static string GetTimestamp()
		{
			return GetTimestamp(DateTime.UtcNow);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static string GetTimestamp(DateTime time)
		{
			return time.ToUniversalTime().ToString("yyyy-MM-dd_HH:mm:ss") + " UTC";
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static string GetTimestamp(DateTimeOffset time)
		{
			return time.ToUniversalTime().ToString("yyyy-MM-dd_HH:mm:ss") + " UTC";
		}
	}
}
