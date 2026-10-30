using System.Runtime.CompilerServices;
using UptimeWatch.Domain;

namespace UptimeWatch.Infrastructure.Persistence;

internal static class MonitorAccessors
{
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "set_OpenIncident")]
    public static extern void SetOpenIncident(SiteMonitor monitor, Incident? incident);
}
