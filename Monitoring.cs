using System;
using Prometheus;

namespace Valkyrja.service
{
	public class Monitoring: IDisposable
	{
		private readonly MetricPusher Prometheus;

		public readonly Gauge VmFedora1 = Metrics.CreateGauge("vm_fedora1", "KVM: Fedora guest 1");
		public readonly Gauge VmRhel1 = Metrics.CreateGauge("vm_rhel1", "KVM: RHEL guest 1");
		public readonly Gauge CpuUtil = Metrics.CreateGauge("hw_cpu_util", "Server: CPU Utilization in %");
		public readonly Gauge MemUsed = Metrics.CreateGauge("hw_mem_used", "Server: Used memory in GiB");
		public readonly Gauge DiskUtil = Metrics.CreateGauge("hw_disk_util", "Server: Disk utilization in MB/s");
		public readonly Gauge NetUtil = Metrics.CreateGauge("hw_net_util", "Server: Network utilization in Mbps");
		public readonly Gauge CpuTemp = Metrics.CreateGauge("hw_cpu_temp", "Server: CPU Temperature in degrees Celsius");
		public readonly Gauge GpuTemp = Metrics.CreateGauge("hw_gpu_temp", "Server: GPU Temperature in degrees Celsius");
		public readonly Gauge LatencyCloudflare = Metrics.CreateGauge("hw_net_latency_cloudflare", "Server: Network latency to Cloudflare");
		public readonly Gauge LatencyGoogle = Metrics.CreateGauge("hw_net_latency_google", "Server: Network latency to Google");
		public readonly Gauge LatencyDiscord = Metrics.CreateGauge("hw_net_latency_discord", "Server: Network latency to Discord");
		public readonly Gauge RootRaidSync = Metrics.CreateGauge("hw_lvm_root_sync", "Server: LVM root RAID array sync");
		public readonly Gauge RootRaidFailedDrives = Metrics.CreateGauge("hw_lvm_root_failed", "Server: LVM root RAID array failures");
		public readonly Gauge DataRaidSync = Metrics.CreateGauge("hw_lvm_data_sync", "Server: LVM data RAID array sync");
		public readonly Gauge DataRaidFailedDrives = Metrics.CreateGauge("hw_lvm_data_failed", "Server: LVM data RAID array failures");
		public readonly Counter Disconnects = Metrics.CreateCounter("discord_valk_dc", "Valkyrja: disconnects");
		public readonly Counter Error500s = Metrics.CreateCounter("discord_valk_500", "Valkyrja: Discord server error 500s");

		public Monitoring(Config config)
		{
			if( this.Prometheus == null )
				this.Prometheus = new MetricPusher(config.PrometheusEndpoint, config.PrometheusJob, "hw", intervalMilliseconds:(long)(1f / config.TargetFps * 1000));
			this.Prometheus.Start();
		}

		public void Dispose()
		{
			this.Prometheus.Stop();
			((IDisposable)this.Prometheus)?.Dispose();
		}
	}
}
