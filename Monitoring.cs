using Prometheus;

namespace Valkyrja.service
{
	public class Monitoring
	{
		private MetricPusher Prometheus;

		public Gauge MemTotal = Metrics.CreateGauge("hw_memory_total", "Server: Total memory");
		public Gauge MemUsed = Metrics.CreateGauge("hw_memory_used", "Server: Used memory");
		public Gauge MemPercent = Metrics.CreateGauge("hw_memory_percent", "Server: Used memory percentage of total");
		public Gauge Temp = Metrics.CreateGauge("hw_memory_temp", "Server: Used memory percentage of total");

		public Monitoring(Config config)
		{
			if( this.Prometheus == null )
				this.Prometheus = new MetricPusher(config.PrometheusEndpoint, config.PrometheusJob, intervalMilliseconds:(long)(1f / config.TargetFps * 1000));
			this.Prometheus.Start();
		}
	}
}
