using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

using guid = System.UInt64;

namespace Valkyrja.entities
{
	[Table("server_config")]
	public class ServerConfig
	{
		[Key]
		[Required]
		[Column("serverid")]
		[DatabaseGenerated(DatabaseGeneratedOption.None)]
		public guid ServerId{ get; set; } = 0;

		[Column("command_prefix", TypeName = "varchar(255)")]
		public string CommandPrefix{ get; set; } = "!";

		[Column("command_prefix_alt", TypeName = "varchar(255)")]
		public string CommandPrefixAlt{ get; set; } = "";
	}
}
