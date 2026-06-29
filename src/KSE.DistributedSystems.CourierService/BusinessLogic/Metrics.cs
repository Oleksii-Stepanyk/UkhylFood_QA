using System.Diagnostics.Metrics;

namespace KSE.DistributedSystems.CourierService.BusinessLogic;

public static class Metrics
{
    private static readonly Meter Meter = new("CourierService.Metrics", "1.0.0");

    public static Counter<int> OrderAssignmentAttempts { get; } =
        Meter.CreateCounter<int>("order_assignment_attempts_total", unit: "attempts",
            description: "Total order assignment attempts");

    public static Histogram<double> OrderAssignmentDuration { get; } =
        Meter.CreateHistogram<double>("order_assignment_duration_milliseconds", unit: "milliseconds",
            description: "Duration of order assignment in milliseconds");
}