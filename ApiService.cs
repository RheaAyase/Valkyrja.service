using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Internal;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using guid = System.UInt64;

namespace Valkyrja.service
{
	public class ApiService
	{
		private class Startup
		{
			public Startup(IConfiguration configuration)
			{
				Configuration = configuration;
			}

			public IConfiguration Configuration { get; }

			// This method gets called by the runtime. Use this method to add services to the container.
			public void ConfigureServices(IServiceCollection services)
			{
				services.AddMvc().SetCompatibilityVersion(CompatibilityVersion.Version_2_2);
			}

			// This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
			public void Configure(IApplicationBuilder app, IHostingEnvironment env)
			{
				if (env.IsDevelopment())
				{
					//app.UseDeveloperExceptionPage();
				}
				else
				{
					// The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
					//app.UseHsts();
				}

				//app.UseHttpsRedirection();
				app.UseMvcWithDefaultRoute();
				app.Use((context, next) => {
					context.Request.Scheme = "https";
					return next();
				});
				app.UseEndpoint();
			}
		}

		[Route("api")]
		[ApiController]
		public class MonitoringController: ControllerBase
		{
			// GET api/status
			[HttpGet("status")]
			public ActionResult<string> Get()
			{
				return Client.IsValkOnline.ToString();
			}

			// GET api/prefixes/guildId
			[HttpGet("prefixes/{id}")]
			public ActionResult<string> Get(guid id)
			{
				return Client.GetPrefix(id);
			}
		}

		internal static SkywinderClient Client = null;

		public ApiService(SkywinderClient client)
		{
			Client = client;
		}

		public void Run()
		{
			new WebHostBuilder().UseKestrel().UseStartup<Startup>().UseUrls("http://127.0.0.1:5080/").Build().Run();
		}
	}
}
